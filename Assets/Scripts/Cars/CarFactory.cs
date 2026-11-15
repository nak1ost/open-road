using RaceGame.Core;
using RaceGame.Data;
using RaceGame.Tuning;
using UnityEngine;

namespace RaceGame.Cars
{
    /// <summary>
    /// Builds a drivable car from a CarSpec: Rigidbody, four WheelColliders,
    /// a body (real prefab from Resources/CarModels/&lt;id&gt; if present, otherwise a placeholder by body type),
    /// then applies tuning.
    /// </summary>
    public static class CarFactory
    {
        struct Shape
        {
            public float lowerFrac;   // lower body height as a fraction of (height - clearance)
            public float cabinLen;    // cabin length as a fraction of total length
            public float cabinOffset; // cabin offset along length (+ = forward)
            public float clearance;   // m
            public float suspension;  // suspension travel, m
        }

        static Shape ShapeFor(BodyType body)
        {
            switch (body)
            {
                case BodyType.Hatchback: return new Shape { lowerFrac = 0.52f, cabinLen = 0.55f, cabinOffset = -0.12f, clearance = 0.16f, suspension = 0.20f };
                case BodyType.Wagon:     return new Shape { lowerFrac = 0.50f, cabinLen = 0.62f, cabinOffset = -0.10f, clearance = 0.15f, suspension = 0.20f };
                case BodyType.Crossover: return new Shape { lowerFrac = 0.55f, cabinLen = 0.58f, cabinOffset = -0.08f, clearance = 0.19f, suspension = 0.24f };
                case BodyType.Suv:       return new Shape { lowerFrac = 0.55f, cabinLen = 0.62f, cabinOffset = -0.08f, clearance = 0.21f, suspension = 0.26f };
                case BodyType.Offroad:   return new Shape { lowerFrac = 0.50f, cabinLen = 0.66f, cabinOffset = -0.08f, clearance = 0.24f, suspension = 0.30f };
                case BodyType.Coupe:     return new Shape { lowerFrac = 0.56f, cabinLen = 0.42f, cabinOffset = -0.06f, clearance = 0.13f, suspension = 0.16f };
                case BodyType.Supercar:  return new Shape { lowerFrac = 0.62f, cabinLen = 0.36f, cabinOffset = 0.02f,  clearance = 0.11f, suspension = 0.12f };
                case BodyType.Pickup:    return new Shape { lowerFrac = 0.55f, cabinLen = 0.34f, cabinOffset = 0.12f,  clearance = 0.23f, suspension = 0.28f };
                default:                 return new Shape { lowerFrac = 0.52f, cabinLen = 0.48f, cabinOffset = -0.03f, clearance = 0.15f, suspension = 0.20f };
            }
        }

