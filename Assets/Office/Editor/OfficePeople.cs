using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// The seven meeting participants (Microsoft Rocketbox business avatars, MIT licence) on the cantilever chairs.
// Animations: only the Rocketbox "sit_table" idles; looking, nodding and knocking on the table are done in OfficeListener.
public static partial class OfficeBuild
{
    const string CharDir = "Assets/Office/Characters/", AnimDir = "Assets/Office/Animations/";

    // seat -> avatar, first level in which the person attends, role (boss / woman / man speak the question lines)
    public static readonly (string seat, string who, int level, string role)[] Seating =
    {
        ("E",  "Business_Male_03",   1, "boss"),
        ("S0", "Business_Female_01", 1, "woman"),
        ("N0", "Business_Male_01",   1, "man"),
        ("N1", "Business_Female_03", 1, ""),
        ("S1", "Business_Male_02",   2, ""),
        ("S2", "Business_Female_04", 2, ""),
        ("N2", "Business_Male_04",   2, ""),
    };

    // upright chair-sitting idles (Rocketbox "sit_chair", long and calm); "Clap" only if a Mixamo sitting clap was added
    static readonly (string state, string clip)[] States =
    {
        ("Sit", "sit_chair_idle_neutral_01"), ("Sit2", "sit_chair_idle_neutral_02"), ("Relaxed", "sit_chair_idle_relaxed_01"),
        ("LookAround", "sit_chair_idle_look_around"), ("Waiting", "sit_chair_idle_waiting_01"),
    };   // not used: m_sit_chair_idle_waiting_02 (bends 63 deg forward), *_relaxed is kept upright by the posture limit
    public const float ChairTurn = 22f;      // side chairs are turned this much toward the presenter
    public const float ChairBack = 0.10f;    // and stand this much further from the table than in the Blender file
    // extra distance for seats whose animated hands still reached into the table (measured with OfficeSetup/Hands.cs)
    // 2026-11-16: the male sit_chair clips hold the hands forward at table-top height, 30-32 cm past the edge -> chair back
    // (E at the head keeps its place: there the hands rest on the table top and look fine)
    // tried +0.34 (hands clear of the table): then the hands hover in the air -> instead closer, so they rest ON the top like E
    static float ExtraBack(string seat) => AllBack + (seat == "N0" || seat == "N2" || seat == "S1" ? ManBack : 0f);
    public static float ManBack = 0.07f;   // men's longer forearms came within 0.1 cm of the edge in the relaxed idle
    // lap idles (f_sit_chair neutral_01/02) lift a hand for a moment; at the old distance a finger touched the edge ~1% of frames
    public static float AllBack = 0.06f;

