using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Runs once when a project made from the ExposiaVR Quest template is opened for the first time:
// OpenXR + Meta Quest features, XRIT Starter Assets, Quest 3 player/quality settings, switch to Android.
// Re-run any time from Tools > ExposiaVR > Run Quest 3 Setup.
[InitializeOnLoad]
public static class ExposiaFirstRun
{
    const string Marker = "ProjectSettings/ExposiaKit.done";
    const string LogFile = "ProjectSettings/ExposiaKit.log";

    static ExposiaFirstRun()
    {
        if (File.Exists(Marker) || Application.isBatchMode) return;
        EditorApplication.delayCall += () => { if (!File.Exists(Marker)) RunAll(); };
    }

    // folder name of the project, e.g. "dogPark"
    public static string ProjectFolder() => new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name;

    public static string ProductName() => "ExposiaVR " + ProjectFolder();

    public static string AppSuffix()
    {
        var s = new string(ProjectFolder().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (s.Length == 0 || char.IsDigit(s[0])) s = "app" + s;
        return s;
    }

    [MenuItem("Tools/ExposiaVR/Run Quest 3 Setup")]
    public static void RunAll()
    {
        File.WriteAllText(Marker, System.DateTime.Now.ToString("s"));
        var log = new StringBuilder("ExposiaVR setup\n");
        // the OpenXR validation throws unless Android is the selected group
        EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;

        try { log.AppendLine(ExposiaXRSetup.Run()); }
        catch (System.Exception e) { log.AppendLine("XR setup failed: " + e.Message); }

        try
        {
            var samples = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.unity.xr.interaction.toolkit", null);
            var starter = samples.FirstOrDefault(s => s.displayName == "Starter Assets");
            if (!starter.isImported) { starter.Import(); log.AppendLine("XRIT Starter Assets imported"); }
        }
        catch (System.Exception e) { log.AppendLine("Starter Assets import failed: " + e.Message); }

        try { log.AppendLine(ExposiaQuestSettings.Execute()); }
        catch (System.Exception e) { log.AppendLine("Quest settings failed: " + e.Message); }

        // XR Management sometimes leaves empty "XR 1", "XR/Settings 1"... folders behind
        foreach (var root in new[] { "Assets", "Assets/XR" })
            foreach (var dir in AssetDatabase.GetSubFolders(root))
                if (System.Text.RegularExpressions.Regex.IsMatch(dir, @"/(XR|Settings) \d+$")
                    && Directory.GetFileSystemEntries(dir).Length == 0)
                    AssetDatabase.DeleteAsset(dir);

        File.WriteAllText(LogFile, log.ToString());
        Debug.Log(log.ToString());
    }
}
