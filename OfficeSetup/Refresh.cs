public static class Refresh
{
    public static string Execute() { UnityEditor.AssetDatabase.Refresh(); return "refreshed"; }
}
