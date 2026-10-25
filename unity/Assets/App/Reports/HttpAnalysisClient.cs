using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using App.Core.Analysis;
using App.Core.Contracts;
using UnityEngine;
using UnityEngine.Networking;

namespace App.Reports
{
    /// <summary>
    /// POST {baseUrl}/api/v1/analyze with the request (it never contains free text). Any failure (network, timeout,
    /// non-2xx, invalid JSON, schema or guard violation) returns the local fallback with the reason. Never throws.
    /// Must be awaited on the Unity main thread.
    /// </summary>
    public sealed class HttpAnalysisClient : IAnalysisClient
    {
        public const int TimeoutSeconds = 15;
        public const string DefaultBaseUrl = "http://127.0.0.1:8000";

        readonly string _baseUrl;
        readonly string _accessToken;

        /// <param name="accessToken">Optional VR_BACKEND_ACCESS_TOKEN of the backend (LAN demo). Never the Gemini key.</param>
        public HttpAnalysisClient(string baseUrl, string accessToken = null)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
            _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
        }

        public string Name => "backend " + _baseUrl;

        public async Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var body = ContractJson.Serialize(request);
                using (var web = new UnityWebRequest(_baseUrl + "/api/v1/analyze", UnityWebRequest.kHttpVerbPOST))
                using (cancellationToken.Register(web.Abort))
                {
                    web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" };
                    web.downloadHandler = new DownloadHandlerBuffer();
                    web.SetRequestHeader("Accept", "application/json");
                    if (_accessToken != null) web.SetRequestHeader("Authorization", "Bearer " + _accessToken);
                    web.timeout = TimeoutSeconds;
                    await Send(web);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (web.result != UnityWebRequest.Result.Success)
                        return Fallback(request, $"backend unreachable ({web.responseCode} {web.error})");
                    var response = ContractJson.Parse<AnalysisResponse>(web.downloadHandler.text);
                    return new AnalysisResult { Response = AnalysisResponseGuard.Accept(request, response) };
                }
            }
            catch (OperationCanceledException)
            {
                return Fallback(request, "request cancelled");
            }
            catch (ContractException e)
            {
                Debug.LogWarning($"[HttpAnalysisClient] rejected response: {e.Message}");
                return Fallback(request, "backend response rejected");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HttpAnalysisClient] {e.GetType().Name}: {e.Message}");
                return Fallback(request, "backend error");
            }
        }

        static Task Send(UnityWebRequest web)
        {
            var tcs = new TaskCompletionSource<bool>();
            web.SendWebRequest().completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        static AnalysisResult Fallback(AnalyzeRequest request, string reason) =>
            new AnalysisResult { Response = LocalFallbackAnalysis.Analyze(request), FallbackReason = reason };
    }
}
