using System.Collections.Generic;
using UnityEngine;

namespace RaceGame.Data
{
    /// <summary>Loads the list of all cars from Resources/Data/cars.json.</summary>
    public static class CarCatalog
    {
        public const string ResourcePath = "Data/cars";

        static List<CarSpec> cars;

        public static IReadOnlyList<CarSpec> All
        {
            get
            {
                if (cars == null) cars = Load();
                return cars;
            }
        }

        public static void Reload() => cars = null;

        public static List<CarSpec> Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"Car catalog not found: Resources/{ResourcePath}.json");
                return new List<CarSpec>();
            }
            return Parse(asset.text);
        }

        public static List<CarSpec> Parse(string json)
        {
            var file = JsonUtility.FromJson<CarCatalogFile>(json);
            var result = new List<CarSpec>();
            if (file?.cars == null) return result;
            foreach (var c in file.cars)
            {
                if (c == null || string.IsNullOrEmpty(c.brand) || string.IsNullOrEmpty(c.model)) continue;
                c.Normalize();
                result.Add(c);
            }
            return result;
        }

        public static CarSpec Find(string id)
        {
            foreach (var c in All)
                if (c.id == id) return c;
            return null;
        }
    }
}
