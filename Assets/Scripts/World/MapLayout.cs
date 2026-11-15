using System.Collections.Generic;
using UnityEngine;

namespace RaceGame.World
{
    public class Town
    {
        public string name;
        public Vector2Int cell;
        public Vector2 center;
        public float radius = 260f;
        public float height;
        public const float StreetStep = 120f;
        public const int StreetHalfCount = 2; // streets at -2..+2 steps
        public float HalfExtent => StreetStep * StreetHalfCount;
    }

    public class Road
    {
        public string id;
        public string name;
        public bool highway;
        public float halfWidth;
        public readonly List<Vector3> points = new List<Vector3>(); // GLOBAL x, height, z
        public Rect bounds;                                         // x/z bounding box
        public float yOffset => highway ? 0.06f : 0.09f;

        public void ComputeBounds()
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var p in points) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            bounds = Rect.MinMaxRect(x0 - halfWidth, z0 - halfWidth, x1 + halfWidth, z1 + halfWidth);
        }
    }

    public struct Building
    {
        public Vector3 position; // GLOBAL base centre
        public Vector3 size;
        public int color;
    }

    public class RouteData
    {
        public string id;
        public string name;
        public bool circuit;
        public int laps = 1;
        public readonly List<Vector3> points = new List<Vector3>(); // GLOBAL
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
    /// Endless deterministic world, generated lazily in 3×3 km cells: relief, towns, highways, buildings, race routes.
    /// Same seed → same world on every device, no matter in which order cells are visited (needed for LAN play).
    /// All coordinates here are GLOBAL (see OriginShift for the local/global conversion).
    /// </summary>
    public class MapLayout
    {
        public const float CellSize = 3000f;
        public const float SeaLevel = 8f;
        public const float MinY = -30f;
        public const float HeightRange = 280f;
        public const float SampleStep = 8f; // road sample spacing, m
        const float TownChance = 0.75f;

        public readonly int seed;
        public readonly List<Town> towns = new List<Town>();         // generated so far
        public readonly List<Road> roads = new List<Road>();         // generated so far
        public readonly List<Building> buildings = new List<Building>();

        public Vector3 SpawnPoint { get; private set; }
        public Vector3 SpawnForward { get; private set; }

        readonly float ox, oz;
        readonly SegmentGrid grid = new SegmentGrid(50f);
        readonly Dictionary<Vector2Int, Town> townByCell = new Dictionary<Vector2Int, Town>();
        readonly HashSet<Vector2Int> townDone = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> roadsDone = new HashSet<Vector2Int>();
        readonly Dictionary<string, Road> roadById = new Dictionary<string, Road>();
        Vector2Int lastEnsuredCell = new Vector2Int(int.MinValue, int.MinValue);

        static readonly string[] Syllables =
        {
            "ка", "ли", "мо", "ре", "сан", "то", "ви", "но", "ра", "де", "лес", "гор", "бе", "ре", "за", "по",
            "ми", "сто", "ва", "ла", "кам", "ни", "ку", "яр", "ос", "тин", "дуб", "ров", "сол", "не",
        };
        static readonly string[] Endings = { "ск", "ово", "ино", "ка", "ье", "ный", "град", "поль", "овка", "ец" };

        public MapLayout(int seed)
        {
            this.seed = seed;
            uint h = Hash(seed, 7, 13);
            ox = (h % 100000) / 100f;
            oz = ((h / 100000) % 100000) / 100f;

            EnsureArea(0f, 0f);
            var start = townByCell[Vector2Int.zero];
            SpawnPoint = new Vector3(start.center.x, start.height + 0.6f, start.center.y - 20f);
            SpawnForward = Vector3.forward;
        }

        // ------------------------------------------------------------------ hashing

        /// <summary>Deterministic integer hash (System.HashCode is randomised per process, so it can't be used).</summary>
        public static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return h;
            }
        }

        static float Hash01(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

        public static Vector2Int CellOf(float x, float z) =>
            new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));

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

        /// <summary>Natural relief without towns and roads: plains, hills, mountain ranges, rivers and seas.</summary>
        public float BaseHeight(float x, float z)
        {
            // continents and seas
            float cont = Fbm(x * 0.00009f + 1000f, z * 0.00009f + 1000f, 3);
            float land = Smooth(0.36f, 0.44f, cont);

            float h = 14f + Fbm(x * 0.0007f, z * 0.0007f, 4) * 55f;

            // mountain ranges
            float mountainMask = Smooth(0.52f, 0.70f, Fbm(x * 0.00015f + 300f, z * 0.00015f + 300f, 2));
            float ridge = 1f - Mathf.Abs(Fbm(x * 0.0011f + 50f, z * 0.0011f + 50f, 3) * 2f - 1f);
            h += ridge * ridge * 130f * mountainMask;

            // meandering rivers
            float rv = Mathf.Abs(Fbm(x * 0.0004f + 700f, z * 0.0004f + 700f, 2) - 0.5f);
            float river = 1f - Smooth(0.005f, 0.018f, rv);
            h = Mathf.Lerp(h, SeaLevel - 4f, river);

            return Mathf.Lerp(MinY + 5f, h, land);
        }

        /// <summary>Final ground height: relief flattened under towns and roads.</summary>
        public float Height(float x, float z)
        {
            EnsureArea(x, z);
            float h = BaseHeight(x, z);
            var c = CellOf(x, z);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!townByCell.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var t)) continue;
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

        public bool NearRoad(float x, float z, float distance)
        {
            EnsureArea(x, z);
            return grid.Nearest(x, z, distance, out _, out _, out _);
        }

        public Town TownAt(float x, float z, float margin = 0f)
        {
            EnsureArea(x, z);
            var c = CellOf(x, z);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (townByCell.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var t) &&
                    Vector2.Distance(new Vector2(x, z), t.center) < t.radius + margin) return t;
            return null;
        }

        // ------------------------------------------------------------------ lazy generation

        /// <summary>
        /// Makes sure everything that can influence the point (x,z) exists: towns of the surrounding cells and
        /// all highways that start within two cells (a highway never leaves its two cells by more than ~1 km).
        /// </summary>
        public void EnsureArea(float x, float z)
        {
            var c = CellOf(x, z);
            if (c == lastEnsuredCell) return; // Height() is called ~60k times per tile, mostly in the same cell
            lastEnsuredCell = c;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                EnsureRoads(new Vector2Int(c.x + dx, c.y + dz));
        }

        Town EnsureTown(Vector2Int cell)
        {
            if (townDone.Contains(cell))
            {
                townByCell.TryGetValue(cell, out var existing);
                return existing;
            }
            townDone.Add(cell);

            uint h = Hash(seed, cell.x, cell.y);
            bool origin = cell == Vector2Int.zero;
            if (!origin && Hash01(h) > TownChance) return null;

            float jx = (Hash01(Hash((int)h, 1, 0)) - 0.5f) * 1400f;
            float jz = (Hash01(Hash((int)h, 2, 0)) - 0.5f) * 1400f;
            var c = new Vector2((cell.x + 0.5f) * CellSize + jx, (cell.y + 0.5f) * CellSize + jz);
            if (origin) c = Vector2.zero;
            float baseH = BaseHeight(c.x, c.y);
            if (!origin && baseH < SeaLevel + 1f) return null; // no towns in the sea

            var town = new Town { cell = cell, center = c, name = TownName(h), height = Mathf.Max(SeaLevel + 3f, baseH) };
            townByCell[cell] = town;
            towns.Add(town);

            float e = town.HalfExtent;
            for (int k = -Town.StreetHalfCount; k <= Town.StreetHalfCount; k++)
            {
                float off = k * Town.StreetStep;
                AddRoad(Straight($"t{cell.x}_{cell.y}_v{k}", $"{town.name}: улица {k + 3}",
                    new Vector3(c.x + off, town.height, c.y - e), new Vector3(c.x + off, town.height, c.y + e)));
                AddRoad(Straight($"t{cell.x}_{cell.y}_h{k}", $"{town.name}: проспект {k + 3}",
                    new Vector3(c.x - e, town.height, c.y + off), new Vector3(c.x + e, town.height, c.y + off)));
            }
            BuildBuildings(town, new System.Random((int)Hash((int)h, 3, 0)));
            return town;
        }

        static string TownName(uint h)
        {
            var rng = new System.Random((int)h);
            int parts = 1 + rng.Next(2);
            string s = "";
            for (int i = 0; i < parts; i++) s += Syllables[rng.Next(Syllables.Length)];
            s += Endings[rng.Next(Endings.Length)];
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        /// <summary>Highways that start in this cell: to the east and north neighbours (north-east if one of them is empty).</summary>
        void EnsureRoads(Vector2Int cell)
        {
            if (roadsDone.Contains(cell)) return;
            roadsDone.Add(cell);
            var a = EnsureTown(cell);
            if (a == null) return;
            var east = EnsureTown(cell + new Vector2Int(1, 0));
            var north = EnsureTown(cell + new Vector2Int(0, 1));
            if (east != null) AddRoad(Highway(HighwayId(cell, "E"), a, east));
            if (north != null) AddRoad(Highway(HighwayId(cell, "N"), a, north));
            if (east == null || north == null)
            {
                var ne = EnsureTown(cell + new Vector2Int(1, 1));
                if (ne != null) AddRoad(Highway(HighwayId(cell, "NE"), a, ne));
            }
        }

        static string HighwayId(Vector2Int cell, string dir) => $"hw_{cell.x}_{cell.y}_{dir}";

        void AddRoad(Road r)
        {
            r.ComputeBounds();
            roads.Add(r);
            roadById[r.id] = r;
            grid.Add(r);
        }

        static Road Straight(string id, string name, Vector3 a, Vector3 b)
        {
            var r = new Road { id = id, name = name, highway = false, halfWidth = 5.5f };
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep));
            for (int i = 0; i <= n; i++) r.points.Add(Vector3.Lerp(a, b, i / (float)n));
            return r;
        }

        Road Highway(string id, Town a, Town b)
        {
            var rng = new System.Random((int)Hash(seed, id.GetHashCodeStable(), 99));
            var road = new Road { id = id, name = $"Трасса {a.name} — {b.name}", highway = true, halfWidth = 7f };
            Vector2 p0 = a.center, p3 = b.center;
            Vector2 dir = (p3 - p0).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float len = Vector2.Distance(p0, p3);
            var ctrl = new List<Vector2>
            {
                p0,
                p0 + dir * Mathf.Min(300f, len * 0.15f),
                Vector2.Lerp(p0, p3, 0.38f) + perp * ((float)rng.NextDouble() - 0.5f) * len * 0.25f,
                Vector2.Lerp(p0, p3, 0.68f) + perp * ((float)rng.NextDouble() - 0.5f) * len * 0.25f,
                p3 - dir * Mathf.Min(300f, len * 0.15f),
                p3,
            };

            var flat = new List<Vector2>();
            for (int s = 0; s < ctrl.Count - 1; s++)
            {
                Vector2 c0 = ctrl[Mathf.Max(0, s - 1)], c1 = ctrl[s], c2 = ctrl[s + 1], c3 = ctrl[Mathf.Min(ctrl.Count - 1, s + 2)];
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(c1, c2) / SampleStep));
                for (int i = 0; i < n; i++) flat.Add(CatmullRom(c0, c1, c2, c3, i / (float)n));
            }
            flat.Add(p3);

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
                    if (w > 0.999f) pinned[i] = true;
                }
            }
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

        void BuildBuildings(Town t, System.Random rng)
        {
            float e = t.HalfExtent;
            int blocks = Town.StreetHalfCount * 2;
            for (int bx = 0; bx < blocks; bx++)
            for (int bz = 0; bz < blocks; bz++)
            {
                float x0 = t.center.x - e + bx * Town.StreetStep + 12f;
                float z0 = t.center.y - e + bz * Town.StreetStep + 12f;
                float inner = Town.StreetStep - 24f;
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

        // ------------------------------------------------------------------ race routes

        /// <summary>Race routes around a point: circuits of nearby towns and sprints along their highways, nearest first.</summary>
        public List<RouteData> RoutesNear(float x, float z)
        {
            EnsureArea(x, z);
            var c = CellOf(x, z);
            var list = new List<RouteData>();
            var nearTowns = new List<Town>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (townByCell.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var t)) nearTowns.Add(t);
            var here = new Vector2(x, z);
            nearTowns.Sort((a, b) => Vector2.Distance(a.center, here).CompareTo(Vector2.Distance(b.center, here)));

            foreach (var t in nearTowns)
            {
                list.Add(FindRoute($"circuit_{t.cell.x}_{t.cell.y}"));
                foreach (var dir in new[] { "E", "N", "NE" })
                {
                    var r = FindRoute($"sprint_{t.cell.x}_{t.cell.y}_{dir}");
                    if (r != null) list.Add(r);
                }
            }
            list.RemoveAll(r => r == null);
            return list;
        }

        /// <summary>Rebuilds a route from its id (ids travel over LAN, so they must be self-contained).</summary>
        public RouteData FindRoute(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var parts = id.Split('_');
            if (parts.Length < 3 || !int.TryParse(parts[1], out int cx) || !int.TryParse(parts[2], out int cz)) return null;
            var cell = new Vector2Int(cx, cz);
            EnsureRoads(cell);

            if (parts[0] == "circuit")
            {
                var t = EnsureTown(cell);
                if (t == null) return null;
                var r = new RouteData { id = id, name = $"Кольцо: {t.name}", circuit = true, laps = 3 };
                float e = t.HalfExtent;
                var c = new Vector3(t.center.x, t.height, t.center.y);
                AddLine(r.points, c + new Vector3(-e, 0, -e), c + new Vector3(e, 0, -e));
                AddLine(r.points, c + new Vector3(e, 0, -e), c + new Vector3(e, 0, e));
                AddLine(r.points, c + new Vector3(e, 0, e), c + new Vector3(-e, 0, e));
                AddLine(r.points, c + new Vector3(-e, 0, e), c + new Vector3(-e, 0, -e));
                r.points.RemoveAt(r.points.Count - 1);
                return r;
            }
            if (parts[0] == "sprint" && parts.Length >= 4)
            {
                if (!roadById.TryGetValue(HighwayId(cell, parts[3]), out var road)) return null;
                var r = new RouteData { id = id, name = "Спринт: " + road.name.Replace("Трасса ", ""), circuit = false };
                r.points.AddRange(road.points);
                return r;
            }
            return null;
        }

        static void AddLine(List<Vector3> list, Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep));
            for (int i = 0; i < n; i++) list.Add(Vector3.Lerp(a, b, i / (float)n));
            list.Add(b);
        }

        // ------------------------------------------------------------------ queries for streaming

        public IEnumerable<Road> RoadsIn(Rect area)
        {
            foreach (var r in roads)
                if (r.bounds.Overlaps(area)) yield return r;
        }

        public IEnumerable<Building> BuildingsIn(Rect area)
        {
            foreach (var b in buildings)
                if (area.Contains(new Vector2(b.position.x, b.position.z))) yield return b;
        }

        /// <summary>Nearest point on any generated road (global), facing along it.</summary>
        public Pose NearestRoadPose(Vector3 p)
        {
            EnsureArea(p.x, p.z);
            float best = float.MaxValue;
            var pose = new Pose(SpawnPoint, Quaternion.LookRotation(SpawnForward));
            var area = new Rect(p.x - 1500f, p.z - 1500f, 3000f, 3000f);
            foreach (var r in RoadsIn(area))
                for (int i = 0; i < r.points.Count - 1; i += 2)
                {
                    float d = (r.points[i] - p).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    Vector3 dir = r.points[i + 1] - r.points[i]; dir.y = 0f;
                    pose = new Pose(r.points[i] + Vector3.up * 1f, Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward));
                }
            return pose;
        }
    }

    static class StableHashExtensions
    {
        /// <summary>String hash that is the same on every run and platform.</summary>
        public static int GetHashCodeStable(this string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }
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
