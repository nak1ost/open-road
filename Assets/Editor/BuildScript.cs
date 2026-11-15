using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace RaceGame.Editor
{
    /// <summary>
    /// Command-line builds:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod RaceGame.Editor.BuildScript.BuildAndroid
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod RaceGame.Editor.BuildScript.ExportIOS
    /// Also available in the menu Tools/Game/Build.
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidPath = "Builds/Android/OpenRoad.apk";
        public const string IosPath = "Builds/iOS";

        static string[] Scenes()
        {
            ProjectSetup.Run();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new Exception("No scenes in build settings");
            return scenes;
        }

        [MenuItem("Tools/Game/Build/Android APK")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false; // .apk, not .aab
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            Directory.CreateDirectory(Path.GetDirectoryName(AndroidPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = AndroidPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Finish(report, AndroidPath);
        }

        [MenuItem("Tools/Game/Build/iOS Xcode project")]
        public static void ExportIOS()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            Directory.CreateDirectory(IosPath);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes(),
                locationPathName = IosPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            Finish(report, IosPath);
        }

        static void Finish(BuildReport report, string path)
        {
            var s = report.summary;
            Debug.Log($"[Build] {s.result}: {path}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime}");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>iOS: local network permission text (needed for LAN games) and no forced signing.</summary>
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetString("NSLocalNetworkUsageDescription", "Игра ищет друзей в вашей Wi-Fi сети для совместных гонок.");
            var services = root.CreateArray("NSBonjourServices");
            services.AddString("_openroad._tcp");
            root.SetBoolean("UIRequiresFullScreen", true);
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
#endif
        }
    }
}
