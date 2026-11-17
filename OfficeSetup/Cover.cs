using System.Collections.Generic;
using System.IO;
using UnityEngine;
// Cover picture for the admin panel: hides the in-VR menu/panels, renders a wide room shot, shows them again.
public class Cover
{
    public static string Execute()
    {
        var hidden = new List<GameObject>();
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.renderMode == RenderMode.WorldSpace && c.gameObject.activeSelf) { c.gameObject.SetActive(false); hidden.Add(c.gameObject); }
        var go = new GameObject("CoverCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(-6.2f, 1.75f, -4.5f); cam.transform.LookAt(new Vector3(-1.4f, 1.0f, -1.9f));
        cam.fieldOfView = 62f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 200f;
        int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 8;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
        var path = Path.GetFullPath("OfficeSetup/shots/cover.jpg"); File.WriteAllBytes(path, tex.EncodeToJPG(90));
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go); Object.DestroyImmediate(tex);
        foreach (var g in hidden) g.SetActive(true);
        return path;
    }
}
