using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using App.Core.Analysis;
using App.Core.Contracts;
using UnityEngine;
using Newtonsoft.Json;
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
            _accessToken = accessToken ?? "";
        }

        public bool BackendResponseAccepted { get; private set; }

        public string Name => "backend HTTP";

        public async Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
        {
            BackendResponseAccepted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var web = CreateRequest(request))
                using (cancellationToken.Register(web.Abort))
                {
                    await Send(web);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (web.result != UnityWebRequest.Result.Success)
                        return Fallback(request, web.responseCode > 0
                            ? $"backend HTTP {web.responseCode}"
                            : "backend connection failed or timed out");
                    var response = ContractJson.Parse<AnalysisResponse>(web.downloadHandler.text);
                    response = AnalysisResponseGuard.Accept(request, response);
                    BackendResponseAccepted = true;
                    return new AnalysisResult
                    {
                        Response = response,
                        FallbackReason = response.AiSource == AiSource.Fallback
                            ? BackendFallbackDescription(web.GetResponseHeader("X-VR-Fallback-Reason")) : null,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return Fallback(request, "request cancelled");
            }
            catch (JsonException)
            {
                return Fallback(request, "backend response rejected");
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
            web.redirectLimit = 0; // Never forward the bearer token or summary to a redirect destination.
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

        /// <summary>Never render an arbitrary server header or provider exception in the UI.</summary>
        public static string BackendFallbackDescription(string code)
        {
            switch (code)
            {
                case "local_only": return "cloud use disabled for this check-in";
                case "non_synthetic_input": return "participant eye data not sent to Gemini";
                case "participant_cloud_disabled": return "participant cloud explanations disabled on backend";
                case "safety_stop": return "session stopped for comfort";
                case "insufficient_data": return "insufficient comparable measurements";
                case "gemini_disabled": return "Gemini disabled";
                case "gemini_not_configured": return "Gemini configuration incomplete";
                case "attempt_limit": return "AI attempt limit reached";
                case "budget_limit": return "AI budget limit reached";
                case "allowance_unavailable": return "AI allowance storage unavailable";
                case "timeout": return "AI request timed out";
                case "provider_rate_limited": return "AI service rate limited";
                case "provider_unavailable": return "AI service temporarily unavailable";
                case "invalid_output": return "AI response rejected";
                case "provider_error": return "AI service error";
                default: return "backend local rules";
            }
        }
    }
}
