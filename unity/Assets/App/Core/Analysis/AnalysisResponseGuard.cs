using System.Collections.Generic;
using App.Core.Contracts;

namespace App.Core.Analysis
{
    /// <summary>
    /// Re-checks a backend response against the request before anything from it is shown or applied. A response that
    /// relabels provenance, changes the deterministic reconciliation, lowers the safety notice or breaks the schema is
    /// rejected (the caller then uses LocalFallbackAnalysis).
    /// </summary>
    public static class AnalysisResponseGuard
    {
        public static IReadOnlyList<string> Check(AnalyzeRequest request, AnalysisResponse response)
        {
            var p = new Problems();
            if (response == null) { p.Add("$", "empty response"); return p.Items; }
            response.Validate(p, "$");
            var s = request.SessionSummary;
            if (response.SessionId != s.SessionId) p.Add("$.session_id", "does not match the request");
            if (response.DataSource != s.DataSource) p.Add("$.data_source", "does not match the request");
            if (response.ReplayOrigin != s.ReplayOrigin) p.Add("$.replay_origin", "does not match the request");
            if (response.Quality != null && response.Quality.Status != s.Quality.Status)
                p.Add("$.quality.status", "does not match the request");
            if (response.Reconciliation != request.Reconciliation) p.Add("$.reconciliation", "the AI may not change it");
            if (response.SafetyNotice < request.SafetyNotice) p.Add("$.safety_notice", "the AI may only raise it");
            if (response.AiSource == AiSource.Live && string.IsNullOrEmpty(response.ModelId))
                p.Add("$.model_id", "a live response must name its model");
            return p.Items;
        }

        /// <summary>Returns the response with its scene config sanitised and participant preferences applied.</summary>
        public static AnalysisResponse Accept(AnalyzeRequest request, AnalysisResponse response)
        {
            var problems = Check(request, response);
            if (problems.Count > 0) throw new ContractException("Backend response rejected", problems);
            response.SceneConfig = SceneConfigSanitizer.Sanitize(response.SceneConfig, request.Preferences,
                response.SafetyNotice);
            return response;
        }
    }
}
