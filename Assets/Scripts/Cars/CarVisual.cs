using System.Collections.Generic;
using RaceGame.Core;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.Cars
{
    /// <summary>
    /// Everything tuning changes on the outside of the car: paint, glass tint, rims,
    /// headlights, neon, spoiler, wheel size. Works for placeholder bodies and real models.
    /// </summary>
    public class CarVisual : MonoBehaviour
    {
        public readonly List<Material> paintMaterials = new List<Material>();
        public readonly List<Material> glassFront = new List<Material>();
        public readonly List<Material> glassSide = new List<Material>();
        public readonly List<Material> glassRear = new List<Material>();
        public readonly List<Material> headlightMaterials = new List<Material>();
        public readonly List<Transform> tires = new List<Transform>();
        public Material rimMaterial;

        // body dimensions (for placing neon and spoiler)
        public float length, width, height, clearance, lowerHeight;
        public float tireWidth = 0.25f;

        GameObject neonObject;
        Light neonLight;
        Material neonMaterial;
        GameObject spoilerObject;
        int builtSpoiler = -1;
        int builtRimStyle = -1;

        public void Apply(TuningData t, float wheelRadius)
        {
            ApplyPaint(t.paint, t.finish);

            SetGlass(glassFront, t.tintFront);
            SetGlass(glassSide, t.tintSide);
            SetGlass(glassRear, t.tintRear);

            if (rimMaterial != null) MaterialFactory.SetColor(rimMaterial, t.rimColor);
            foreach (var tire in tires)
                tire.localScale = new Vector3(wheelRadius * 2f, tireWidth * 0.5f, wheelRadius * 2f);
            if (builtRimStyle != t.rimStyle) BuildRims(t.rimStyle);

            foreach (var m in headlightMaterials)
            {
                MaterialFactory.SetColor(m, t.headlightColor);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", t.headlightColor * 2f);
            }

            ApplyNeon(t.neon, t.neonColor);
            if (builtSpoiler != t.spoiler) BuildSpoiler(t.spoiler);
        }

        void ApplyPaint(Color color, PaintFinish finish)
        {
            float metallic, smooth;
            switch (finish)
            {
                case PaintFinish.Matte: metallic = 0.0f; smooth = 0.18f; break;
                case PaintFinish.Gloss: metallic = 0.05f; smooth = 0.9f; break;
                case PaintFinish.Pearl: metallic = 0.45f; smooth = 0.96f; break;
                case PaintFinish.Chrome: metallic = 1.0f; smooth = 0.98f; break;
                default: metallic = 0.75f; smooth = 0.82f; break;
            }
            if (finish == PaintFinish.Chrome) color = Color.Lerp(color, Color.white, 0.35f);
            foreach (var m in paintMaterials)
            {
                MaterialFactory.SetColor(m, color);
                MaterialFactory.SetSurface(m, metallic, smooth);
            }
        }

        static void SetGlass(List<Material> mats, float tint)
        {
            var c = new Color(0.04f, 0.05f, 0.06f, Mathf.Lerp(0.15f, 0.97f, Mathf.Clamp01(tint)));
            foreach (var m in mats) MaterialFactory.SetColor(m, c);
        }

        void BuildRims(int style)
        {
            builtRimStyle = style;
            int spokes = TuningCatalog.RimSpokes[Mathf.Clamp(style, 0, TuningCatalog.RimSpokes.Length - 1)];
            foreach (var tire in tires)
            {
                for (int i = tire.childCount - 1; i >= 0; i--)
                {
                    var child = tire.GetChild(i);
                    if (child.name.StartsWith("Spoke")) Destroy(child.gameObject);
                    else if (child.name == "Rim") child.localScale = spokes == 0 ? new Vector3(0.74f, 1.06f, 0.74f) : new Vector3(0.66f, 1.04f, 0.66f);
                }
                for (int s = 0; s < spokes; s++)
                {
                    float angle = s * 360f / spokes;
                    float rad = angle * Mathf.Deg2Rad;
                    var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    spoke.name = "Spoke";
                    DestroyImmediate(spoke.GetComponent<Collider>());
                    spoke.transform.SetParent(tire, false);
                    spoke.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                    spoke.transform.localPosition = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad)) * 0.2f;
                    spoke.transform.localScale = new Vector3(0.42f, 2.12f, spokes > 6 ? 0.05f : 0.09f); // cylinder is 2 units tall
                    spoke.GetComponent<Renderer>().sharedMaterial = rimMaterial;
                }
            }
        }

        void ApplyNeon(bool on, Color color)
        {
            if (!on)
            {
                if (neonObject != null) neonObject.SetActive(false);
                return;
            }
            if (neonObject == null)
            {
                neonObject = new GameObject("Neon");
                neonObject.transform.SetParent(transform, false);
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "NeonStrip";
                DestroyImmediate(strip.GetComponent<Collider>());
                strip.transform.SetParent(neonObject.transform, false);
                strip.transform.localPosition = new Vector3(0f, Mathf.Max(0.04f, clearance * 0.5f), 0f);
                strip.transform.localScale = new Vector3(width * 0.8f, 0.02f, length * 0.75f);
                neonMaterial = MaterialFactory.Create(MaterialFactory.Emissive, color);
                strip.GetComponent<Renderer>().sharedMaterial = neonMaterial;

                var lightGo = new GameObject("NeonLight");
                lightGo.transform.SetParent(neonObject.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                neonLight = lightGo.AddComponent<Light>();
                neonLight.type = LightType.Point;
                neonLight.range = Mathf.Max(length, width) * 0.9f;
                neonLight.intensity = 3f;
                neonLight.shadows = LightShadows.None;
            }
            neonObject.SetActive(true);
            MaterialFactory.SetColor(neonMaterial, color);
            if (neonMaterial.HasProperty("_EmissionColor")) neonMaterial.SetColor("_EmissionColor", color * 3f);
            neonLight.color = color;
        }

        void BuildSpoiler(int level)
        {
            builtSpoiler = level;
            if (spoilerObject != null) Destroy(spoilerObject);
            spoilerObject = null;
            if (level <= 0 || paintMaterials.Count == 0) return;

            spoilerObject = new GameObject("Spoiler");
            spoilerObject.transform.SetParent(transform, false);
            var paint = paintMaterials[0];
            float top = clearance + lowerHeight;
            float z = -length * 0.5f + 0.15f;

            if (level == 1)
            {
                Part("Lip", new Vector3(0f, top + 0.03f, z), new Vector3(width * 0.8f, 0.04f, 0.18f), Quaternion.Euler(-12f, 0f, 0f), paint);
            }
            else
            {
                float wingY = top + 0.32f;
                Part("Wing", new Vector3(0f, wingY, z - 0.05f), new Vector3(width * 0.92f, 0.03f, 0.3f), Quaternion.Euler(-8f, 0f, 0f), paint);
                foreach (float x in new[] { -width * 0.3f, width * 0.3f })
                    Part("Strut", new Vector3(x, top + 0.16f, z), new Vector3(0.04f, 0.32f, 0.12f), Quaternion.identity, paint);
                foreach (float x in new[] { -width * 0.46f, width * 0.46f })
                    Part("Endplate", new Vector3(x, wingY + 0.04f, z - 0.05f), new Vector3(0.02f, 0.14f, 0.32f), Quaternion.identity, paint);
            }
        }

        void Part(string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(spoilerObject.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
