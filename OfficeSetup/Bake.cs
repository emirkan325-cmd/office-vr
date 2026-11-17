using UnityEditor;
using UnityEditor.SceneManagement;
public class Bake
{
    public static string Execute()
    {
        var s = EditorSceneManager.GetActiveScene();
        if (s.path != OfficeBuild.ScenePath) s = EditorSceneManager.OpenScene(OfficeBuild.ScenePath);
        Lightmapping.bakeCompleted -= Done; Lightmapping.bakeCompleted += Done;
        System.IO.File.Delete("OfficeSetup/bake_done.txt");
        return "bake started: " + Lightmapping.BakeAsync();
    }
    static void Done() { EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); System.IO.File.WriteAllText("OfficeSetup/bake_done.txt", "done " + System.DateTime.Now); }
    public static string State() { return "running " + Lightmapping.isRunning + " progress " + Lightmapping.buildProgress.ToString("0.00") + " lightmaps " + UnityEngine.LightmapSettings.lightmaps.Length; }
}
