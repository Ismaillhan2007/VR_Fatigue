using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace VRFatigue.Signals.Tests
{
    public sealed class BaselineCalculationTests
    {
        private static string Root
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VR_FATIGUE_ROOT");
                if (!string.IsNullOrEmpty(configured)) return configured;
                var d = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "fixtures/baseline"))) d = d.Parent;
                return d != null ? d.FullName : throw new InvalidOperationException("Cannot locate baseline plans.");
            }
        }
        private static Direction3 Direction(double degrees)
        { return new Direction3(Math.Sin(degrees * Math.PI / 180), 0, Math.Cos(degrees * Math.PI / 180)); }
        private static HeadPose Head(double degrees)
        { return new HeadPose(new Direction3(), 0, Math.Sin(degrees * Math.PI / 360), 0, Math.Cos(degrees * Math.PI / 360)); }
        private static EyeSample Sample(long time, double gaze = 0, double head = 0, bool valid = true)
        { return new EyeSample(time, new EyeReading(valid, valid ? (Direction3?)Direction(gaze) : null), new EyeReading(valid, valid ? (Direction3?)Direction(gaze) : null), Head(head)); }
        private static AssessmentPhase Phase(long start = 0, long end = 120000000, string conditions = "neutral-probes-v1")
        { return new AssessmentPhase(start, end, conditions); }
        private static SessionInfo Info(long end = 160000000, string id = "00000000-0000-4000-8000-000000000003", string source = "synthetic", long start = 0)
        {
            return new SessionInfo(id, source, source == "replay" ? "synthetic" : null, start, end,
                new EyeCapabilities(new SignalAvailability(true, null), new SignalAvailability(false, "not_exposed_by_runtime"), new SignalAvailability(false, "not_exposed_by_runtime"), false));
        }
        private static EyeSession Steady(Func<long, EyeSample> make = null, ProbeEvent[] probes = null)
        { return new EyeSession(Info(), Enumerable.Range(0, 1601).Select(i => make == null ? Sample(i * 100000L) : make(i * 100000L)), probes ?? new ProbeEvent[0]); }
        private static EyeSession Demo()
        {
            var rows = new List<EyeSample>(); var events = new List<ProbeEvent>();
            for (long t = 0; t <= 160000000; t += 100000)
            {
                long index = t / 10000000, within = t % 10000000;
                bool baseline = t < 120000000;
                double error = within < (baseline ? 100000 : 200000) ? 6 : baseline ? 1 : 2;
                rows.Add(Sample(t, (index % 2 == 0 ? 0 : 12) + error));
            }
            for (int i = 0; i < 16; i++) events.AddRange(Probe(i * 10000000L, i * 10000000L + 1000000, i * 10000000L + 2000000, i % 2 == 0 ? 0 : 12, "probe-" + i));
            return new EyeSession(Info(source: "replay"), rows, events);
        }
        private static ProbeEvent[] Probe(long onset, long hold, long end, double target, string id)
        { return new[] { new ProbeEvent(onset, id, "target_onset", Direction(target)), new ProbeEvent(hold, id, "hold_start", Direction(target)), new ProbeEvent(end, id, "target_end", Direction(target)) }; }
        private static BaselineResult Build(EyeSession session, AssessmentPhase phase = null, BaselineSelfReport ratings = null, BaselinePolicy policy = null)
        { return BaselineEvaluator.Build(session, phase ?? Phase(), ratings ?? new BaselineSelfReport(2, 2), policy); }
        private static BaselineComparisonResult Fixture(string name)
        {
            string text = File.ReadAllText(Path.Combine(Root, "fixtures/baseline", name + ".plan.json"));
            var result = BaselineEvaluator.Evaluate(Demo(), BaselineJson.ParsePlan(text));
            string reports = Environment.GetEnvironmentVariable("VR_FATIGUE_REPORTS_DIR");
            if (!string.IsNullOrEmpty(reports)) File.WriteAllText(Path.Combine(reports, name + ".baseline.json"), BaselineJson.Serialize(result));
            return result;
        }

        [Test] public void SixFullWindowsProduceReferencesAndKnownProbeDeviations()
        {
            var result = Fixture("stable_then_changed");
            Assert.That(result.Baseline.Status, Is.EqualTo("ready"));
            Assert.That(result.Baseline.LowConfidence, Is.False);
            Assert.That(result.Baseline.Windows.Count, Is.EqualTo(6));
            Assert.That(result.Baseline.Windows.All(w => w.Eligible), Is.True);
            Assert.That(result.Baseline.References["probe_latency_ms"].Median.Value, Is.EqualTo(100));
            Assert.That(result.Baseline.References["probe_latency_ms"].Scale.Value, Is.EqualTo(10));
            Assert.That(result.Baseline.References["probe_rms_error_deg"].Median.Value, Is.EqualTo(1).Within(1e-8));
            foreach (var window in result.Windows)
            {
                Assert.That(window.Deviations["probe_latency_ms"].Delta.Value, Is.EqualTo(100));
                Assert.That(window.Deviations["probe_latency_ms"].ZScore.Value, Is.EqualTo(4));
                Assert.That(window.Deviations["probe_latency_ms"].Clipped.Value, Is.True);
                Assert.That(window.Deviations["probe_rms_error_deg"].ZScore.Value, Is.EqualTo(4));
            }
        }
        [Test] public void NinetySecondBaselineDoesNotPretendToHaveSixWindows()
        {
            var result = Fixture("short_baseline");
            Assert.That(result.Baseline.Status, Is.EqualTo("insufficient_data"));
            Assert.That(result.Baseline.Windows.Count(w => w.Complete), Is.EqualTo(4));
            Assert.That(result.Baseline.Windows.Last().Eligible, Is.False);
            CollectionAssert.Contains(result.Baseline.LowConfidenceReasons, "too_short");
            Assert.That(result.Windows.All(w => w.Deviations.Values.All(d => !d.ZScore.HasValue)), Is.True);
        }
        [Test] public void AlreadyTiredBaselineIsFlaggedButStillDescribesChangeSinceStart()
        {
            var result = Fixture("tired_baseline");
            Assert.That(result.Baseline.LowConfidence, Is.True);
            CollectionAssert.Contains(result.Baseline.LowConfidenceReasons, "self_reported_tired");
            Assert.That(result.Baseline.Status, Is.EqualTo("ready"));
            Assert.That(result.Status, Is.EqualTo("comparable"));
        }
        [Test] public void WindowSamplesAreHalfOpenWithoutComparisonBoundaryLeakage()
        {
            var session = Steady(t => Sample(t, t >= 120000000 ? 90 : 0));
            var reference = Build(session);
            Assert.That(reference.Windows.All(w => w.Measurements.Quality.AcceptedSamples == 200), Is.True);
            Assert.That(reference.Windows.Last().Measurements.Quality.AcceptedStream.Last().TimestampUs, Is.EqualTo(119900000));
            Assert.That(reference.Windows.Sum(w => w.Measurements.GazeValidDurationUs), Is.EqualTo(119400000));
            var result = BaselineEvaluator.Compare(reference, session, Phase(120000000, 160000000));
            Assert.That(result.Windows[0].Measurements.Quality.AcceptedStream[0].TimestampUs, Is.EqualTo(120000000));
        }
        [Test] public void MedianAndMadAreRobustToOneLargeWindowOutlier()
        {
            double[] speeds = { 1, 2, 3, 4, 5, 100, 6, 6 };
            var session = Steady(t => Sample(t, 0, speeds[(int)Math.Min(7, t / 20000000)] * ((t % 20000000) / 1000000.0)));
            var reference = Build(session);
            var head = reference.References["head_speed_deg_s"];
            Assert.That(head.Median.Value, Is.EqualTo(3.5).Within(1e-8));
            Assert.That(head.Mad.Value, Is.EqualTo(1.5).Within(1e-8));
            Assert.That(head.Scale.Value, Is.EqualTo(1.4826 * 1.5).Within(1e-8));
            var comparison = BaselineEvaluator.Compare(reference, session, Phase(120000000, 160000000));
            Assert.That(comparison.Windows[0].Deviations["head_speed_deg_s"].ZScore.Value, Is.EqualTo(2.5 / (1.4826 * 1.5)).Within(1e-8));
        }
        [Test] public void ZeroMadUsesRecordedPhysicalUnitFloors()
        {
            var reference = Build(Steady());
            var speed = reference.References["gaze_speed_median_deg_s"];
            Assert.That(speed.Mad.Value, Is.Zero); Assert.That(speed.Scale.Value, Is.EqualTo(1));
            Assert.That(reference.References["gaze_dispersion_deg"].Scale.Value, Is.EqualTo(0.1));
        }
        [Test] public void SignedZScoresClipWithoutOverflowEvenWithTinyFloors()
        {
            foreach (bool falling in new[] { false, true })
            {
                var source = Steady(t => Sample(t, 0, (t < 120000000 ? (falling ? 10 : 0) : (falling ? 0 : 10)) * ((t % 20000000) / 1000000.0)));
                var floors = new Dictionary<string, double>(new BaselinePolicy().Floors) { ["head_speed_deg_s"] = double.Epsilon };
                var result = BaselineEvaluator.Compare(Build(source, policy: new BaselinePolicy("tiny-floor-test", floors: floors)), source, Phase(120000000, 160000000));
                var d = result.Windows[0].Deviations["head_speed_deg_s"];
                Assert.That(d.ZScore.Value, Is.EqualTo(falling ? -4 : 4));
                Assert.That(d.Clipped.Value, Is.True);
            }
        }
        [Test] public void ExactZClipBoundaryIsNotMarkedAsClipped()
        {
            var demo = Demo();
            var rows = demo.Samples.Select(s => s.TimestampUs < 120000000 ? s : Sample(s.TimestampUs,
                (s.TimestampUs / 10000000 % 2 == 0 ? 0 : 12) + 12));
            var source = new EyeSession(demo.Info, rows, demo.ProbeEvents);
            var floors = new Dictionary<string, double>(new BaselinePolicy().Floors) { ["probe_miss_fraction"] = 0.25 };
            var result = BaselineEvaluator.Compare(Build(source, policy: new BaselinePolicy("exact-clip-test", floors: floors)), source, Phase(120000000, 160000000));
            var d = result.Windows[0].Deviations["probe_miss_fraction"];
            Assert.That(d.ZScore.Value, Is.EqualTo(4));
            Assert.That(d.Clipped.Value, Is.False);
        }
        [Test] public void OneBadWindowRequiresAnAdditionalCompleteUsableWindow()
        {
            var session = Steady(t => Sample(t, valid: t < 20000000 || t >= 40000000));
            var shortReference = Build(session);
            Assert.That(shortReference.Status, Is.EqualTo("insufficient_data"));
            CollectionAssert.Contains(shortReference.LowConfidenceReasons, "insufficient_usable_windows");
            var extended = Build(session, Phase(0, 140000000));
            Assert.That(extended.Status, Is.EqualTo("ready"));
            Assert.That(extended.Windows.Count(w => w.Eligible), Is.EqualTo(6));
        }
        [Test] public void MissingMetricInOneWindowDoesNotBorrowValuesFromOtherMetrics()
        {
            var session = Steady(t => { var s = Sample(t); return new EyeSample(t, s.Left, s.Right, t >= 100000000 && t < 120000000 ? null : s.Head); });
            var reference = Build(session);
            Assert.That(reference.Status, Is.EqualTo("ready"));
            Assert.That(reference.References["head_speed_deg_s"].UsableWindowCount, Is.EqualTo(5));
            Assert.That(reference.References["head_speed_deg_s"].Median, Is.Null);
            var comparison = BaselineEvaluator.Compare(reference, session, Phase(120000000, 160000000));
            Assert.That(comparison.Windows[0].Deviations["head_speed_deg_s"].UnavailableReason, Is.EqualTo("baseline_metric_unavailable"));
        }
        [Test] public void BaselineChoosesEyeWithoutLookingAtComparisonThenPinsIt()
        {
            var source = Steady(t => new EyeSample(t, new EyeReading(t < 120000000, Direction(0)),
                new EyeReading(t >= 120000000, Direction(30)), Head(0)));
            var baseline = Build(source);
            Assert.That(baseline.SelectedEye, Is.EqualTo("left"));
            var compared = BaselineEvaluator.Compare(baseline, source, Phase(120000000, 160000000));
            Assert.That(compared.Windows.All(w => w.Measurements.SelectedEye == "left" && !w.Eligible), Is.True);
            Assert.That(compared.Windows[0].Deviations["gaze_speed_p95_deg_s"].UnavailableReason, Is.EqualTo("insufficient_window_data"));
        }
        [Test] public void MissingSelfReportIsAVisibleLowConfidenceFlag()
        {
            var baseline = BaselineEvaluator.Build(Steady(), Phase());
            Assert.That(baseline.Status, Is.EqualTo("ready"));
            CollectionAssert.Contains(baseline.LowConfidenceReasons, "self_report_missing");
        }
        [Test] public void DifferentOrUnknownConditionsBlockDeviations()
        {
            var session = Steady();
            foreach (string conditions in new[] { "different-lighting", null })
            {
                var compared = BaselineEvaluator.Compare(Build(session), session, Phase(120000000, 160000000, conditions));
                Assert.That(compared.Status, Is.EqualTo("not_comparable"));
                Assert.That(compared.UnavailableReason, Is.EqualTo(conditions == null ? "conditions_unconfirmed" : "conditions_mismatch"));
                Assert.That(compared.Windows.All(w => w.Deviations.Values.All(d => !d.ZScore.HasValue)), Is.True);
            }
            CollectionAssert.Contains(Build(session, Phase(conditions: null)).LowConfidenceReasons, "conditions_unconfirmed");
        }
        [Test] public void PartialComparisonTailKeepsMeasurementsButHasNoDeviations()
        {
            var session = Steady();
            var compared = BaselineEvaluator.Compare(Build(session), session, Phase(120000000, 150000000));
            Assert.That(compared.Windows.Count, Is.EqualTo(2));
            Assert.That(compared.Windows[1].Complete, Is.False);
            Assert.That(compared.Windows[1].Deviations.Values.All(d => d.UnavailableReason == "partial_window"), Is.True);
        }
        [Test] public void GlobalOrderingIsCleanedBeforeWindowFiltering()
        {
            var source = Steady(); var rows = source.Samples.ToList();
            rows.Insert(199, Sample(21000000));
            var session = new EyeSession(source.Info, rows, source.ProbeEvents);
            var baseline = Build(session);
            Assert.That(baseline.Windows[0].Measurements.Quality.AcceptedStream.Last().TimestampUs, Is.EqualTo(19800000));
            var compared = BaselineEvaluator.Compare(baseline, session, Phase(120000000, 160000000));
            Assert.That(compared.SessionQuality.OutOfOrderTimestamps, Is.GreaterThan(0));
            Assert.That(compared.SessionQuality.DuplicateTimestamps, Is.EqualTo(1));
        }
        [Test] public void BoundaryCrossingProbesAreCountedAndNeverTruncatedIntoNewTrials()
        {
            var source = Steady(probes: Probe(19500000, 20500000, 21500000, 0, "crossing"));
            var baseline = Build(source);
            Assert.That(baseline.Windows[0].BoundaryProbeCount, Is.EqualTo(1));
            Assert.That(baseline.Windows[1].BoundaryProbeCount, Is.EqualTo(1));
            Assert.That(baseline.Windows.Take(2).All(w => w.Measurements.Probes.Count == 0), Is.True);
        }
        [Test] public void TargetSpacingIsValidatedAcrossWindowBoundaries()
        {
            var events = Probe(18000000, 18500000, 19000000, 0, "a").Concat(Probe(20000000, 20500000, 21000000, 1, "b")).ToArray();
            var reference = Build(Steady(probes: events));
            Assert.That(reference.Windows[1].Measurements.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("targets_too_close"));
        }
        [Test] public void CrossWindowOverlapStillInvalidatesAContainedTrial()
        {
            var events = Probe(19500000, 20500000, 21500000, 0, "crossing")
                .Concat(Probe(20800000, 21300000, 22000000, 12, "contained")).OrderBy(p => p.TimestampUs).ToArray();
            var reference = Build(Steady(probes: events));
            Assert.That(reference.Windows[1].Measurements.Probes.Single().AcquisitionUnavailableReason, Is.EqualTo("overlapping_probes"));
        }
        [Test] public void FrozenBaselineCanCompareAnAppendedCaptureWithoutChangingItsReferences()
        {
            var source = Demo();
            var early = new EyeSession(Info(120000000, source: "replay"), source.Samples.Where(s => s.TimestampUs <= 120000000), source.ProbeEvents.Where(p => p.TimestampUs < 120000000));
            var reference = Build(early);
            double value = reference.References["probe_latency_ms"].Median.Value;
            var compared = BaselineEvaluator.Compare(reference, source, Phase(120000000, 160000000));
            Assert.That(compared.Status, Is.EqualTo("comparable"));
            Assert.That(reference.References["probe_latency_ms"].Median.Value, Is.EqualTo(value));
        }
        [Test] public void UnrelatedSessionsAndClockOriginsCannotShareBaseline()
        {
            var source = Steady(); var baseline = Build(source);
            var another = new EyeSession(Info(id: "00000000-0000-4000-8000-000000000004"), source.Samples, source.ProbeEvents);
            Assert.Throws<ArgumentException>(() => BaselineEvaluator.Compare(baseline, another, Phase(120000000, 160000000)));
            another = new EyeSession(Info(source: "replay"), source.Samples, source.ProbeEvents);
            Assert.Throws<ArgumentException>(() => BaselineEvaluator.Compare(baseline, another, Phase(120000000, 160000000)));
            another = new EyeSession(Info(start: 1), source.Samples, source.ProbeEvents);
            Assert.Throws<ArgumentException>(() => BaselineEvaluator.Compare(baseline, another, Phase(120000000, 160000000)));
        }
        [Test] public void OverlappingOutsideAndExcessiveWindowsAreRejected()
        {
            var source = Steady(); var baseline = Build(source);
            Assert.Throws<ArgumentException>(() => BaselineEvaluator.Compare(baseline, source, Phase(100000000, 140000000)));
            Assert.Throws<ArgumentException>(() => Build(source, Phase(0, 180000000)));
            Assert.Throws<ArgumentException>(() => Build(source, policy: new BaselinePolicy("too-many", windowDurationUs: 1)));
        }
        [Test] public void EmptyPhasesReturnExplicitUnavailableResults()
        {
            var source = Steady(); var reference = Build(source, Phase(0, 0));
            Assert.That(reference.Windows, Is.Empty); Assert.That(reference.Status, Is.EqualTo("insufficient_data"));
            var compared = BaselineEvaluator.Compare(Build(source), source, Phase(120000000, 120000000));
            Assert.That(compared.UnavailableReason, Is.EqualTo("no_comparable_windows"));
        }
        [Test] public void PolicyFloorsAreCopiedAndRejectZeroOrMissingEntries()
        {
            var floors = new Dictionary<string, double>(new BaselinePolicy().Floors);
            var policy = new BaselinePolicy(floors: floors); floors["head_speed_deg_s"] = 99;
            Assert.That(policy.Floors["head_speed_deg_s"], Is.EqualTo(1));
            floors["head_speed_deg_s"] = 0;
            Assert.Throws<ArgumentException>(() => new BaselinePolicy(floors: floors));
            floors.Remove("head_speed_deg_s");
            Assert.Throws<ArgumentException>(() => new BaselinePolicy(floors: floors));
            Assert.Throws<ArgumentException>(() => new BaselineSelfReport(0, 2));
            Assert.Throws<ArgumentException>(() => new BaselinePolicy(minimumWindows: 5));
        }
        [Test] public void PlanReaderRejectsUnknownFieldsBadRatingsAndOverlappingPhases()
        {
            string text = File.ReadAllText(Path.Combine(Root, "fixtures/baseline/stable_then_changed.plan.json"));
            var o = JObject.Parse(text); o["script"] = "do-not-execute";
            Assert.Throws<FormatException>(() => BaselineJson.ParsePlan(o.ToString()));
            o = JObject.Parse(text); o["baseline"]["self_report"]["eye_strain"] = 8;
            Assert.Throws<FormatException>(() => BaselineJson.ParsePlan(o.ToString()));
            o = JObject.Parse(text); o["comparison"]["start_timestamp_us"] = 100;
            Assert.Throws<FormatException>(() => BaselineJson.ParsePlan(o.ToString()));
            Assert.Throws<FormatException>(() => BaselineJson.ParsePlan(text + "{}"));
        }
    }
}
