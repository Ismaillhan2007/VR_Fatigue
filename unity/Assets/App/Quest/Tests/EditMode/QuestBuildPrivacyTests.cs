using App.Quest.Editor;
using NUnit.Framework;
using UnityEditor;

public class QuestBuildPrivacyTests
{
    [Test]
    public void BuildDisablesAndRemovesSdkDevelopmentCredentialsThenRestoresLocalSettings()
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(QuestBuildPrivacy.AssetPath);
        Assert.NotNull(asset, "pinned SDK import creates its settings asset");
        QuestBuildPrivacy.Snapshot();
        try
        {
            var data = new SerializedObject(asset);
            data.FindProperty("accessToken").stringValue = "synthetic-test-marker";
            data.FindProperty("serverAddress").stringValue = "192.0.2.1";
            data.FindProperty("enabled").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            QuestBuildPrivacy.Sanitize();
            data.Update();
            Assert.IsEmpty(data.FindProperty("accessToken").stringValue);
            Assert.IsEmpty(data.FindProperty("serverAddress").stringValue);
            Assert.IsFalse(data.FindProperty("enabled").boolValue);
            Assert.IsFalse(data.FindProperty("useVoiceSdkForInput").boolValue);
            Assert.IsNull(data.FindProperty("witConfiguration").objectReferenceValue);
        }
        finally { QuestBuildPrivacy.Restore(); }
    }
}
