using App.Core.Contracts;

namespace App.Core.Metrics
{
    /// <summary>Baseline trust rules from docs/CONCEPT.md section 4.</summary>
    public static class BaselinePolicy
    {
        public const int TiredRating = 5;
        public const int MinWindows = 6;
        public const double WindowSeconds = 20;

        public const string LowConfidenceWording = "Compared with how you started today, not with your rested self.";

        /// <summary>True if the participant already reported feeling tired at baseline (either slider >= 5).</summary>
        public static bool SelfReportedTired(SelfReport baseline) =>
            baseline != null && (baseline.EyeStrain >= TiredRating || baseline.MentalFatigue >= TiredRating);

        /// <summary>Priority: words first, then too few windows, then poor quality.</summary>
        public static (bool lowConfidence, LowConfidenceReason? reason) Evaluate(SelfReport baseline, int windows, bool poorQuality)
        {
            if (SelfReportedTired(baseline)) return (true, LowConfidenceReason.SelfReportedTired);
            if (windows < MinWindows) return (true, LowConfidenceReason.TooShort);
            if (poorQuality) return (true, LowConfidenceReason.PoorQuality);
            return (false, null);
        }
    }
}
