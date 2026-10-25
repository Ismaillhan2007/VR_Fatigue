using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace VRFatigue.Signals.Tests
{
    public sealed class SignalPipelineTests
    {
        private static string Root
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VR_FATIGUE_ROOT");
                if (!string.IsNullOrEmpty(configured)) return configured;
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "fixtures", "replay"))) dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("Cannot locate synthetic fixtures; set VR_FATIGUE_ROOT.");
                return dir.FullName;
            }
        }
        private static string Fixture(string name) { return Path.Combine(Root, "fixtures", "replay", name + ".json"); }
        private static EyeSession Load(string name = "steady_gaze") { return SignalJson.ParseSession(File.ReadAllText(Fixture(name))); }
        private static EyeSample Sample(long time, bool valid = true)
        {
            var eye = new EyeReading(valid, valid ? (Direction3?)new Direction3(0, 0, 1) : null, 0);
            return new EyeSample(time, eye, eye);
        }
        private static SessionInfo Info(long start = 0, long end = 1000000)
        { return new SessionInfo("00000000-0000-4000-8000-000000000001", "synthetic", null, start, end, Load().Info.Capabilities); }
        private static void CheckFixture(string name)
        {
            var session = Load(name);
            var provider = new ReplayEyeProvider(session);
            var samples = provider.ReadAvailable(provider.Info.DurationUs).Samples;
            var result = QualityEvaluator.Evaluate(provider.Info, samples);
            string actual = SignalJson.SerializeQuality(result);
            string expected = File.ReadAllText(Fixture(name + ".quality"));
            Assert.That(JToken.DeepEquals(JToken.Parse(actual), JToken.Parse(expected)), Is.True, name + " actual:\n" + actual);
            string reports = Environment.GetEnvironmentVariable("VR_FATIGUE_REPORTS_DIR");
            if (!string.IsNullOrEmpty(reports)) File.WriteAllText(Path.Combine(reports, name + ".quality.json"), actual);
        }

        [Test] public void SteadyFixtureHasFullCoverage() { CheckFixture("steady_gaze"); }
        [Test] public void TrackingGapFixtureCountsLostTime() { CheckFixture("tracking_gap"); }
        [Test] public void DuplicateAndOutOfOrderFixtureDoesNotInflateTime() { CheckFixture("duplicate_and_out_of_order"); }
        [Test] public void InvalidGazeFixturePreservesInvalidBarriers() { CheckFixture("invalid_gaze"); }
        [Test] public void OneEyeAndZeroConfidenceRemainUsable() { CheckFixture("monocular_zero_confidence"); }
        [Test] public void UnsupportedGazeRetainsUnavailableReasons() { CheckFixture("unsupported_gaze"); }
        [Test] public void EmptyFixtureReturnsInsufficientData() { CheckFixture("empty_session"); }
        [Test] public void ZeroDurationFixtureHasFiniteZeroCoverage() { CheckFixture("zero_duration"); }

        [Test] public void ReplayRespectsElapsedTimeAndDoesNotEmitTwice()
        {
            var p = new ReplayEyeProvider(Load());
            Assert.That(p.ReadAvailable(0).Samples.Count, Is.EqualTo(1));
            Assert.That(p.ReadAvailable(99999).Samples.Count, Is.EqualTo(0));
            var batch = p.ReadAvailable(100000);
            Assert.That(batch.Samples.Single().TimestampUs, Is.EqualTo(100000));
            Assert.That(batch.ProbeEvents.Single().Kind, Is.EqualTo("hold_start"));
            Assert.That(p.ReadAvailable(100000).Samples.Count, Is.Zero);
            Assert.That(p.IsComplete, Is.False);
            Assert.That(p.ReadAvailable(1000000).Samples.Count, Is.EqualTo(9));
            Assert.That(p.IsComplete, Is.True);
            Assert.That(p.ReadAvailable(2000000).ProbeEvents.Count, Is.Zero);
        }
        [Test] public void ReplayResetReproducesInputAndRejectsBackwardsClock()
        {
            var p = new ReplayEyeProvider(Load());
            var first = p.ReadAvailable(500000).Samples.Select(s => s.TimestampUs).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadAvailable(499999));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadAvailable(-1));
            p.Reset();
            Assert.That(p.IsComplete, Is.False);
            CollectionAssert.AreEqual(first, p.ReadAvailable(500000).Samples.Select(s => s.TimestampUs).ToArray());
        }
        [Test] public void ReplayChunkSizeDoesNotChangeQualityOrArrivalOrder()
        {
            var input = Load("duplicate_and_out_of_order");
            var p = new ReplayEyeProvider(input);
            var chunks = new List<EyeSample>();
            for (long t = 0; t <= p.Info.DurationUs; t += 25000) chunks.AddRange(p.ReadAvailable(t).Samples);
            CollectionAssert.AreEqual(input.Samples, chunks);
            var incremental = SignalJson.SerializeQuality(QualityEvaluator.Evaluate(p.Info, chunks));
            p.Reset();
            var whole = SignalJson.SerializeQuality(QualityEvaluator.Evaluate(p.Info, p.ReadAvailable(p.Info.DurationUs).Samples));
            Assert.That(incremental, Is.EqualTo(whole));
        }
        [Test] public void ReplayNeverClaimsLiveData()
        {
            var s = Load();
            var live = new SessionInfo(s.Info.SessionId, "live", null, 0, 1000000, s.Info.Capabilities);
            var p = new ReplayEyeProvider(new EyeSession(live, s.Samples, s.ProbeEvents));
            Assert.That(p.Info.DataSource, Is.EqualTo("replay"));
            Assert.That(p.Info.ReplayOrigin, Is.EqualTo("recorded"));
            Assert.That(live.DataSource, Is.EqualTo("live"));
            var repeated = new ReplayEyeProvider(new EyeSession(s.Info.AsReplay(), s.Samples, s.ProbeEvents));
            Assert.That(repeated.Info.ReplayOrigin, Is.EqualTo("synthetic"));
        }
        [Test] public void ReplayWaitsForTrailingUnobservedTime()
        {
            var p = new ReplayEyeProvider(new EyeSession(Info(), new[] { Sample(0) }, new ProbeEvent[0]));
            p.ReadAvailable(0);
            Assert.That(p.IsComplete, Is.False);
            p.ReadAvailable(1000000);
            Assert.That(p.IsComplete, Is.True);
        }
        [Test] public void ReplayUsesRelativeClockAndStillDeliversOutOfBoundsRowsForAccounting()
        {
            var source = Load();
            var rows = new[] { Sample(999999) }.Concat(source.Samples.Select(s => Sample(1000000 + s.TimestampUs)))
                .Concat(new[] { Sample(2000001) });
            var events = source.ProbeEvents.Select(p => new ProbeEvent(p.TimestampUs + 1000000, p.ProbeId, p.Kind, p.TargetDirection));
            var pvd = new ReplayEyeProvider(new EyeSession(Info(1000000, 2000000), rows, events));
            var collected = new List<EyeSample>();
            var first = pvd.ReadAvailable(0);
            Assert.That(first.Samples.Count, Is.EqualTo(2));
            Assert.That(first.ProbeEvents.Single().TimestampUs, Is.EqualTo(1000000));
            collected.AddRange(first.Samples);
            var next = pvd.ReadAvailable(100000);
            Assert.That(next.Samples.Single().TimestampUs, Is.EqualTo(1100000));
            collected.AddRange(next.Samples);
            collected.AddRange(pvd.ReadAvailable(1000000).Samples);
            var quality = QualityEvaluator.Evaluate(pvd.Info, collected);
            Assert.That(quality.Status, Is.EqualTo("usable"));
            Assert.That(quality.OutOfBoundsTimestamps, Is.EqualTo(2));
        }
        [Test] public void SessionOwnsItsInputCollections()
        {
            var samples = new[] { Sample(0) };
            var s = new EyeSession(Info(), samples, new ProbeEvent[0]);
            samples[0] = Sample(500000);
            Assert.That(s.Samples[0].TimestampUs, Is.Zero);
        }

        [Test] public void LeadingAndTrailingTimeCountAsMissing()
        {
            var stamps = Enumerable.Range(0, 6).Select(i => Sample(250000 + i * 100000));
            var q = QualityEvaluator.Evaluate(Info(), stamps);
            Assert.That(q.ValidDurationUs, Is.EqualTo(500000));
            Assert.That(q.GapDurationUs, Is.EqualTo(500000));
            Assert.That(q.LongestGapUs, Is.EqualTo(250000));
            Assert.That(q.ValidCoverage, Is.EqualTo(0.5));
        }
        [Test] public void GapLimitIsInclusiveAndLongerGapsAreNotInterpolated()
        {
            var exact = QualityEvaluator.Evaluate(Info(), new[] { Sample(0), Sample(100000) });
            var longer = QualityEvaluator.Evaluate(Info(), new[] { Sample(0), Sample(100001) });
            Assert.That(exact.ValidDurationUs, Is.EqualTo(100000));
            Assert.That(longer.ValidDurationUs, Is.Zero);
        }
        [Test] public void InvalidNonfiniteRowSplitsValidIntervals()
        {
            var input = Load().Samples.ToArray();
            var invalid = new EyeReading(true, new Direction3(double.NaN, 0, 1));
            input[5] = new EyeSample(500000, invalid, invalid);
            var q = QualityEvaluator.Evaluate(Info(), input);
            Assert.That(q.ValidDurationUs, Is.EqualTo(800000));
            Assert.That(q.LongestGapUs, Is.EqualTo(200000));
            Assert.That(q.ValidIntervals.Count, Is.EqualTo(2));
            Assert.That(q.AcceptedStream[5], Is.SameAs(input[5]));
        }
        [Test] public void InfinityAndZeroDirectionAreNotMeasurements()
        {
            Assert.That(new Direction3(double.PositiveInfinity, 0, 1).IsUsable, Is.False);
            Assert.That(new Direction3(0, 0, 0).IsUsable, Is.False);
            Assert.That(new EyeReading(false, new Direction3(0, 0, 1)).IsUsable, Is.False);
            Assert.That(new EyeReading(true, null).IsUsable, Is.False);
        }
        [Test] public void RepeatedTimestampUsesFirstOccurrenceEvenWhenInvalid()
        {
            var q = QualityEvaluator.Evaluate(Info(), new[] { Sample(0), Sample(100000, false), Sample(100000), Sample(200000) });
            Assert.That(q.ValidDurationUs, Is.Zero);
            Assert.That(q.InvalidGazeSamples, Is.EqualTo(1));
            Assert.That(q.DuplicateTimestamps, Is.EqualTo(1));
        }
        [Test] public void OutsideBoundsAndLateRowsAreExcludedWithoutSorting()
        {
            var q = QualityEvaluator.Evaluate(Info(), new[] { Sample(-1), Sample(0), Sample(100000), Sample(50000), Sample(50000), Sample(200000), Sample(1000001) });
            Assert.That(q.ValidDurationUs, Is.EqualTo(200000));
            Assert.That(q.OutOfBoundsTimestamps, Is.EqualTo(2));
            Assert.That(q.OutOfOrderTimestamps, Is.EqualTo(1));
            Assert.That(q.DuplicateTimestamps, Is.EqualTo(1));
        }
        [Test] public void CapabilityGateWinsOverApparentlyValidSamples()
        {
            var s = Load("unsupported_gaze");
            var q = QualityEvaluator.Evaluate(s.Info, Load().Samples);
            Assert.That(q.ValidDurationUs, Is.Zero);
            Assert.That(q.ValidGazeSamples, Is.Zero);
            Assert.That(q.InvalidGazeSamples, Is.EqualTo(11));
            CollectionAssert.Contains(q.Reasons, "gaze_unavailable");
        }
        [Test] public void CoverageAndDurationThresholdsAreInclusive()
        {
            var rows = Load().Samples.Where(s => s.TimestampUs <= 800000).ToArray();
            var q = QualityEvaluator.Evaluate(Info(), rows, new QualityPolicy("test", 100000, 800000, 0.8, 2));
            Assert.That(q.Status, Is.EqualTo("usable"));
            Assert.That(q.Reasons, Is.Empty);
            q = QualityEvaluator.Evaluate(Info(), rows, new QualityPolicy("test", 100000, 800001, 0.8, 2));
            Assert.That(q.Status, Is.EqualTo("insufficient_data"));
            CollectionAssert.Contains(q.Reasons, "too_short");
        }
        [Test] public void LargeAbsoluteTimestampsDoNotLosePrecision()
        {
            long start = long.MaxValue - 1000000;
            var q = QualityEvaluator.Evaluate(Info(start, long.MaxValue), Load().Samples.Select(s => Sample(start + s.TimestampUs)));
            Assert.That(q.ValidDurationUs, Is.EqualTo(1000000));
            Assert.That(q.Status, Is.EqualTo("usable"));
        }
        [Test] public void QualityAccountingHoldsForIrregularStreams()
        {
            var random = new Random(41);
            for (int run = 0; run < 100; run++)
            {
                var rows = new List<EyeSample>();
                long stamp = 0;
                for (int i = 0; i < 40; i++) { stamp += random.Next(0, 50000); rows.Add(Sample(stamp, random.Next(4) != 0)); }
                var q = QualityEvaluator.Evaluate(Info(), rows);
                Assert.That(q.ValidDurationUs + q.GapDurationUs, Is.EqualTo(1000000));
                Assert.That(q.ValidCoverage, Is.InRange(0.0, 1.0));
                Assert.That(q.AcceptedSamples + q.DuplicateTimestamps + q.OutOfOrderTimestamps + q.OutOfBoundsTimestamps, Is.EqualTo(q.InputSamples));
                Assert.That(q.ValidGazeSamples + q.InvalidGazeSamples, Is.EqualTo(q.AcceptedSamples));
                Assert.That(q.ValidIntervals.Sum(v => v.EndUs - v.StartUs), Is.EqualTo(q.ValidDurationUs));
                Assert.That(q.LongestGapUs, Is.LessThanOrEqualTo(q.GapDurationUs));
            }
        }

        [Test] public void JsonRejectsUnknownFieldsAtEveryBoundary()
        {
            string[] paths = { "", "capabilities", "capabilities.gaze", "samples[0]", "samples[0].left", "samples[0].left.direction", "samples[0].head_pose", "samples[0].head_pose.position_m", "probe_events[0]", "probe_events[0].target_direction" };
            foreach (string path in paths)
            {
                var o = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
                ((JObject)(path == "" ? o : o.SelectToken(path)))["script"] = "do-not-execute";
                Assert.Throws<FormatException>(() => SignalJson.ParseSession(o.ToString()), path);
            }
        }
        [Test] public void JsonRejectsMissingRequiredNullFieldsAndDuplicateProperties()
        {
            var o = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
            ((JObject)o["samples"][0]["left"]).Remove("confidence");
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(o.ToString()));
            string text = File.ReadAllText(Fixture("steady_gaze"));
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(text.Replace("\"confidence_informative\": false", "\"confidence_informative\": false, \"confidence_informative\": true")));
        }
        [Test] public void JsonRejectsBadVersionsFramesTypesAndProvenance()
        {
            var changes = new Dictionary<string, JToken> {
                { "schema_version", "eye-session.v2" }, { "coordinate_frame", "world" }, { "session_id", "not-a-uuid" },
                { "data_source", "head_gaze" }, { "replay_origin", "recorded" }, { "start_timestamp_us", -1 },
                { "end_timestamp_us", -1 }, { "capabilities.confidence_informative", "false" },
                { "samples[0].timestamp_us", 0.5 }, { "samples[0].left.confidence", 1.1 },
                { "samples[0].left.direction.x", double.PositiveInfinity }, { "probe_events[0].kind", "execute" },
                { "samples[0].head_pose.rotation_xyzw[3]", 0 }
            };
            foreach (var change in changes)
            {
                var o = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
                o.SelectToken(change.Key).Replace(change.Value.DeepClone());
                Assert.Throws<FormatException>(() => SignalJson.ParseSession(o.ToString()), change.Key);
            }
            var inverted = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
            inverted["start_timestamp_us"] = 2000000;
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(inverted.ToString()));
        }
        [Test] public void JsonRejectsInvalidProbeTimesAndUnavailableReasons()
        {
            var o = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
            o["probe_events"][0]["timestamp_us"] = 1000001;
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(o.ToString()));
            o = JObject.Parse(File.ReadAllText(Fixture("steady_gaze")));
            o["capabilities"]["pupil_diameter"]["reason"] = JValue.CreateNull();
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(o.ToString()));
        }
        [Test] public void InvalidPoliciesAndSessionBoundsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 0, 1, 0.8, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, -1, 0.8, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, 0, 0.8, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, 1, 0, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, 1, double.NaN, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, 1, 1.1, 2));
            Assert.Throws<ArgumentException>(() => new QualityPolicy("test", 1, 1, 0.8, 1));
            Assert.Throws<ArgumentException>(() => Info(1, 0));
        }
        [Test] public void JsonRejectsIntegerOverflowAndTrailingDocuments()
        {
            string text = File.ReadAllText(Fixture("steady_gaze"));
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(text.Replace("\"start_timestamp_us\": 0", "\"start_timestamp_us\": 9223372036854775808")));
            Assert.Throws<FormatException>(() => SignalJson.ParseSession(text + "{}"));
        }
    }
}
