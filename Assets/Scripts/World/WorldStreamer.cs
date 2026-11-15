using System.Collections;
using System.Collections.Generic;
using RaceGame.Core;
using UnityEngine;

namespace RaceGame.World
{
    /// <summary>
    /// Builds the 8×8 km island in 1×1 km tiles around the player: terrain, road meshes (with colliders),
    /// buildings and trees. Far tiles are destroyed, near ones are generated over several frames.
    /// </summary>
    public class WorldStreamer : MonoBehaviour
    {
        public const float TileSize = 1000f;
        public const int TilesPerSide = 8;
        const int HeightRes = 257;
        const int AlphaRes = 128;
        const float LoadRadius = 1700f;
        const float UnloadRadius = 2400f;

        public MapLayout Layout { get; private set; }
        public Transform target;

        readonly Dictionary<Vector2Int, GameObject> tiles = new Dictionary<Vector2Int, GameObject>();
        readonly HashSet<Vector2Int> building = new HashSet<Vector2Int>();
        TerrainLayer[] layers;
        Material terrainMat, roadMat, waterMat, trunkMat, crownMat;
        Material[] buildingMats;
        Mesh cylinderMesh, sphereMesh;
        bool busy;

        public void Init(MapLayout layout)
        {
            Layout = layout;
            CreateMaterials();
            CreateWater();
            CreateBorders();
        }

        static Vector2Int TileOf(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt((p.x + MapLayout.Half) / TileSize), Mathf.FloorToInt((p.z + MapLayout.Half) / TileSize));

        static Vector3 TileOrigin(Vector2Int t) =>
            new Vector3(t.x * TileSize - MapLayout.Half, MapLayout.MinY, t.y * TileSize - MapLayout.Half);

        static bool Valid(Vector2Int t) => t.x >= 0 && t.y >= 0 && t.x < TilesPerSide && t.y < TilesPerSide;

        /// <summary>Synchronously builds the tiles around a point (before spawning a car there).</summary>
        public void Prewarm(Vector3 point)
        {
            foreach (var t in WantedTiles(point))
                if (!tiles.ContainsKey(t))
                {
                    var it = BuildTile(t);
                    while (it.MoveNext()) { }
                }
        }

        List<Vector2Int> WantedTiles(Vector3 p)
        {
            var list = new List<Vector2Int>();
            for (int x = 0; x < TilesPerSide; x++)
            for (int z = 0; z < TilesPerSide; z++)
            {
                var t = new Vector2Int(x, z);
                if (DistanceToTile(p, t) < LoadRadius) list.Add(t);
            }
            list.Sort((a, b) => DistanceToTile(p, a).CompareTo(DistanceToTile(p, b)));
            return list;
        }

