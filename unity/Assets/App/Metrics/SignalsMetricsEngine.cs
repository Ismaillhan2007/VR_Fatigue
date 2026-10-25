using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Contracts;
using App.Core.Metrics;
using Sig = VRFatigue.Signals;
using AppCapture = App.Core.Capture;

namespace App.Metrics
{
    /// <summary>
    /// Bridge from the application seam to the VRFatigue.Signals module (feat/signals-metrics-quest-pro): converts
    /// the captured baseline + task samples into one eye-session on the sensor clock, runs quality, metrics, baseline
    /// comparison and the experimental indicator there, and maps the results onto the v1 SessionSummary.
    /// The maths lives in Assets/Signals; this class only translates.
    /// </summary>
    public sealed class SignalsMetricsEngine : IMetricsEngine
    {
        /// <summary>
        /// Conditions id per phase: the probe layout id when the phase showed probes, otherwise the plain canvas.
        /// Both phases run on the same canvas, so they match when both (or neither) showed the same layout; a phase
        /// with probes and one without do not match, and the module then refuses to compare them.
        /// </summary>
        public const string ConditionsId = AppCapture.ProbeSchedule.LayoutId;
        public const string NoProbeConditionsId = "assessment_canvas_no_probes";

        /// <summary>
        /// Stricter than the module default (3 degrees / 1 s): a rested recording had zero misses at the default, so it
        /// could not show a change. 2 degrees within 500 ms; experimental, not a validated threshold.
        /// </summary>
        public static Sig.MetricPolicy ProbePolicy => new Sig.MetricPolicy(probeToleranceDeg: 2, acquisitionDeadlineUs: 500000);

        public string MetricVersion => Sig.MetricPolicy.AlgorithmVersion + "+capture.v2";

        /// <summary>Per-trial counts of the last Compute, for the summary screen (not part of the v1 contract).</summary>
        public ProbeTrialStats LastProbeStats { get; private set; }

