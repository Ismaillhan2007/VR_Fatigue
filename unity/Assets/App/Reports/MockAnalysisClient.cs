using System.IO;
using System.Threading;
using System.Threading.Tasks;
using App.Core.Analysis;
using App.Core.Contracts;
using UnityEngine;

namespace App.Reports
{
    /// <summary>
    /// Canned response for UI work without a backend: StreamingAssets/fixtures/analysis_fallback.json with this
    /// session's id and provenance patched in. Stays labelled ai_source = "fallback" (it is not AI output).
    /// </summary>
    public sealed class MockAnalysisClient : IAnalysisClient
    {
        public static string FixturePath => Path.Combine(Application.streamingAssetsPath, "fixtures", "analysis_fallback.json");

        public string Name => "mock fixture";

        public Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var r = ContractJson.Parse<AnalysisResponse>(File.ReadAllText(FixturePath));
                var s = request.SessionSummary;
                r.SessionId = s.SessionId;
                r.DataSource = s.DataSource;
                r.ReplayOrigin = s.ReplayOrigin;
                r.Quality = s.Quality;
                r.Reconciliation = request.Reconciliation;
                r.SafetyNotice = SafetyRules.Max(r.SafetyNotice, request.SafetyNotice);
                r.AiSource = AiSource.Fallback;
                r.ModelId = null;
                return Task.FromResult(new AnalysisResult
                    { Response = AnalysisResponseGuard.Accept(request, r), FallbackReason = "mock fixture" });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MockAnalysisClient] {e.Message}; using local rules");
                return Task.FromResult(new AnalysisResult
                    { Response = LocalFallbackAnalysis.Analyze(request), FallbackReason = "mock fixture missing" });
            }
        }
    }
}
