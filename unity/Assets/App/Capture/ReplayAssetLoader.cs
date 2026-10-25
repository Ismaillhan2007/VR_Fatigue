using System;
using System.Collections;
using System.IO;
using App.Core.Contracts;
using UnityEngine.Networking;

namespace App.Capture
{
    /// <summary>Bundled replay locations can be APK URLs. Never send replay loading to a remote host.</summary>
    public static class ReplayAssetLoader
    {
        public static bool IsAssetUrl(string path) => path != null &&
            (path.StartsWith("jar:file:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("file://", StringComparison.OrdinalIgnoreCase));

        public static string Resolve(string path, string dataPath, string streamingAssetsPath, bool android)
        {
            if (string.IsNullOrWhiteSpace(path) || IsAssetUrl(path)) return path;
            if (android && !Path.IsPathRooted(path))
                return streamingAssetsPath.TrimEnd('/') + "/fixtures/replay/" + Path.GetFileName(path);
            var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(dataPath, "..", path));
            if (File.Exists(full)) return full;
            var packaged = streamingAssetsPath.TrimEnd('/') + "/fixtures/replay/" + Path.GetFileName(path);
            return IsAssetUrl(packaged) || File.Exists(packaged) ? packaged : full;
        }

        public static IEnumerator Load(string location, ReplayOrigin origin, Action<ReplayEyeProvider, string> complete)
        {
            if (!IsAssetUrl(location))
            {
                complete(null, "Packaged replay location is unavailable. Retry before starting capture.");
                yield break;
            }
            using (var web = UnityWebRequest.Get(location))
            {
                web.timeout = 15;
                web.redirectLimit = 0;
                yield return web.SendWebRequest();
                if (web.result != UnityWebRequest.Result.Success)
                {
                    complete(null, "Packaged replay could not be loaded. Retry before starting capture.");
                    yield break;
                }
                ReplayEyeProvider provider = null;
                try
                {
                    provider = location.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? ReplayEyeProvider.FromEyeSessionJson(web.downloadHandler.text, origin)
                        : ReplayEyeProvider.FromCsv(web.downloadHandler.text, origin);
                }
                catch (Exception) { } // No file contents, local paths or parser messages in participant diagnostics.
                complete(provider, provider == null ? "Packaged replay was rejected. Retry before starting capture." : null);
            }
        }
    }
}
