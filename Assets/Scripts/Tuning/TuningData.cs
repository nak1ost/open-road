using System;
using UnityEngine;

namespace RaceGame.Tuning
{
    public enum PaintFinish { Gloss, Matte, Metallic, Pearl, Chrome }
    public enum TireType { Street, Sport, SemiSlick, Drift, Offroad }

    /// <summary>All tuning of one car. Stored in the save file per car id.</summary>
    [Serializable]
    public class TuningData
    {
        // --- paint ---
        public Color paint = new Color(0.75f, 0.05f, 0.05f);
        public PaintFinish finish = PaintFinish.Metallic;

        // --- tint, 0 = clear, 1 = black ---
        public float tintFront = 0.1f;
        public float tintSide = 0.2f;
        public float tintRear = 0.2f;

        // --- suspension (sliders work when the coilover kit is installed, free) ---
        public bool coilovers;
        public float rideHeight;          // m, minus = lowered
        public float springStiffness = 1f;
        public float damperStiffness = 1f;
        public float camber;              // degrees, minus = top of wheel inward
        public float trackOffset;         // m, wheels pushed out

        // --- wheels ---
        public int rimStyle;
        public Color rimColor = new Color(0.75f, 0.75f, 0.78f);
        public float wheelSize;           // m added to radius
        public TireType tires = TireType.Street;

        // --- engine (levels) ---
        public int ecu;                   // 0..3
        public int turbo;                 // 0..3
        public int exhaust;               // 0..2
        public int weightReduction;       // 0..3

        // --- transmission ---
        public bool adjustableGearbox;
        public float finalDriveMul = 1f;  // < 1 longer gears (top speed), > 1 shorter (acceleration)

        // --- brakes ---
        public int brakes;                // 0..3
        public float brakeBias = 0.6f;    // share of front brakes

        // --- exterior ---
        public bool neon;
        public Color neonColor = new Color(0f, 0.8f, 1f);
        public Color headlightColor = new Color(1f, 0.97f, 0.85f);
        public int spoiler;               // 0..2

        public TuningData Clone() => (TuningData)MemberwiseClone();

        public static TuningData Default(Color paint) => new TuningData { paint = paint };

        /// <summary>Clamps all values to the allowed ranges (protects against a broken save file).</summary>
        public void Clamp()
        {
            tintFront = Mathf.Clamp(tintFront, 0f, TuningCatalog.MaxTintFront);
            tintSide = Mathf.Clamp01(tintSide);
            tintRear = Mathf.Clamp01(tintRear);
            rideHeight = Mathf.Clamp(rideHeight, TuningCatalog.RideHeightMin, TuningCatalog.RideHeightMax);
            springStiffness = Mathf.Clamp(springStiffness, 0.6f, 1.6f);
            damperStiffness = Mathf.Clamp(damperStiffness, 0.6f, 1.6f);
            camber = Mathf.Clamp(camber, TuningCatalog.CamberMin, TuningCatalog.CamberMax);
            trackOffset = Mathf.Clamp(trackOffset, 0f, TuningCatalog.TrackMax);
            rimStyle = Mathf.Clamp(rimStyle, 0, TuningCatalog.RimStyleNames.Length - 1);
            wheelSize = Mathf.Clamp(wheelSize, 0f, TuningCatalog.WheelSizeMax);
            ecu = Mathf.Clamp(ecu, 0, TuningCatalog.MaxLevel(TuningCatalog.EcuNames));
            turbo = Mathf.Clamp(turbo, 0, TuningCatalog.MaxLevel(TuningCatalog.TurboNames));
            exhaust = Mathf.Clamp(exhaust, 0, TuningCatalog.MaxLevel(TuningCatalog.ExhaustNames));
            weightReduction = Mathf.Clamp(weightReduction, 0, TuningCatalog.MaxLevel(TuningCatalog.WeightNames));
            finalDriveMul = Mathf.Clamp(finalDriveMul, 0.85f, 1.15f);
            brakes = Mathf.Clamp(brakes, 0, TuningCatalog.MaxLevel(TuningCatalog.BrakeNames));
            brakeBias = Mathf.Clamp(brakeBias, 0.5f, 0.75f);
            spoiler = Mathf.Clamp(spoiler, 0, TuningCatalog.MaxLevel(TuningCatalog.SpoilerNames));
        }
    }
}