        public SessionSummary Compute(PhaseRecording task, BaselineData baseline, MetricsContext context)
        {
            LastProbeStats = null;
            var baselineSamples = baseline?.Recording?.Samples ?? new List<AppCapture.GazeSample>();
            var taskSamples = task?.Samples ?? new List<AppCapture.GazeSample>();
            var all = baselineSamples.Concat(taskSamples).ToList();
            var fallbackClock = new FrameToSensor(all, 0);
            var taskBounds = Bounds(task, fallbackClock);
            var baselineBounds = baseline?.Recording == null ? (taskBounds.start, taskBounds.start)
                : Bounds(baseline.Recording, fallbackClock);
            bool hasBaseline = baseline?.Recording != null && (baselineSamples.Count > 0 ||
                baseline.Recording.DurationSeconds > 0 || baseline.Recording.SensorStartTime.HasValue);
            if (!hasBaseline) baselineBounds = (taskBounds.start, taskBounds.start);
            if (baselineBounds.Item2 > taskBounds.start + 0.000001)
                throw new ArgumentException("Baseline and task acquisition bounds overlap.");

            // Explicit phase bounds survive missing edge samples and remain on the acquisition clock.
            var origin = baselineBounds.Item1;
            long Us(double seconds) => (long)Math.Round((seconds - origin) * 1e6, MidpointRounding.AwayFromZero);
            var samples = all.Select(s => ToEyeSample(s, Us(s.SensorTimestamp))).ToList();
            var endUs = Us(taskBounds.end);
            var baselineEndUs = Us(baselineBounds.Item2);
            var taskStartUs = Us(taskBounds.start);

            var info = new Sig.SessionInfo(context.SessionId, Wire(context.DataSource), Wire(context.ReplayOrigin), 0, endUs, Capabilities(context));
            var clock = new FrameToSensor(all, origin);
            var baselineProbes = Probes(baseline?.Recording, clock, endUs).ToList();
            var taskProbes = Probes(task, clock, endUs).ToList();
            var probes = baselineProbes.Concat(taskProbes).OrderBy(p => p.TimestampUs).ToList();
            var session = new Sig.EyeSession(info, samples, probes);

            var report = baseline?.SelfReport;
            var plan = new Sig.BaselinePlan(context.SessionId,
                new Sig.AssessmentPhase(0, baselineEndUs, baselineProbes.Count > 0 ? ConditionsId : NoProbeConditionsId),
                new Sig.AssessmentPhase(taskStartUs, endUs, taskProbes.Count > 0 ? ConditionsId : NoProbeConditionsId),
                report == null ? null : new Sig.BaselineSelfReport(report.EyeStrain, report.MentalFatigue));
            var comparison = Sig.BaselineEvaluator.Evaluate(session, plan, metricPolicy: ProbePolicy);
            var indicator = Sig.IndicatorEvaluator.Evaluate(comparison);

            var clean = comparison.SessionQuality;
            var taskInfo = new Sig.SessionInfo(info.SessionId, info.DataSource, info.ReplayOrigin, taskStartUs, endUs, info.Capabilities);
            var taskQuality = Sig.QualityEvaluator.Evaluate(taskInfo,
                clean.AcceptedStream.Where(s => s.TimestampUs >= taskStartUs), clean.Policy);
            // Only trials that lie wholly inside the task phase count for the task-level probe metrics.
            var inTask = probes.GroupBy(p => p.ProbeId).Where(g => g.All(p => p.TimestampUs >= taskStartUs)).SelectMany(g => g)
                .OrderBy(p => p.TimestampUs).ToList();
            var taskMetrics = Sig.MetricsEvaluator.Evaluate(taskQuality, inTask, comparison.Baseline.MetricPolicy);

            var usable = clean.Status == "usable" && taskQuality.Status == "usable";
            var summary = new SessionSummary
            {
                SessionId = context.SessionId,
                DataSource = context.DataSource,
                ReplayOrigin = context.ReplayOrigin,
                MetricVersion = MetricVersion,
                Capabilities = context.Capabilities,
                Quality = QualityOf(clean, usable, context, probes.Count > 0),
                Baseline = BaselineOf(comparison.Baseline, baselineEndUs),
                Metrics = Values(name => usable ? taskMetrics.Metrics[name].Value : null),
                Deviations = Values(name => usable ? MedianZ(comparison, name) : null),
                SelfReport = context.PostTaskReport,
            };
            summary.DurationMs = summary.Quality.ValidDurationMs + summary.Quality.GapDurationMs;
            summary.StrainIndicator = IndicatorOf(indicator, summary, usable);
            LastProbeStats = ProbeTrialStats.From(comparison.Baseline.Windows, taskMetrics.Probes);
            return summary;
        }

