using System.Collections.Generic;
using NUnit.Framework;
using RaceGame.Cars;
using RaceGame.Data;

namespace RaceGame.Tests
{
    public class CatalogTests
    {
        [Test]
        public void Catalog_LoadsCarsForEveryBrand()
        {
            var cars = CarCatalog.Load();
            Assert.That(cars.Count, Is.GreaterThan(20));

            var brands = new HashSet<string>();
            foreach (var c in cars) brands.Add(c.brand);
            foreach (var b in new[] { "Changan", "Lada", "Mercedes-Benz", "BMW", "Lexus", "Lamborghini", "Audi" })
                Assert.That(brands, Does.Contain(b), b);
        }

        [Test]
        public void Catalog_IdsUniqueAndSpecsValid()
        {
            var ids = new HashSet<string>();
            foreach (var c in CarCatalog.Load())
            {
                Assert.That(ids.Add(c.id), Is.True, "duplicate id " + c.id);
                Assert.That(c.massKg, Is.InRange(600, 4000), c.id);
                Assert.That(c.powerHp, Is.InRange(40, 2000), c.id);
                Assert.That(c.wheelbaseMm, Is.LessThan(c.lengthMm), c.id);
                Assert.That(c.wheelRadiusM, Is.GreaterThan(0.2f), c.id);
            }
        }

        [Test]
        public void Parse_FillsDefaults()
        {
            var cars = CarCatalog.Parse("{\"cars\":[{\"brand\":\"Lada\",\"model\":\"Test\"}]}");
            Assert.That(cars.Count, Is.EqualTo(1));
            Assert.That(cars[0].id, Is.EqualTo("lada_test"));
            Assert.That(cars[0].gears, Is.GreaterThan(0));
            Assert.That(cars[0].Body, Is.EqualTo(BodyType.Sedan));
        }
    }

    public class CarMathTests
    {
        [Test]
        public void GearRatios_Decrease()
        {
            var r = CarMath.GearRatios(8);
            Assert.That(r.Length, Is.EqualTo(8));
            for (int i = 1; i < r.Length; i++) Assert.That(r[i], Is.LessThan(r[i - 1]));
        }

        [Test]
        public void TopGearAtRedline_ReachesTopSpeed()
        {
            var r = CarMath.GearRatios(7);
            float fd = CarMath.FinalDrive(6500, 0.33f, r[6], 210);
            float wheelRadPerSec = 6500f / 60f * 2f * UnityEngine.Mathf.PI / (r[6] * fd);
            float kmh = wheelRadPerSec * 0.33f * 3.6f;
            Assert.That(kmh, Is.InRange(210f, 230f));
        }

        [Test]
        public void EngineTorque_LimitedByPowerAndRedline()
        {
            // 188 hp, 300 N·m: at 6000 rpm torque must not exceed power limit
            float t = CarMath.EngineTorque(6000, 300, 188, 6500);
            float powerHp = t * 6000f / 60f * 2f * UnityEngine.Mathf.PI / CarMath.HpToWatt;
            Assert.That(powerHp, Is.LessThanOrEqualTo(188.5f));
            Assert.That(CarMath.EngineTorque(6600, 300, 188, 6500), Is.EqualTo(0f));
            Assert.That(CarMath.EngineTorque(3000, 300, 188, 6500), Is.EqualTo(300f).Within(0.01f));
        }
    }
}
