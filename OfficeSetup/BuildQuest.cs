using System.IO;
using UnityEditor;
public class BuildQuest
{
    // runs the build now (the MCP call may time out; the result lands in OfficeSetup/build_result.txt)
    public static string Execute()
    {
        File.Delete("OfficeSetup/build_result.txt");
        PlayerSettings.productName = "ExposiaVR Besprechung";
        // same naming as the other ExposiaVR products in the admin panel / Hub (2026-11-17)
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.exposiavr.office");
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Office/Scenes/Besprechung.unity" },
            locationPathName = "Builds/ExposiaVR_Besprechung.apk",
            target = BuildTarget.Android, options = BuildOptions.None
        };
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        string msg = s.result + " | " + s.totalTime + " | " + (s.totalSize / (1024 * 1024)) + " MB | errors " + s.totalErrors;
        foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == UnityEngine.LogType.Error || m.type == UnityEngine.LogType.Exception) msg += "\n" + m.content;
        File.WriteAllText("OfficeSetup/build_result.txt", msg);
        return msg;
    }
}
