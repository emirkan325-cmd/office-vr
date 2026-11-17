using UnityEditor;
public class Refresh2
{
    public static string Execute() { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); return "refreshed, compiling=" + EditorApplication.isCompiling; }
    public static string BuildApk() { bool ok = EditorApplication.ExecuteMenuItem("Tools/ExposiaVR/Build Office Quest APK"); return "build ran: " + ok; }
}
