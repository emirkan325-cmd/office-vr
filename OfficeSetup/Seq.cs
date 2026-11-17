using System.IO;
using UnityEngine;
// play mode: one front picture of one listener now, named with a running number + current clip (OfficeSetup/shots/q_<seat>_<n>.png)
public class Seq
{
    static int n;
    public static string N0() { return Shot("N0"); }
    public static string S1() { return Shot("S1"); }
    public static string N2() { return Shot("N2"); }
    static string Shot(string seat)
    {
        var s = Object.FindAnyObjectByType<OfficeSession>(); OfficeListener l = null;
        foreach (var o in s.listeners) if (o.seat == seat) l = o;
        if (!l) return "no " + seat;
        foreach (var o in s.listeners) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = o == l;
        var t = l.transform; var c = t.position + Vector3.up * 0.75f;
        var toTable = new Vector3(-3.7f, 0f, -2.4f) - t.position; toTable.y = 0f; toTable.Normalize();
        var side = Vector3.Cross(Vector3.up, toTable);
        var cam = new GameObject("ShotCam").AddComponent<Camera>();
        cam.transform.position = c + toTable * 1.5f + side * 0.3f + Vector3.up * 0.45f; cam.transform.LookAt(c);
        cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f;
        var rt = new RenderTexture(360, 360, 24); rt.antiAliasing = 4; cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(360, 360, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 360, 360), 0, 0); tex.Apply(); RenderTexture.active = null;
        Directory.CreateDirectory("OfficeSetup/shots");
        File.WriteAllBytes("OfficeSetup/shots/q_" + seat + "_" + n + ".png", tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(cam.gameObject);
        foreach (var o in s.listeners) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = true;
        var info = l.GetComponent<Animator>().GetCurrentAnimatorClipInfo(0);
        return "q_" + seat + "_" + (n++) + " " + (info.Length > 0 ? info[0].clip.name : "?");
    }
}
