using NUnit.Framework;
using RaceGame.Data;
using RaceGame.Save;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.Tests
{
    public class TuningTests
    {
        static CarSpec Spec()
        {
            var s = new CarSpec { brand = "Changan", model = "UNI-V", powerHp = 188, torqueNm = 300, massKg = 1450, topSpeedKmh = 210, price = 30000 };
            s.Normalize();
            return s;
        }

        [Test]
        public void StockTuning_KeepsStockNumbers()
        {
            var spec = Spec();
            var s = TuningStats.Compute(spec, new TuningData());
            Assert.That(s.powerHp, Is.EqualTo(188f).Within(0.01f));
            Assert.That(s.massKg, Is.EqualTo(1450f).Within(0.01f));
            Assert.That(s.topSpeedKmh, Is.EqualTo(210f).Within(1f));
        }

        [Test]
        public void EngineUpgrades_AddPowerAndTopSpeed()
        {
            var spec = Spec();
            var t = new TuningData { ecu = 3, turbo = 3, exhaust = 2, weightReduction = 3 };
            var s = TuningStats.Compute(spec, t);
            Assert.That(s.powerHp, Is.GreaterThan(188f * 1.5f));
            Assert.That(s.massKg, Is.LessThan(1450f));
            Assert.That(s.topSpeedKmh, Is.GreaterThan(210f));
        }

        [Test]
        public void SuspensionSliders_IgnoredWithoutKit()
        {
            var t = new TuningData { rideHeight = -0.08f, camber = -5f };
            var s = TuningStats.Compute(Spec(), t);
            Assert.That(s.rideHeight, Is.EqualTo(0f));
            Assert.That(s.camber, Is.EqualTo(0f));
            t.coilovers = true;
            s = TuningStats.Compute(Spec(), t);
            Assert.That(s.rideHeight, Is.EqualTo(-0.08f).Within(0.0001f));
        }

        [Test]
        public void Clamp_FixesBrokenValues()
        {
            var t = new TuningData { tintFront = 1f, turbo = 99, rideHeight = 5f, finalDriveMul = 3f };
            t.Clamp();
            Assert.That(t.tintFront, Is.EqualTo(TuningCatalog.MaxTintFront));
            Assert.That(t.turbo, Is.EqualTo(3));
            Assert.That(t.rideHeight, Is.EqualTo(TuningCatalog.RideHeightMax));
            Assert.That(t.finalDriveMul, Is.EqualTo(1.15f));
        }

        [Test]
        public void Save_RoundTripKeepsTuning()
        {
            var data = new SaveData { playerName = "Тест", selectedCarId = "changan_uni_v" };
            data.cars.Add(new CarSave
            {
                carId = "changan_uni_v",
                tuning = new TuningData { paint = Color.green, finish = PaintFinish.Matte, tintSide = 0.8f, turbo = 2, neon = true },
            });
            var back = SaveSystem.FromJson(SaveSystem.ToJson(data));
            Assert.That(back.playerName, Is.EqualTo("Тест"));
            Assert.That(back.selectedCarId, Is.EqualTo("changan_uni_v"));
            var t = back.cars[0].tuning;
            Assert.That(t.paint, Is.EqualTo(Color.green));
            Assert.That(t.finish, Is.EqualTo(PaintFinish.Matte));
            Assert.That(t.tintSide, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(t.turbo, Is.EqualTo(2));
            Assert.That(t.neon, Is.True);
        }
    }
}
