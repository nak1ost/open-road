using RaceGame.Cars;
using RaceGame.Data;
using UnityEngine;

namespace RaceGame.Tuning
{
    /// <summary>Car characteristics after tuning. Pure math, used by physics and the garage screen.</summary>
    public struct TunedStats
    {
        public float powerHp;
        public float torqueNm;
        public float massKg;
        public float finalDriveMul;
        public float brakeMul;
        public float brakeBias;
        public float gripForward;
        public float gripSide;
        public float rearSideMul;
        public float downforceMul;
        public float dragMul;
        public float wheelRadius;
        public float springMul;
        public float damperMul;
        public float rideHeight;
        public float trackOffset;
        public float camber;
        public float topSpeedKmh;
        public float hpPerTon;
    }

    public static class TuningStats
    {
        public static TunedStats Compute(CarSpec spec, TuningData t)
        {
            var grip = TuningCatalog.TireGrip[Mathf.Clamp((int)t.tires, 0, TuningCatalog.TireGrip.Length - 1)];
            var s = new TunedStats
            {
                powerHp = spec.powerHp * (1f + 0.06f * t.ecu + 0.12f * t.turbo + 0.04f * t.exhaust),
                torqueNm = spec.torqueNm * (1f + 0.05f * t.ecu + 0.15f * t.turbo + 0.03f * t.exhaust),
                massKg = spec.massKg * (1f - 0.03f * t.weightReduction),
                finalDriveMul = t.adjustableGearbox ? t.finalDriveMul : 1f,
                brakeMul = 1f + 0.15f * t.brakes,
                brakeBias = t.brakes > 0 ? t.brakeBias : 0.6f,
                gripForward = grip.x,
                gripSide = grip.y,
                rearSideMul = grip.z,
                downforceMul = 1f + 0.8f * t.spoiler,
                dragMul = 1f + 0.04f * t.spoiler,
                wheelRadius = spec.wheelRadiusM + t.wheelSize,
                springMul = t.coilovers ? t.springStiffness : 1f,
                damperMul = t.coilovers ? t.damperStiffness : 1f,
                rideHeight = t.coilovers ? t.rideHeight : 0f,
                trackOffset = t.coilovers ? t.trackOffset : 0f,
                camber = t.coilovers ? t.camber : 0f,
            };

            // top speed: limited either by power vs air drag, or by top gear at redline
            float k = CarMath.DragCoefficient(spec.powerHp, spec.topSpeedKmh) * s.dragMul;
            float p = s.powerHp * CarMath.HpToWatt * CarMath.DrivetrainEfficiency;
            float dragTop = Mathf.Pow(p / k, 1f / 3f) * 3.6f;
            float gearTop = spec.topSpeedKmh * 1.05f / s.finalDriveMul * (s.wheelRadius / spec.wheelRadiusM);
            s.topSpeedKmh = Mathf.Min(dragTop, gearTop);
            s.hpPerTon = s.powerHp / (s.massKg / 1000f);
            return s;
        }
    }
}
