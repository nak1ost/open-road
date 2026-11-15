using System.IO;
using NUnit.Framework;
using RaceGame.Net;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.Tests
{
    public class WorldTests
    {
        const int Seed = 20261115;

        static void AssertHighwaysOk(MapLayout m)
        {
            foreach (var r in m.roads)
            {
                if (!r.highway) continue;
                for (int i = 1; i < r.points.Count; i++)
                {
                    Assert.That(r.points[i].y, Is.GreaterThan(MapLayout.SeaLevel), r.name);
                    float run = Vector2.Distance(new Vector2(r.points[i].x, r.points[i].z), new Vector2(r.points[i - 1].x, r.points[i - 1].z));
                    if (run > 0.5f) Assert.That(Mathf.Abs(r.points[i].y - r.points[i - 1].y) / run, Is.LessThan(0.2f), r.name);
                }
            }
        }

        [Test]
        public void StartArea_HasTownsRoadsAndRoutes()
        {
            var m = new MapLayout(Seed);
            Assert.That(m.towns.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(m.roads.FindAll(r => r.highway).Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(m.buildings.Count, Is.GreaterThan(30));
            var routes = m.RoutesNear(0f, 0f);
            Assert.That(routes.Count, Is.GreaterThanOrEqualTo(3));
            foreach (var r in routes) Assert.That(r.Length, Is.GreaterThan(800f), r.name);
        }

        [Test]
        public void World_IsEndless()
        {
            var m = new MapLayout(Seed);
            // 100 km and 1000 km away there are still towns and roads
            foreach (var far in new[] { new Vector2(100000f, -100000f), new Vector2(-1000000f, 700000f) })
            {
                m.EnsureArea(far.x, far.y);
                bool townNear = m.towns.Exists(t => Vector2.Distance(t.center, far) < 6000f);
                Assert.That(townNear, Is.True, $"no town near {far}");
                Assert.That(float.IsNaN(m.Height(far.x, far.y)), Is.False);
            }
        }

        [Test]
        public void World_DoesNotDependOnVisitOrder()
        {
            // player A goes east first, player B goes north first — the same place must look the same
            var a = new MapLayout(Seed);
            var b = new MapLayout(Seed);
            a.EnsureArea(20000f, 0f); a.EnsureArea(20000f, 20000f);
            b.EnsureArea(0f, 20000f); b.EnsureArea(20000f, 20000f);
            for (int i = 0; i < 50; i++)
            {
                float x = 17000f + i * 131f, z = 18000f + i * 97f;
                Assert.That(a.Height(x, z), Is.EqualTo(b.Height(x, z)).Within(1e-4f), $"at {x},{z}");
            }
            var ra = a.FindRoute("circuit_6_6");
            var rb = b.FindRoute("circuit_6_6");
            Assert.That(ra == null, Is.EqualTo(rb == null));
        }

        [Test]
        public void RouteIds_RoundTrip()
        {
            var m = new MapLayout(Seed);
            foreach (var r in m.RoutesNear(0f, 0f))
            {
                var again = new MapLayout(Seed).FindRoute(r.id);
                Assert.That(again, Is.Not.Null, r.id);
                Assert.That(again.points.Count, Is.EqualTo(r.points.Count), r.id);
                Assert.That(again.points[again.points.Count / 2], Is.EqualTo(r.points[r.points.Count / 2]), r.id);
            }
        }

        [Test]
        public void DifferentSeeds_GiveDifferentWorlds()
        {
            var a = new MapLayout(1);
            var b = new MapLayout(987654321);
            Assert.That(a.BaseHeight(500f, 500f), Is.Not.EqualTo(b.BaseHeight(500f, 500f)));
        }

        [TestCase(1)]
        [TestCase(42)]
        [TestCase(777777)]
        [TestCase(999999999)]
        public void AnySeed_GivesPlayableStart(int seed)
        {
            var m = new MapLayout(seed);
            var s = m.SpawnPoint;
            Assert.That(m.Height(s.x, s.z), Is.GreaterThan(MapLayout.SeaLevel), "spawn on land");
            AssertHighwaysOk(m);
        }

        [Test]
        public void FarHighways_AreDrivable()
        {
            var m = new MapLayout(Seed);
            m.EnsureArea(50000f, 50000f);
            AssertHighwaysOk(m);
        }
    }

    public class NetTests
    {
        [Test]
        public void Frame_RoundTrip()
        {
            var info = new PlayerInfo { id = 3, name = "Тест", carId = "bmw_m3", tuningJson = "{\"turbo\":2}" };
            var frame = LanProtocol.Frame(Msg.CarChanged, w => info.Write(w));
            using (var ms = new MemoryStream(frame))
            {
                Assert.That(LanProtocol.ReadFrame(ms, out var type, out var payload), Is.True);
                Assert.That(type, Is.EqualTo(Msg.CarChanged));
                using (var r = LanProtocol.Reader(payload))
                {
                    var back = PlayerInfo.Read(r);
                    Assert.That(back.id, Is.EqualTo(3));
                    Assert.That(back.name, Is.EqualTo("Тест"));
                    Assert.That(back.carId, Is.EqualTo("bmw_m3"));
                    Assert.That(back.tuningJson, Is.EqualTo("{\"turbo\":2}"));
                }
            }
        }

        [Test]
        public void CarState_RoundTrip()
        {
            var s = new CarState
            {
                playerId = 2, seq = 77, position = new Vector3(1, 2, 3),
                rotation = Quaternion.Euler(0, 90, 0), velocity = new Vector3(4, 5, 6), steer = -0.5f,
            };
            var bytes = s.ToBytes();
            Assert.That(bytes.Length, Is.EqualTo(CarState.Size));
            Assert.That(CarState.TryRead(bytes, bytes.Length, out var b), Is.True);
            Assert.That(b.playerId, Is.EqualTo(2));
            Assert.That(b.seq, Is.EqualTo(77u));
            Assert.That(b.position, Is.EqualTo(s.position));
            Assert.That(b.velocity, Is.EqualTo(s.velocity));
            Assert.That(b.steer, Is.EqualTo(-0.5f));
        }
    }
}