        static float DistanceToTile(Vector3 p, Vector2Int t)
        {
            var o = TileOrigin(t);
            float dx = Mathf.Max(o.x - p.x, 0f, p.x - (o.x + TileSize));
            float dz = Mathf.Max(o.z - p.z, 0f, p.z - (o.z + TileSize));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Update()
        {
            if (Layout == null || target == null) return;
            Vector3 p = target.position;

            // unload far tiles
            List<Vector2Int> remove = null;
            foreach (var kv in tiles)
                if (DistanceToTile(p, kv.Key) > UnloadRadius) (remove ??= new List<Vector2Int>()).Add(kv.Key);
            if (remove != null)
                foreach (var t in remove) { Destroy(tiles[t]); tiles.Remove(t); }

            // load the nearest missing tile
            if (busy) return;
            foreach (var t in WantedTiles(p))
            {
                if (tiles.ContainsKey(t) || building.Contains(t)) continue;
                StartCoroutine(BuildTileAsync(t));
                break;
            }
        }

        IEnumerator BuildTileAsync(Vector2Int t)
        {
            busy = true;
            building.Add(t);
            var it = BuildTile(t);
            while (it.MoveNext()) yield return null;
            building.Remove(t);
            busy = false;
        }

        // ------------------------------------------------------------------ tile

        IEnumerator BuildTile(Vector2Int t)
        {
            Vector3 origin = TileOrigin(t);
            var root = new GameObject($"Tile_{t.x}_{t.y}");
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            // --- heights ---
            var heights = new float[HeightRes, HeightRes];
            float step = TileSize / (HeightRes - 1);
            for (int z = 0; z < HeightRes; z++)
            {
                for (int x = 0; x < HeightRes; x++)
                {
                    float h = Layout.Height(origin.x + x * step, origin.z + z * step);
                    heights[z, x] = Mathf.Clamp01((h - MapLayout.MinY) / MapLayout.HeightRange);
                }
                if ((z & 31) == 31) yield return null;
            }

            // --- splat map: grass / rock / sand / dirt by slope and height ---
            var alpha = new float[AlphaRes, AlphaRes, 4];
            float aStep = (HeightRes - 1f) / (AlphaRes - 1f);
            for (int z = 0; z < AlphaRes; z++)
            {
                for (int x = 0; x < AlphaRes; x++)
                {
                    int hx = Mathf.Min(HeightRes - 2, Mathf.RoundToInt(x * aStep));
                    int hz = Mathf.Min(HeightRes - 2, Mathf.RoundToInt(z * aStep));
                    float h0 = heights[hz, hx] * MapLayout.HeightRange + MapLayout.MinY;
                    float dx = (heights[hz, hx + 1] - heights[hz, hx]) * MapLayout.HeightRange / step;
                    float dz = (heights[hz + 1, hx] - heights[hz, hx]) * MapLayout.HeightRange / step;
                    float slope = Mathf.Sqrt(dx * dx + dz * dz);
                    float sand = h0 < MapLayout.SeaLevel + 2.5f ? 1f : 0f;
                    float rock = Mathf.Clamp01((slope - 0.45f) * 3f) + Mathf.Clamp01((h0 - 120f) / 40f);
                    float dirt = Mathf.Clamp01((slope - 0.2f) * 3f) * 0.6f;
                    float grass = Mathf.Max(0f, 1f - sand - rock - dirt);
                    float sum = grass + rock + sand + dirt;
                    alpha[z, x, 0] = grass / sum;
                    alpha[z, x, 1] = rock / sum;
                    alpha[z, x, 2] = sand / sum;
                    alpha[z, x, 3] = dirt / sum;
                }
                if ((z & 31) == 31) yield return null;
            }

            var data = new TerrainData { heightmapResolution = HeightRes };
            data.size = new Vector3(TileSize, MapLayout.HeightRange, TileSize);
            data.SetHeights(0, 0, heights);
            data.alphamapResolution = AlphaRes;
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, alpha);

            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            terrainGo.transform.position = origin;
            var terrain = terrainGo.GetComponent<Terrain>();
            if (terrainMat != null) terrain.materialTemplate = terrainMat;
            terrain.drawTreesAndFoliage = false;
            terrain.allowAutoConnect = true; // stitches LOD seams with neighbour tiles
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 400f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            yield return null;

            BuildRoads(t, root.transform);
            yield return null;
            BuildBuildings(t, root.transform);
            BuildTrees(t, root.transform);

            root.SetActive(true);
            tiles[t] = root;
        }

