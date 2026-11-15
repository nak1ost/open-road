using System.Collections.Generic;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.UI
{
    /// <summary>Island map texture (land, water, roads, towns) and helpers to draw it with markers.</summary>
    public class Minimap
    {
        public const int Res = 256;
        public Texture2D Texture { get; private set; }
        readonly MapLayout layout;
        static Texture2D dot;

        public Minimap(MapLayout layout)
        {
            this.layout = layout;
            Build();
        }

        void Build()
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[Res * Res];
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                Vector2 w = ToWorld(x + 0.5f, y + 0.5f);
                float h = layout.BaseHeight(w.x, w.y);
                Color c;
                if (h < MapLayout.SeaLevel) c = new Color(0.10f, 0.25f, 0.38f, 0.9f);
                else if (h > 120f) c = new Color(0.50f, 0.48f, 0.45f, 0.9f);
                else c = Color.Lerp(new Color(0.25f, 0.42f, 0.20f, 0.9f), new Color(0.40f, 0.45f, 0.25f, 0.9f), h / 120f);
                px[y * Res + x] = c;
            }
            foreach (var t in layout.towns)
            {
                var a = ToPixel(new Vector3(t.center.x - t.HalfExtent, 0, t.center.y - t.HalfExtent));
                var b = ToPixel(new Vector3(t.center.x + t.HalfExtent, 0, t.center.y + t.HalfExtent));
                for (int y = (int)a.y; y <= (int)b.y; y++)
                for (int x = (int)a.x; x <= (int)b.x; x++)
                    if (x >= 0 && y >= 0 && x < Res && y < Res) px[y * Res + x] = new Color(0.55f, 0.52f, 0.48f, 0.95f);
            }
            foreach (var r in layout.roads)
            {
                var col = r.highway ? new Color(0.95f, 0.85f, 0.4f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
                foreach (var p in r.points)
                {
                    var q = ToPixel(p);
                    int x = (int)q.x, y = (int)q.y;
                    if (x >= 0 && y >= 0 && x < Res && y < Res) px[y * Res + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            Texture = tex;
        }

        static Vector2 ToWorld(float px, float py) =>
            new Vector2(px / Res * MapLayout.Size - MapLayout.Half, py / Res * MapLayout.Size - MapLayout.Half);

        /// <summary>World position → texture pixel (origin bottom-left).</summary>
        public static Vector2 ToPixel(Vector3 world) =>
            new Vector2((world.x + MapLayout.Half) / MapLayout.Size * Res, (world.z + MapLayout.Half) / MapLayout.Size * Res);

        /// <summary>Draws the whole map in a GUI rect with markers (pos, color, size).</summary>
        public void Draw(Rect rect, IEnumerable<(Vector3 pos, Color color, float size)> markers, IList<Vector3> route = null)
        {
            if (dot == null) dot = Texture2D.whiteTexture;
            GUI.DrawTexture(rect, Texture);
            if (route != null)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 0.3f, 0.9f, 1f);
                for (int i = 0; i < route.Count; i += 4) DrawDot(rect, route[i], 2f);
                GUI.color = old;
            }
            foreach (var m in markers)
            {
                var old = GUI.color;
                GUI.color = m.color;
                DrawDot(rect, m.pos, m.size);
                GUI.color = old;
            }
        }

        static void DrawDot(Rect rect, Vector3 world, float size)
        {
            var p = ToPixel(world) / Res;
            float x = rect.x + p.x * rect.width, y = rect.y + (1f - p.y) * rect.height;
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), dot);
        }
    }
}
