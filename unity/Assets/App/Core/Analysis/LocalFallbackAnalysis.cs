using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Contracts;
using App.Core.Metrics;

namespace App.Core.Analysis
{
    /// <summary>
    /// Deterministic offline analysis (docs/CONCEPT.md 7.3): saved preset, then chip modifiers in table order, then
    /// clamp, then explicit Preferences. No AI and no network, so it always works. Labelled ai_source = "fallback".
    /// </summary>
    public static class LocalFallbackAnalysis
    {
        public const string PromptVersion = "none";
        const double DeviationThreshold = 1.0;

        public static AnalysisResponse Analyze(AnalyzeRequest request)
        {
            var s = request.SessionSummary;
            var post = s.SelfReport;
            return new AnalysisResponse
            {
                SessionId = s.SessionId,
                DataSource = s.DataSource,
                ReplayOrigin = s.ReplayOrigin,
                Quality = s.Quality,
                Reconciliation = request.Reconciliation,
                Observations = Observations(s),
                Narrative = ReportText.FallbackNarrative(request.Reconciliation, s.DataSource),
                Limitations = ReportText.Limitations(s),
                SafetyNotice = request.SafetyNotice,
                SceneRationale = Rationale(post),
                SceneConfig = SceneConfigSanitizer.Sanitize(SceneFor(post, request.Preferences),
                    request.Preferences, request.SafetyNotice),
                AiSource = AiSource.Fallback,
                ModelId = null,
                PromptVersion = PromptVersion,
            };
        }

        /// <summary>Chip/slider mapping. A later table row wins when two rows set the same field.</summary>
        public static SceneConfig SceneFor(SelfReport report, Preferences preferences)
        {
            var c = SceneConfigPresets.CalmForestSunset();
            var chips = new HashSet<Chip>(report?.Chips ?? new List<Chip>());

            if (chips.Contains(Chip.TiredEyes))
            {
                c.Brightness -= 0.15;
                c.LightWarmth += 0.15;
                c.FogDensity += 0.1;
                c.MotionSpeed = 0;
            }
            if (chips.Contains(Chip.Overstimulated))
            {
                c.Particles.Clear();
                c.AnimalsEnabled = false;
                c.SoundLayers.Wind = c.SoundLayers.Birds = c.SoundLayers.Water = 0;
            }
            if (chips.Contains(Chip.Tense) || chips.Contains(Chip.Restless))
            {
                c.Orb.Enabled = true;
                c.Orb.BreathSeconds = 6;
            }
            if (chips.Contains(Chip.LowEnergy))
            {
                c.TimeOfDay = TimeOfDay.Day;
                c.SoundLayers.Birds += 0.2;
                c.Motion = MotionMode.Gentle;
            }
            if (chips.Contains(Chip.WantQuiet)) c.MasterVolume = 0.2;
            if (chips.Contains(Chip.WantDim)) c.Brightness = 0.5;
            if (chips.Contains(Chip.WantNature))
            {
                c.SoundLayers.Birds += 0.2;
                c.SoundLayers.Water += 0.2;
            }
            if (chips.Contains(Chip.WantMovement))
            {
                c.Motion = MotionMode.Gentle;
                c.MotionSpeed = 0.3;
            }
            return SceneConfigSanitizer.Sanitize(c, preferences);
        }

        public static List<Observation> Observations(SessionSummary s)
        {
            if (s.Quality.Status == QualityStatus.InsufficientData)
                return new List<Observation> { new Observation { Code = ObservationCode.DataQualityLimited } };
            if (s.Baseline.State == BaselineState.Absent || s.Baseline.Windows < 6)
                return new List<Observation> { new Observation { Code = ObservationCode.NoBaseline } };

            var list = new List<Observation>();
            var hasComparison = false;
            foreach (var m in new[] { MetricName.ProbeLatencyMs, MetricName.ProbeRmsErrorDeg,
                MetricName.ProbeMissFraction, MetricName.GazeSpeedP95DegS, MetricName.GazeSpeedMedianDegS,
                MetricName.GazeDispersionDeg, MetricName.HeadSpeedDegS })
            {
                var z = s.Deviations.Get(m);
                if (!z.HasValue || !s.Metrics.Get(m).HasValue) continue;
                hasComparison = true;
                var code = CodeFor(m, z.Value);
                if (code.HasValue && !list.Any(o => o.Code == code.Value))
                    list.Add(new Observation { Code = code.Value, Metric = m });
            }
            if (!hasComparison)
                return new List<Observation> { new Observation { Code = ObservationCode.DataQualityLimited } };
            if (list.Count == 0) list.Add(new Observation { Code = ObservationCode.WithinBaseline });
            return list.Take(6).ToList();
        }

        static ObservationCode? CodeFor(MetricName m, double z)
        {
            var changed = Math.Abs(z) >= DeviationThreshold;
            var rose = z >= DeviationThreshold;
            switch (m)
            {
                case MetricName.GazeSpeedMedianDegS:
                case MetricName.GazeSpeedP95DegS: return changed ? ObservationCode.GazeSpeedChanged : (ObservationCode?)null;
                case MetricName.GazeDispersionDeg: return changed ? ObservationCode.GazeSpreadChanged : (ObservationCode?)null;
                case MetricName.ProbeLatencyMs: return rose ? ObservationCode.ProbeSlower : (ObservationCode?)null;
                case MetricName.ProbeRmsErrorDeg: return rose ? ObservationCode.ProbeLessAccurate : (ObservationCode?)null;
                case MetricName.ProbeMissFraction: return rose ? ObservationCode.ProbeMoreMisses : (ObservationCode?)null;
                case MetricName.HeadSpeedDegS: return rose ? ObservationCode.HeadMoreActive : (ObservationCode?)null;
                default: return null;
            }
        }

        static string Rationale(SelfReport report)
        {
            var chips = report?.Chips ?? new List<Chip>();
            var text = chips.Count == 0
                ? "Saved calm forest preset (no check-in chips selected), adjusted by local rules."
                : "Calm forest adjusted by local rules for what you reported: "
                  + string.Join(", ", chips.Select(ReportText.ChipLabel)) + ".";
            return text.Length <= 200 ? text : text.Substring(0, 197) + "...";
        }
    }
}
