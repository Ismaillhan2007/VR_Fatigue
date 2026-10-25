using System;
using System.Globalization;
using System.Text;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using App.Core.Metrics;

namespace App.UI
{
    /// <summary>Plain-text summary for the placeholder UI. Wording follows CONCEPT 5.3.</summary>
    public static class SummaryFormatter
    {
        public static string Format(SessionContext c, bool analysisInProgress, string probeStats = null)
        {
            var s = c.Summary;
            if (s == null) return "Computing measurements...";
            var b = new StringBuilder();
            b.AppendLine(ProvenanceLabels.Data(c));
            b.AppendLine(analysisInProgress ? "Report ready on-device. Optional explanation pending; you can finish now." : ProvenanceLabels.Ai(c));
            b.AppendLine();

            var a = c.Analysis;
            if (a != null && a.SafetyNotice != SafetyNotice.None) b.AppendLine("!! " + SafetyRules.BreakMessage).AppendLine();
            if (s.Baseline.LowConfidence) b.AppendLine($"Baseline: low confidence ({s.Baseline.LowConfidenceReason}). {BaselinePolicy.LowConfidenceWording}");

            var ind = s.StrainIndicator;
            b.AppendLine(ind.Level == StrainLevel.Unavailable
                ? $"{ReportText.IndicatorLabel}: unavailable"
                : $"{ReportText.IndicatorLabel}: {ind.Level.ToString().ToLowerInvariant()} ({ind.Value?.ToString("0.00", CultureInfo.InvariantCulture)})");
            b.AppendLine(ReportText.IndicatorDisclaimer);
            b.AppendLine($"Data quality: {s.Quality.Status}, valid coverage {s.Quality.ValidCoverage:P0}, metrics {s.MetricVersion}");
            if (c.PostTaskReport != null)
                b.AppendLine($"Self-report after the task (1-7): eye strain {c.PostTaskReport.EyeStrain}, mental fatigue {c.PostTaskReport.MentalFatigue}.");
            b.AppendLine();
            b.AppendLine("Per-metric deviation from your baseline (robust z, clamped to +/-4):");
            foreach (MetricName m in Enum.GetValues(typeof(MetricName)))
            {
                var v = s.Metrics.Get(m);
                var z = s.Deviations.Get(m);
                b.AppendLine($"  {m}: {(v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "unavailable")}"
                    + (z.HasValue ? $"  (z {z.Value:+0.0;-0.0;0.0})" : ""));
            }

            if (!string.IsNullOrEmpty(probeStats)) b.AppendLine(probeStats);

            if (a == null) return b.ToString();
            b.AppendLine();
            b.AppendLine(ReportText.ReconciliationLabel(a.Reconciliation));
            foreach (var o in a.Observations) b.AppendLine("  - " + ReportText.ObservationLabel(o.Code));
            b.AppendLine();
            b.AppendLine(a.Narrative);
            b.AppendLine();
            b.AppendLine("Limitations:");
            foreach (var l in a.Limitations) b.AppendLine("  - " + l);
            b.AppendLine();
            return b.ToString();
        }

        public static string SceneLine(SceneConfig k) => string.Format(CultureInfo.InvariantCulture,
            "{0}, brightness {1:0.00}, warmth {2:0.00}, fog {3:0.00}, sound {4} (vol {5:0.00}), motion {6} {7:0.00}, animals {8}, particles [{9}], orb {10}",
            k.TimeOfDay, k.Brightness, k.LightWarmth, k.FogDensity, k.Sound, k.MasterVolume, k.Motion, k.MotionSpeed,
            k.AnimalsEnabled ? "on" : "off", string.Join(",", k.Particles), k.Orb.Enabled ? $"{k.Orb.BreathSeconds:0.0}s" : "off");
    }
}
