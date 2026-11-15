using System.IO;
using NUnit.Framework;
using RaceGame.Net;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.Tests
{
    public class WorldTests
    {
        static MapLayout map;
        static MapLayout Map => map ??= new MapLayout(20261115);

        [Test]
        public void Map_HasTownsRoadsAndRoutes()
        {
            Assert.That(Map.towns.Count, Is.EqualTo(8));
            Assert.That(Map.roads.FindAll(r => r.highway).Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(Map.routes.Count, Is.GreaterThanOrEqualTo(15));
            Assert.That(Map.buildings.Count, Is.GreaterThan(200));
        }

        [Test]
        public void Map_IsDeterministic()
        {
            var other = new MapLayout(20261115);
            Assert.That(other.Height(123f, -456f), Is.EqualTo(Map.Height(123f, -456f)));
            Assert.That(other.roads[90].points[10], Is.EqualTo(Map.roads[90].points[10]));
        }

        [Test]
        public void Spawn_IsOnLand()
        {
            var s = Map.SpawnPoint;
            Assert.That(Map.Height(s.x, s.z), Is.GreaterThan(MapLayout.SeaLevel));
        }

        [Test]
        public void Highways_StayAboveWater_AndAreNotTooSteep()
        {
            foreach (var r in Map.roads)
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
        public void Map_IsEightByEightKilometres()
        {
            Assert.That(MapLayout.Size, Is.EqualTo(8000f));
            foreach (var r in Map.roads)
            foreach (var p in r.points)
            {
                Assert.That(Mathf.Abs(p.x), Is.LessThan(MapLayout.Half));
                Assert.That(Mathf.Abs(p.z), Is.LessThan(MapLayout.Half));
            }
        }

        [Test]
        public void Routes_AreLongEnough()
        {
            foreach (var r in Map.routes)
                Assert.That(r.Length, Is.GreaterThan(800f), r.name);
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
