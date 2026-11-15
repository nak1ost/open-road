using UnityEngine;

namespace RaceGame.Tuning
{
    /// <summary>Limits and names of all tuning parts. Everything is free.</summary>
    public static class TuningCatalog
    {
        public const float MaxTintFront = 0.7f;   // windscreen can't be fully black
        public const float RideHeightMin = -0.08f, RideHeightMax = 0.06f;
        public const float CamberMin = -8f, CamberMax = 2f;
        public const float TrackMax = 0.05f;
        public const float WheelSizeMax = 0.05f;

        public static readonly string[] FinishNames = { "Глянец", "Матовая", "Металлик", "Перламутр", "Хром" };

        public static readonly string[] TireNames = { "Городские", "Спортивные", "Полуслики", "Дрифт", "Внедорожные" };
        // forward grip, sideways grip, rear sideways multiplier
        public static readonly Vector3[] TireGrip =
        {
            new Vector3(1.00f, 1.00f, 1.00f),
            new Vector3(1.12f, 1.10f, 1.00f),
            new Vector3(1.25f, 1.22f, 1.00f),
            new Vector3(1.05f, 0.95f, 0.75f),
            new Vector3(0.95f, 0.95f, 1.00f),
        };

        public static readonly string[] RimStyleNames = { "Крест", "5 спиц", "6 спиц", "10 спиц", "Диск" };
        public static readonly int[] RimSpokes = { 4, 5, 6, 10, 0 };

        // level names; index 0 = stock, so max level = Length - 1
        public static readonly string[] EcuNames = { "Сток", "Stage 1", "Stage 2", "Stage 3" };
        public static readonly string[] TurboNames = { "Сток", "Малая турбина", "Большая турбина", "Твин-турбо" };
        public static readonly string[] ExhaustNames = { "Сток", "Спорт", "Прямоток" };
        public static readonly string[] WeightNames = { "Сток", "−3 %", "−6 %", "−9 %" };
        public static readonly string[] BrakeNames = { "Сток", "Спорт", "Гоночные", "Карбон-керамика" };
        public static readonly string[] SpoilerNames = { "Нет", "Лип-спойлер", "Антикрыло" };

        public static int MaxLevel(string[] names) => names.Length - 1;

        public static readonly Color[] Palette =
        {
            new Color(0.95f, 0.95f, 0.95f), new Color(0.04f, 0.04f, 0.05f), new Color(0.55f, 0.56f, 0.58f), new Color(0.25f, 0.26f, 0.28f),
            new Color(0.75f, 0.05f, 0.05f), new Color(0.45f, 0.02f, 0.06f), new Color(0.95f, 0.45f, 0.05f), new Color(0.95f, 0.8f, 0.05f),
            new Color(0.15f, 0.5f, 0.15f), new Color(0.35f, 0.75f, 0.1f), new Color(0.05f, 0.2f, 0.55f), new Color(0.1f, 0.55f, 0.85f),
            new Color(0.35f, 0.1f, 0.55f), new Color(0.85f, 0.3f, 0.6f), new Color(0.55f, 0.4f, 0.25f), new Color(0.85f, 0.78f, 0.62f),
        };
    }
}
