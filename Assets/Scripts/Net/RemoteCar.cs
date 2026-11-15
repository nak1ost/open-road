using RaceGame.Cars;
using UnityEngine;

namespace RaceGame.Net
{
    /// <summary>
    /// Another player's car: kinematic, follows the latest network state with extrapolation and smoothing.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class RemoteCar : MonoBehaviour
    {
        public int playerId;
        public string playerName;

        CarController car;
        Vector3 lastPos, lastVel;
        Quaternion lastRot = Quaternion.identity;
        float lastTime = -1f;
        uint lastSeq;

        void Awake()
        {
            car = GetComponent<CarController>();
        }

        void Start()
        {
            car.SetFrozen(true);
        }

        public void Apply(CarState s)
        {
            if (lastTime >= 0f && s.seq <= lastSeq && lastSeq - s.seq < 1000) return; // old packet
            lastSeq = s.seq;
            bool first = lastTime < 0f;
            lastPos = s.position;
            lastRot = s.rotation;
            lastVel = s.velocity;
            lastTime = Time.time;
            car.steerInput = s.steer;
            if (first) transform.SetPositionAndRotation(lastPos, lastRot);
        }

        void Update()
        {
            if (lastTime < 0f) return;
            float age = Mathf.Min(Time.time - lastTime, 0.3f);
            Vector3 target = lastPos + lastVel * age;
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude > 400f) transform.position = target; // teleported
            else transform.position = Vector3.Lerp(transform.position, target, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, lastRot, k);
        }

        public float SpeedKmh => lastVel.magnitude * 3.6f;
    }
}