    static List<OfficeListener> People(GameObject env, StringBuilder log)
    {
        log.AppendLine(ImportCharacters());
        ImportClips(log);
        // still mode: the men get the women's sit_chair clips too (hands in the lap); the male ones hold the forearms at
        // 0.70 m = inside a 0.74 table top in every frame
        var cm = Ctrl("Listener_M", OfficeListener.Still ? 'f' : 'm', log); var cf = Ctrl("Listener_F", 'f', log);
        var root = new GameObject("People").transform;
        var patient = Find(env.transform, "PATIENT_SPOT").position; patient.y = 0f;
        var list = new List<OfficeListener>();
        foreach (var (seat, who, level, role) in Seating)
        {
            var c = Find(env.transform, "SEAT_" + seat); if (!c) { log.AppendLine("no seat " + seat); continue; }
            bool female = who.Contains("Female");
            // straight at the table first (side seats across it, the head seat down the table) ...
            Vector3 baseFwd = seat == "E" ? Vector3.right : seat.StartsWith("S") ? Vector3.back : Vector3.forward;
            var pos = new Vector3(c.position.x, 0f, c.position.z) - baseFwd * (ChairBack + ExtraBack(seat));
            // ... then the chair is turned toward the presenter, as people do in a meeting
            var fwd = seat == "E" ? baseFwd : Vector3.RotateTowards(baseFwd, (patient - pos).normalized, ChairTurn * Mathf.Deg2Rad, 0f);
            var chair = Find(env.transform, "Chair_" + seat);
            if (chair)
            {
                chair.rotation = Quaternion.FromToRotation(baseFwd, fwd) * chair.rotation;
                chair.position = new Vector3(pos.x, chair.position.y, pos.z);
            }
            var go = Person(who, "Listener_" + seat, pos, Quaternion.LookRotation(fwd), female ? cf : cm, root);
            var l = go.AddComponent<OfficeListener>(); l.seat = seat; l.minLevel = level; l.role = role; l.female = female;
            l.seatHeight = c.position.y;
            l.calmOnly = seat == "N0" || seat == "S0";   // front seats right beside the presenter: calm idles only
            var tableR = Find(env.transform, "Table").GetComponent<Renderer>();
            l.hasTable = tableR != null; if (tableR) l.table = tableR.bounds;
            // bind pose: the face looks along the character's forward axis
            var hb = go.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            l.headForwardLocal = hb.InverseTransformDirection(go.transform.forward);
            MeasureClips(go, l, log);
            // what lies in front of this person on the table (laptop or notepad): the place to look down at
            var item = Find(env.transform, "Laptop_" + seat); if (!item) item = Find(env.transform, "Notepad_" + seat);
            var r = item ? item.GetComponent<Renderer>() : null;
            l.deskPoint = r ? r.bounds.center : go.transform.position + baseFwd * 0.5f + Vector3.up * 0.76f;
            l.usesLaptop = item && item.name.StartsWith("Laptop");
            l.awayPoint = U(seat.StartsWith("N") ? 6.5f : 3.3f, seat.StartsWith("N") ? 0.75f : 4.8f, seat.StartsWith("N") ? 2.2f : 1.6f);   // the clock / the window
            list.Add(l);
        }
        foreach (var l in list)
        {
            // neighbours: next seat on the same side (whispering partner)
            l.neighbours = list.Where(o => o != l && o.seat[0] == l.seat[0] && o.seat.Length > 1 && l.seat.Length > 1 && Mathf.Abs(o.seat[1] - l.seat[1]) == 1).ToArray();
        }
        log.AppendLine("people " + list.Count);
        return list;
    }

    // Where does each clip put the hips? (average over the clip, character space) -> OfficeListener shifts the body onto the seat.
    static void MeasureClips(GameObject go, OfficeListener l, StringBuilder log)
    {
        var an = go.GetComponent<Animator>(); var hips = an.GetBoneTransform(HumanBodyBones.Hips);
        var clips = an.runtimeAnimatorController.animationClips.Distinct().ToArray();
        l.clipNames = clips.Select(c => c.name).ToArray(); l.clipHips = new Vector3[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            Vector3 sum = Vector3.zero; const int N = 12;
            for (int k = 0; k < N; k++) { clips[i].SampleAnimation(go, clips[i].length * (k + 0.5f) / N); sum += go.transform.InverseTransformPoint(hips.position); }
            l.clipHips[i] = sum / N;
        }
        clips[0].SampleAnimation(go, 0.5f);
        if (l.seat == "E" || l.seat == "S0") log.AppendLine(l.seat + " hips: " + string.Join(", ", clips.Select((c, i) => c.name + " " + l.clipHips[i].ToString("F2"))));
    }

