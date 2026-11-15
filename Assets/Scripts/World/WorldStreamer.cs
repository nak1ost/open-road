using System.Collections;
using System.Collections.Generic;
using RaceGame.Core;
using UnityEngine;

namespace RaceGame.World
{
    /// <summary>
    /// Builds the endless world in 1×1 km tiles around the player: terrain, road meshes (with colliders),
    /// buildings and trees. Far tiles are destroyed, near ones are generated over several frames.
    /// This object stays at -OriginShift.Offset, so its children are placed in GLOBAL coordinates.
    /// </summary>
    public class WorldStreamer : MonoBehaviour
    {
        public const float TileSize = 1000f;
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
        Transform water;
        bool busy;

        public void Init(MapLayout layout)
        {
            Layout = layout;
            transform.position = -OriginShift.Offset;
            CreateMaterials();
            CreateWater();
        }

        static Vector2Int TileOf(Vector3 global) =>
            new Vector2Int(Mathf.FloorToInt(global.x / TileSize), Mathf.FloorToInt(global.z / TileSize));

        static Vector3 TileOrigin(Vector2Int t) => new Vector3(t.x * TileSize, MapLayout.MinY, t.y * TileSize);

        /// <summary>Synchronously builds the tiles around a GLOBAL point (before putting a car there).</summary>
        public void Prewarm(Vector3 global)
        {
            foreach (var t in WantedTiles(global))
                if (!tiles.ContainsKey(t))
                {
                    var it = BuildTile(t);
                    while (it.MoveNext()) { }
                }
        }

        static List<Vector2Int> WantedTiles(Vector3 p)
        {
            var list = new List<Vector2Int>();
            var c = TileOf(p);
            int r = Mathf.CeilToInt(LoadRadius / TileSize) + 1;
            for (int x = c.x - r; x <= c.x + r; x++)
            for (int z = c.y - r; z <= c.y + r; z++)
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
            Vector3 p = OriginShift.ToGlobal(target.position);

            // the sea follows the player: there is water at the horizon everywhere
            water.position = new Vector3(target.position.x, MapLayout.SeaLevel, target.position.z);

            List<Vector2Int> remove = null;
            foreach (var kv in tiles)
                if (DistanceToTile(p, kv.Key) > UnloadRadius) (remove ??= new List<Vector2Int>()).Add(kv.Key);
            if (remove != null)
                foreach (var t in remove) { Destroy(tiles[t]); tiles.Remove(t); }

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
            root.transform.localPosition = new Vector3(origin.x, 0f, origin.z); // children are relative to the tile corner
            root.SetActive(false);

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
            terrainGo.transform.localPosition = new Vector3(0f, MapLayout.MinY, 0f);
            var terrain = terrainGo.GetComponent<Terrain>();
            if (terrainMat != null) terrain.materialTemplate = terrainMat;
            terrain.drawTreesAndFoliage = false;
            terrain.allowAutoConnect = true;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 400f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            yield return null;

            BuildRoads(origin, root.transform);
            yield return null;
            BuildBuildings(origin, root.transform);
            BuildTrees(t, origin, root.transform);

            root.SetActive(true);
            tiles[t] = root;
        }

        void BuildRoads(Vector3 o, Transform parent)
        {
            var area = new Rect(o.x, o.z, TileSize, TileSize);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var corner = new Vector3(o.x, 0f, o.z);

            foreach (var road in Layout.RoadsIn(area))
            {
                var pts = road.points;
                int i = 0;
                while (i < pts.Count)
                {
                    while (i < pts.Count && !Inside(pts[i], o)) i++;
                    if (i >= pts.Count) break;
                    int start = Mathf.Max(0, i - 1);
                    while (i < pts.Count && Inside(pts[i], o)) i++;
                    int end = Mathf.Min(pts.Count - 1, i);
                    AddStrip(road, start, end, corner, verts, uvs, tris);
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

        /// <summary>Road strip; vertices are relative to the tile corner to keep float precision far from (0,0).</summary>
        static void AddStrip(Road road, int start, int end, Vector3 corner, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
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
                Vector3 p = pts[i] - corner + Vector3.up * road.yOffset;
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

        void BuildBuildings(Vector3 o, Transform parent)
        {
            var area = new Rect(o.x, o.z, TileSize, TileSize);
            var corner = new Vector3(o.x, 0f, o.z);
            Transform group = null;
            foreach (var b in Layout.BuildingsIn(area))
            {
                if (group == null)
                {
                    group = new GameObject("Buildings").transform;
                    group.SetParent(parent, false);
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(group, false);
                go.transform.localPosition = b.position - corner + Vector3.up * (b.size.y * 0.5f - 0.5f);
                go.transform.localScale = b.size + Vector3.up;
                go.GetComponent<Renderer>().sharedMaterial = buildingMats[b.color % buildingMats.Length];
            }
        }

        void BuildTrees(Vector2Int t, Vector3 o, Transform parent)
        {
            var rng = new System.Random((int)MapLayout.Hash(Layout.seed, t.x * 131 + 7, t.y));
            var trunks = new List<CombineInstance>();
            var crowns = new List<CombineInstance>();
            var collidersGo = new GameObject("Trees");
            collidersGo.transform.SetParent(parent, false);
            var corner = new Vector3(o.x, 0f, o.z);

            for (int i = 0; i < 260 && trunks.Count < 140; i++)
            {
                float x = o.x + (float)rng.NextDouble() * TileSize;
                float z = o.z + (float)rng.NextDouble() * TileSize;
                float h = Layout.Height(x, z);
                if (h < MapLayout.SeaLevel + 1.5f || h > 160f) continue;
                if (Layout.TownAt(x, z, 40f) != null) continue;
                if (Layout.NearRoad(x, z, 16f)) continue;

                float s = 0.8f + (float)rng.NextDouble() * 0.6f;
                var basePos = new Vector3(x, h, z) - corner;
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Sea";
            Destroy(go.GetComponent<Collider>());
            // not a child of the world: it follows the player in local space instead
            water = go.transform;
            water.position = new Vector3(0f, MapLayout.SeaLevel, 0f);
            water.localScale = new Vector3(1000f, 1f, 1000f); // 10×10 km, always centred under the player
            go.GetComponent<Renderer>().sharedMaterial = waterMat;
        }

        void OnDestroy()
        {
            if (water != null) Destroy(water.gameObject);
        }

        /// <summary>Ground height under a LOCAL point using physics (roads, terrain), falls back to the layout.</summary>
        public float GroundHeight(Vector3 local)
        {
            if (Physics.Raycast(new Vector3(local.x, 400f, local.z), Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            var g = OriginShift.ToGlobal(local);
            return Layout.Height(g.x, g.z);
        }
    }
}
