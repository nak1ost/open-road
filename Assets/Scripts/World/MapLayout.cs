using System.Collections.Generic;
using UnityEngine;

namespace RaceGame.World
{
    public class Town
    {
        public string name;
        public Vector2 center;
        public float radius = 260f;
        public float height;
        public const float StreetStep = 120f;
        public const int StreetHalfCount = 2; // streets at -2..+2 steps
        public float HalfExtent => StreetStep * StreetHalfCount;
    }

    public class Road
    {
        public string name;
        public bool highway;
        public float halfWidth;
        public readonly List<Vector3> points = new List<Vector3>(); // x, height, z
        public float yOffset => highway ? 0.06f : 0.09f;
    }

    public struct Building
    {
        public Vector3 position; // base centre
        public Vector3 size;
        public int color;
    }

    public class RouteData
    {
        public string id;
        public string name;
        public bool circuit;
        public int laps = 1;
        public readonly List<Vector3> points = new List<Vector3>();
        public float Length
        {
            get
            {
                float l = 0f;
                for (int i = 1; i < points.Count; i++) l += Vector3.Distance(points[i - 1], points[i]);
                if (circuit && points.Count > 1) l += Vector3.Distance(points[points.Count - 1], points[0]);
                return l;
            }
        }
    }

    /// <summary>
    /// Deterministic description of the whole 8×8 km island: relief, towns, roads, buildings, race routes.
    /// The same seed gives the same map on every device (needed for LAN play).
    /// The map is centred on the origin: x and z go from -4000 to 4000.
    /// </summary>
    public class MapLayout
    {
        public const float Size = 8000f;
        public const float Half = Size * 0.5f;
        public const float SeaLevel = 8f;
        public const float MinY = -30f;
        public const float HeightRange = 280f;
        public const float SampleStep = 8f; // road sample spacing, m

        public readonly int seed;
        public readonly List<Town> towns = new List<Town>();
        public readonly List<Road> roads = new List<Road>();
        public readonly List<Building> buildings = new List<Building>();
        public readonly List<RouteData> routes = new List<RouteData>();

        public Vector3 SpawnPoint { get; private set; }
        public Vector3 SpawnForward { get; private set; }

        readonly float ox, oz;
        readonly SegmentGrid grid = new SegmentGrid(50f);

        static readonly (string name, float x, float z)[] TownSpots =
        {
            ("Приморск", -0.62f, -0.58f),
            ("Лесное", -0.08f, -0.40f),
            ("Солнечный", 0.48f, -0.64f),
            ("Восток", 0.66f, -0.12f),
            ("Заречье", 0.58f, 0.48f),
            ("Каменка", -0.02f, 0.72f),
            ("Северск", -0.52f, 0.60f),
            ("Центральный", -0.30f, 0.08f),
        };

        public MapLayout(int seed)
        {
            this.seed = seed;
            var rng = new System.Random(seed);
            ox = (float)rng.NextDouble() * 1000f;
            oz = (float)rng.NextDouble() * 1000f;

            BuildTowns();
            BuildRoads(rng);
            foreach (var r in roads) grid.Add(r);
            BuildBuildings(rng);
            BuildRoutes();

            var start = towns[0];
            SpawnPoint = new Vector3(start.center.x, start.height + 0.6f, start.center.y - 20f);
            SpawnForward = Vector3.forward;
        }

        // ------------------------------------------------------------------ relief

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        float Perlin(float x, float z) => Mathf.PerlinNoise(x + ox, z + oz);

        float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x * freq, z * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>X coordinate of the river at a given z.</summary>
        public static float RiverX(float z) => 900f + 450f * Mathf.Sin(z * 0.0011f + 1.3f) + 150f * Mathf.Sin(z * 0.003f);

        /// <summary>Natural relief without towns and roads.</summary>
        public float BaseHeight(float x, float z)
        {
            float nx = x / Half, nz = z / Half;
            float edge = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(nz));
            float coast = (Perlin(x * 0.0008f, z * 0.0008f) - 0.5f) * 0.18f;
            float land = 1f - Smooth(0.80f, 0.96f, edge + coast);

            float h = 14f + Fbm(x * 0.0007f, z * 0.0007f, 4) * 55f;