    // ------------------------------------------------------------------ import
    static string ImportCharacters()
    {
        var log = new StringBuilder();
        foreach (var dir in Directory.GetDirectories(CharDir))
        {
            var name = Path.GetFileName(dir); var d = CharDir + name + "/";
            var fbx = d + name + ".fbx"; if (!File.Exists(fbx)) continue;
            if (File.Exists(d + "M_" + name + "_body.mat")) { log.AppendLine(name + ": already imported"); continue; }
            var tex = Directory.GetFiles(d + "Textures").Where(x => x.EndsWith(".tga") || x.EndsWith(".png")).Select(x => x.Replace('\\', '/')).ToArray();
            foreach (var path in tex)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path); if (ti == null) continue;
                var f = Path.GetFileNameWithoutExtension(path);
                bool nrm = f.EndsWith("_normal"), opa = f.Contains("opacity"), headC = f.Contains("head_color");
                ti.textureType = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !nrm; ti.mipmapEnabled = true; ti.anisoLevel = 2; ti.maxTextureSize = 2048;
                ti.alphaSource = opa ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = opa;
                var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true;
                a.maxTextureSize = 1024;                                             // the listeners sit close: 1k bodies + heads
                a.format = headC ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(a); ti.SaveAndReimport();
            }
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.SaveAndReimport();
            string T(string part) => tex.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Contains(part));
            foreach (var em in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbx).OfType<Material>().Select(m => m.name).Distinct().ToList())
            {
                string low = em.ToLower();
                string part = low.Contains("head") ? "head" : low.Contains("opacity") || low.Contains("hair") || low.Contains("lash") ? "opacity" : "body";
                var matPath = d + "M_" + name + "_" + part + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, matPath); }
                m.shader = Shader.Find("Universal Render Pipeline/Lit"); m.shaderKeywords = new string[0];
                if (part == "opacity")
                {
                    var o = T("opacity_color");
                    m.SetTexture("_BaseMap", o != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(o) : null);
                    m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.58f); m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cull", 0); m.renderQueue = (int)RenderQueue.AlphaTest; m.SetFloat("_Smoothness", 0.2f);
                }
                else
                {
                    var c = T(part + "_color");
                    m.SetTexture("_BaseMap", c != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(c) : null);
                    var n = T(part + "_normal");
                    if (n != null) { m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(n)); m.EnableKeyword("_NORMALMAP"); }
                    m.SetFloat("_Smoothness", part == "head" ? 0.36f : 0.22f);
                }
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Metallic", 0f); m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), em), m);
            }
            mi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            log.AppendLine($"{name}: avatar valid={(av && av.isValid && av.isHuman)}");
        }
        return log.ToString();
    }

    static void ImportClips(StringBuilder log)
    {
        int n = 0;
        foreach (var p in Directory.GetFiles(AnimDir, "*.fbx").Select(x => x.Replace('\\', '/')))
        {
            var name = Path.GetFileNameWithoutExtension(p);
            var mi = (ModelImporter)AssetImporter.GetAtPath(p);
            if (mi.animationType == ModelImporterAnimationType.Human && mi.clipAnimations.Length > 0 && mi.clipAnimations[0].name == name && mi.clipAnimations[0].loopTime) { n++; continue; }
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true; mi.importCameras = false; mi.importLights = false; mi.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name; c.loopTime = true;
                c.lockRootRotation = true; c.keepOriginalOrientation = true; c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips; mi.SaveAndReimport(); n++;
        }
        log.AppendLine("clips " + n);
    }

    static AnimatorController Ctrl(string name, char g, StringBuilder log)
    {
        string path = Gen + "/" + name + ".controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var layers = ctrl.layers; layers[0].iKPass = true; ctrl.layers = layers;
        var sm = ctrl.layers[0].stateMachine;
        foreach (var (st, cl) in States)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimDir + g + "_" + cl + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) { log.AppendLine("missing clip " + g + "_" + cl); continue; }
            var s = sm.AddState(st); s.motion = clip; s.iKOnFeet = false;
            if (sm.defaultState == null) sm.defaultState = s;
        }
        var clap = AssetDatabase.LoadAllAssetsAtPath(AnimDir + "Seated_Clap.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        if (clap != null) { var cs = sm.AddState("Clap"); cs.motion = clap; cs.iKOnFeet = false; }
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static GameObject Person(string name, string label, Vector3 pos, Quaternion rot, AnimatorController ctrl, Transform parent)
    {
        var fbx = CharDir + name + "/" + name + ".fbx";
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        go.name = label; go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var an = go.GetComponent<Animator>();
        if (an == null) an = go.AddComponent<Animator>();
        an.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First(); an.runtimeAnimatorController = ctrl; an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.quality = SkinQuality.Bone4; smr.shadowCastingMode = ShadowCastingMode.Off; smr.skinnedMotionVectors = false;
            smr.lightProbeUsage = LightProbeUsage.BlendProbes; smr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            smr.updateWhenOffscreen = false;
        }
        var ss = go.AddComponent<StartState>(); ss.state = "Sit";
        return go;
    }
}
