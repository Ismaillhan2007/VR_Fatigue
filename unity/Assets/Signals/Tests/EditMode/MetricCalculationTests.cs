using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VRFatigue.Signals.Tests
{
    public sealed class MetricCalculationTests
    {
        private static string Root
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VR_FATIGUE_ROOT");
                if (!string.IsNullOrEmpty(configured)) return configured;
                var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures", "metrics"))) directory = directory.Parent;
                return directory != null ? directory.FullName : throw new InvalidOperationException("Cannot find metric fixtures.");
            }
        }
        private static Direction3 Direction(double degrees)
        { return new Direction3(Math.Sin(degrees * Math.PI / 180), 0, Math.Cos(degrees * Math.PI / 180)); }
        private static HeadPose Head(double degrees)
        { return new HeadPose(new Direction3(0, 0, 0), 0, Math.Sin(degrees * Math.PI / 360), 0, Math.Cos(degrees * Math.PI / 360)); }
        private static EyeSample Sample(long t, double degrees = 0, double head = 0)
        { return new EyeSample(t, new EyeReading(true, Direction(degrees)), new EyeReading(true, Direction(degrees)), Head(head)); }
        private static SessionInfo Info(long end = 2000000, long start = 0)
        {
            return new SessionInfo("00000000-0000-4000-8000-000000000002", "synthetic", null, start, end,
                new EyeCapabilities(new SignalAvailability(true, null), new SignalAvailability(false, "not_exposed_by_runtime"), new SignalAvailability(false, "not_exposed_by_runtime"), false));
        }
        private static EyeSample[] Steady() { return Enumerable.Range(0, 41).Select(i => Sample(i * 50000)).ToArray(); }
        private static ProbeEvent[] Probe(long onset = 0, long hold = 1000000, long end = 2000000, double target = 0, string id = "probe-1")
        {
            return new[] { new ProbeEvent(onset, id, "target_onset", Direction(target)), new ProbeEvent(hold, id, "hold_start", Direction(target)), new ProbeEvent(end, id, "target_end", Direction(target)) };
        }
        private static MetricsResult Run(IEnumerable<EyeSample> rows, ProbeEvent[] probes = null, MetricPolicy policy = null, SessionInfo info = null, QualityPolicy qualityPolicy = null)
        { return MetricsEvaluator.Evaluate(QualityEvaluator.Evaluate(info ?? Info(), rows, qualityPolicy), probes ?? new ProbeEvent[0], policy); }
        private static MetricsResult Fixture(string name)
        {
            var session = SignalJson.ParseSession(File.ReadAllText(Path.Combine(Root, "fixtures", "metrics", name + ".json")));
            var replay = new ReplayEyeProvider(session);
            var batch = replay.ReadAvailable(replay.Info.DurationUs);
            var result = MetricsEvaluator.Evaluate(QualityEvaluator.Evaluate(replay.Info, batch.Samples), batch.ProbeEvents);
            string reports = Environment.GetEnvironmentVariable("VR_FATIGUE_REPORTS_DIR");
            if (!string.IsNullOrEmpty(reports)) File.WriteAllText(Path.Combine(reports, name + ".metrics.json"), MetricsJson.Serialize(result));
            return result;
        }
        private static void Near(MetricsResult result, string key, double value, double tolerance = 1e-8)
        {
            Assert.That(result.Metrics[key].Value.HasValue, Is.True, key + ": " + result.Metrics[key].UnavailableReason);
            Assert.That(result.Metrics[key].Value.Value, Is.EqualTo(value).Within(tolerance), key);
            Assert.That(result.Metrics[key].UnavailableReason, Is.Null);
        }
        private static void Missing(MetricsResult result, string key, string reason)
        { Assert.That(result.Metrics[key].Value, Is.Null); Assert.That(result.Metrics[key].UnavailableReason, Is.EqualTo(reason)); }

        [Test] public void SteadyGazeAndAcquiredProbeHaveAnalyticalZeroMetrics()
        {
            var result = Fixture("steady_acquired");
            foreach (string key in result.Metrics.Keys) Near(result, key, 0);
            Assert.That(result.SelectedEye, Is.EqualTo("left"));
            Assert.That(result.GazeSpeedDurationUs, Is.EqualTo(2000000));
            Assert.That(result.Probes.Single().Outcome, Is.EqualTo("acquired"));
            Assert.That(result.Quality.Info.DataSource, Is.EqualTo("replay"));
        }
        [Test] public void KnownAngularRotationHasExpectedSpeedDispersionAndHeadContext()
        {
            var result = Fixture("known_rotation");
            Near(result, "gaze_speed_median_deg_s", 2, 0.0001);
            Near(result, "gaze_speed_p95_deg_s", 2, 0.0001);
            // Four-degree sweep; trapezoidal angular variance = L^2/12 + step^2/6.
            Near(result, "gaze_dispersion_deg", Math.Sqrt(16.0 / 12 + 0.01 / 6));
            Near(result, "head_speed_deg_s", 20);
            Missing(result, "probe_latency_ms", "no_probes");
            Missing(result, "probe_miss_fraction", "no_probes");
        }
        [Test] public void IrregularTimestampsKeepConstantAngularSpeedInPhysicalUnits()
        {
            var result = Fixture("irregular_rotation");
            Near(result, "gaze_speed_median_deg_s", 2, 0.0001);
            Near(result, "gaze_speed_p95_deg_s", 2, 0.0001);
            Near(result, "head_speed_deg_s", 20);
            Assert.That(result.GazeSpeedDurationUs, Is.EqualTo(2000000));
        }
        [Test] public void ProbeLatencyUsesRawSamplesInsteadOfSmoothedLookAhead()
        {
            var result = Fixture("delayed_acquisition");
            Near(result, "probe_latency_ms", 200);
            Near(result, "probe_miss_fraction", 0);
            Near(result, "probe_rms_error_deg", 0);
        }
        [Test] public void FullyObservedMissHasNoInventedLatency()
        {
            var result = Fixture("probe_missed");
            Near(result, "probe_miss_fraction", 1);
            Near(result, "probe_rms_error_deg", 12);
            Missing(result, "probe_latency_ms", "no_acquired_probes");
            Assert.That(result.Probes.Single().Outcome, Is.EqualTo("missed"));
        }
        [Test] public void AcquisitionTrackingLossIsExcludedInsteadOfCountedAsMiss()
        {
            var result = Fixture("acquisition_tracking_loss");
            Assert.That(result.Quality.Status, Is.EqualTo("usable"));
            Assert.That(result.Probes.Single().Outcome, Is.EqualTo("unavailable"));
            Assert.That(result.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("acquisition_tracking_loss"));
            Missing(result, "probe_miss_fraction", "no_evaluable_probes");
            Near(result, "probe_rms_error_deg", 0);
        }
        [Test] public void GazeRemainsHeadRelativeWhileHeadRotates()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, 0, 20 * s.TimestampUs / 1000000.0)));
            Near(result, "gaze_speed_p95_deg_s", 0);
            Near(result, "head_speed_deg_s", 20);
        }
        [Test] public void FixedEyeSelectionNeverSwitchesToBridgeTrackingLoss()
        {
            var rows = Steady().Select(s => new EyeSample(s.TimestampUs,
                new EyeReading(s.TimestampUs <= 1000000, Direction(0)), new EyeReading(s.TimestampUs >= 1000000, Direction(30)), s.Head));
            var result = Run(rows);
            Assert.That(result.Quality.Status, Is.EqualTo("usable"));
            Assert.That(result.SelectedEye, Is.EqualTo("left"));
            Missing(result, "gaze_speed_median_deg_s", "insufficient_eye_coverage");
            Missing(result, "gaze_dispersion_deg", "insufficient_eye_coverage");
        }
        [Test] public void AutoEyeSelectionUsesValidDurationAndCanBePinned()
        {
            var rows = Steady().Select(s => new EyeSample(s.TimestampUs, new EyeReading(false, null), s.Right, s.Head)).ToArray();
            var automatic = Run(rows);
            Assert.That(automatic.SelectedEye, Is.EqualTo("right"));
            Near(automatic, "gaze_speed_p95_deg_s", 0);
            var pinned = Run(rows, policy: new MetricPolicy(gazeEye: "left"));
            Assert.That(pinned.SelectedEye, Is.EqualTo("left"));
            Missing(pinned, "gaze_speed_p95_deg_s", "no_continuous_gaze");
        }
        [Test] public void ATrackingGapDoesNotBecomeAnAngularVelocitySpike()
        {
            var rows = Steady().Where(s => s.TimestampUs <= 800000 || s.TimestampUs >= 1000000)
                .Select(s => Sample(s.TimestampUs, s.TimestampUs < 1000000 ? 0 : 90));
            var result = Run(rows);
            Near(result, "gaze_speed_p95_deg_s", 0);
            Assert.That(result.GazeSpeedDurationUs, Is.EqualTo(1800000));
            Assert.That(result.Metrics["gaze_dispersion_deg"].Value.Value, Is.GreaterThan(40));
        }
        [Test] public void MissingSmoothingSupportProducesNullInsteadOfRawFallback()
        {
            var result = Run(Steady(), policy: new MetricPolicy(smoothingWindow: 101));
            Missing(result, "gaze_speed_p95_deg_s", "insufficient_smoothing_support");
            Near(result, "gaze_dispersion_deg", 0);
        }
        [Test] public void OppositeDirectionsHaveNoArbitraryMeanDispersion()
        {
            var times = Enumerable.Range(0, 21).Select(i => i * 50000L).Concat(Enumerable.Range(0, 21).Select(i => 1200000L + i * 50000));
            var result = Run(times.Select(t => Sample(t, t < 1200000 ? 0 : 180)), info: Info(2200000));
            Missing(result, "gaze_dispersion_deg", "ambiguous_mean_direction");
        }
        [Test] public void HugeAndUnnormalizedDirectionsDoNotOverflow()
        {
            var result = Run(Steady().Select(s => new EyeSample(s.TimestampUs,
                new EyeReading(true, new Direction3(double.MaxValue, 0, double.MaxValue)), s.Right, s.Head)), policy: new MetricPolicy(gazeEye: "left"));
            Near(result, "gaze_speed_p95_deg_s", 0);
            Near(result, "gaze_dispersion_deg", 0);
        }
        [Test] public void QuaternionSignChangesDoNotCreateHeadMotion()
        {
            var result = Run(Steady().Select((s, i) => new EyeSample(s.TimestampUs, s.Left, s.Right,
                new HeadPose(new Direction3(), 0, 0, 0, i % 2 == 0 ? 1 : -1))));
            Near(result, "head_speed_deg_s", 0);
        }
        [Test] public void MissingHeadPoseCannotBeBridgedBetweenDistantSamples()
        {
            var rows = Steady().Select(s => new EyeSample(s.TimestampUs, s.Left, s.Right,
                s.TimestampUs == 0 || s.TimestampUs == 2000000 ? s.Head : null));
            Missing(Run(rows), "head_speed_deg_s", "no_head_pose_intervals");
        }
        [Test] public void LowHeadPoseCoverageDoesNotProduceASessionMedian()
        {
            var rows = Steady().Select(s => new EyeSample(s.TimestampUs, s.Left, s.Right, s.TimestampUs <= 450000 ? s.Head : null));
            Missing(Run(rows), "head_speed_deg_s", "insufficient_head_coverage");
        }
        [Test] public void MedianHeadSpeedIsWeightedByDurationNotSampleDensity()
        {
            var times = Enumerable.Range(0, 17).Select(i => i * 100000L).Concat(Enumerable.Range(1, 80).Select(i => 1600000L + i * 5000));
            var result = Run(times.Select(t => Sample(t, 0, t <= 1600000 ? 2 * t / 1000000.0 : 3.2 + 100 * (t - 1600000) / 1000000.0)));
            Near(result, "head_speed_deg_s", 2);
        }
        [Test] public void HitExactlyOnDeadlineCountsAsAcquired()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, s.TimestampUs < 1000000 ? 12 : 0)), Probe());
            Near(result, "probe_latency_ms", 1000);
            Near(result, "probe_miss_fraction", 0);
        }
        [Test] public void HitAfterDeadlineRemainsAMiss()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, s.TimestampUs <= 1000000 ? 12 : 0)), Probe());
            Near(result, "probe_miss_fraction", 1);
            Missing(result, "probe_latency_ms", "no_acquired_probes");
        }
        [Test] public void ShortUnacquiredTrialIsNotAFalseMiss()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, 12)), Probe(0, 250000, 500000));
            Assert.That(result.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("probe_too_short"));
            Missing(result, "probe_miss_fraction", "no_evaluable_probes");
        }
        [Test] public void HoldCoverageCanFailIndependentlyOfSuccessfulAcquisition()
        {
            var rows = Steady().Select(s => s.TimestampUs >= 1300000 && s.TimestampUs <= 1500000 ?
                new EyeSample(s.TimestampUs, new EyeReading(false, null), new EyeReading(false, null), s.Head) : s);
            var result = Run(rows, Probe());
            Assert.That(result.Quality.Status, Is.EqualTo("usable"));
            Near(result, "probe_latency_ms", 0);
            Assert.That(result.Probes.Single().HoldUnavailableReason, Is.EqualTo("insufficient_hold_coverage"));
            Missing(result, "probe_rms_error_deg", "no_usable_hold");
        }
        [Test] public void ProbeErrorsAreIntegratedOnlyInsideClippedHoldBounds()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, 12)), Probe(25000, 1025000, 1825000));
            Near(result, "probe_rms_error_deg", 12);
            Assert.That(result.Probes.Single().HoldValidDurationMs, Is.EqualTo(800));
        }
        [Test] public void ChangingHoldErrorsUseClippedSquaredErrorIntegration()
        {
            var result = Run(Steady().Select(s => Sample(s.TimestampUs, 10 * s.TimestampUs / 1000000.0)), Probe(0, 1025000, 1825000));
            double a = 1.025, b = 1.825, step = 0.05;
            // Integral of (10t)^2 plus the trapezoidal piecewise-linear squared-error correction.
            double expected = Math.Sqrt(100 * ((a * a + a * b + b * b) / 3 + step * step / 6));
            Near(result, "probe_rms_error_deg", expected);
        }
        [Test] public void AggregateMissFractionAndRmsUseEvaluableTrialsAndValidHoldTime()
        {
            var events = Probe(0, 500000, 1000000, 0, "a").Concat(Probe(1000000, 1500000, 2000000, 12, "b")).ToArray();
            var result = Run(Steady(), events);
            Near(result, "probe_miss_fraction", 0.5);
            Near(result, "probe_latency_ms", 0);
            Near(result, "probe_rms_error_deg", Math.Sqrt(72));
            Assert.That(result.Probes.Count, Is.EqualTo(2));
        }
        [Test] public void MissingOrRepeatedProbePhasesAreUnavailable()
        {
            foreach (var events in new[] { Probe().Where(p => p.Kind != "hold_start").ToArray(), Probe().Concat(new[] { Probe()[2] }).ToArray() })
            {
                var result = Run(Steady(), events);
                Assert.That(result.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("invalid_probe_sequence"));
                Missing(result, "probe_miss_fraction", "no_evaluable_probes");
            }
        }
        [Test] public void MovingTargetIsNotTreatedAsAStaticProbe()
        {
            var events = Probe();
            events[1] = new ProbeEvent(1000000, "probe-1", "hold_start", Direction(12));
            Assert.That(Run(Steady(), events).Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("moving_target_unsupported"));
        }
        [Test] public void OverlappingTrialsAreBothExcluded()
        {
            var events = Probe().Concat(Probe(500000, 1500000, 2000000, 12, "b")).OrderBy(p => p.TimestampUs).ToArray();
            var result = Run(Steady(), events);
            Assert.That(result.Probes.All(p => p.AcquisitionUnavailableReason == "overlapping_probes"), Is.True);
        }
        [Test] public void TargetSeparationRuleHasAnInclusiveSixDegreeBoundary()
        {
            foreach (double angle in new[] { 5.9, 6.0 })
            {
                var events = Probe(0, 500000, 1000000).Concat(Probe(1000000, 1500000, 2000000, angle, "b")).ToArray();
                var result = Run(Steady(), events);
                Assert.That(result.Probes[1].AcquisitionUnavailableReason, Is.EqualTo(angle < 6 ? "targets_too_close" : null));
                Assert.That(result.Probes[1].Outcome, Is.EqualTo(angle < 6 ? "unavailable" : "missed"));
            }
        }
        [Test] public void InsufficientDataSuppressesEveryMetric()
        {
            var result = Fixture("insufficient_gaze");
            Assert.That(result.Metrics.Values.All(v => !v.Value.HasValue && v.UnavailableReason == "insufficient_data"), Is.True);
            Assert.That(result.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("insufficient_data"));
        }
        [Test] public void MetricPolicyRejectsBadWindowsAndNonfiniteThresholds()
        {
            Assert.Throws<ArgumentException>(() => new MetricPolicy(smoothingWindow: 6));
            Assert.Throws<ArgumentException>(() => new MetricPolicy(gazeEye: "head"));
            Assert.Throws<ArgumentException>(() => new MetricPolicy(probeToleranceDeg: double.NaN));
            Assert.Throws<ArgumentException>(() => new MetricPolicy(acquisitionDeadlineUs: 0));
            Assert.Throws<ArgumentException>(() => new MetricPolicy(minimumHoldCoverage: 0));
        }
        [Test] public void MetricCalculationsPreserveMicrosecondPrecisionAtLargeTimestamps()
        {
            long start = long.MaxValue - 2000000;
            var result = Run(Steady().Select(s => Sample(start + s.TimestampUs)), info: Info(long.MaxValue, start));
            Near(result, "gaze_speed_median_deg_s", 0);
            Assert.That(result.GazeSpeedDurationUs, Is.EqualTo(2000000));
        }
    }
}
