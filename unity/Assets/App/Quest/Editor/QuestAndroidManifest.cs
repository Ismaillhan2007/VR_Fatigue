using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace App.Quest.Editor
{
    // The OpenXR eye-foveation manifest library requires eyes even when Meta project support is optional.
    // Explicitly override that merged attribute so labelled replay remains installable without native eyes.
    public sealed class QuestAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            var xml = new XmlDocument(); xml.Load(manifest);
            KeepEyeTrackingOptional(xml);
            xml.Save(manifest);
        }

        public static void KeepEyeTrackingOptional(XmlDocument xml)
        {
            const string android = "http://schemas.android.com/apk/res/android";
            const string tools = "http://schemas.android.com/tools";
            var root = xml.DocumentElement;
            XmlElement eye = null;
            foreach (XmlNode node in root.ChildNodes)
                if (node is XmlElement element && element.Name == "uses-feature" &&
                    element.GetAttribute("name", android) == "oculus.software.eye_tracking") eye = element;
            if (eye == null)
            {
                eye = xml.CreateElement("uses-feature"); root.AppendChild(eye);
                eye.SetAttribute("name", android, "oculus.software.eye_tracking");
            }
            eye.SetAttribute("required", android, "false");
            eye.SetAttribute("replace", tools, "android:required");
        }
    }
}
