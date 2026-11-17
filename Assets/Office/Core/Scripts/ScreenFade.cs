using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Black fade in front of the eyes (quad just past the near plane, drawn last, ignores depth).
public class ScreenFade : MonoBehaviour
{
    public Material fadeMaterial;   // URP Unlit transparent; assigned in the editor so the shader ships in builds
    public float seconds = 3f;
    public float Alpha { get; private set; } = 1f;

    Renderer quad; Material mat;

    public static ScreenFade Create(Camera cam, Material template, float seconds)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "Fade"; q.transform.SetParent(cam.transform, false);
        q.transform.localPosition = new Vector3(0, 0, cam.nearClipPlane + 0.02f);
        var f = q.AddComponent<ScreenFade>(); f.fadeMaterial = template; f.seconds = seconds; f.Init();
        return f;
    }

    void Init()
    {
        mat = fadeMaterial != null ? new Material(fadeMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1); mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZTest", (int)CompareFunction.Always);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 5000;
        quad = GetComponent<Renderer>(); quad.sharedMaterial = mat; quad.shadowCastingMode = ShadowCastingMode.Off;
        Set(1f);
    }

    public void Set(float a)
    {
        Alpha = a;
        mat.SetColor("_BaseColor", new Color(0, 0, 0, a));
        quad.enabled = a > 0.001f;
    }

    public IEnumerator To(float target)
    {
        float from = Alpha;
        for (float t = 0; t < seconds; t += Time.deltaTime) { Set(Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, t / seconds))); yield return null; }
        Set(target);
    }
}
