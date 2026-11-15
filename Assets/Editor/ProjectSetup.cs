using System.IO;
using RaceGame.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RaceGame.Editor
{
    /// <summary>
    /// One-time project setup, runs automatically when the project is first opened
    /// (and manually via Tools/Game/Setup Project):
    /// URP asset, material templates, Main scene, mobile Player settings.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string SettingsDir = "Assets/Settings";
        const string MaterialsDir = "Assets/Resources/Materials";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string DoneKey = "RaceGame.SetupDone.v1";

        static ProjectSetup()
        {
            if (SessionState.GetBool(DoneKey, false)) return;
            SessionState.SetBool(DoneKey, true);
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath) || GraphicsSettings.defaultRenderPipeline == null)
                    Run();
            };
        }

        [MenuItem("Tools/Game/Setup Project")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureUrp();
            EnsureMaterials();
            ConfigurePlayer();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[RaceGame] Project setup done. Open Assets/Scenes/Main.unity and press Play.");
        }

        static void EnsureDir(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        static void EnsureUrp()
        {
            EnsureDir(SettingsDir);
            string assetPath = SettingsDir + "/URP-Mobile.asset";
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, SettingsDir + "/URP-Mobile-Renderer.asset");
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            urp.shadowDistance = 120f;
            urp.renderScale = 1f;
            urp.supportsHDR = false;          // cheaper on phones
            urp.msaaSampleCount = 2;
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
        }

        static void EnsureMaterials()
        {
            EnsureDir(MaterialsDir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[RaceGame] URP Lit shader not found. Check that the URP package is installed.");
                return;
            }
            Make(MaterialFactory.Paint, lit, new Color(0.7f, 0.05f, 0.05f), 0.6f, 0.85f, false, false);
            Make(MaterialFactory.Glass, lit, new Color(0.05f, 0.07f, 0.09f, 0.45f), 0.0f, 0.95f, true, false);
            Make(MaterialFactory.Plastic, lit, new Color(0.1f, 0.1f, 0.1f), 0.0f, 0.3f, false, false);
            Make(MaterialFactory.Emissive, lit, Color.white, 0.0f, 0.5f, false, true);
            Make(MaterialFactory.Ground, lit, Color.white, 0.0f, 0.15f, false, false);

            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null)
            {
                string path = MaterialsDir + "/Terrain.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                    AssetDatabase.CreateAsset(new Material(terrainShader), path);
            }
        }

        static void Make(string name, Shader shader, Color color, float metallic, float smoothness, bool transparent, bool emissive)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);

            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "RaceGame";
            PlayerSettings.productName = "Open Road";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.racegame.openroad");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.racegame.openroad");

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.appleEnableAutomaticSigning = false; // SideStore signs the app itself
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                EnsureDir("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                new GameObject("Game").AddComponent<GameManager>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
            }
        }
    }
}
