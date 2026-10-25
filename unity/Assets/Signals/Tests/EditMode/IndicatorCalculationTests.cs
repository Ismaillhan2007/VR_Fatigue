using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace VRFatigue.Signals.Tests
{
    public sealed class IndicatorCalculationTests
    {
        private static string Root
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VR_FATIGUE_ROOT");
                if (!string.IsNullOrEmpty(configured)) return configured;
                var d = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "fixtures/indicator"))) d = d.Parent;
                return d != null ? d.FullName : throw new InvalidOperationException("Cannot find indicator fixtures.");
            }
        }
        private static Direction3 Direction(double degrees)
        { return new Direction3(Math.Sin(degrees * Math.PI / 180), 0, Math.Cos(degrees * Math.PI / 180)); }
        private static EyeSession Demo(bool probes = true, Func<EyeSample, EyeSample> transform = null)
        {
            var info = new SessionInfo("00000000-0000-4000-8000-000000000003", "replay", "synthetic", 0, 200000000,
                new EyeCapabilities(new SignalAvailability(true, null), new SignalAvailability(false, "not_exposed_by_runtime"),
                    new SignalAvailability(false, "not_exposed_by_runtime"), false));
            var rows = new List<EyeSample>(); var events = new List<ProbeEvent>();
            for (long t = 0; t <= info.EndTimestampUs; t += 100000)
            {
                long index = t / 10000000, within = t % 10000000;
                bool baseline = t < 120000000;
                double error = within < (baseline ? 100000 : 200000) ? 6 : baseline ? 1 : 2;
                var eye = new EyeReading(true, Direction((index % 2 == 0 ? 0 : 12) + error), 0);
                var sample = new EyeSample(t, eye, eye, new HeadPose(new Direction3(), 0, 0, 0, 1));
                rows.Add(transform == null ? sample : transform(sample));
            }
            if (probes)
            for (int i = 0; i < 20; i++)
            {
                var target = Direction(i % 2 == 0 ? 0 : 12);
                events.Add(new ProbeEvent(i * 10000000L, "probe-" + i, "target_onset", target));
                events.Add(new ProbeEvent(i * 10000000L + 1000000, "probe-" + i, "hold_start", target));
                events.Add(new ProbeEvent(i * 10000000L + 2000000, "probe-" + i, "target_end", target));
            }
            return new EyeSession(info, rows, events);
        }
        private static BaselineComparisonResult Compare(EyeSession source, long baselineEnd = 120000000,
            long comparisonEnd = 200000000, string conditions = "neutral-probes-v1", BaselinePolicy policy = null, MetricPolicy metrics = null)
        {
            var baseline = BaselineEvaluator.Build(source, new AssessmentPhase(0, baselineEnd, "neutral-probes-v1"), new BaselineSelfReport(2, 2), policy, metricPolicy: metrics);
            return BaselineEvaluator.Compare(baseline, source, new AssessmentPhase(baselineEnd, comparisonEnd, conditions));
        }
        private static IndicatorResult Fixture(string name)
        {
            var plan = BaselineJson.ParsePlan(File.ReadAllText(Path.Combine(Root, "fixtures/indicator", name + ".plan.json")));
            var source = Demo(name != "no_probes", name == "tracking_gap" ? (Func<EyeSample, EyeSample>)(s =>
                s.TimestampUs >= 160000000 && s.TimestampUs < 180000000 ?
                    new EyeSample(s.TimestampUs, new EyeReading(false, null), new EyeReading(false, null), s.Head) : s) : null);
            var result = IndicatorEvaluator.Evaluate(BaselineEvaluator.Evaluate(source, plan));
            string reports = Environment.GetEnvironmentVariable("VR_FATIGUE_REPORTS_DIR");
            if (!string.IsNullOrEmpty(reports)) File.WriteAllText(Path.Combine(reports, name + ".indicator.json"), IndicatorJson.Serialize(result));
            return result;
        }
        private static IndicatorState Advance(IndicatorStateMachine state, int index, double? value, string reason = null)
        { return state.Advance(index * 20000000L, (index + 1) * 20000000L, value, reason); }
        private static void Sequence(string name)
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(Root, "fixtures/indicator", name + ".sequence.json")));
            Assert.That(o["data_source"].Value<string>(), Is.EqualTo("synthetic"));
            Assert.That(o["test_only"].Value<bool>(), Is.True);
            var raw = (JArray)o["raw_values"];
            foreach (string key in new[] { "expected_values", "expected_levels", "expected_events", "expected_active", "expected_high_counts" })
                Assert.That(((JArray)o[key]).Count, Is.EqualTo(raw.Count));
            var machine = new IndicatorStateMachine(new IndicatorPolicy("sequence-test.v1", alpha: o["alpha"].Value<double>()));
            for (int i = 0; i < raw.Count; i++)
            {
                double? value = raw[i].Type == JTokenType.Null ? (double?)null : raw[i].Value<double>();
                var point = Advance(machine, i, value, value.HasValue ? null : "synthetic_unavailable");
                if (o["expected_values"][i].Type == JTokenType.Null) Assert.That(point.Value, Is.Null);
                else Assert.That(point.Value.Value, Is.EqualTo(o["expected_values"][i].Value<double>()).Within(1e-12));
                Assert.That(point.Level, Is.EqualTo(o["expected_levels"][i].Value<string>()));
                Assert.That(point.NudgeEvent, Is.EqualTo(o["expected_events"][i].Value<string>()));
                Assert.That(point.NudgeActive, Is.EqualTo(o["expected_active"][i].Value<bool>()));
                Assert.That(point.ConsecutiveHighWindows, Is.EqualTo(o["expected_high_counts"][i].Value<int>()));
            }
        }

        [Test] public void SyntheticRiseAndReleaseSequenceHasKnownEwmaAndLatchBehavior() { Sequence("rise_and_release"); }
        [Test] public void SyntheticMissingWindowSequenceRestartsConfirmation() { Sequence("missing_resets"); }
        [Test] public void SyntheticThresholdSequenceUsesExplicitInclusiveAndStrictBoundaries() { Sequence("threshold_boundaries"); }
        [Test] public void FourSustainedMeasuredWindowsOfferOnceOnTheThirdWindow()
        {
            var result = Fixture("sustained_change");
            Assert.That(result.Experimental, Is.True);
            Assert.That(result.Windows.Count, Is.EqualTo(4));
            Assert.That(result.Windows.All(w => w.State.Level == "high"), Is.True);
            CollectionAssert.AreEqual(new[] { "none", "none", "offer_break", "none" }, result.Windows.Select(w => w.State.NudgeEvent).ToArray());
            Assert.That(result.Windows[2].State.NudgeActive, Is.True);
        }
        [Test] public void TrackingLossDoesNotCompleteAHighStreakOrLeaveAStaleValue()
        {
            var result = Fixture("tracking_gap");
            Assert.That(result.Windows[2].State.Level, Is.EqualTo("unavailable"));
            Assert.That(result.Windows[2].State.Value, Is.Null);
            Assert.That(result.Windows[2].State.NudgeActive, Is.False);
            Assert.That(result.Windows[3].State.ConsecutiveHighWindows, Is.EqualTo(1));
            Assert.That(result.Windows.All(w => w.State.NudgeEvent == "none"), Is.True);
        }
        [Test] public void MissingBaselineProbesNeverProduceAZeroOrAnIndicator()
        {
            var result = Fixture("no_probes");
            Assert.That(result.Comparison.Baseline.Status, Is.EqualTo("ready"));
            Assert.That(result.Windows.All(w => w.State.Value == null && w.State.UnavailableReason == "baseline_probes_unavailable"), Is.True);
        }
        [Test] public void AlreadyTiredBaselineKeepsItsVisibleLimitation()
        {
            var result = Fixture("tired_baseline");
            Assert.That(result.Comparison.Baseline.LowConfidence, Is.True);
            Assert.That(result.Windows.All(w => w.State.Value.HasValue), Is.True);
            var json = JObject.Parse(IndicatorJson.Serialize(result));
            Assert.That(json["baseline_low_confidence"].Value<bool>(), Is.True);
            CollectionAssert.Contains(json["baseline_low_confidence_reasons"].Values<string>().ToArray(), "self_reported_tired");
        }
        [Test] public void ShortBaselineDisablesTheEntireIndicatorTimeline()
        {
            var result = Fixture("short_baseline");
            Assert.That(result.Windows.All(w => w.State.Level == "unavailable" && w.State.UnavailableReason == "baseline_unavailable"), Is.True);
        }
        [Test] public void ContributionsUseFixedWeightsAndNegativeP95Direction()
        {
            var comparison = Compare(Demo());
            var point = IndicatorEvaluator.Evaluate(comparison).Windows[0];
            Assert.That(point.Contributions["gaze_speed_p95_deg_s"].Direction, Is.EqualTo(-1));
            double expected = (3 * comparison.Windows[0].Deviations["probe_latency_ms"].ZScore.Value +
                2 * comparison.Windows[0].Deviations["probe_rms_error_deg"].ZScore.Value +
                2 * comparison.Windows[0].Deviations["probe_miss_fraction"].ZScore.Value -
                comparison.Windows[0].Deviations["gaze_speed_p95_deg_s"].ZScore.Value) / 8;
            Assert.That(point.SignedCombination.Value, Is.EqualTo(expected).Within(1e-12));
            Assert.That(point.State.RawValue.Value, Is.EqualTo(Math.Max(0, Math.Min(1, expected / 3))).Within(1e-12));
            Assert.That(point.Contributions["gaze_dispersion_deg"].Status, Is.EqualTo("disabled"));
            Assert.That(point.Contributions["head_speed_deg_s"].Contribution.Value, Is.Zero);
        }
        [Test] public void MissingAcquisitionLatencyDoesNotRenormalizeOtherWeights()
        {
            var source = Demo(transform: s => {
                if (s.TimestampUs < 120000000) return s;
                var eye = new EyeReading(true, Direction((s.TimestampUs / 10000000 % 2 == 0 ? 0 : 12) + 12));
                return new EyeSample(s.TimestampUs, eye, eye, s.Head);
            });
            var result = IndicatorEvaluator.Evaluate(Compare(source));
            Assert.That(result.Windows.All(w => w.State.UnavailableReason == "required_deviation_unavailable"), Is.True);
            Assert.That(result.Windows.All(w => w.SignedCombination == null), Is.True);
            CollectionAssert.Contains(result.Windows[0].MissingMetrics, "probe_latency_ms");
            Assert.That(result.Windows[0].Contributions["probe_rms_error_deg"].NormalizedWeight, Is.EqualTo(0.25));
        }
        [Test] public void NegativeSignedCombinationMapsToZeroWithoutClaimingAnImprovement()
        {
            var source = Demo(transform: s => {
                bool baseline = s.TimestampUs < 120000000;
                long within = s.TimestampUs % 10000000;
                double error = within < (baseline ? 200000 : 100000) ? 6 : baseline ? 2 : 1;
                var eye = new EyeReading(true, Direction((s.TimestampUs / 10000000 % 2 == 0 ? 0 : 12) + error));
                return new EyeSample(s.TimestampUs, eye, eye, s.Head);
            });
            var result = IndicatorEvaluator.Evaluate(Compare(source));
            Assert.That(result.Windows.All(w => w.SignedCombination < 0 && w.State.RawValue == 0 && w.State.Level == "low"), Is.True);
            Assert.That(result.Windows.All(w => w.State.NudgeActive == false), Is.True);
        }
        [Test] public void LargeSupportedCombinationClampsToOne()
        {
            var source = Demo(transform: s => {
                if (s.TimestampUs < 120000000 || s.TimestampUs / 10000000 % 2 == 0) return s;
                var eye = new EyeReading(true, Direction(24));
                return new EyeSample(s.TimestampUs, eye, eye, s.Head);
            });
            var result = IndicatorEvaluator.Evaluate(Compare(source));
            Assert.That(result.Windows.All(w => w.SignedCombination >= 3 && w.State.RawValue == 1 && w.State.Value == 1), Is.True);
        }
        [Test] public void MissingCurrentProbesAreExplicitlyUnavailable()
        {
            var source = Demo();
            source = new EyeSession(source.Info, source.Samples, source.ProbeEvents.Where(p => p.TimestampUs < 120000000));
            var result = IndicatorEvaluator.Evaluate(Compare(source));
            Assert.That(result.Windows.All(w => w.State.UnavailableReason == "probes_unavailable"), Is.True);
        }
        [Test] public void MissingP95ReferenceIsNotReplacedByDispersionOrHeadContext()
        {
            var source = Demo(transform: s => s.TimestampUs % 20000000 == 8000000 || s.TimestampUs % 20000000 == 18000000 ?
                new EyeSample(s.TimestampUs, new EyeReading(false, null), new EyeReading(false, null), s.Head) : s);
            var comparison = Compare(source, metrics: new MetricPolicy(smoothingWindow: 101));
            Assert.That(comparison.Baseline.References["probe_latency_ms"].Median.HasValue, Is.True);
            var result = IndicatorEvaluator.Evaluate(comparison);
            Assert.That(result.Windows.All(w => w.State.Level == "unavailable"), Is.True);
            CollectionAssert.Contains(result.Windows[0].MissingMetrics, "gaze_speed_p95_deg_s");
        }
        [Test] public void UnavailableDisabledHeadMetricDoesNotBlockRequiredGazeMetrics()
        {
            var source = Demo(transform: s => new EyeSample(s.TimestampUs, s.Left, s.Right));
            var point = IndicatorEvaluator.Evaluate(Compare(source)).Windows[0];
            Assert.That(point.State.Value.HasValue, Is.True);
            Assert.That(point.Contributions["head_speed_deg_s"].ZScore, Is.Null);
            Assert.That(point.Contributions["head_speed_deg_s"].Status, Is.EqualTo("disabled"));
        }
        [Test] public void DifferentConditionsBlockTheIndicatorEvenWhenMetricsExist()
        {
            var result = IndicatorEvaluator.Evaluate(Compare(Demo(), conditions: "different-light"));
            Assert.That(result.Windows.All(w => w.State.UnavailableReason == "conditions_mismatch"), Is.True);
        }
        [Test] public void PartialTailClearsAnActiveOfferWithoutInventingAReleaseObservation()
        {
            var result = IndicatorEvaluator.Evaluate(Compare(Demo(), comparisonEnd: 190000000));
            Assert.That(result.Windows[2].State.NudgeActive, Is.True);
            Assert.That(result.Windows[3].State.UnavailableReason, Is.EqualTo("partial_window"));
            Assert.That(result.Windows[3].State.NudgeActive, Is.False);
            Assert.That(result.Windows[3].State.NudgeEvent, Is.EqualTo("none"));
        }
        [Test] public void DifferentZClippingPolicyCannotSilentlyChangeTheIndicator()
        {
            var result = IndicatorEvaluator.Evaluate(Compare(Demo(), policy: new BaselinePolicy("custom-clip", zClip: 2)));
            Assert.That(result.Windows.All(w => w.State.UnavailableReason == "incompatible_baseline_policy"), Is.True);
        }
        [Test] public void DifferentWindowLengthCannotSilentlyChangeEwmaTiming()
        {
            var comparison = Compare(Demo(), baselineEnd: 60000000, comparisonEnd: 100000000,
                policy: new BaselinePolicy("custom-window", windowDurationUs: 10000000));
            var result = IndicatorEvaluator.Evaluate(comparison);
            Assert.That(result.Windows.All(w => w.State.UnavailableReason == "incompatible_baseline_policy"), Is.True);
        }
        [Test] public void RepeatedEvaluationStartsFreshAndIsDeterministic()
        {
            var comparison = Compare(Demo());
            string a = IndicatorJson.Serialize(IndicatorEvaluator.Evaluate(comparison));
            string b = IndicatorJson.Serialize(IndicatorEvaluator.Evaluate(comparison));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(IndicatorEvaluator.Evaluate(comparison).Windows[0].State.NudgeActive, Is.False);
        }
        [Test] public void FirstValidValueSeedsEwmaInsteadOfAnInventedZero()
        {
            var machine = new IndicatorStateMachine();
            Assert.That(Advance(machine, 0, 0.8).Value.Value, Is.EqualTo(0.8));
            Assert.That(Advance(machine, 1, 0).Value.Value, Is.EqualTo(0.56).Within(1e-12));
        }
        [Test] public void TimeGapResetsBothEwmaAndHighConfirmation()
        {
            var machine = new IndicatorStateMachine();
            Advance(machine, 0, 1); Advance(machine, 1, 1);
            var after = Advance(machine, 3, 1);
            Assert.That(after.ResetReason, Is.EqualTo("time_gap"));
            Assert.That(after.ConsecutiveHighWindows, Is.EqualTo(1));
            Assert.That(after.NudgeActive, Is.False);
            machine.Reset(); Advance(machine, 0, 0);
            Assert.That(Advance(machine, 2, 1).Value.Value, Is.EqualTo(1));
        }
        [Test] public void PublicResetClearsTimeCursorValueAndOfferState()
        {
            var machine = new IndicatorStateMachine();
            for (int i = 0; i < 3; i++) Advance(machine, i, 1);
            machine.Reset();
            var point = Advance(machine, 0, 0);
            Assert.That(point.Value.Value, Is.Zero); Assert.That(point.NudgeActive, Is.False);
            Assert.That(point.PreviousLevel, Is.EqualTo("unavailable")); Assert.That(point.ResetReason, Is.EqualTo("initial"));
        }
        [Test] public void UnavailableWindowImmediatelyClearsAnActiveOffer()
        {
            var machine = new IndicatorStateMachine();
            for (int i = 0; i < 3; i++) Advance(machine, i, 1);
            var point = Advance(machine, 3, null, "synthetic_unavailable");
            Assert.That(point.Value, Is.Null); Assert.That(point.RawValue, Is.Null);
            Assert.That(point.ConsecutiveHighWindows, Is.Zero); Assert.That(point.NudgeActive, Is.False);
            Assert.That(point.StateReset, Is.True); Assert.That(point.NudgeEvent, Is.EqualTo("none"));
        }
        [Test] public void DuplicateOverlappingAndBackwardsWindowsCannotCountTwice()
        {
            var machine = new IndicatorStateMachine(); Advance(machine, 0, 1);
            Assert.Throws<ArgumentException>(() => Advance(machine, 0, 1));
            Assert.Throws<ArgumentException>(() => machine.Advance(10000000, 30000000, 1));
            Assert.That(Advance(machine, 1, 1).ConsecutiveHighWindows, Is.EqualTo(2));
        }
        [Test] public void InvalidRawValuesAndAmbiguousReasonsAreRejectedBeforeStateChanges()
        {
            var machine = new IndicatorStateMachine();
            foreach (double value in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentException>(() => Advance(machine, 0, value));
            Assert.Throws<ArgumentException>(() => Advance(machine, 0, null));
            Assert.Throws<ArgumentException>(() => Advance(machine, 0, 1, "contradiction"));
            Assert.Throws<ArgumentException>(() => machine.Advance(0, 10000000, 1));
            Assert.That(Advance(machine, 0, 1).ConsecutiveHighWindows, Is.EqualTo(1));
        }
        [Test] public void HighCounterSaturatesAndOfferDoesNotRepeatWhileLatched()
        {
            var machine = new IndicatorStateMachine(); int offers = 0;
            for (int i = 0; i < 1005; i++)
            {
                var point = Advance(machine, i, 1);
                if (point.NudgeEvent == "offer_break") offers++;
                Assert.That(point.ConsecutiveHighWindows, Is.LessThanOrEqualTo(3));
            }
            Assert.That(offers, Is.EqualTo(1));
        }
        [Test] public void PolicyCopiesWeightsAndRejectsUnapprovedContextTerms()
        {
            var weights = new Dictionary<string, double>(new IndicatorPolicy().Weights);
            var policy = new IndicatorPolicy(weights: weights); weights["probe_latency_ms"] = 99;
            Assert.That(policy.Weights["probe_latency_ms"], Is.EqualTo(3));
            weights["gaze_dispersion_deg"] = 0.5;
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(weights: weights));
            weights["gaze_dispersion_deg"] = 0; weights["probe_latency_ms"] = 0;
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(weights: weights));
        }
        [Test] public void InvalidPolicyThresholdsAndOverflowingWeightsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(alpha: 0));
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(alpha: double.NaN));
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(normalizationScale: 0));
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(lowThreshold: 0.7));
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(releaseThreshold: 0.6));
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(requiredHighWindows: 0));
            var weights = new Dictionary<string, double>(new IndicatorPolicy().Weights);
            foreach (string key in weights.Keys.ToArray()) if (weights[key] > 0) weights[key] = double.MaxValue;
            Assert.Throws<ArgumentException>(() => new IndicatorPolicy(weights: weights));
        }
        [Test] public void LargeAbsoluteTimestampsKeepWindowTimingExact()
        {
            var machine = new IndicatorStateMachine();
            long start = long.MaxValue - 20000000;
            Assert.That(machine.Advance(start, long.MaxValue, 1).Value.Value, Is.EqualTo(1));
        }
    }
}
