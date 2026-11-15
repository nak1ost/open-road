using UnityEngine;
using UnityEngine.Rendering;

namespace RaceGame.Core
{
    /// <summary>
    /// Creates materials at runtime from templates in Resources/Materials
    /// (made by Tools/Game/Setup Project), so URP shaders are included in builds.
    /// </summary>
    public static class MaterialFactory
    {
        public const string Paint = "CarPaint";
        public const string Glass = "CarGlass";
        public const string Plastic = "Plastic";
        public const string Emissive = "Emissive";
        public const string Ground = "Ground";

        public static Material Create(string template, Color color)
        {
            var src = Resources.Load<Material>("Materials/" + template);
            Material m = src != null ? new Material(src) : new Material(FallbackShader());
            SetColor(m, color);
            if (template == Emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2f);
            }
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetSurface(Material m, float metallic, float smoothness)
        {
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        }

        static Shader FallbackShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null) return pipeline.defaultMaterial.shader;
            return Shader.Find("Standard");
        }
    }
}
