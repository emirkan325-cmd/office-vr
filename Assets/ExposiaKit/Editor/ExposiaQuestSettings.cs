using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Quest 3 standalone project settings (build target, player, quality, URP asset).
public static class ExposiaQuestSettings
{
    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"target {EditorUserBuildSettings.activeBuildTarget}, colorspace {PlayerSettings.colorSpace}");
        var names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            var rp = QualitySettings.GetRenderPipelineAssetAt(i);
            sb.AppendLine($"quality[{i}] {names[i]} -> {(rp ? AssetDatabase.GetAssetPath(rp) : "default")}");
        }
        sb.AppendLine("default RP: " + AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline));
        return sb.ToString();
    }

    public static string Execute()
    {
        var sb = new StringBuilder();

        // player
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
        PlayerSettings.companyName = "VRTherapy";
        PlayerSettings.productName = ExposiaFirstRun.ProductName();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.vrtherapy." + ExposiaFirstRun.AppSuffix());
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        // one quality level for Android: the lightest URP asset
        var names = QualitySettings.names;
        int mobile = System.Array.FindIndex(names, n => n.ToLower().Contains("mobile"));
        if (mobile < 0) mobile = 0;
        var rp = QualitySettings.GetRenderPipelineAssetAt(mobile) as UniversalRenderPipelineAsset
                 ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        QualitySettings.SetQualityLevel(mobile, true);
        var so = new SerializedObject(QualitySettings.GetQualitySettings());
        var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
        for (int i = 0; i < perPlatform.arraySize; i++)
        {
            var e = perPlatform.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == "Android")
                e.FindPropertyRelative("second").intValue = mobile;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        if (rp != null)
        {
            rp.supportsHDR = false;
            rp.msaaSampleCount = 4;
            rp.renderScale = 1f;
            rp.supportsCameraDepthTexture = false;
            rp.supportsCameraOpaqueTexture = false;
            rp.shadowDistance = 10f;
            rp.shadowCascadeCount = 1;
            // everything is baked: no realtime main/additional light shadows needed
            var rso = new SerializedObject(rp);
            Set(rso, "m_MainLightShadowsSupported", false);
            Set(rso, "m_AdditionalLightShadowsSupported", false);
            Set(rso, "m_SoftShadowsSupported", false);
            Set(rso, "m_AdditionalLightsRenderingMode", 0); // disabled
            Set(rso, "m_ReflectionProbeBlending", true);
            Set(rso, "m_ReflectionProbeBoxProjection", true);
            Set(rso, "m_UseSRPBatcher", true);
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            sb.AppendLine("URP asset tuned: " + AssetDatabase.GetAssetPath(rp));
        }

        // camera: no post-processing
        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>(); if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false; data.requiresColorTexture = false;
        }

        AssetDatabase.SaveAssets();
        sb.AppendLine($"quality level for Android: {names[mobile]}");

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            sb.AppendLine("switch to Android: " + ok);
        }
        return sb.ToString();
    }

    static void Set(SerializedObject so, string prop, bool v) { var p = so.FindProperty(prop); if (p != null) p.boolValue = v; }
    static void Set(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }
}
