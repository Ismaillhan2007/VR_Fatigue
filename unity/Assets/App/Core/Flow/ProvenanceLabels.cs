using App.Core.Contracts;

namespace App.Core.Flow
{
    /// <summary>Always-visible source labels. Synthetic, replay, cached and fallback are never shown as live.</summary>
    public static class ProvenanceLabels
    {
        public static string Data(SessionContext c)
        {
            if (c?.DataSource == null) return "Data: not started";
            switch (c.DataSource.Value)
            {
                case DataSource.Live:
                    return c.Capabilities != null && c.Capabilities.NativeEyeGaze
                        ? "Data: LIVE eye tracking"
                        : "Data: live headset, no native eye gaze";
                case DataSource.Replay:
                    return c.ReplayOrigin == ReplayOrigin.Recorded
                        ? "Data: REPLAY of a recorded session (not live)"
                        : "Data: REPLAY of synthetic data (not live)";
                default:
                    return "Data: SYNTHETIC (no headset, not a measurement)";
            }
        }

        public static string Ai(SessionContext c)
        {
            if (c?.Analysis == null) return "AI: not run yet";
            switch (c.Analysis.AiSource)
            {
                case AiSource.Live: return $"AI: live ({c.Analysis.ModelId})";
                case AiSource.Cached: return $"AI: CACHED earlier output ({c.Analysis.ModelId ?? "unknown model"})";
                default:
                    return string.IsNullOrEmpty(c.AnalysisFallbackReason)
                        ? "AI: LOCAL FALLBACK (no AI used)"
                        : $"AI: LOCAL FALLBACK (no AI used: {c.AnalysisFallbackReason})";
            }
        }
    }
}