        void BuildRoads(Vector2Int t, Transform parent)
        {
            Vector3 o = TileOrigin(t);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            foreach (var road in Layout.roads)
            {
                var pts = road.points;
                int i = 0;
                while (i < pts.Count)
                {
                    // find a run of samples inside this tile
                    while (i < pts.Count && !Inside(pts[i], o)) i++;
                    if (i >= pts.Count) break;
                    int start = Mathf.Max(0, i - 1);
                    while (i < pts.Count && Inside(pts[i], o)) i++;
                    int end = Mathf.Min(pts.Count - 1, i);
                    AddStrip(road, start, end, verts, uvs, tris);
                }
            }
            if (verts.Count == 0) return;

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Roads");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = roadMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static bool Inside(Vector3 p, Vector3 o) =>
            p.x >= o.x && p.x < o.x + TileSize && p.z >= o.z && p.z < o.z + TileSize;

        static void AddStrip(Road road, int start, int end, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var pts = road.points;
            if (end <= start) return;
            int baseIndex = verts.Count;
            float v = 0f;
            for (int i = start; i <= end; i++)
            {
                Vector3 prev = pts[Mathf.Max(0, i - 1)], next = pts[Mathf.Min(pts.Count - 1, i + 1)];
                Vector3 dir = next - prev; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();
                var right = new Vector3(dir.z, 0f, -dir.x);
                Vector3 p = pts[i] + Vector3.up * road.yOffset;
                if (i > start) v += Vector3.Distance(pts[i], pts[i - 1]) / (road.halfWidth * 4f);
                verts.Add(p - right * road.halfWidth);
                verts.Add(p + right * road.halfWidth);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                if (i > start)
                {
                    int a = baseIndex + (i - start - 1) * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
        }

        void BuildBuildings(Vector2Int t, Transform parent)
        {
            Vector3 o = TileOrigin(t);
            Transform group = null;
            foreach (var b in Layout.buildings)
            {
                if (!Inside(b.position, o)) continue;
                if (group == null)
                {
                    group = new GameObject("Buildings").transform;
                    group.SetParent(parent, false);
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(group, false);
                go.transform.position = b.position + Vector3.up * (b.size.y * 0.5f - 0.5f);
                go.transform.localScale = b.size + Vector3.up;
                go.GetComponent<Renderer>().sharedMaterial = buildingMats[b.color % buildingMats.Length];
                go.isStatic = true;
            }
        }

        void BuildTrees(Vector2Int t, Transform parent)
        {
            Vector3 o = TileOrigin(t);
            var rng = new System.Random(Layout.seed * 7919 + t.x * 131 + t.y);
            var trunks = new List<CombineInstance>();
            var crowns = new List<CombineInstance>();
            var collidersGo = new GameObject("Trees");
            collidersGo.transform.SetParent(parent, false);

            for (int i = 0; i < 260 && trunks.Count < 140; i++)
            {
                float x = o.x + (float)rng.NextDouble() * TileSize;
                float z = o.z + (float)rng.NextDouble() * TileSize;
                float h = Layout.Height(x, z);
                if (h < MapLayout.SeaLevel + 1.5f || h > 160f) continue;
                if (Layout.TownAt(x, z, 40f) != null) continue;
                if (Layout.NearRoad(x, z, 16f)) continue;

                float s = 0.8f + (float)rng.NextDouble() * 0.6f;
                var basePos = new Vector3(x, h, z);
                trunks.Add(new CombineInstance
                {
                    mesh = cylinderMesh,
                    transform = Matrix4x4.TRS(basePos + Vector3.up * 2f * s, Quaternion.identity, new Vector3(0.35f, 2f, 0.35f) * s),
                });
                crowns.Add(new CombineInstance
                {
                    mesh = sphereMesh,
                    transform = Matrix4x4.TRS(basePos + Vector3.up * 5.5f * s, Quaternion.identity, new Vector3(4f, 5f, 4f) * s),
                });
                var col = collidersGo.AddComponent<CapsuleCollider>();
                col.center = basePos + Vector3.up * 2f * s;
                col.radius = 0.35f * s;
                col.height = 4f * s;
            }
            if (trunks.Count == 0) return;
            CombinedRenderer(collidersGo.transform, "Trunks", trunks, trunkMat);
            CombinedRenderer(collidersGo.transform, "Crowns", crowns, crownMat);
        }

        static void CombinedRenderer(Transform parent, string name, List<CombineInstance> parts, Material mat)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ shared things

        void CreateMaterials()
        {
            layers = new[]
            {
                Layer(NoiseTexture(new Color(0.30f, 0.45f, 0.20f), new Color(0.24f, 0.38f, 0.16f), 1), 6f),
                Layer(NoiseTexture(new Color(0.45f, 0.43f, 0.40f), new Color(0.35f, 0.34f, 0.32f), 2), 10f),
                Layer(NoiseTexture(new Color(0.80f, 0.74f, 0.55f), new Color(0.72f, 0.66f, 0.48f), 3), 6f),
                Layer(NoiseTexture(new Color(0.42f, 0.33f, 0.22f), new Color(0.35f, 0.27f, 0.18f), 4), 6f),
            };
            terrainMat = Resources.Load<Material>("Materials/Terrain");

            roadMat = MaterialFactory.Create(MaterialFactory.Ground, Color.white);
            roadMat.mainTexture = RoadTexture();
            MaterialFactory.SetSurface(roadMat, 0f, 0.25f);

            waterMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.10f, 0.32f, 0.45f));
            MaterialFactory.SetSurface(waterMat, 0.1f, 0.9f);
            trunkMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.30f, 0.20f, 0.12f));
            crownMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.16f, 0.36f, 0.14f));
            buildingMats = new[]
            {
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.78f, 0.74f, 0.66f)),
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.62f, 0.60f, 0.58f)),
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.70f, 0.50f, 0.40f)),
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.55f, 0.62f, 0.68f)),
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.85f, 0.82f, 0.70f)),
                MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.45f, 0.45f, 0.48f)),
            };

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinderMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        static TerrainLayer Layer(Texture2D tex, float tile) =>
            new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0f, metallic = 0f };

        static Texture2D NoiseTexture(Color a, Color b, int seed)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var rng = new System.Random(seed);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = Mathf.PerlinNoise(x * 0.15f + seed * 10f, y * 0.15f) * 0.7f + (float)rng.NextDouble() * 0.3f;
                tex.SetPixel(x, y, Color.Lerp(a, b, v));
            }
            tex.Apply(true);
            return tex;
        }

        static Texture2D RoadTexture()
        {
            const int w = 64, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var rng = new System.Random(5);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float g = 0.20f + (float)rng.NextDouble() * 0.05f;
                Color c = new Color(g, g, g * 1.05f);
                bool edge = x == 2 || x == 3 || x == w - 3 || x == w - 4;
                bool centre = (x == w / 2 || x == w / 2 - 1) && (y % 64) < 32;
                if (edge) c = new Color(0.85f, 0.85f, 0.82f);
                if (centre) c = new Color(0.90f, 0.80f, 0.30f);
                tex.SetPixel(x, y, c);
            }
            tex.Apply(true);
            return tex;
        }

        void CreateWater()
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Sea";
            Destroy(water.GetComponent<Collider>());
            water.transform.SetParent(transform, false);
            water.transform.position = new Vector3(0f, MapLayout.SeaLevel, 0f);
            water.transform.localScale = new Vector3(MapLayout.Size * 1.6f / 10f, 1f, MapLayout.Size * 1.6f / 10f);
            water.GetComponent<Renderer>().sharedMaterial = waterMat;
        }

        void CreateBorders()
        {
            float e = MapLayout.Half + 50f;
            Wall(new Vector3(0f, 50f, e), new Vector3(MapLayout.Size + 200f, 200f, 10f));
            Wall(new Vector3(0f, 50f, -e), new Vector3(MapLayout.Size + 200f, 200f, 10f));
            Wall(new Vector3(e, 50f, 0f), new Vector3(10f, 200f, MapLayout.Size + 200f));
            Wall(new Vector3(-e, 50f, 0f), new Vector3(10f, 200f, MapLayout.Size + 200f));
        }

        void Wall(Vector3 pos, Vector3 size)
        {
            var go = new GameObject("Border");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Ground height under a point using physics (roads, terrain), falls back to the layout.</summary>
        public float GroundHeight(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 400f, p.z), Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return Layout.Height(p.x, p.z);
        }
    }
}
