using System.Collections.Generic;
using System.IO;
using UnityEngine;
// play mode: close-ups of one listener's lap with everything else hidden, at several times of the idle clip
// OfficeSetup/shots/lap_<seat>_<time>_<view>.png
public class Lap
{
    static float[] Times = { 0.15f, 0.4f, 0.65f, 0.9f };
    public static string E69() { Times = new[] { 0.69f }; return Run("E"); }
    public static string N119() { Times = new[] { 0.19f }; return Run("N1"); }
    public static string N0() { return Run("N0"); }
    public static string N0x() { Times = new[] { 0.06f, 0.19f, 0.31f, 0.44f, 0.56f, 0.69f, 0.81f, 0.94f }; return Run("N0"); }
    public static string N1x() { Times = new[] { 0.06f, 0.19f, 0.31f, 0.44f, 0.56f, 0.69f, 0.81f, 0.94f }; return Run("N1"); }
    public static string Ex() { Times = new[] { 0.06f, 0.19f, 0.31f, 0.44f, 0.56f, 0.69f, 0.81f, 0.94f }; return Run("E"); }
    public static string S0x() { Times = new[] { 0.06f, 0.19f, 0.31f, 0.44f, 0.56f, 0.69f, 0.81f, 0.94f }; return Run("S0"); }
    public static string S2x() { Times = new[] { 0.06f, 0.19f, 0.31f, 0.44f, 0.56f, 0.69f, 0.81f, 0.94f }; return Run("S2"); }
    public static string S1() { return Run("S1"); }
    public static string E() { return Run("E"); }
    public static string N2() { return Run("N2"); }
    public static string S0() { return Run("S0"); }
    public static string N1() { return Run("N1"); }
    public static string S2() { return Run("S2"); }

    static string Run(string seat)
    {
        var s = Object.FindAnyObjectByType<OfficeSession>(); OfficeListener l = null;
        foreach (var o in s.listeners) if (o.seat == seat) l = o;
        if (!l) return "no " + seat;
        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && !r.transform.IsChildOf(l.transform)) { r.enabled = false; hidden.Add(r); }
        var an = l.GetComponent<Animator>(); float keepT = an.GetCurrentAnimatorStateInfo(0).normalizedTime; float keepS = an.speed;
        string res = ""; OfficeListener.InstantLift = true;
        var late = typeof(OfficeListener).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (float t in Times)
        {
            an.speed = 1f; an.Play(OfficeListener.StillState, 0, t); an.Update(0f); late.Invoke(l, null);
            var c = (an.GetBoneTransform(HumanBodyBones.LeftHand).position + an.GetBoneTransform(HumanBodyBones.RightHand).position) * 0.5f;
            var f = l.transform.forward; var side = l.transform.right;
            string tag = "lap_" + seat + "_" + Mathf.RoundToInt(t * 100);
            Snap(tag + "_front", c + f * 0.6f + Vector3.up * 0.12f, c);
            Snap(tag + "_side", c + side * 0.6f + Vector3.up * 0.05f, c);
            Snap(tag + "_top", c + f * 0.15f + Vector3.up * 0.6f, c);
            res += tag + " ";
        }
        an.Play(OfficeListener.StillState, 0, keepT); an.speed = keepS; an.Update(0f);
        foreach (var r in hidden) r.enabled = true;
        OfficeListener.InstantLift = false;
        return res;
    }
    static void Snap(string name, Vector3 pos, Vector3 target)
    {
        var cam = new GameObject("ShotCam").AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(target); cam.fieldOfView = 50f; cam.nearClipPlane = 0.02f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.75f, 0.8f, 0.85f);
        var rt = new RenderTexture(320, 320, 24); rt.antiAliasing = 4; cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(320, 320, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 320, 320), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes("OfficeSetup/shots/" + name + ".png", tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(cam.gameObject);
    }
}
