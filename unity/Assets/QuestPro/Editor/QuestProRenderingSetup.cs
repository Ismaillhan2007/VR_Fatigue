using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.Management;

namespace VRFatigue.QuestPro.Editor
{
    // Explicit menu only: never rewrite settings on import or silently during a build.
    public sealed class QuestProRenderingSetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        private const string Menu = "App/Quest Pro/Apply 90 Hz Meta Foveation Settings";

        [MenuItem(Menu)]
        public static void Apply()
        {
            if (Application.unityVersion != "6000.6.7f1")
                throw new InvalidOperationException("Apply XR settings from the project's required Unity 6000.6.7f1 editor.");
#if VR_FATIGUE_META_207
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new InvalidOperationException("Install Android Build Support and enable Android OpenXR first.");
            var fixedFeature = settings.GetFeature<Meta.XR.MetaXRFoveationFeature>();
            var eyeFeature = settings.GetFeature<Meta.XR.MetaXREyeTrackedFoveationFeature>();
            var config = OVRProjectConfig.CachedProjectConfig;
            if (fixedFeature == null || eyeFeature == null || config == null)
                throw new InvalidOperationException("Restore Meta XR Core 207.0.1 and its Android OpenXR features first.");
            ConfigureLoader();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced; // Multiview on Quest.
            settings.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.Legacy;
            fixedFeature.enabled = true;
            // The SDK's callback uses the active editor target, which may be desktop.
            // Preserve that target's feature when configuring Android from a desktop editor.
            var activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var otherFeature = activeGroup == BuildTargetGroup.Android ? null :
                OpenXRSettings.GetSettingsForBuildTargetGroup(activeGroup)?.GetFeature<Meta.XR.MetaXRFoveationFeature>();
            bool otherEnabled = otherFeature != null && otherFeature.enabled;
            eyeFeature.enabled = true;
            if (otherFeature != null && otherFeature.enabled != otherEnabled)
            { otherFeature.enabled = otherEnabled; EditorUtility.SetDirty(otherFeature); }
            var unityFeature = settings.GetFeature<FoveatedRenderingFeature>();
            if (unityFeature != null) { unityFeature.enabled = false; EditorUtility.SetDirty(unityFeature); }
            config.eyeTrackingSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(config);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(fixedFeature); EditorUtility.SetDirty(eyeFeature);
            AssetDatabase.SaveAssets();
            var errors = Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("Quest Pro foveation configured. Commit settings separately on a chore/ branch. " +
                "A runtime controller requests 90 Hz; device performance still requires measurement.");
#else
            throw new InvalidOperationException("Meta XR Core 207.0.1 is required.");
#endif
        }

        private static void ConfigureLoader()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget all);
            if (all == null)
            {
                var found = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (found.Length > 1) throw new InvalidOperationException("Multiple XR settings assets; select the shared one in XR Plug-in Management.");
                if (found.Length == 1) all = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                    AssetDatabase.GUIDToAssetPath(found[0]));
                else
                {
                    all = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(all, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, all, true);
            }
            if (!all.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                all.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!all.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                all.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var xr = all.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (xr.Manager.activeLoaders.Any(loader => !(loader is OpenXRLoader)))
                throw new InvalidOperationException("Disable other Android XR loaders before selecting the Meta OpenXR path.");
            xr.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(xr.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not assign the Android OpenXR loader.");
            EditorUtility.SetDirty(all); EditorUtility.SetDirty(xr); EditorUtility.SetDirty(xr.Manager);
        }

        public static List<string> Validate()
        {
            var errors = new List<string>();
            if (Application.unityVersion != "6000.6.7f1") errors.Add("Build with Unity 6000.6.7f1");
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ||
                apis.Length != 1 || apis[0] != GraphicsDeviceType.Vulkan)
                errors.Add("Android must use Vulkan only, with Auto Graphics API disabled");
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (xr == null || xr.Manager == null || !xr.InitManagerOnStart ||
                xr.Manager.activeLoaders.Count != 1 || !(xr.Manager.activeLoaders[0] is OpenXRLoader))
                errors.Add("Android OpenXR loader must initialize on startup");
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) { errors.Add("Android OpenXR settings missing"); return errors; }
            if (settings.renderMode != OpenXRSettings.RenderMode.SinglePassInstanced)
                errors.Add("Android OpenXR must use Multiview (SinglePassInstanced)");
            if (settings.foveatedRenderingApi != OpenXRSettings.BackendFovationApi.Legacy)
                errors.Add("Select the Legacy backend for this Meta API controller");
            if (settings.GetFeature<FoveatedRenderingFeature>()?.enabled == true)
                errors.Add("Disable Unity Foveated Rendering to avoid competing controllers");
#if VR_FATIGUE_META_207
            if (settings.GetFeature<Meta.XR.MetaXRFoveationFeature>()?.enabled != true ||
                settings.GetFeature<Meta.XR.MetaXREyeTrackedFoveationFeature>()?.enabled != true)
                errors.Add("Enable both Meta XR Foveation and Meta XR Eye Tracked Foveation for Android");
            if (OVRProjectConfig.CachedProjectConfig?.eyeTrackingSupport == OVRProjectConfig.FeatureSupport.None ||
                OVRProjectConfig.CachedProjectConfig == null)
                errors.Add("Declare optional eye tracking support in the Meta project config");
#else
            errors.Add("Meta XR Core 207.0.1 required");
#endif
            return errors;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var errors = Validate();
            if (errors.Count > 0) throw new BuildFailedException("Quest Pro rendering setup: " +
                string.Join("; ", errors) + ". Run " + Menu + " in Unity 6000.6.7f1.");
        }
    }
}
