using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace App.Reports
{
    /// <summary>Operator-provisioned device setup, stored in Android's private app directory, never the APK.</summary>
    public static class OperatorBackendDefaults
    {
        public const string FileName = "backend-connection.json";

        public static string AndroidPrivatePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var directory = activity.Call<AndroidJavaObject>("getFilesDir"))
                    return Path.Combine(directory.Call<string>("getAbsolutePath"), FileName);
            }
            catch { return null; }
#else
            return null;
#endif
        }

        public static bool TryLoad(string path, out BackendSettings settings, out string status)
        {
            settings = null;
            status = "Headset backend setup missing. Ask the operator to provision it from the laptop. A local report is available.";
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            status = "Headset backend setup invalid. Ask the operator to provision it again. A local report is available.";
            try
            {
                if (new FileInfo(path).Length > 8192) return false;
                var body = JObject.Parse(File.ReadAllText(path));
                if (body.Count != 3 || body["backend_url"]?.Type != JTokenType.String
                    || body["backend_token"]?.Type != JTokenType.String
                    || body["local_development"]?.Type != JTokenType.Boolean) return false;
                var token = (string)body["backend_token"];
                if (string.IsNullOrWhiteSpace(token)
                    || !BackendSettings.TryCreate((string)body["backend_url"], token,
                        (bool)body["local_development"], out settings, out _)) return false;
                status = "Operator backend setup loaded. No typing needed. Gemini still requires your session consent.";
                return true;
            }
            catch (Exception) { return false; } // Never print file contents, credentials or exception messages.
        }
    }
}
