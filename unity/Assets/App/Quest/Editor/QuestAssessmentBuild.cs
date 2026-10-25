using System;
using System.IO;
using System.Linq;
using App.UI;
using App.UI.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using VRFatigue.QuestPro.Editor;

namespace App.Quest.Editor
{
    /// <summary>Explicit setup/build; no import-time mutations or implicit merge of report-only dependencies.</summary>
    public sealed class QuestAssessmentBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -900;
        const string Identifier = "com.vrfatigue.prototype";

        [MenuItem("App/Quest Pro/Apply Android Assessment Settings")]
        public static void ApplySettings()
        {
            QuestProRenderingSetup.Apply();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, Identifier);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var touch = settings.GetFeature<OculusTouchControllerProfile>();
            if (touch == null) throw new BuildFailedException("Restore the pinned OpenXR Oculus Touch profile");
            touch.enabled = true;
            EditorUtility.SetDirty(touch);
            var quest = settings.GetFeature<MetaQuestFeature>();
            quest.enableSystemKeyboard = true;
            EditorUtility.SetDirty(quest);
            AssetDatabase.SaveAssets();
            Debug.Log("Android assessment settings applied; publish ProjectSettings/XR asset changes in a separate chore PR.");
        }

        [MenuItem("App/Quest Pro/Create UI Ray Material")]
        public static void CreateResources()
        {
            const string folder = "Assets/App/Quest/Resources";
            const string path = folder + "/QuestRayMaterial.mat";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new BuildFailedException("The pinned URP Unlit shader is missing");
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(0.3f, 0.85f, 1));
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
        }

        public static string[] Validate()
        {
            var errors = QuestProRenderingSetup.Validate();
            if (PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                errors.Add("Android requires IL2CPP");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64) errors.Add("Select ARM64 only");
            if ((int)PlayerSettings.Android.minSdkVersion < 32) errors.Add("Select Android minimum API 32 or newer");
            if ((int)PlayerSettings.Android.targetSdkVersion != 36) errors.Add("Install and select Android target API 36");
            if (OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android)
                ?.GetFeature<OculusTouchControllerProfile>()?.enabled != true) errors.Add("Enable Android Oculus Touch Controller Profile");
            if (OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android)
                ?.GetFeature<MetaQuestFeature>()?.enableSystemKeyboard != true) errors.Add("Enable the Meta Quest system keyboard");
            if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) != Identifier)
                errors.Add("Use the VR Fatigue prototype application identifier");
            return errors.ToArray();
        }

        [MenuItem("App/Quest Pro/Build Assessment Development APK")]
        public static void BuildDevelopmentApk()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Unity 6000.6.7f1 Android Build Support with SDK/NDK/OpenJDK first");
            var errors = Validate();
            if (errors.Length > 0) throw new BuildFailedException(string.Join("; ", errors));
            // This branch deliberately does not copy or merge #16. Final report-only assets need that dependency.
            if (typeof(SessionController).GetMethod("FinishWithReport") == null)
                throw new BuildFailedException("Integrate report-only completion (#16 / PR #26) before generating the Quest assessment APK");
            CreateResources();
            AssessmentSceneBuilder.BuildAll();
            var output = Environment.GetEnvironmentVariable("VR_QUEST_APK_PATH");
            if (string.IsNullOrWhiteSpace(output)) output = Path.GetFullPath("../builds/quest/VRFatigue-development.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.Android, locationPathName = output,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Quest APK build failed: " + report.summary.result);
            Debug.Log("Quest APK built: " + report.summary.outputPath + "; bytes=" + report.summary.totalSize);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var errors = Validate();
            if (errors.Length > 0) throw new BuildFailedException(string.Join("; ", errors));
        }
    }
}
