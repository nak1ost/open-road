using System;
using System.Collections.Generic;
using System.IO;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.Save
{
    [Serializable]
    public class CarSave
    {
        public string carId;
        public TuningData tuning;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 2;
        public string selectedCarId;
        public string playerName = "Игрок";
        public int mapSeed = 20261115;
        public List<CarSave> cars = new List<CarSave>();
    }

    /// <summary>Player progress in persistentDataPath/save.json.</summary>
    public static class SaveSystem
    {
        static SaveData data;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static SaveData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static void Load()
        {
            data = null;
            try
            {
                if (File.Exists(FilePath)) data = FromJson(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Could not read save, starting fresh: " + e.Message);
            }
            if (data == null) data = new SaveData();
        }

        public static void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, ToJson(Data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Save] Could not write save: " + e.Message);
            }
        }

        public static string ToJson(SaveData d) => JsonUtility.ToJson(d, true);

        public static SaveData FromJson(string json)
        {
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null) return null;
            if (d.cars == null) d.cars = new List<CarSave>();
            foreach (var c in d.cars) c.tuning?.Clamp();
            return d;
        }

        /// <summary>Saved tuning of a car, or a fresh default one (not stored until SetTuning).</summary>
        public static TuningData GetTuning(string carId, Color defaultPaint)
        {
            foreach (var c in Data.cars)
                if (c.carId == carId && c.tuning != null) return c.tuning.Clone();
            return TuningData.Default(defaultPaint);
        }

        public static void SetTuning(string carId, TuningData tuning)
        {
            foreach (var c in Data.cars)
            {
                if (c.carId != carId) continue;
                c.tuning = tuning.Clone();
                return;
            }
            Data.cars.Add(new CarSave { carId = carId, tuning = tuning.Clone() });
        }

        /// <summary>Deletes progress (used by tests and the settings menu later).</summary>
        public static void ResetAll()
        {
            data = new SaveData();
            Save();
        }
    }
}
