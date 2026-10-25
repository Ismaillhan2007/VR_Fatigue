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

        public HttpAnalysisClient(string baseUrl, string accessToken = null)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
            _accessToken = accessToken ?? Environment.GetEnvironmentVariable("VR_BACKEND_ACCESS_TOKEN");
        }

        public string Name => "backend " + _baseUrl;

        public async Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                using (var web = CreateRequest(request))
                using (cancellationToken.Register(web.Abort))
                {
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
            catch (ContractException)
            {
                Debug.LogWarning("[HttpAnalysisClient] rejected response");
                return Fallback(request, "backend response rejected");
            }
            catch (Exception e)
            {
                // Header/transport exceptions may contain the runtime token: log the type only.
                Debug.LogWarning($"[HttpAnalysisClient] {e.GetType().Name}");
                return Fallback(request, "backend error");
            }
        }

        /// <summary>Builds the wire request. The optional demo token is runtime-only, never a scene field.</summary>
        public UnityWebRequest CreateRequest(AnalyzeRequest request)
        {
            var body = ContractJson.Serialize(request);
            var web = new UnityWebRequest(_baseUrl + "/api/v1/analyze", UnityWebRequest.kHttpVerbPOST);
            web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" };
            web.downloadHandler = new DownloadHandlerBuffer();
            web.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(_accessToken)) web.SetRequestHeader("Authorization", "Bearer " + _accessToken);
            web.timeout = TimeoutSeconds;
            return web;
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
