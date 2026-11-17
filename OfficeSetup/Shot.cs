using System.IO;
using UnityEngine;
// Renders a picture of the open scene from a Unity-space camera. Custom(name, px,py,pz, tx,ty,tz, fov)
public class Shot
{
    // patient's eyes: Blender (1.2, 2.4, 1.65) looking down the table
    public static string Patient() { return Custom("patient", -1.2f, 1.65f, -2.4f, -5.5f, 1.0f, -2.4f, 95f); }
    public static string Overview() { return Custom("overview", -0.3f, 2.3f, -0.4f, -4.5f, 0.7f, -3.2f, 85f); }
    public static string All() { Custom("back", -6.3f, 1.7f, -4.4f, -1.0f, 1.1f, -1.6f, 80f); Custom("side", -3.7f, 1.5f, -0.3f, -3.7f, 0.9f, -3.2f, 80f); Custom("close", -1.9f, 1.55f, -2.4f, -3.0f, 1.1f, -1.4f, 60f); return Patient(); }
    public static string Custom(string name, float px, float py, float pz, float tx, float ty, float tz, float fov)
    {
        var go = new GameObject("ShotCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(px, py, pz); cam.transform.LookAt(new Vector3(tx, ty, tz));
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 200f; cam.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply(); RenderTexture.active = null;
        Directory.CreateDirectory("OfficeSetup/shots");
        var path = "OfficeSetup/shots/" + name + ".png"; File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        return Path.GetFullPath(path);
    }
}
