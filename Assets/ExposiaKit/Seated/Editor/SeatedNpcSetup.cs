using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// ExposiaKit - editor side of SeatedNpc: measure the clips, judge new clips, photograph the result.
// RULE for every game with seated people: after building the scene run Photograph() in play mode and LOOK at the sheet
// before giving the game to anyone.
public static class SeatedNpcSetup
{
    // Fill clipNames / clipHips / headForwardLocal. Call right after instantiating the character (still in its bind pose),
    // with the Animator controller already assigned.
    public static void Measure(GameObject go)
    {
        var npc = go.GetComponent<SeatedNpc>(); var an = go.GetComponent<Animator>();
        var hips = an.GetBoneTransform(HumanBodyBones.Hips); var head = an.GetBoneTransform(HumanBodyBones.Head);
        npc.headForwardLocal = head.InverseTransformDirection(go.transform.forward);
        var clips = an.runtimeAnimatorController.animationClips.Distinct().ToArray();
        npc.clipNames = clips.Select(c => c.name).ToArray(); npc.clipHips = new Vector3[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            Vector3 sum = Vector3.zero; const int N = 12;
            for (int k = 0; k < N; k++) { clips[i].SampleAnimation(go, clips[i].length * (k + 0.5f) / N); sum += go.transform.InverseTransformPoint(hips.position); }
            npc.clipHips[i] = sum / N;
        }
        if (clips.Length > 0) clips[0].SampleAnimation(go, 0.5f);
        EditorUtility.SetDirty(npc);
    }

    // Is a clip usable for sitting at a table? Numbers over the whole clip, sampled on 'go' (any humanoid of the right gender):
    // trunk lean forward (deg, + = forward), sideways lean, how far forward / how high above the hips the hands get.
    // Good listeners' clips: lean within about -17..+12, hands forward < 0.40. Reject clips with lean > 30.
    public static string Judge(GameObject go, string folder, string prefix)
    {
        var an = go.GetComponent<Animator>(); var t = go.transform; var sb = new StringBuilder();
        var hips = an.GetBoneTransform(HumanBodyBones.Hips); var neck = an.GetBoneTransform(HumanBodyBones.Neck);
        var lh = an.GetBoneTransform(HumanBodyBones.LeftHand); var rh = an.GetBoneTransform(HumanBodyBones.RightHand);
        foreach (var p in Directory.GetFiles(folder, prefix + "*.fbx").Select(x => x.Replace('\\', '/')))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview"));
            if (clip == null) continue;
            float leanMin = 99, leanMax = -99, sideMax = 0, handZ = -9, handYmin = 9, handYmax = -9, hipY = 0; const int N = 40;
            for (int k = 0; k < N; k++)
            {
                clip.SampleAnimation(go, clip.length * (k + 0.5f) / N);
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
            sb.AppendLine(Path.GetFileNameWithoutExtension(p) + "  len " + clip.length.ToString("F0") + "s  lean " + leanMin.ToString("F0") + ".." + leanMax.ToString("F0")
                + "  side " + sideMax.ToString("F0") + "  handsForwardMax " + handZ.ToString("F2") + "  handsAboveHips " + handYmin.ToString("F2") + ".." + handYmax.ToString("F2")
                + "  hipsY " + hipY.ToString("F2") + (leanMax > 30 ? "   <-- REJECT (bends too far)" : ""));
        }
        return sb.ToString();
    }

    // PLAY MODE: a side and a front picture of every active SeatedNpc with the others hidden -> <outDir>/seated_<name>_<tag>_s|f.png
    // 'focus' = a point the group sits around (table centre); the front camera stands there, above the table.
    public static string Photograph(string outDir, string tag, Vector3 focus)
    {
        Directory.CreateDirectory(outDir);
        var all = Object.FindObjectsByType<SeatedNpc>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        foreach (var n in all)
        {
            foreach (var o in all) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = o == n;
            var t = n.transform; var c = t.position + Vector3.up * 0.75f;
            var to = focus - t.position; to.y = 0f; if (to.magnitude < 0.5f) to = t.forward; to.Normalize();
            var side = Vector3.Cross(Vector3.up, to);
            Snap(outDir + "/seated_" + n.name + "_" + tag + "_s.png", c + side * 2.4f - to * 0.15f + Vector3.up * 0.25f, c);
            Snap(outDir + "/seated_" + n.name + "_" + tag + "_f.png", c + to * 1.7f + side * 0.5f + Vector3.up * 1.0f, c + Vector3.up * 0.1f);
            sb.Append(n.name).Append(' ');
        }
        foreach (var o in all) foreach (var m in o.GetComponentsInChildren<SkinnedMeshRenderer>()) m.enabled = true;
        return sb.ToString();
    }

    static void Snap(string path, Vector3 pos, Vector3 target)
    {
        var go = new GameObject("SeatedShotCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(target);
        cam.fieldOfView = 42f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
        var rt = new RenderTexture(480, 480, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(480, 480, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 480, 480), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