        static (double start, double end) Bounds(PhaseRecording recording, FrameToSensor fallback)
        {
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            if (recording.SensorStartTime.HasValue != recording.SensorEndTime.HasValue)
                throw new ArgumentException("Both acquisition bounds are required.");
            double start, end;
            if (recording.SensorStartTime.HasValue)
            { start = recording.SensorStartTime.Value; end = recording.SensorEndTime.Value; }
            else
            {
                if (!Finite(recording.StartTime) || !Finite(recording.EndTime) || recording.EndTime < recording.StartTime ||
                    !Finite(recording.SensorClockRate) || recording.SensorClockRate <= 0)
                    throw new ArgumentException("Invalid capture phase clock.");
                if (recording.DurationSeconds == 0 && recording.Samples.Count > 0)
                    throw new ArgumentException("Samples require explicit phase bounds.");
                start = fallback.At(recording.StartTime, recording.SensorClockRate);
                end = fallback.At(recording.EndTime, recording.SensorClockRate);
            }
            if (!Finite(start) || !Finite(end) || end < start || (end - start) * 1e6 > long.MaxValue)
                throw new ArgumentException("Invalid acquisition bounds.");
            return (start, end);
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        // ----- mapping -----

        static Sig.EyeSample ToEyeSample(AppCapture.GazeSample s, long timestampUs)
        {
            double? confidence = s.Confidence.HasValue ? (double?)Math.Max(0, Math.Min(1, s.Confidence.Value)) : null;
            Sig.EyeReading Eye(bool valid, System.Numerics.Vector3 d) => valid && s.Valid
                ? new Sig.EyeReading(true, new Sig.Direction3(d.X, d.Y, d.Z), confidence)
                : new Sig.EyeReading(false, null, confidence);
            var q = s.HeadRotation;
            var head = s.HeadTracked
                ? new Sig.HeadPose(new Sig.Direction3(s.HeadPosition.X, s.HeadPosition.Y, s.HeadPosition.Z), q.X, q.Y, q.Z, q.W)
                : null;
            return new Sig.EyeSample(timestampUs, Eye(s.LeftValid, s.LeftDirection), Eye(s.RightValid, s.RightDirection), head);
        }

        /// <summary>App probes use the frame clock; map each time onto the sensor clock through the samples.</summary>
        static IEnumerable<Sig.ProbeEvent> Probes(PhaseRecording r, FrameToSensor clock, long endUs)
        {
            if (r == null) yield break;
            foreach (var p in r.Probes)
            {
                var target = new Sig.Direction3(p.TargetDirection.X, p.TargetDirection.Y, p.TargetDirection.Z);
                long onset = clock.Us(p.OnsetTime, endUs, r.SensorClockRate), hold = clock.Us(p.HoldStartTime, endUs, r.SensorClockRate), end = clock.Us(p.EndTime, endUs, r.SensorClockRate);
                if (!(onset < hold && hold < end)) continue; // clipped at the session edge: not an evaluable trial
                var id = string.IsNullOrEmpty(p.TrialId) ? $"t{onset}" : p.TrialId;
                yield return new Sig.ProbeEvent(onset, id, "target_onset", target);
                yield return new Sig.ProbeEvent(hold, id, "hold_start", target);
                yield return new Sig.ProbeEvent(end, id, "target_end", target);
            }
        }

        /// <summary>Piecewise-linear frame-clock -> sensor-clock map built from the captured rows.</summary>
        sealed class FrameToSensor
        {
            readonly double[] _frame, _sensor;
            readonly double _origin;

            public FrameToSensor(List<AppCapture.GazeSample> rows, double origin)
            {
                if (rows.Any(s => !Finite(s.FrameTime) || !Finite(s.SensorTimestamp)))
                    throw new ArgumentException("Nonfinite capture clock.");
                var ordered = rows.OrderBy(s => s.FrameTime).ToList();
                _frame = ordered.Select(s => s.FrameTime).ToArray();
                _sensor = ordered.Select(s => s.SensorTimestamp).ToArray();
                _origin = origin;
            }

            public long Us(double frameTime, long endUs, double rate)
                => Math.Max(0, Math.Min(endUs, (long)Math.Round((At(frameTime, rate) - _origin) * 1e6)));

            public double At(double frameTime, double rate)
            {
                if (!Finite(frameTime)) throw new ArgumentException("Nonfinite phase/probe time.");
                var n = _frame.Length;
                if (n == 0) return frameTime * rate;
                double sensor;
                var i = Array.BinarySearch(_frame, frameTime);
                if (i >= 0) sensor = _sensor[i];
                else
                {
                    i = ~i;
                    if (i == 0) sensor = _sensor[0] - (_frame[0] - frameTime) * rate;
                    else if (i >= n) sensor = _sensor[n - 1] + (frameTime - _frame[n - 1]) * rate;
                    else
                    {
                        var f = (frameTime - _frame[i - 1]) / Math.Max(1e-9, _frame[i] - _frame[i - 1]);
                        sensor = _sensor[i - 1] + f * (_sensor[i] - _sensor[i - 1]);
                    }
                }
                return sensor;
            }
        }

        static Sig.EyeCapabilities Capabilities(MetricsContext c)
        {
            var gaze = c.Capabilities != null && c.Capabilities.GazePose
                ? new Sig.SignalAvailability(true, null)
                : new Sig.SignalAvailability(false, "not_exposed_by_runtime");
            var notExposed = new Sig.SignalAvailability(false, "not_exposed_by_runtime");
            return new Sig.EyeCapabilities(gaze, notExposed, notExposed, false);
        }

        static Quality QualityOf(Sig.QualityResult q, bool usable, MetricsContext c, bool hadProbes)
        {
            var durationMs = (long)Math.Round(q.Info.DurationUs / 1000.0);
            var validMs = Math.Min(durationMs, (long)Math.Round(q.ValidDurationUs / 1000.0));
            var deltas = new List<double>();
            for (var i = 1; i < q.AcceptedStream.Count; i++)
                deltas.Add((q.AcceptedStream[i].TimestampUs - q.AcceptedStream[i - 1].TimestampUs) / 1000.0);
            deltas.Sort();

            var unavailable = new List<UnavailableSignal>(c.Unavailable ?? Array.Empty<UnavailableSignal>());
            if (!hadProbes) unavailable.Add(new UnavailableSignal { Signal = Signal.Probe, Reason = UnavailableReason.NotCollected });
            if (!usable) unavailable.Add(new UnavailableSignal { Signal = Signal.Gaze, Reason = UnavailableReason.TrackingLost });
            return new Quality
            {
                Status = usable ? QualityStatus.Usable : QualityStatus.InsufficientData,
                ValidCoverage = Math.Round(Math.Max(0, Math.Min(1, q.ValidCoverage)), 4),
                ValidDurationMs = validMs,
                GapDurationMs = durationMs - validMs,
                MaxGapMs = (long)Math.Round(q.LongestGapUs / 1000.0),
                RepeatedSampleFraction = q.InputSamples == 0 ? 0 : Math.Round((double)q.DuplicateTimestamps / q.InputSamples, 4),
                MedianDtMs = deltas.Count == 0 ? (double?)null : Math.Round(deltas[deltas.Count / 2], 3),
                Unavailable = unavailable.GroupBy(u => u.Signal).Select(g => g.First()).Take(8).ToList(),
                PolicyVersion = q.Policy.Version,
            };
        }

        static Baseline BaselineOf(Sig.BaselineResult b, long baselineEndUs)
        {
            // conditions_unconfirmed (no probe layout in a phase) concerns comparability, not the reference itself;
            // it already makes the indicator unavailable, so it is not reported as a low-confidence baseline.
            var reasons = b.LowConfidenceReasons.Where(r => r != "conditions_unconfirmed").ToList();
            LowConfidenceReason? reason = reasons.Contains("self_reported_tired") ? LowConfidenceReason.SelfReportedTired
                : reasons.Contains("too_short") ? LowConfidenceReason.TooShort
                : reasons.Count > 0 ? LowConfidenceReason.PoorQuality : (LowConfidenceReason?)null;
            return new Baseline
            {
                State = baselineEndUs > 0 && b.Windows.Count > 0 ? BaselineState.Present : BaselineState.Absent,
                DurationMs = (long)Math.Round(baselineEndUs / 1000.0),
                Windows = b.Windows.Count(w => w.Eligible),
                LowConfidence = reasons.Count > 0,
                LowConfidenceReason = reason,
            };
        }

        /// <summary>Median of the comparison windows' robust z-scores (already clipped to +/-4 by the module).</summary>
        static double? MedianZ(Sig.BaselineComparisonResult c, string name)
        {
            var z = c.Windows.Where(w => w.Deviations != null && w.Deviations[name].ZScore.HasValue)
                .Select(w => w.Deviations[name].ZScore.Value).OrderBy(v => v).ToList();
            if (z.Count == 0) return null;
            var m = z.Count % 2 == 1 ? z[z.Count / 2] : (z[z.Count / 2 - 1] + z[z.Count / 2]) / 2;
            return Math.Round(Math.Max(-4, Math.Min(4, m)), 4);
        }

        static StrainIndicator IndicatorOf(Sig.IndicatorResult r, SessionSummary s, bool usable)
        {
            // An unavailable/partial final window resets the current state; never resurrect a past value.
            var last = r.Windows.LastOrDefault()?.State;
            var probesMeasured = s.Metrics.ProbeLatencyMs.HasValue && s.Metrics.ProbeRmsErrorDeg.HasValue && s.Metrics.ProbeMissFraction.HasValue;
            var available = usable && last?.Value != null && s.Baseline.Windows >= BaselinePolicy.MinWindows && probesMeasured;
            return new StrainIndicator
            {
                Level = available ? Level(last.Level) : StrainLevel.Unavailable,
                Value = available ? Math.Round(last.Value.Value, 4) : (double?)null,
                MethodVersion = Sig.IndicatorPolicy.MethodVersion,
            };
        }

        static StrainLevel Level(string level) =>
            level == "low" ? StrainLevel.Low : level == "moderate" ? StrainLevel.Moderate : level == "high" ? StrainLevel.High : StrainLevel.Unavailable;

        static MetricValues Values(Func<string, double?> get)
        {
            double? R(string n) { var v = get(n); return v.HasValue ? Math.Round(v.Value, 4) : (double?)null; }
            return new MetricValues
            {
                GazeSpeedMedianDegS = R("gaze_speed_median_deg_s"),
                GazeSpeedP95DegS = R("gaze_speed_p95_deg_s"),
                GazeDispersionDeg = R("gaze_dispersion_deg"),
                ProbeLatencyMs = R("probe_latency_ms"),
                ProbeRmsErrorDeg = R("probe_rms_error_deg"),
                ProbeMissFraction = R("probe_miss_fraction"),
                HeadSpeedDegS = R("head_speed_deg_s"),
            };
        }

        static string Wire(DataSource d) => d == DataSource.Live ? "live" : d == DataSource.Replay ? "replay" : "synthetic";
        static string Wire(ReplayOrigin? o) => o == null ? null : o == ReplayOrigin.Recorded ? "recorded" : "synthetic";
        }

