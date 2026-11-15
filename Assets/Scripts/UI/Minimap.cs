using System.Collections.Generic;
using RaceGame.World;
using UnityEngine;

namespace RaceGame.UI
{
    /// <summary>
    /// Map of the 4×4 km around the player (the world is endless, so the map moves with you).
    /// The texture is redrawn when the player leaves the middle part of it. All positions are GLOBAL.
    /// </summary>
    public class Minimap
    {
        public const int Res = 192;
        public const float Span = 4000f;

        public Texture2D Texture { get; private set; }
        readonly MapLayout layout;
        Vector2 center = new Vector2(float.MaxValue, float.MaxValue);
        static Texture2D dot;

        public Minimap(MapLayout layout)
        {
            this.layout = layout;
            Texture = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        /// <summary>Recentres the map if the player moved more than 800 m from its centre.</summary>
        public void Follow(Vector3 global)
        {
            var p = new Vector2(global.x, global.z);
            if (Vector2.Distance(p, center) < 800f) return;
            center = new Vector2(Mathf.Round(p.x / 100f) * 100f, Mathf.Round(p.y / 100f) * 100f);
            Redraw();
        }

        void Redraw()
        {
            layout.EnsureArea(center.x, center.y);
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
            var area = new Rect(center.x - Span * 0.5f, center.y - Span * 0.5f, Span, Span);
            foreach (var t in layout.towns)
            {
                if (!area.Contains(t.center)) continue;
                var a = ToPixel(new Vector3(t.center.x - t.HalfExtent, 0, t.center.y - t.HalfExtent));
                var b = ToPixel(new Vector3(t.center.x + t.HalfExtent, 0, t.center.y + t.HalfExtent));
                for (int y = (int)a.y; y <= (int)b.y; y++)
                for (int x = (int)a.x; x <= (int)b.x; x++)
                    if (x >= 0 && y >= 0 && x < Res && y < Res) px[y * Res + x] = new Color(0.55f, 0.52f, 0.48f, 0.95f);
            }
            foreach (var r in layout.RoadsIn(area))
            {
                var col = r.highway ? new Color(0.95f, 0.85f, 0.4f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
                foreach (var p in r.points)
                {
                    var q = ToPixel(p);
                    int x = (int)q.x, y = (int)q.y;
                    if (x >= 0 && y >= 0 && x < Res && y < Res) px[y * Res + x] = col;
                }
            }
            Texture.SetPixels(px);
            Texture.Apply();
        }

        Vector2 ToWorld(float px, float py) =>
            new Vector2(center.x - Span * 0.5f + px / Res * Span, center.y - Span * 0.5f + py / Res * Span);

        /// <summary>GLOBAL world position → texture pixel (origin bottom-left).</summary>
        Vector2 ToPixel(Vector3 world) =>
            new Vector2((world.x - center.x + Span * 0.5f) / Span * Res, (world.z - center.y + Span * 0.5f) / Span * Res);

        /// <summary>Draws the map in a GUI rect with markers (GLOBAL pos, color, size) and an optional route.</summary>
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

        void DrawDot(Rect rect, Vector3 world, float size)
        {
            var p = ToPixel(world) / Res;
            if (p.x < 0f || p.y < 0f || p.x > 1f || p.y > 1f) return;
            float x = rect.x + p.x * rect.width, y = rect.y + (1f - p.y) * rect.height;
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), dot);
        }
    }
}
