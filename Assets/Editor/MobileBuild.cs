#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnimatedDrawingsWorld.EditorTools
{
    // One-click phone builds. Needs the Android / iOS Build Support modules installed in Unity Hub.
    //   Android: Builds/Android/AnimatedDrawingsWorld.apk — copy to the phone and install
    //            (or "Build and Run" with the phone connected over USB).
    //   iOS:     Builds/iOS — an Xcode project; open it on a Mac, choose your team, run on the iPhone.
    public static class MobileBuild
    {
        private const string Scene = "Assets/Scenes/GameScene.unity";
        private const string BundleId = "com.animatedrawing.world";

        [MenuItem("AnimatedDrawingsWorld/Build for Android (APK)")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/AnimatedDrawingsWorld.apk", false);

        [MenuItem("AnimatedDrawingsWorld/Build and Run on Android")]
        public static void BuildAndRunAndroid() => Build(BuildTarget.Android, "Builds/Android/AnimatedDrawingsWorld.apk", true);

        [MenuItem("AnimatedDrawingsWorld/Build for iPhone (Xcode project)")]
        public static void BuildIos() => Build(BuildTarget.iOS, "Builds/iOS", false);

        public static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Drawings Come Alive";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);

            // drawings are wide: landscape both ways
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // store-ready 64-bit builds
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // iOS asks the user with these texts (NativeGallery adds the photo library one)
            PlayerSettings.iOS.cameraUsageDescription = "Take a photo of a drawing to bring it to life.";
        }

        private static void Build(BuildTarget target, string path, bool run)
        {
            ConfigurePlayer();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"MobileBuild: {target} build ready at {Path.GetFullPath(path)} ({report.summary.totalSize / (1024 * 1024)} MB)");
                EditorUtility.RevealInFinder(path);
            }
            else Debug.LogError($"MobileBuild: {target} build failed ({report.summary.result}). See the Console for details.");
        }
    }
}
#endif
