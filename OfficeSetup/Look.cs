using System.IO;
using UnityEngine;
// close-up pictures of every active listener (play mode): OfficeSetup/shots/p_<seat>_<tag>.png
public class Look
{
    static string Run(string tag)
    {
        var s = Object.FindAnyObjectByType<OfficeSession>(); string r = "";
        foreach (var l in s.listeners)
        {
            if (!l.gameObject.activeSelf) continue;
            // only this person is drawn
            foreach (var o in s.listeners) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = o == l;
            var t = l.transform; var c = t.position + Vector3.up * 0.75f;
            var toTable = new Vector3(-3.7f, 0f, -2.4f) - t.position; toTable.y = 0f;
            if (toTable.magnitude < 0.5f) toTable = t.forward; toTable.Normalize();
            var side = Vector3.Cross(Vector3.up, toTable);
            if (l.seat == "E") side = -side;
            Snap("p_" + l.seat + "_" + tag + "_s", c + side * 2.4f - toTable * 0.15f + Vector3.up * 0.25f, c, 42f);
            Snap("p_" + l.seat + "_" + tag + "_f", c + toTable * 1.7f + side * 0.5f + Vector3.up * 1.0f, c + Vector3.up * 0.1f, 42f);
            r += l.seat + " ";
        }
        foreach (var o in s.listeners) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = true;
        return r;
    }
    public static string Idle() { return Run("idle"); }
    public static string Stare() { var s = Object.FindAnyObjectByType<OfficeSession>(); foreach (var l in s.listeners) l.LookAtPatient(30f); return "stare"; }
    public static string Stared() { return Run("stare"); }
    public static string Knock() { var s = Object.FindAnyObjectByType<OfficeSession>(); foreach (var l in s.listeners) if (l.gameObject.activeSelf) l.Clap(30f); return "knock"; }
    public static string Knocked() { return Run("knock"); }
    public static string Pause() { Time.timeScale = 0f; return "paused"; }
    public static string Resume() { Time.timeScale = 1f; return "resumed"; }
    static string Snap(string name, Vector3 pos, Vector3 target, float fov)
    {
        var go = new GameObject("ShotCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(target);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
        var rt = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(480, 480, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); tex.Apply(); RenderTexture.active = null;
        Directory.CreateDirectory("OfficeSetup/shots");
        var path = "OfficeSetup/shots/" + name + ".png"; File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        return name;
    }
}