        public static CarController Build(CarSpec spec, Vector3 position, Quaternion rotation, TuningData tuning)
        {
            var shape = ShapeFor(spec.Body);
            float L = spec.LengthM, W = spec.WidthM, H = spec.HeightM;
            float r = spec.wheelRadiusM;

            var root = new GameObject(spec.DisplayName);
            root.transform.SetPositionAndRotation(position, rotation);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = spec.massKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;

            // --- body collider (lower body + cabin) ---
            float lowerH = (H - shape.clearance) * shape.lowerFrac;
            float cabinH = H - shape.clearance - lowerH;
            var colliders = new GameObject("Colliders");
            colliders.transform.SetParent(root.transform, false);
            var lowerCol = colliders.AddComponent<BoxCollider>();
            lowerCol.center = new Vector3(0f, shape.clearance + lowerH * 0.5f, 0f);
            lowerCol.size = new Vector3(W, lowerH, L);
            var cabinCol = colliders.AddComponent<BoxCollider>();
            cabinCol.center = new Vector3(0f, shape.clearance + lowerH + cabinH * 0.5f, shape.cabinOffset * L);
            cabinCol.size = new Vector3(W * 0.85f, cabinH, L * shape.cabinLen);

            rb.centerOfMass = new Vector3(0f, shape.clearance + lowerH * 0.35f, 0.05f);

            // --- body visual ---
            var visual = root.AddComponent<CarVisual>();
            visual.length = L; visual.width = W; visual.height = H;
            visual.clearance = shape.clearance; visual.lowerHeight = lowerH;
            visual.rimMaterial = MaterialFactory.Create(MaterialFactory.Paint, tuning.rimColor);

            var prefab = Resources.Load<GameObject>("CarModels/" + spec.id);
            if (prefab != null) AttachModel(prefab, root.transform, visual);
            else BuildPlaceholderBody(root.transform, visual, shape, L, W, H, lowerH, cabinH);

            // --- wheels ---
            float wheelY = r + shape.suspension * (CarController.SuspensionTarget + CarController.StaticSag);
            float halfTrack = W * 0.5f - r * 0.55f;
            float halfBase = spec.WheelbaseM * 0.5f;
            var positions = new[]
            {
                new Vector3(-halfTrack, wheelY,  halfBase),
                new Vector3( halfTrack, wheelY,  halfBase),
                new Vector3(-halfTrack, wheelY, -halfBase),
                new Vector3( halfTrack, wheelY, -halfBase),
            };
            var names = new[] { "FL", "FR", "RL", "RR" };
            var wheels = new WheelCollider[4];
            var visuals = new Transform[4];
            visual.tireWidth = Mathf.Clamp(r * 0.65f, 0.2f, 0.32f);
            var tireMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.06f, 0.06f, 0.06f));

            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Wheel_" + names[i]);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = positions[i];
                var wc = go.AddComponent<WheelCollider>();
                wc.mass = 20f;
                wc.radius = r;
                wc.suspensionDistance = shape.suspension;
                wc.forceAppPointDistance = 0.15f;
                wc.wheelDampingRate = 0.25f;
                wheels[i] = wc;
                visuals[i] = BuildWheelVisual(root.transform, names[i], r, visual, tireMat);
            }

            var car = root.AddComponent<CarController>();
            car.Init(spec, wheels, visuals, shape.suspension);
            TuningApplier.Apply(car, tuning);
            return car;
        }

        static void AttachModel(GameObject prefab, Transform root, CarVisual visual)
        {
            var model = Object.Instantiate(prefab, root);
            model.name = "Model";
            foreach (var col in model.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            foreach (var rend in model.GetComponentsInChildren<Renderer>())
            {
                string n = rend.name.ToLowerInvariant();
                if (n.Contains("glass") || n.Contains("window"))
                {
                    var m = rend.material;
                    if (n.Contains("front") || n.Contains("wind")) visual.glassFront.Add(m);
                    else if (n.Contains("rear") || n.Contains("back")) visual.glassRear.Add(m);
                    else visual.glassSide.Add(m);
                }
                else if (n.Contains("body") || n.Contains("paint")) visual.paintMaterials.Add(rend.material);
                else if (n.Contains("headlight")) visual.headlightMaterials.Add(rend.material);
            }
        }

        static void BuildPlaceholderBody(Transform root, CarVisual visual, Shape s, float L, float W, float H, float lowerH, float cabinH)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            var glassColor = new Color(0.05f, 0.07f, 0.09f, 0.45f);
            var paintMat = MaterialFactory.Create(MaterialFactory.Paint, Color.white);
            var glassFrontMat = MaterialFactory.Create(MaterialFactory.Glass, glassColor);
            var glassSideMat = MaterialFactory.Create(MaterialFactory.Glass, glassColor);
            var glassRearMat = MaterialFactory.Create(MaterialFactory.Glass, glassColor);
            var plasticMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.08f, 0.08f, 0.08f));
            var interiorMat = MaterialFactory.Create(MaterialFactory.Plastic, new Color(0.03f, 0.03f, 0.03f));
            var headMat = MaterialFactory.Create(MaterialFactory.Emissive, new Color(1f, 0.97f, 0.85f));
            var tailMat = MaterialFactory.Create(MaterialFactory.Emissive, new Color(0.9f, 0.05f, 0.05f));
            visual.paintMaterials.Add(paintMat);
            visual.glassFront.Add(glassFrontMat);
            visual.glassSide.Add(glassSideMat);
            visual.glassRear.Add(glassRearMat);
            visual.headlightMaterials.Add(headMat);

            float baseY = s.clearance;
            float cabinZ = s.cabinOffset * L;
            float cabinLen = L * s.cabinLen;
            float cabinW = W * 0.84f;
            float cabinY = baseY + lowerH + cabinH * 0.48f;
            float glassH = cabinH * 0.94f;
            const float pane = 0.02f;

            Box(body, "LowerBody", new Vector3(0f, baseY + lowerH * 0.5f, 0f), new Vector3(W, lowerH, L), paintMat);
            // dark interior so tinted glass reads properly
            Box(body, "Interior", new Vector3(0f, cabinY, cabinZ), new Vector3(cabinW * 0.94f, glassH * 0.9f, cabinLen * 0.94f), interiorMat);
            // glass: windscreen, rear window, two sides
            Box(body, "Glass_Front", new Vector3(0f, cabinY, cabinZ + cabinLen * 0.5f), new Vector3(cabinW, glassH, pane), glassFrontMat);
            Box(body, "Glass_Rear", new Vector3(0f, cabinY, cabinZ - cabinLen * 0.5f), new Vector3(cabinW, glassH, pane), glassRearMat);
            Box(body, "Glass_Left", new Vector3(-cabinW * 0.5f, cabinY, cabinZ), new Vector3(pane, glassH, cabinLen), glassSideMat);
            Box(body, "Glass_Right", new Vector3(cabinW * 0.5f, cabinY, cabinZ), new Vector3(pane, glassH, cabinLen), glassSideMat);
            Box(body, "Roof", new Vector3(0f, baseY + lowerH + cabinH - 0.02f, cabinZ - cabinLen * 0.02f), new Vector3(cabinW * 1.02f, 0.05f, cabinLen * 0.9f), paintMat);

            // pillars
            float py = baseY + lowerH + glassH * 0.5f;
            foreach (float x in new[] { -cabinW * 0.5f, cabinW * 0.5f })
            {
                Box(body, "PillarA", new Vector3(x, py, cabinZ + cabinLen * 0.48f), new Vector3(0.07f, glassH, 0.08f), paintMat);
                Box(body, "PillarB", new Vector3(x, py, cabinZ + cabinLen * 0.02f), new Vector3(0.07f, glassH, 0.08f), paintMat);
                Box(body, "PillarC", new Vector3(x, py, cabinZ - cabinLen * 0.47f), new Vector3(0.07f, glassH, 0.12f), paintMat);
            }

            // bumpers and lights
            Box(body, "BumperF", new Vector3(0f, baseY + 0.12f, L * 0.5f + 0.02f), new Vector3(W * 0.96f, 0.16f, 0.06f), plasticMat);
            Box(body, "BumperR", new Vector3(0f, baseY + 0.12f, -L * 0.5f - 0.02f), new Vector3(W * 0.96f, 0.16f, 0.06f), plasticMat);
            float lightY = baseY + lowerH * 0.75f;
            foreach (float x in new[] { -W * 0.36f, W * 0.36f })
            {
                Box(body, "HeadLight", new Vector3(x, lightY, L * 0.5f + 0.005f), new Vector3(W * 0.2f, 0.08f, 0.02f), headMat);
                Box(body, "TailLight", new Vector3(x, lightY, -L * 0.5f - 0.005f), new Vector3(W * 0.22f, 0.07f, 0.02f), tailMat);
            }

            if (s.cabinLen < 0.4f && s.cabinOffset > 0.05f) // pickup bed walls
            {
                float bedLen = L * 0.42f;
                float bedZ = -L * 0.5f + bedLen * 0.5f + 0.05f;
                float wallY = baseY + lowerH + 0.15f;
                Box(body, "BedL", new Vector3(-W * 0.47f, wallY, bedZ), new Vector3(0.06f, 0.3f, bedLen), paintMat);
                Box(body, "BedR", new Vector3(W * 0.47f, wallY, bedZ), new Vector3(0.06f, 0.3f, bedLen), paintMat);
                Box(body, "Tailgate", new Vector3(0f, wallY, -L * 0.5f + 0.03f), new Vector3(W * 0.94f, 0.3f, 0.06f), paintMat);
            }
        }

        static Transform BuildWheelVisual(Transform root, string name, float r, CarVisual visual, Material tireMat)
        {
            var pivot = new GameObject("WheelVisual_" + name).transform;
            pivot.SetParent(root, false);

            var tire = Primitive(PrimitiveType.Cylinder, pivot, "Tire", tireMat);
            tire.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tire.localScale = new Vector3(r * 2f, visual.tireWidth * 0.5f, r * 2f);
            visual.tires.Add(tire);

            var rim = Primitive(PrimitiveType.Cylinder, tire, "Rim", visual.rimMaterial);
            rim.localScale = new Vector3(0.66f, 1.04f, 0.66f);
            return pivot; // spokes are added by CarVisual according to the rim style
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var t = Primitive(PrimitiveType.Cube, parent, name, mat);
            t.localPosition = pos;
            t.localScale = size;
        }

        static Transform Primitive(PrimitiveType type, Transform parent, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }
    }
}
