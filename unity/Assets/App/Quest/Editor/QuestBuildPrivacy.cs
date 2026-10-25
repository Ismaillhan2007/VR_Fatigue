using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace App.Quest.Editor
{
    // Meta 207 injects the editor AgentBridge token/IP at callback order 1, even when its UI is disabled.
    // Keep the prototype APK disconnected from that development service, then restore local settings.
    public sealed class QuestBuildPrivacy : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string AssetPath = "Assets/Resources/DevAgentSettings.asset";
        static UnityEngine.Object _original;
        static string _snapshot;
        public int callbackOrder => 10000;

        public static void Snapshot()
        {
            Restore();
            _original = AssetDatabase.LoadMainAssetAtPath(AssetPath);
            _snapshot = _original == null ? null : EditorJsonUtility.ToJson(_original);
        }

        public static void Sanitize()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetPath);
            if (asset == null) return;
            var data = new SerializedObject(asset);
            foreach (var name in new[] { "serverAddress", "accessToken", "witClientAccessToken" })
            {
                var field = data.FindProperty(name);
                if (field == null) throw new BuildFailedException("Review changed Meta DevAgent settings before building");
                field.stringValue = "";
            }
            data.FindProperty("enabled").boolValue = false;
            data.FindProperty("useVoiceSdkForInput").boolValue = false;
            data.FindProperty("witConfiguration").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            EditorApplication.update -= RestoreAfterBuild;
            EditorApplication.update += RestoreAfterBuild;
        }

        static void RestoreAfterBuild() { if (!BuildPipeline.isBuildingPlayer) Restore(); }

        public static void Restore()
        {
            EditorApplication.update -= RestoreAfterBuild;
            if (_original != null && _snapshot != null)
            {
                EditorJsonUtility.FromJsonOverwrite(_snapshot, _original);
                EditorUtility.SetDirty(_original);
                AssetDatabase.SaveAssets();
            }
            _original = null; _snapshot = null;
        }

        public void OnPreprocessBuild(BuildReport report)
        { if (report.summary.platform == BuildTarget.Android) Sanitize(); }
        public void OnPostprocessBuild(BuildReport report)
        { if (report.summary.platform == BuildTarget.Android) Restore(); }
    }

    public sealed class QuestBuildPrivacySnapshot : IPreprocessBuildWithReport
    {
        public int callbackOrder => -2000;
        public void OnPreprocessBuild(BuildReport report)
        { if (report.summary.platform == BuildTarget.Android) QuestBuildPrivacy.Snapshot(); }
    }
}
