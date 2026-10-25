using App.Core.Metrics;
using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Contracts;
using Sig = VRFatigue.Signals;

namespace App.Metrics
{
    /// <summary>
    /// Owned by the signals/metrics owner: returns the real engine (SignalsMetricsEngine bridges to Assets/Signals). The app only calls this factory, so swapping the
    /// engine needs no change outside App/Metrics.
    /// </summary>
    public static class MetricsEngineFactory
    {
        public static IMetricsEngine Create() => new SignalsMetricsEngine();

        /// <summary>Calculation failures preserve provenance but produce no measurements or indicator.</summary>
        public static bool TryCompute(IMetricsEngine engine, PhaseRecording task, BaselineData baseline,
            MetricsContext context, out SessionSummary summary)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            try
            {
                summary = engine.Compute(task, baseline, context);
                if (summary == null) throw new InvalidOperationException("Missing metric result.");
                return true;
            }
            catch (Exception)
            {
                var baselineMs = DurationMs(baseline?.Recording);
                var taskMs = DurationMs(task);
                var durationMs = baselineMs <= long.MaxValue - taskMs ? baselineMs + taskMs : 0;
                var unavailable = new List<UnavailableSignal>(context.Unavailable ?? Array.Empty<UnavailableSignal>())
                {
                    new UnavailableSignal { Signal = Signal.Gaze, Reason = UnavailableReason.NotCollected },
                    new UnavailableSignal { Signal = Signal.Probe, Reason = UnavailableReason.NotCollected },
                };
                summary = new SessionSummary
                {
                    SessionId = context.SessionId, DataSource = context.DataSource, ReplayOrigin = context.ReplayOrigin,
                    Capabilities = context.Capabilities, MetricVersion = "processing-error.v1", SelfReport = context.PostTaskReport,
                    DurationMs = durationMs,
                    Quality = new Quality
                    {
                        Status = QualityStatus.InsufficientData, GapDurationMs = durationMs, MaxGapMs = durationMs,
                        PolicyVersion = Sig.QualityPolicy.PrototypeV1.Version,
                        Unavailable = unavailable.GroupBy(u => u.Signal).Select(g => g.First()).Take(8).ToList(),
                    },
                    Baseline = new Baseline
                    {
                        State = BaselineState.Absent, DurationMs = baselineMs,
                        LowConfidence = true, LowConfidenceReason = LowConfidenceReason.PoorQuality,
                    },
                    StrainIndicator = new StrainIndicator { MethodVersion = Sig.IndicatorPolicy.MethodVersion },
                };
                return false;
            }
        }

        static long DurationMs(PhaseRecording recording)
        {
            if (recording == null) return 0;
            var seconds = recording.SensorStartTime.HasValue && recording.SensorEndTime.HasValue
                ? recording.SensorEndTime.Value - recording.SensorStartTime.Value
                : recording.DurationSeconds * recording.SensorClockRate;
            return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds >= long.MaxValue / 1000.0
                ? 0 : (long)Math.Round(seconds * 1000);
        }
    }
}