    /// <summary>Per-trial probe outcome counts and medians for baseline and task (diagnostics, not the v1 contract).</summary>
    public sealed class ProbeTrialStats
    {
        public PhaseStats Baseline = new PhaseStats();
        public PhaseStats Task = new PhaseStats();

        public sealed class PhaseStats
        {
            public int Acquired, Missed, Unavailable;
            public double? MedianLatencyMs, MedianHoldRmsDeg;

            public int Trials => Acquired + Missed + Unavailable;

            public override string ToString() => Trials == 0
                ? "no trials"
                : $"{Trials} trials: {Acquired} acquired, {Missed} missed, {Unavailable} not evaluable; median latency "
                  + (MedianLatencyMs.HasValue ? $"{MedianLatencyMs.Value:0} ms" : "n/a")
                  + ", median hold error " + (MedianHoldRmsDeg.HasValue ? $"{MedianHoldRmsDeg.Value:0.00} deg" : "n/a");

            internal static PhaseStats Of(IEnumerable<Sig.ProbeMeasurement> probes)
            {
                var list = probes.ToList();
                return new PhaseStats
                {
                    Acquired = list.Count(p => p.Outcome == "acquired"),
                    Missed = list.Count(p => p.Outcome == "missed"),
                    Unavailable = list.Count(p => p.Outcome == "unavailable"),
                    MedianLatencyMs = Median(list.Where(p => p.LatencyMs.HasValue).Select(p => p.LatencyMs.Value)),
                    MedianHoldRmsDeg = Median(list.Where(p => p.HoldRmsErrorDeg.HasValue).Select(p => p.HoldRmsErrorDeg.Value)),
                };
            }

            static double? Median(IEnumerable<double> values)
            {
                var v = values.OrderBy(x => x).ToList();
                if (v.Count == 0) return null;
                return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
            }
        }

        internal static ProbeTrialStats From(IEnumerable<Sig.MeasurementWindow> baselineWindows, IEnumerable<Sig.ProbeMeasurement> task) =>
            new ProbeTrialStats
            {
                Baseline = PhaseStats.Of(baselineWindows.Where(w => w.Measurements != null).SelectMany(w => w.Measurements.Probes)),
                Task = PhaseStats.Of(task),
            };
    }
}
