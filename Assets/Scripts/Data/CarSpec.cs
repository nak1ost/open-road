using System;
using UnityEngine;

namespace RaceGame.Data
{
    public enum BodyType { Sedan, Hatchback, Wagon, Crossover, Suv, Offroad, Coupe, Supercar, Pickup }
    public enum DriveType { FWD, RWD, AWD }

    /// <summary>
    /// Characteristics of one car model. Loaded from Resources/Data/cars.json.
    /// Figures are approximate and serve as a base for physics and tuning.
    /// </summary>
    [Serializable]
    public class CarSpec
    {
        public string id;
        public string brand;
        public string model;
        public string years;
        public string body = "Sedan";      // see BodyType
        public string drive = "FWD";       // FWD / RWD / AWD
        public int lengthMm;
        public int widthMm;
        public int heightMm;
        public int wheelbaseMm;
        public int massKg;
        public int powerHp;
        public int torqueNm;
        public int redlineRpm;
        public int gears;
        public int topSpeedKmh;
        public int price;
        public float wheelRadiusM;         // 0 = by body type

        public string DisplayName => $"{brand} {model}";

        public BodyType Body =>
            Enum.TryParse(body, true, out BodyType b) ? b : BodyType.Sedan;

        public DriveType Drive =>
            Enum.TryParse(drive, true, out DriveType d) ? d : DriveType.FWD;

        public float LengthM => lengthMm / 1000f;
        public float WidthM => widthMm / 1000f;
        public float HeightM => heightMm / 1000f;
        public float WheelbaseM => wheelbaseMm / 1000f;

        /// <summary>Fills missing values with sensible defaults.</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(id)) id = $"{brand}_{model}".ToLowerInvariant().Replace(' ', '_');
            if (lengthMm <= 0) lengthMm = 4500;
            if (widthMm <= 0) widthMm = 1800;
            if (heightMm <= 0) heightMm = 1450;
            if (wheelbaseMm <= 0) wheelbaseMm = Mathf.RoundToInt(lengthMm * 0.6f);
            if (massKg <= 0) massKg = 1500;
            if (powerHp <= 0) powerHp = 150;
            if (torqueNm <= 0) torqueNm = Mathf.RoundToInt(powerHp * 1.5f);
            if (redlineRpm <= 0) redlineRpm = 6500;
            if (gears <= 0) gears = 6;
            if (topSpeedKmh <= 0) topSpeedKmh = Mathf.RoundToInt(120 + powerHp * 0.35f);
            if (wheelRadiusM <= 0f) wheelRadiusM = DefaultWheelRadius(Body);
        }

        public static float DefaultWheelRadius(BodyType body)
        {
            switch (body)
            {
                case BodyType.Hatchback: return 0.31f;
                case BodyType.Crossover: return 0.36f;
                case BodyType.Suv: return 0.38f;
                case BodyType.Offroad: return 0.40f;
                case BodyType.Pickup: return 0.39f;
                case BodyType.Supercar: return 0.35f;
                case BodyType.Coupe: return 0.34f;
                default: return 0.33f;
            }
        }
    }

    [Serializable]
    public class CarCatalogFile
    {
        public bool useRealBrandNames = true;
        public CarSpec[] cars;
    }
}
