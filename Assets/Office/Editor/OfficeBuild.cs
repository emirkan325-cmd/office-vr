using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the "Besprechung" scene (presentation anxiety) from the Blender export (Environment.fbx + materials.json via RoomImport):
// lights, probes, sky, wall clock, the people at the table, the standing XR rig at the head of the table and the session.
// Blender (x, y, z) -> Unity (-x, z, -y). Room: Unity x -6.6..0, z -4.8..0. Screen wall x = 0, windows z = -4.8.
public static partial class OfficeBuild
{
    public const string ScenePath = "Assets/Office/Scenes/Besprechung.unity";
    const string EnvRoot = "Assets/Office/Environment", EnvFbx = EnvRoot + "/Environment.fbx";
    const string Gen = "Assets/Office/Generated";
    const string HandsRig = "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
    const string SimPrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab";

    public static Vector3 U(float x, float y, float z) => new Vector3(-x, z, -y);

    public static string Execute()
    {
        var log = new StringBuilder();
        foreach (var d in new[] { Gen, "Assets/Office/Scenes", "Assets/Office/Materials" })
            if (!AssetDatabase.IsValidFolder(d)) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();

        log.AppendLine(RoomImport.Run(EnvRoot));
        TuneMaterials(log);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var env = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnvFbx), scene);
        env.name = "Environment";
        PrefabUtility.UnpackPrefabInstance(env, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        SetupEnvironment(env, log);

        var game = new GameObject("OfficeGame");
        new GameObject("XR Interaction Manager").AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>().transform.SetParent(game.transform);

        Lighting(env, log);
        var people = People(env, log);
        Clock(env, log);
        var rig = Rig(env, game, log);
        Session(env, game, rig, people, log);

        RenderSettings.skybox = Sky(log);
        RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.0f; RenderSettings.fog = false;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        log.AppendLine("scene saved " + ScenePath);
        return log.ToString();
    }

    public static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>(EnvRoot + "/Materials/" + n + ".mat");

    // ------------------------------------------------------------------ materials
    static void TuneMaterials(StringBuilder log)
    {
        // painted walls: flat white, only a faint plaster relief
        var wall = Mat("M_Wall");
        if (wall)
        {
            wall.SetTexture("_BaseMap", null); wall.SetColor("_BaseColor", new Color(0.93f, 0.93f, 0.91f));
            wall.SetTexture("_MetallicGlossMap", null); wall.DisableKeyword("_METALLICSPECGLOSSMAP");
            wall.SetFloat("_Metallic", 0f); wall.SetFloat("_Smoothness", 0.12f); wall.SetFloat("_BumpScale", 0.3f);
            EditorUtility.SetDirty(wall);
        }
        var glass = new (string name, Color c)[]
        {
            ("M_Glass", new Color(0.82f, 0.9f, 0.94f, 0.10f)), ("M_SoftGlass", new Color(0.86f, 0.92f, 0.92f, 0.22f)), ("M_BottleClear", new Color(0.85f, 0.92f, 0.94f, 0.32f)),
        };
        foreach (var (n, c) in glass)
        {
            var m = Mat(n); if (!m) { log.AppendLine("no " + n); continue; }
            RoomImport.MakeTransparent(m, c); m.SetFloat("_Smoothness", 0.94f); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
        }
        // LED ceiling panels + listeners' laptop screens glow, the baked area lights do the lighting
        var led = Mat("M_LEDPanel");
        if (led) { led.EnableKeyword("_EMISSION"); led.SetColor("_EmissionColor", new Color(1f, 0.97f, 0.92f) * 1.6f); led.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; EditorUtility.SetDirty(led); }
        var lap = Mat("M_LaptopScreen");
        if (lap) { lap.EnableKeyword("_EMISSION"); lap.SetColor("_BaseColor", new Color(0.1f, 0.12f, 0.15f)); lap.SetColor("_EmissionColor", new Color(0.62f, 0.72f, 0.88f) * 0.9f); lap.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; EditorUtility.SetDirty(lap); }
        // slide screens: unlit, the session swaps the texture
        var first = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Office/Resources/Slides/slide_0.png");
        foreach (var n in new[] { "M_Screen", "M_PatientScreen" })
        {
            var m = Mat(n); if (!m) { log.AppendLine("no " + n); continue; }
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetTexture("_BaseMap", first); m.SetColor("_BaseColor", n == "M_Screen" ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.8f, 0.8f, 0.8f));
            EditorUtility.SetDirty(m);
        }
        foreach (var p in Directory.GetFiles("Assets/Office/Resources/Slides", "*.png"))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(p.Replace('\\', '/'));
            if (ti == null || ti.anisoLevel == 8) continue;
            ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.filterMode = FilterMode.Trilinear; ti.maxTextureSize = 2048; ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        var carpet = Mat("M_Carpet");
        // clean office carpet: flat anthracite-blue, only the weave relief of the texture stays
        if (carpet) { carpet.SetTexture("_BaseMap", null); carpet.SetColor("_BaseColor", new Color(0.27f, 0.285f, 0.31f)); carpet.SetTexture("_MetallicGlossMap", null); carpet.DisableKeyword("_METALLICSPECGLOSSMAP"); carpet.SetFloat("_Metallic", 0f); carpet.SetFloat("_Smoothness", 0.05f); carpet.SetFloat("_BumpScale", 0.8f); EditorUtility.SetDirty(carpet); }
        AssetDatabase.SaveAssets();
        log.AppendLine("materials tuned");
    }

    // ------------------------------------------------------------------ environment
    static readonly string[] Moving = { "Clock_Hour", "Clock_Minute", "TV_Display", "PatientLaptop_Screen" };
    static readonly string[] SmallProps = { "Glass_", "Cup_", "Notepad_", "Laptop_", "Bottle_", "Plant_", "Marker_", "SB_", "Thermos_", "Clicker", "ConferencePhone", "Tray",
                                            "Sideboard_Tray", "Flipchart_Text", "PatientLaptop", "BoardEraser", "Clock_Body", "LightSwitch", "Door_Handle", "Table_CableBox", "LEDPanel_", "CeilingGrid", "Blind_", "Picture_" };

    static void SetupEnvironment(GameObject env, StringBuilder log)
    {
        int stat = 0;
        foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.name;
            bool moving = Moving.Contains(n);
            bool transparent = r.sharedMaterials.Any(m => m && m.renderQueue >= (int)RenderQueue.Transparent);
            bool small = SmallProps.Any(p => n.StartsWith(p));
            bool outside = n.StartsWith("Street");
            var flags = moving ? (StaticEditorFlags)0 : StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            if (!moving && !transparent && !small) flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
            r.receiveGI = (flags & StaticEditorFlags.ContributeGI) != 0 ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            r.shadowCastingMode = transparent || small && !n.StartsWith("Plant_") && !n.StartsWith("Laptop_") && !n.StartsWith("PatientLaptop") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (n == "Ceiling" || n.StartsWith("LEDPanel_")) r.shadowCastingMode = ShadowCastingMode.Off;
            if (flags != 0) stat++;
            var so = new SerializedObject(r);
            var sp = so.FindProperty("m_ScaleInLightmap");
            if (sp != null)
            {
                sp.floatValue = n == "Floor" || n.StartsWith("Wall_") || n == "Ceiling" || n == "Table" ? 1.0f : n.StartsWith("Chair_") ? 0.4f : outside ? 0.03f : 0.7f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        log.AppendLine($"environment: static {stat}");
    }

    static void Clock(GameObject env, StringBuilder log)
    {
        var body = Find(env.transform, "Clock_Body"); if (!body) { log.AppendLine("no clock"); return; }
        var c = body.gameObject.AddComponent<WallClock>();
        c.hour = Find(env.transform, "Clock_Hour"); c.minute = Find(env.transform, "Clock_Minute");
        c.faceNormal = Vector3.right;      // east wall (Unity x = -6.6), the face looks toward +x
        c.audible = false;
    }

    // ------------------------------------------------------------------ lighting
    static Light NewLight(string name, Transform parent, LightType type, Color c, float intensity)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var l = go.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; l.lightmapBakeType = LightmapBakeType.Baked; l.shadows = LightShadows.Soft;
        return l;
    }

    public static float PanelIntensity = 5.0f, SunIntensity = 2.2f, WindowFill = 3.4f;

    static void Lighting(GameObject env, StringBuilder log)
    {
        var root = new GameObject("Lights").transform;
        int n = 0;
        foreach (var t in env.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("LGT_Panel")).ToList())
        {
            var l = NewLight(t.name, root, LightType.Rectangle, new Color(1f, 0.96f, 0.90f), PanelIntensity);
            l.transform.position = t.position; l.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            l.areaSize = new Vector2(0.56f, 0.56f); l.range = 7f; n++;
        }
        // sun on the building across the street (the windows face north: only sky light comes in)
        var sun = NewLight("Sun", root, LightType.Directional, new Color(1f, 0.95f, 0.86f), SunIntensity);
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(-0.45f, -0.6f, -0.66f).normalized);
        sun.shadowAngle = 1.5f;
        foreach (var (name, cx) in new[] { ("WindowFill_0", 1.5f), ("WindowFill_1", 3.3f), ("WindowFill_2", 5.1f) })
        {
            var l = NewLight(name, root, LightType.Rectangle, new Color(0.84f, 0.90f, 1f), WindowFill);
            l.transform.position = U(cx, 4.78f, 1.65f); l.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.15f, 1f).normalized, Vector3.up);
            l.areaSize = new Vector2(1.4f, 1.45f); l.range = 8f;
        }
        log.AppendLine("lights: panels " + n);

        var rp = new GameObject("RP_Room").AddComponent<ReflectionProbe>(); rp.transform.SetParent(root);
        rp.transform.position = U(3.3f, 2.4f, 1.4f); rp.size = new Vector3(6.6f, 2.75f, 4.8f); rp.center = new Vector3(0, -0.025f, 0);
        rp.boxProjection = true; rp.mode = ReflectionProbeMode.Baked; rp.resolution = 256; rp.hdr = false;

        var pos = new List<Vector3>();
        for (float x = 0.3f; x <= 6.4f; x += 0.6f)
            for (float y = 0.3f; y <= 4.6f; y += 0.6f)
                foreach (var z in new[] { 0.25f, 0.8f, 1.25f, 1.75f, 2.5f }) pos.Add(U(x, y, z));
        var lpg = new GameObject("LightProbes").AddComponent<LightProbeGroup>(); lpg.transform.SetParent(root); lpg.probePositions = pos.ToArray();

        var ls = new LightingSettings { name = "OfficeLighting" };
        ls.bakedGI = true; ls.realtimeGI = false; ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = 30; ls.lightmapPadding = 4; ls.lightmapMaxSize = 2048; ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly; ls.directSampleCount = 64; ls.indirectSampleCount = 2048; ls.environmentSampleCount = 256;
        ls.maxBounces = 4; ls.filteringMode = LightingSettings.FilterMode.Auto; ls.lightmapCompression = LightmapCompression.NormalQuality;
        AssetDatabase.DeleteAsset(Gen + "/OfficeLighting.lighting");
        AssetDatabase.CreateAsset(ls, Gen + "/OfficeLighting.lighting");
        Lightmapping.lightingSettings = ls;
    }

    static Material Sky(StringBuilder log)
    {
        string hdr = EnvRoot + "/Sky_rustig_koppie_puresky_4k.hdr";
        var ti = (TextureImporter)AssetImporter.GetAtPath(hdr);
        ti.textureShape = TextureImporterShape.TextureCube; ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
        ti.maxTextureSize = 2048; ti.mipmapEnabled = true;
        var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = 1024; a.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(a);
        ti.SaveAndReimport();
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(hdr);
        string p = "Assets/Office/Materials/M_Sky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Skybox/Cubemap")); AssetDatabase.CreateAsset(m, p); }
        m.SetTexture("_Tex", cube); m.SetFloat("_Exposure", 0.85f); m.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(m);
        log.AppendLine("sky " + (cube != null));
        return m;
    }

    // ------------------------------------------------------------------ XR rig: the patient STANDS at the head of the table
    static GameObject Rig(GameObject env, GameObject game, StringBuilder log)
    {
        var spotM = Find(env.transform, "PATIENT_SPOT");
        var spot = new GameObject("PatientSpot").transform; spot.SetParent(game.transform);
        // faces Blender +x = Unity -x: down the table
        spot.SetPositionAndRotation(new Vector3(spotM.position.x, 0f, spotM.position.z), Quaternion.LookRotation(Vector3.left));

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HandsRig));
        var origin = rig.GetComponent<XROrigin>();
        rig.transform.SetPositionAndRotation(spot.position, spot.rotation);
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        var cam = origin.Camera;
        cam.nearClipPlane = 0.02f; cam.farClipPlane = 150f;
        if (cam.tag != "MainCamera") cam.tag = "MainCamera";
        var cd = cam.GetComponent<UniversalAdditionalCameraData>();
        if (cd == null) cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None; cd.requiresDepthTexture = false; cd.requiresColorTexture = false;
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var tn = mb.GetType().Name;
            if (tn.Contains("Locomotion") || tn.Contains("TurnProvider") || tn.Contains("MoveProvider") || tn.Contains("TeleportationProvider") || tn.Contains("ClimbProvider") || tn.Contains("GrabMoveProvider"))
                mb.enabled = false;
        }
        var cal = rig.AddComponent<StandingCalibration>(); cal.origin = origin; cal.speakerFloor = spot;
        var sim = game.AddComponent<SimulatorWhenNoHeadset>();
        var simPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimPrefab);
        if (simPrefab != null) { var so = (GameObject)PrefabUtility.InstantiatePrefab(simPrefab); so.SetActive(false); sim.simulator = so; }
        log.AppendLine("rig at " + rig.transform.position);
        return rig;
    }

    // ------------------------------------------------------------------ session wiring
    static void Session(GameObject env, GameObject game, GameObject rig, List<OfficeListener> people, StringBuilder log)
    {
        var s = game.AddComponent<OfficeSession>();
        s.listeners = people.ToArray();
        s.mic = game.AddComponent<MicInput>();
        s.patientHead = rig.GetComponent<XROrigin>().Camera.transform;
        s.tv = Find(env.transform, "TV_Display").GetComponent<MeshRenderer>();
        s.laptop = Find(env.transform, "PatientLaptop_Screen").GetComponent<MeshRenderer>();
        s.phone = Find(env.transform, "ConferencePhone");
        s.slides = Directory.GetFiles("Assets/Office/Resources/Slides", "*.png").OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p.Replace('\\', '/'))).ToArray();
        foreach (var l in people) l.patientHead = s.patientHead;
        log.AppendLine("session: slides " + s.slides.Length + ", listeners " + people.Count);
    }
}