            // mountains in the north-west
            float mountainMask = Smooth(0.15f, 0.65f, (-nx + nz) * 0.5f + 0.15f);
            float ridge = 1f - Mathf.Abs(Fbm(x * 0.0011f + 50f, z * 0.0011f + 50f, 3) * 2f - 1f);
            h += ridge * ridge * 130f * mountainMask;

            // river
            float dr = Mathf.Abs(x - RiverX(z));
            float river = 1f - Smooth(25f, 90f, dr);
            h = Mathf.Lerp(h, SeaLevel - 4f, river);

            return Mathf.Lerp(MinY + 5f, h, land);
        }

        /// <summary>Final ground height: relief flattened under towns and roads.</summary>
        public float Height(float x, float z)
        {
            float h = BaseHeight(x, z);
            foreach (var t in towns)
            {
                float d = Vector2.Distance(new Vector2(x, z), t.center);
                if (d > t.radius + 260f) continue;
                float w = 1f - Smooth(t.radius, t.radius + 260f, d);
                h = Mathf.Lerp(h, t.height - 0.1f, w);
            }
            if (grid.Nearest(x, z, 40f, out float dist, out float roadH, out float halfWidth))
            {
                float w = 1f - Smooth(halfWidth + 3f, halfWidth + 30f, dist);
                h = Mathf.Lerp(h, roadH - 0.08f, w);
            }
            return h;
        }

        public bool IsLand(float x, float z) => Height(x, z) > SeaLevel + 0.8f;

        public bool NearRoad(float x, float z, float distance) =>
            grid.Nearest(x, z, distance, out _, out _, out _);

        public Town TownAt(float x, float z, float margin = 0f)
        {
            foreach (var t in towns)
                if (Vector2.Distance(new Vector2(x, z), t.center) < t.radius + margin) return t;
            return null;
        }

        // ------------------------------------------------------------------ towns

        void BuildTowns()
        {
            foreach (var spot in TownSpots)
            {
                var c = new Vector2(spot.x * Half, spot.z * Half);
                var town = new Town { name = spot.name, center = c };
                town.height = Mathf.Max(SeaLevel + 3f, BaseHeight(c.x, c.y));
                towns.Add(town);

                // street grid
                float e = town.HalfExtent;
                for (int k = -Town.StreetHalfCount; k <= Town.StreetHalfCount; k++)
                {
                    float off = k * Town.StreetStep;
                    roads.Add(Straight($"{town.name}: улица {k + 3}", new Vector3(c.x + off, town.height, c.y - e), new Vector3(c.x + off, town.height, c.y + e)));
                    roads.Add(Straight($"{town.name}: проспект {k + 3}", new Vector3(c.x - e, town.height, c.y + off), new Vector3(c.x + e, town.height, c.y + off)));
                }
            }
        }

