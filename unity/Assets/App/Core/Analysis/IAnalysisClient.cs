using System.Threading;
using System.Threading.Tasks;
using App.Core.Contracts;

namespace App.Core.Analysis
{
    /// <summary>Result of an analysis call. <see cref="FallbackReason"/> is set when local rules replaced the AI.</summary>
    public sealed class AnalysisResult
    {
        public AnalysisResponse Response;
        public string FallbackReason;
    }

    /// <summary>
    /// Seam between the app and the AI explanation. Implementations must never throw for network/AI failures:
    /// they return the deterministic local fallback instead, labelled ai_source = "fallback".
    /// </summary>
    public interface IAnalysisClient
    {
        string Name { get; }
        Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken);
    }

    /// <summary>No network, no AI: always works.</summary>
    public sealed class LocalAnalysisClient : IAnalysisClient
    {
        public string Name => "local rules";

        public Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AnalysisResult { Response = LocalFallbackAnalysis.Analyze(request), FallbackReason = "local mode" });
    }
}
