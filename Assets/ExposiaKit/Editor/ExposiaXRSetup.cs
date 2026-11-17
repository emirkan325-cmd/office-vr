using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

// OpenXR for Quest 3 (Android) and Quest Link (Standalone, for testing from the editor).
public static class ExposiaXRSetup
{
    const string OpenXRLoaderName = "UnityEngine.XR.OpenXR.OpenXRLoader";

    [MenuItem("Tools/ExposiaVR/Configure Quest 3 (OpenXR)")]
    public static void Menu() { Debug.Log(Run()); }

    public static string Validate()
    {
        var issues = new System.Collections.Generic.List<UnityEngine.XR.OpenXR.Features.OpenXRFeature.ValidationRule>();
        UnityEditor.XR.OpenXR.OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
        var sb = new StringBuilder("android openxr issues=" + issues.Count + "\n");
        foreach (var i in issues) sb.AppendLine((i.error ? "ERROR: " : "warn: ") + i.message);
        return sb.ToString();
    }

    public static string Run()
    {
        var log = new StringBuilder();

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget bts) || bts == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
            bts = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(bts, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, bts, true);
            log.AppendLine("created XRGeneralSettingsPerBuildTarget");
        }
        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
        {
            var settings = bts.SettingsForBuildTarget(group);
            if (settings == null) { bts.CreateDefaultManagerSettingsForBuildTarget(group); settings = bts.SettingsForBuildTarget(group); }
            if (settings == null) { log.AppendLine($"{group}: could not create XR settings"); continue; }
            settings.InitManagerOnStart = true;
            bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoaderName, group);
            log.AppendLine($"{group}: OpenXR loader assigned={ok}");
        }

        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
        {
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            if (group == BuildTargetGroup.Android)
            {
                var set = UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager.GetFeatureSetWithId(group, "com.unity.openxr.featureset.metaquest");
                if (set != null) { set.isEnabled = true; UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group); log.AppendLine("Android: Meta Quest feature set enabled"); }
            }
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) { log.AppendLine($"{group}: no OpenXR settings"); continue; }
            var touch = oxr.GetFeature<OculusTouchControllerProfile>();
            if (touch != null) { touch.enabled = true; EditorUtility.SetDirty(touch); }
            if (group == BuildTargetGroup.Android)
            {
                var quest = oxr.GetFeature<MetaQuestFeature>();
                if (quest != null) { quest.enabled = true; EditorUtility.SetDirty(quest); log.AppendLine("Android: Meta Quest Support enabled"); }
            }
            foreach (var f in oxr.GetFeatures())
            {
                var tn = f.GetType().Name;
                if (tn == "HandTracking" || tn == "MetaHandTrackingAim" || tn == "HandInteractionProfile") { f.enabled = true; EditorUtility.SetDirty(f); log.AppendLine($"{group}: {tn} on"); }
            }
            oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(oxr);
            log.AppendLine($"{group}: touch profile={(touch != null)}, single-pass instanced");
        }

        PlayerSettings.Android.startInFullscreen = true;
        AssetDatabase.SaveAssets();
        return log.ToString();
    }
}