        static Road Straight(string name, Vector3 a, Vector3 b)
        {
            var r = new Road { name = name, highway = false, halfWidth = 5.5f };
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep));
            for (int i = 0; i <= n; i++) r.points.Add(Vector3.Lerp(a, b, i / (float)n));
            return r;
        }

        // ------------------------------------------------------------------ highways

        void BuildRoads(System.Random rng)
        {
            // ring through the outer towns (indices 0..6), plus roads from the central town
            for (int i = 0; i < 7; i++)
                roads.Add(Highway(towns[i], towns[(i + 1) % 7], rng));
            roads.Add(Highway(towns[7], towns[0], rng));
            roads.Add(Highway(towns[7], towns[6], rng));
            roads.Add(Highway(towns[7], towns[1], rng));
            roads.Add(Highway(towns[7], towns[4], rng));
        }

        Road Highway(Town a, Town b, System.Random rng)
        {
            var road = new Road { name = $"Трасса {a.name} — {b.name}", highway = true, halfWidth = 7f };
            Vector2 p0 = a.center, p3 = b.center;
            Vector2 dir = (p3 - p0).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float len = Vector2.Distance(p0, p3);
            // control points: leave each town straight along the main street, bend in between
            var ctrl = new List<Vector2>
            {
                p0,
                p0 + dir * Mathf.Min(300f, len * 0.15f),
                Vector2.Lerp(p0, p3, 0.38f) + perp * ((float)rng.NextDouble() - 0.5f) * len * 0.3f,
                Vector2.Lerp(p0, p3, 0.68f) + perp * ((float)rng.NextDouble() - 0.5f) * len * 0.3f,
                p3 - dir * Mathf.Min(300f, len * 0.15f),
                p3,
            };

            // Catmull-Rom sampling every SampleStep metres
            var flat = new List<Vector2>();
            for (int s = 0; s < ctrl.Count - 1; s++)
            {
                Vector2 c0 = ctrl[Mathf.Max(0, s - 1)], c1 = ctrl[s], c2 = ctrl[s + 1], c3 = ctrl[Mathf.Min(ctrl.Count - 1, s + 2)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(c1, c2) / SampleStep));
                for (int i = 0; i < n; i++) flat.Add(CatmullRom(c0, c1, c2, c3, i / (float)n));
            }
            flat.Add(p3);

            // height profile: follow relief, never below water, smooth, limit grade, exact town height inside towns
            int count = flat.Count;
            var h = new float[count];
            for (int i = 0; i < count; i++) h[i] = Mathf.Max(SeaLevel + 2.5f, BaseHeight(flat[i].x, flat[i].y));
            for (int pass = 0; pass < 3; pass++) h = MovingAverage(h, 12);
            const float maxGrade = 0.09f;
            for (int i = 1; i < count; i++) h[i] = Mathf.Clamp(h[i], h[i - 1] - maxGrade * SampleStep, h[i - 1] + maxGrade * SampleStep);
            for (int i = count - 2; i >= 0; i--) h[i] = Mathf.Clamp(h[i], h[i + 1] - maxGrade * SampleStep, h[i + 1] + maxGrade * SampleStep);
            var pinned = new bool[count];
            for (int i = 0; i < count; i++)
            {
                foreach (var t in new[] { a, b })
                {
                    float d = Vector2.Distance(flat[i], t.center);
                    float w = 1f - Smooth(t.radius - 20f, t.radius + 180f, d);
                    h[i] = Mathf.Lerp(h[i], t.height, w);
                    if (w > 0.999f) pinned[i] = true; // inside the town: exactly the street height
                }
            }
            // blending into a town on a hill may steepen the road: limit the grade again, keeping town points fixed
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < count; i++)
                    if (!pinned[i]) h[i] = Mathf.Clamp(h[i], h[i - 1] - maxGrade * SampleStep, h[i - 1] + maxGrade * SampleStep);
                for (int i = count - 2; i >= 0; i--)
                    if (!pinned[i]) h[i] = Mathf.Clamp(h[i], h[i + 1] - maxGrade * SampleStep, h[i + 1] + maxGrade * SampleStep);
            }
            for (int i = 0; i < count; i++) road.points.Add(new Vector3(flat[i].x, Mathf.Max(h[i], SeaLevel + 1f), flat[i].y));
            return road;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static float[] MovingAverage(float[] src, int radius)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float sum = 0f; int n = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(src.Length - 1, i + radius); k++) { sum += src[k]; n++; }
                dst[i] = sum / n;
            }
            return dst;
        }

        // ------------------------------------------------------------------ buildings

        void BuildBuildings(System.Random rng)
        {
            foreach (var t in towns)
            {
                float e = t.HalfExtent;
                int blocks = Town.StreetHalfCount * 2;
                for (int bx = 0; bx < blocks; bx++)
                for (int bz = 0; bz < blocks; bz++)
                {
                    float x0 = t.center.x - e + bx * Town.StreetStep + 12f;
                    float z0 = t.center.y - e + bz * Town.StreetStep + 12f;
                    float inner = Town.StreetStep - 24f; // 96 m of buildable space
                    // 2×2 plots per block, some left empty as parking/park
                    for (int px = 0; px < 2; px++)
                    for (int pz = 0; pz < 2; pz++)
                    {
                        if (rng.NextDouble() < 0.2) continue;
                        float plot = inner * 0.5f;
                        float w = plot * (0.55f + (float)rng.NextDouble() * 0.35f);
                        float d = plot * (0.55f + (float)rng.NextDouble() * 0.35f);
                        bool centre = Mathf.Abs(bx - blocks / 2 + 0.5f) < 1f && Mathf.Abs(bz - blocks / 2 + 0.5f) < 1f;
                        float hgt = centre ? 15f + (float)rng.NextDouble() * 35f : 6f + (float)rng.NextDouble() * 14f;
                        buildings.Add(new Building
                        {
                            position = new Vector3(x0 + plot * (px + 0.5f), t.height, z0 + plot * (pz + 0.5f)),
                            size = new Vector3(w, hgt, d),
                            color = rng.Next(0, 6),
                        });
                    }
                }
            }
        }

        // ------------------------------------------------------------------ race routes

        void BuildRoutes()
        {
            // circuits around each town along the outer streets
            foreach (var t in towns)
            {
                var r = new RouteData { id = "circuit_" + towns.IndexOf(t), name = $"Кольцо: {t.name}", circuit = true, laps = 3 };
                float e = t.HalfExtent;
                var c = new Vector3(t.center.x, t.height, t.center.y);
                AddLine(r.points, c + new Vector3(-e, 0, -e), c + new Vector3(e, 0, -e));
                AddLine(r.points, c + new Vector3(e, 0, -e), c + new Vector3(e, 0, e));
                AddLine(r.points, c + new Vector3(e, 0, e), c + new Vector3(-e, 0, e));
                AddLine(r.points, c + new Vector3(-e, 0, e), c + new Vector3(-e, 0, -e));
                r.points.RemoveAt(r.points.Count - 1); // circuit: last point = first
                routes.Add(r);
            }

            // sprints along every highway
            int n = 0;
            foreach (var road in roads)
            {
                if (!road.highway) continue;
                var r = new RouteData { id = "sprint_" + n++, name = "Спринт: " + road.name.Replace("Трасса ", ""), circuit = false };
                r.points.AddRange(road.points);
                routes.Add(r);
            }

            // grand tour: the whole outer ring
            var ring = new RouteData { id = "grand_ring", name = "Большое кольцо острова", circuit = true, laps = 1 };
            for (int i = 0; i < 7; i++)
            {
                var pts = roads.Find(rd => rd.highway && rd.name == $"Трасса {towns[i].name} — {towns[(i + 1) % 7].name}").points;
                for (int k = 0; k < pts.Count - 1; k++) ring.points.Add(pts[k]);
            }
            routes.Add(ring);
        }

        static void AddLine(List<Vector3> list, Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep));
            for (int i = 0; i < n; i++) list.Add(Vector3.Lerp(a, b, i / (float)n));
            list.Add(b);
        }

        public RouteData FindRoute(string id) => routes.Find(r => r.id == id);
    }

    /// <summary>Spatial hash of road segments for fast "nearest road" queries.</summary>
    public class SegmentGrid
    {
        struct Seg { public Vector3 a, b; public float halfWidth; }

        readonly float cell;
        readonly Dictionary<long, List<Seg>> cells = new Dictionary<long, List<Seg>>();

        public SegmentGrid(float cellSize) { cell = cellSize; }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Road road)
        {
            const float pad = 40f;
            for (int i = 0; i < road.points.Count - 1; i++)
            {
                var s = new Seg { a = road.points[i], b = road.points[i + 1], halfWidth = road.halfWidth };
                int x0 = Mathf.FloorToInt((Mathf.Min(s.a.x, s.b.x) - pad) / cell), x1 = Mathf.FloorToInt((Mathf.Max(s.a.x, s.b.x) + pad) / cell);
                int z0 = Mathf.FloorToInt((Mathf.Min(s.a.z, s.b.z) - pad) / cell), z1 = Mathf.FloorToInt((Mathf.Max(s.a.z, s.b.z) + pad) / cell);
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = Key(x, z);
                    if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Seg>();
                    list.Add(s);
                }
            }
        }

        /// <summary>Nearest road segment within maxDist: distance to its centre line, its height there, its half width.</summary>
        public bool Nearest(float x, float z, float maxDist, out float dist, out float height, out float halfWidth)
        {
            dist = float.MaxValue; height = 0f; halfWidth = 0f;
            if (!cells.TryGetValue(Key(Mathf.FloorToInt(x / cell), Mathf.FloorToInt(z / cell)), out var list)) return false;
            bool found = false;
            foreach (var s in list)
            {
                float abx = s.b.x - s.a.x, abz = s.b.z - s.a.z;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 0f ? Mathf.Clamp01(((x - s.a.x) * abx + (z - s.a.z) * abz) / len2) : 0f;
                float px = s.a.x + abx * t, pz = s.a.z + abz * t;
                float d = Mathf.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                // prefer the road whose surface reaches us (distance minus width)
                if (d - s.halfWidth < dist - halfWidth && d <= maxDist)
                {
                    dist = d; halfWidth = s.halfWidth;
                    height = Mathf.Lerp(s.a.y, s.b.y, t);
                    found = true;
                }
            }
            return found;
        }
    }
}
