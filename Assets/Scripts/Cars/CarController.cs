using RaceGame.Data;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.Cars
{
    /// <summary>
    /// Car physics on four WheelColliders: engine, automatic gearbox,
    /// brakes, handbrake, anti-roll bars, air drag and downforce.
    /// All numbers come from CarSpec + tuning (ApplyTuning).
    /// Wheel order everywhere: 0 FL, 1 FR, 2 RL, 3 RR.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        public const float SuspensionTarget = 0.3f;
        public const float StaticSag = 0.35f;

        [Range(-1f, 1f)] public float throttleInput;
        [Range(-1f, 1f)] public float steerInput;
        public bool handbrakeInput;

        public CarSpec Spec { get; private set; }
        public TunedStats Stats { get; private set; }
        public int Gear { get; private set; } = 1;   // -1 = reverse, 1..N = forward
        public float EngineRpm { get; private set; } = CarMath.IdleRpm;
        public float SpeedKmh => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
        public float ForwardSpeed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
        public int GearCount => gearRatios != null ? gearRatios.Length : 0;
        public Rigidbody Body => rb;

        WheelCollider[] wheels;
        Transform[] wheelVisuals;
        Vector3[] baseWheelPositions;
        float suspensionTravel;
        Rigidbody rb;

        float[] gearRatios;
        float baseFinalDrive, finalDrive;
        float baseDragK, dragK;
        float baseDownforceK, downforceK;
        float antiRollStiffness;
        float brakeTorqueMax;
        float brakeBias = 0.6f;
        float gripSideRear = 1f;
        float camber;
        float maxSteerAngle = 35f;
        float currentSteer;
        float shiftTimer;
        float rearSideStiffness = 1f;

        public void Init(CarSpec spec, WheelCollider[] wheelColliders, Transform[] visuals, float travel)
        {
            Spec = spec;
            wheels = wheelColliders;
            wheelVisuals = visuals;
            suspensionTravel = travel;
            rb = GetComponent<Rigidbody>();

            baseWheelPositions = new Vector3[4];
            for (int i = 0; i < 4; i++) baseWheelPositions[i] = wheels[i].transform.localPosition;

            gearRatios = CarMath.GearRatios(spec.gears);
            baseFinalDrive = CarMath.FinalDrive(spec.redlineRpm, spec.wheelRadiusM, gearRatios[gearRatios.Length - 1], spec.topSpeedKmh);
            baseDragK = CarMath.DragCoefficient(spec.powerHp, spec.topSpeedKmh);
            baseDownforceK = spec.Body == BodyType.Supercar ? 0.6f : 0.15f;
            maxSteerAngle = spec.Body == BodyType.Supercar ? 30f : 36f;

            wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            Gear = 1;
            ApplyTuning(TuningStats.Compute(spec, new TuningData()));
        }

        /// <summary>Applies tuned characteristics to the physics (can be called at any time).</summary>
        public void ApplyTuning(TunedStats s)
        {
            Stats = s;
            rb.mass = s.massKg;

            // bigger wheels make gearing "longer" by themselves; finalDrive is per wheel turn
            finalDrive = baseFinalDrive * s.finalDriveMul;
            dragK = baseDragK * s.dragMul;
            downforceK = baseDownforceK * s.downforceMul;
            brakeTorqueMax = s.massKg * 9.81f * s.wheelRadius * 0.35f * s.brakeMul;
            brakeBias = s.brakeBias;
            antiRollStiffness = s.massKg * 4f * s.springMul;
            gripSideRear = s.gripSide * s.rearSideMul;
            camber = s.camber;

            float sprungMass = s.massKg * 0.25f;
            float spring = sprungMass * 9.81f / (StaticSag * suspensionTravel) * s.springMul;
            float damper = 2f * Mathf.Sqrt(spring * sprungMass) * 0.35f * s.damperMul;

            for (int i = 0; i < 4; i++)
            {
                var wc = wheels[i];
                wc.radius = s.wheelRadius;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = SuspensionTarget };

                // ride height: raising the car = moving the suspension mount down relative to the body
                Vector3 p = baseWheelPositions[i];
                p.y -= s.rideHeight;
                p.x += Mathf.Sign(p.x) * s.trackOffset;
                wc.transform.localPosition = p;

                var fwd = wc.forwardFriction; fwd.stiffness = 1.2f * s.gripForward; wc.forwardFriction = fwd;
                var side = wc.sidewaysFriction; side.stiffness = i < 2 ? s.gripSide : gripSideRear; wc.sidewaysFriction = side;
            }
        }

        void FixedUpdate()
        {
            if (wheels == null || rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            float speedKmh = SpeedKmh;
            float fwd = ForwardSpeed;

            // --- driving direction and pedals ---
            float accel = 0f, brake = 0f;
            float t = throttleInput;
            if (Gear > 0)
            {
                if (t > 0.01f) accel = t;
                else if (t < -0.01f)
                {
                    if (fwd > 1f) brake = -t;
                    else Gear = -1;
                }
            }
            else
            {
                if (t < -0.01f) accel = -t;
                else if (t > 0.01f)
                {
                    if (fwd < -1f) brake = t;
                    else Gear = 1;
                }
            }

            // --- engine RPM from driven wheels ---
            float ratio = Gear > 0 ? gearRatios[Gear - 1] : CarMath.ReverseRatio;
            float wheelRpm = Mathf.Abs(DrivenWheelRpm());
            float targetRpm = Mathf.Max(CarMath.IdleRpm, wheelRpm * ratio * finalDrive);
            if (accel > 0f && wheelRpm * ratio * finalDrive < CarMath.IdleRpm * 2f)
                targetRpm = Mathf.Max(targetRpm, Mathf.Lerp(CarMath.IdleRpm, Spec.redlineRpm * 0.45f, accel)); // clutch slip at launch
            EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Min(targetRpm, Spec.redlineRpm), 0.3f);

            // --- automatic gearbox ---
            if (shiftTimer > 0f) shiftTimer -= dt;
            else if (Gear > 0)
            {
                if (EngineRpm > Spec.redlineRpm * 0.92f && Gear < gearRatios.Length) { Gear++; shiftTimer = 0.25f; }
                else if (EngineRpm < Spec.redlineRpm * 0.45f && Gear > 1) { Gear--; shiftTimer = 0.2f; }
            }

            // --- drive torque ---
            float engineTorque = shiftTimer > 0f ? 0f
                : CarMath.EngineTorque(EngineRpm, Stats.torqueNm, Stats.powerHp, Spec.redlineRpm) * accel;
            float wheelTorque = engineTorque * ratio * finalDrive * CarMath.DrivetrainEfficiency * (Gear > 0 ? 1f : -1f);
            ApplyDrive(wheelTorque);

            // --- brakes ---
            float engineBrake = accel <= 0.01f && brake <= 0.01f ? brakeTorqueMax * 0.03f : 0f;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float share = front ? brakeBias : 1f - brakeBias;
                float b = brake * brakeTorqueMax * 2f * share + engineBrake;
                if (!front && handbrakeInput) b = brakeTorqueMax * 2.5f;
                wheels[i].brakeTorque = b;
            }

            // --- handbrake: less rear grip for drifting ---
            float targetRear = handbrakeInput ? 0.5f : 1f;
            rearSideStiffness = Mathf.MoveTowards(rearSideStiffness, targetRear, dt * 3f);
            for (int i = 2; i < 4; i++)
            {
                var side = wheels[i].sidewaysFriction;
                side.stiffness = rearSideStiffness * gripSideRear;
                wheels[i].sidewaysFriction = side;
            }

            // --- steering: less angle at speed ---
            float speedFactor = Mathf.Lerp(1f, 0.3f, speedKmh / 160f);
            float targetSteer = steerInput * maxSteerAngle * speedFactor;
            currentSteer = Mathf.MoveTowards(currentSteer, targetSteer, 120f * dt);
            wheels[0].steerAngle = currentSteer;
            wheels[1].steerAngle = currentSteer;

            // --- anti-roll bars, drag, downforce ---
            AntiRoll(wheels[0], wheels[1]);
            AntiRoll(wheels[2], wheels[3]);

            Vector3 v = rb.linearVelocity;
            float v2 = v.sqrMagnitude;
            if (v2 > 0.01f) rb.AddForce(-v.normalized * dragK * v2);
            rb.AddForce(-transform.up * downforceK * v2);
        }

        void LateUpdate()
        {
            if (wheels == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (wheelVisuals[i] == null) continue;
                wheels[i].GetWorldPose(out var pos, out var rot);
                // camber: tilt the top of the wheel inward (visual only)
                float c = (i % 2 == 0) ? camber : -camber;
                Quaternion tilt = Quaternion.AngleAxis(c, transform.forward);
                wheelVisuals[i].SetPositionAndRotation(pos, tilt * rot);
            }
        }

        float DrivenWheelRpm()
        {
            switch (Spec.Drive)
            {
                case DriveType.FWD: return (wheels[0].rpm + wheels[1].rpm) * 0.5f;
                case DriveType.RWD: return (wheels[2].rpm + wheels[3].rpm) * 0.5f;
                default: return (wheels[0].rpm + wheels[1].rpm + wheels[2].rpm + wheels[3].rpm) * 0.25f;
            }
        }

        void ApplyDrive(float totalTorque)
        {
            for (int i = 0; i < 4; i++) wheels[i].motorTorque = 0f;
            switch (Spec.Drive)
            {
                case DriveType.FWD:
                    wheels[0].motorTorque = wheels[1].motorTorque = totalTorque * 0.5f;
                    break;
                case DriveType.RWD:
                    wheels[2].motorTorque = wheels[3].motorTorque = totalTorque * 0.5f;
                    break;
                default: // AWD 40/60
                    wheels[0].motorTorque = wheels[1].motorTorque = totalTorque * 0.2f;
                    wheels[2].motorTorque = wheels[3].motorTorque = totalTorque * 0.3f;
                    break;
            }
        }

        void AntiRoll(WheelCollider left, WheelCollider right)
        {
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out var hitL);
            bool groundedR = right.GetGroundHit(out var hitR);
            if (groundedL)
                travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
            if (groundedR)
                travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;
            float force = (travelL - travelR) * antiRollStiffness;
            if (groundedL) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        /// <summary>Stops the car and freezes/unfreezes it (garage).</summary>
        public void SetFrozen(bool frozen)
        {
            if (frozen)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                for (int i = 0; i < 4; i++) { wheels[i].motorTorque = 0f; wheels[i].brakeTorque = brakeTorqueMax; }
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = false;
                rb.WakeUp();
            }
            throttleInput = steerInput = 0f;
            handbrakeInput = false;
        }

        /// <summary>Puts the car back on its wheels (R key / button).</summary>
        public void ResetCar()
        {
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            transform.SetPositionAndRotation(transform.position + Vector3.up * 1.5f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            Gear = 1;
        }
    }
}
