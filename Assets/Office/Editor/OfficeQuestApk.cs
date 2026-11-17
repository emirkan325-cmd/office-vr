using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Quest APK of the presentation app (com.exposiavr.office) -> Builds/com.exposiavr.office_v<code>.apk
// Menu: Tools > ExposiaVR > Build Office Quest APK. Batch:
//   Unity -batchmode -quit -projectPath <project> -buildTarget Android -executeMethod OfficeQuestApk.Build
// Bumps the Android versionCode (+1) every build (Meta and adb updates need a higher code).
public static class OfficeQuestApk
{
    [MenuItem("Tools/ExposiaVR/Build Office Quest APK")]
    public static void Build()
    {
        PlayerSettings.productName = "ExposiaVR Besprechung";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.exposiavr.office");
        PlayerSettings.Android.bundleVersionCode += 1;
        int code = PlayerSettings.Android.bundleVersionCode;
        AssetDatabase.SaveAssets();

        Directory.CreateDirectory("Builds");
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Office/Scenes/Besprechung.unity" },
            locationPathName = $"Builds/com.exposiavr.office_v{code}.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"[OfficeQuestApk] Build result: {s.result} | {opts.locationPathName} | v{PlayerSettings.bundleVersion} ({code}) | {s.totalSize / (1024 * 1024)} MB | errors {s.totalErrors} | {s.totalTime}");
        if (Application.isBatchMode) EditorApplication.Exit(s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
