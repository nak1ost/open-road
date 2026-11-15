using UnityEngine;

namespace RaceGame.Cars
{
    /// <summary>Pure drivetrain math (no Unity objects), covered by EditMode tests.</summary>
    public static class CarMath
    {
        public const float HpToWatt = 745.7f;
        public const float DrivetrainEfficiency = 0.88f;
        public const float IdleRpm = 900f;
        public const float ReverseRatio = 3.4f;

        /// <summary>Geometric series of ratios from first to last gear.</summary>
        public static float[] GearRatios(int gearCount)
        {
            gearCount = Mathf.Clamp(gearCount, 1, 10);
            float first = gearCount >= 8 ? 4.7f : gearCount >= 6 ? 3.8f : 3.6f;
            float last = gearCount >= 8 ? 0.65f : gearCount >= 6 ? 0.75f : 0.82f;
            var ratios = new float[gearCount];
            if (gearCount == 1) { ratios[0] = 1f; return ratios; }
            float step = Mathf.Pow(last / first, 1f / (gearCount - 1));
            for (int i = 0; i < gearCount; i++) ratios[i] = first * Mathf.Pow(step, i);
            return ratios;
        }

        /// <summary>Final drive picked so that top gear at redline is slightly above top speed.</summary>
        public static float FinalDrive(float redlineRpm, float wheelRadius, float lastRatio, float topSpeedKmh)
        {
            float vTop = Mathf.Max(topSpeedKmh, 60f) / 3.6f * 1.05f;
            float wheelRadPerSec = vTop / wheelRadius;
            float engineRadPerSec = redlineRpm / 60f * 2f * Mathf.PI;
            return engineRadPerSec / (wheelRadPerSec * lastRatio);
        }

        /// <summary>Air-drag coefficient k (F = k·v²) so that power runs out exactly at top speed.</summary>
        public static float DragCoefficient(float powerHp, float topSpeedKmh)
        {
            float p = powerHp * HpToWatt * DrivetrainEfficiency;
            float v = Mathf.Max(topSpeedKmh, 60f) / 3.6f;
            return p / (v * v * v);
        }

        /// <summary>
        /// Engine torque (N·m): rises to the peak, then is limited by maximum power.
        /// </summary>
        public static float EngineTorque(float rpm, float peakTorque, float powerHp, float redlineRpm)
        {
            if (rpm >= redlineRpm) return 0f;
            rpm = Mathf.Max(rpm, IdleRpm);
            float t = Mathf.InverseLerp(IdleRpm, redlineRpm * 0.35f, rpm);
            float shaped = peakTorque * Mathf.Lerp(0.65f, 1f, t);
            float omega = rpm / 60f * 2f * Mathf.PI;
            float powerLimited = powerHp * HpToWatt / omega;
            return Mathf.Min(shaped, powerLimited);
        }
    }
}
