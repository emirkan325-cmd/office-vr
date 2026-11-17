using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
// EDIT MODE: samples sitting clips (straight from the FBX files) on one listener - no IK, no scripts - and photographs
// the result from the side and from the front with the other people hidden. Reports where hips / head / hands / feet are.
public static class OfficeDiag
{
    public static string Run(string seat, string prefix)
    {
        AssetDatabase.Refresh();
        var all = Object.FindObjectsByType<OfficeListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        OfficeListener me = null;
        foreach (var l in all) { l.gameObject.SetActive(l.seat == seat); if (l.seat == seat) me = l; }
        var an = me.GetComponent<Animator>(); string r = "";
        var t = me.transform; var c = t.position + Vector3.up * 0.8f;
        foreach (var p in Directory.GetFiles("Assets/Office/Animations", prefix + "*.fbx").Select(x => x.Replace('\\', '/')))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview"));
            if (clip == null) { r += "no clip " + p + "\n"; continue; }
            string n = Path.GetFileNameWithoutExtension(p);
            clip.SampleAnimation(me.gameObject, clip.length * 0.35f);
            r += n + " len " + clip.length.ToString("F1") + " hips " + P(t, an, HumanBodyBones.Hips) + " head " + P(t, an, HumanBodyBones.Head) + " Lhand " + P(t, an, HumanBodyBones.LeftHand) + " Rhand " + P(t, an, HumanBodyBones.RightHand) + " Lfoot " + P(t, an, HumanBodyBones.LeftFoot) + " Lknee " + P(t, an, HumanBodyBones.LeftLowerLeg) + "\n";
            Snap("d_" + n + "_side", c + t.right * 2.3f + Vector3.up * 0.1f, c, 45f);
            Snap("d_" + n + "_front", c + t.forward * 2.2f + Vector3.up * 0.9f + t.right * 0.5f, c, 45f);
        }
        foreach (var l in all) l.gameObject.SetActive(true);
        return r;
    }
    // numbers over the whole clip: forward lean of the trunk (deg), how far forward / how high the hands get (character space, raw clip)
    public static string Measure(string seat, string prefix)
    {
        var all = Object.FindObjectsByType<OfficeListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        OfficeListener me = null; foreach (var l in all) if (l.seat == seat) me = l;
        var an = me.GetComponent<Animator>(); var t = me.transform; string r = "";
        var hips = an.GetBoneTransform(HumanBodyBones.Hips); var neck = an.GetBoneTransform(HumanBodyBones.Neck);
        var lh = an.GetBoneTransform(HumanBodyBones.LeftHand); var rh = an.GetBoneTransform(HumanBodyBones.RightHand);
        foreach (var p in Directory.GetFiles("Assets/Office/Animations", prefix + "*.fbx").Select(x => x.Replace('\\', '/')))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview"));
            if (clip == null) continue;
            float leanMin = 99, leanMax = -99, sideMax = 0, handZ = -9, handYmin = 9, handYmax = -9, hipY = 0; const int N = 40;
            for (int k = 0; k < N; k++)
            {
                clip.SampleAnimation(me.gameObject, clip.length * (k + 0.5f) / N);
                var v = t.InverseTransformDirection(neck.position - hips.position);
                float lean = Mathf.Atan2(v.z, v.y) * Mathf.Rad2Deg; leanMin = Mathf.Min(leanMin, lean); leanMax = Mathf.Max(leanMax, lean);
                sideMax = Mathf.Max(sideMax, Mathf.Abs(Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg));
                float hy = t.InverseTransformPoint(hips.position).y; hipY += hy / N;
                foreach (var h in new[] { lh, rh })
                {
                    var q = t.InverseTransformPoint(h.position);
                    handZ = Mathf.Max(handZ, q.z); handYmin = Mathf.Min(handYmin, q.y - hy); handYmax = Mathf.Max(handYmax, q.y - hy);
                }
            }
            r += Path.GetFileNameWithoutExtension(p) + " len " + clip.length.ToString("F0") + " lean " + leanMin.ToString("F0") + ".." + leanMax.ToString("F0") + " side " + sideMax.ToString("F0")
               + " handFwdMax " + handZ.ToString("F2") + " handAboveHips " + handYmin.ToString("F2") + ".." + handYmax.ToString("F2") + " hipY " + hipY.ToString("F2") + "\n";
        }
        return r;
    }
    static string P(Transform t, Animator an, HumanBodyBones b) { return t.InverseTransformPoint(an.GetBoneTransform(b).position).ToString("F2"); }
    static void Snap(string name, Vector3 pos, Vector3 target, float fov)
    {
        var go = new GameObject("ShotCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(target);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
        var rt = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(480, 480, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); tex.Apply(); RenderTexture.active = null;
        Directory.CreateDirectory("OfficeSetup/shots");
        File.WriteAllBytes("OfficeSetup/shots/" + name + ".png", tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
