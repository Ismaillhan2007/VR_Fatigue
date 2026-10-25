using System;
using App.Quest.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

public class QuestBuildSettingsTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void ManifestOverridesRequiredEyesAndPreservesPermission(bool existing)
    {
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml("<manifest xmlns:android='http://schemas.android.com/apk/res/android'>"
            + (existing ? "<uses-feature android:name='oculus.software.eye_tracking' android:required='true'/>" : "")
            + "<uses-permission android:name='com.oculus.permission.EYE_TRACKING'/></manifest>");
        QuestAndroidManifest.KeepEyeTrackingOptional(xml);
        QuestAndroidManifest.KeepEyeTrackingOptional(xml);
        var feature = (System.Xml.XmlElement)xml.SelectSingleNode("/manifest/uses-feature");
        Assert.AreEqual(1, xml.SelectNodes("/manifest/uses-feature").Count);
        Assert.AreEqual("false", feature.GetAttribute("required", "http://schemas.android.com/apk/res/android"));
        Assert.AreEqual("android:required", feature.GetAttribute("replace", "http://schemas.android.com/tools"));
        Assert.AreEqual(1, xml.SelectNodes("/manifest/uses-permission").Count);
    }

    [TestCase("touch")]
    [TestCase("keyboard")]
    [TestCase("architecture")]
    [TestCase("minimum")]
    [TestCase("target")]
    [TestCase("backend")]
    public void RejectsIncompleteAndroidSetup(string broken)
    {
        QuestAssessmentBuild.ApplySettings();
        Assert.IsEmpty(QuestAssessmentBuild.Validate());
        Action restore;
        var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        switch (broken)
        {
            case "touch":
                var touch = xr.GetFeature<OculusTouchControllerProfile>(); touch.enabled = false;
                restore = () => touch.enabled = true; break;
            case "keyboard":
                var quest = xr.GetFeature<MetaQuestFeature>(); quest.enableSystemKeyboard = false;
                restore = () => quest.enableSystemKeyboard = true; break;
            case "architecture":
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
                restore = () => PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; break;
            case "minimum":
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                restore = () => PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32; break;
            case "target":
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                restore = () => PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36; break;
            default:
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
                restore = () =>
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                }; break;
        }
        try { Assert.IsNotEmpty(QuestAssessmentBuild.Validate(), broken); }
        finally { restore(); }
        Assert.IsEmpty(QuestAssessmentBuild.Validate());
    }
}
