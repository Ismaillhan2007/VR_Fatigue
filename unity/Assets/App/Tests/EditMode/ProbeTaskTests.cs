using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using App.Capture;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.Core.Analysis;
using App.Core.Metrics;
using App.Metrics;
using NUnit.Framework;
using Vector3 = System.Numerics.Vector3;

namespace App.Tests
{
    public class ProbeTaskTests
    {
        const double Hz = 90;

        static MetricsContext Context(IEyeTrackingProvider p) => new MetricsContext
        {
            SessionId = SessionContext.NewSessionId(),
            DataSource = p.DataSource,
            ReplayOrigin = p.ReplayOrigin,
            Capabilities = p.Capabilities,
            Unavailable = p.Unavailable,
        };

        static void Begin(ReplayEyeProvider p, PhaseRecording phase, double now, double seconds)
        {
            p.BeginPhase(seconds, now);
            phase.StartTime = now;
            phase.SensorStartTime = p.SensorTime(now);
            phase.SensorClockRate = p.Speed;
        }

        static void End(ReplayEyeProvider p, PhaseRecording phase, double now)
        {
            phase.EndTime = now;
            phase.SensorEndTime = p.SensorTime(now);
        }

        /// <summary>Plays a replay at 1x through both phases, the way SessionController does (hold between phases).</summary>
        static (PhaseRecording task, BaselineData baseline, MetricsContext ctx) PlayDemo(double baselineS = 130, double taskS = 60)
        {
            var p = ReplayEyeProvider.FromFile(Path.Combine(TestFixtures.Root, "replay/demo_session.json"), ReplayOrigin.Recorded);
            var baseline = new BaselineData { SelfReport = new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 } };
            var task = new PhaseRecording();
            p.Start(0);
            double t = 0;
            Begin(p, baseline.Recording, t, baselineS);
            for (; t <= baselineS; t += 1 / 60.0) { p.ReadSamples(baseline.Recording.Samples, t); p.ReadProbes(baseline.Recording.Probes, t); }
            End(p, baseline.Recording, t);
            p.SetHold(true, t);
            t += 7; // participant answers the baseline sliders; the replay clock is held
            p.SetHold(false, t);
            Begin(p, task, t, taskS);
            var end = t + taskS + 1;
            for (; t <= end; t += 1 / 60.0) { p.ReadSamples(task.Samples, t); p.ReadProbes(task.Probes, t); }
            End(p, task, t);
            return (task, baseline, Context(p));
        }

        [Test]
        public void DemoReplayYieldsProbeMetricsDeviationsAndIndicator()
        {
            var (task, baseline, ctx) = PlayDemo();
            Assert.That(baseline.Recording.Probes.Count, Is.InRange(65, 66));
            var engine = new SignalsMetricsEngine();
            var s = engine.Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(DataSource.Replay, s.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, s.ReplayOrigin, "the demo file is synthetic and must stay labelled so");
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.That(s.Baseline.Windows, Is.GreaterThanOrEqualTo(6));
            Assert.IsNotNull(s.Metrics.ProbeLatencyMs);
            Assert.IsNotNull(s.Metrics.ProbeRmsErrorDeg);
            Assert.IsNotNull(s.Metrics.ProbeMissFraction);
            Assert.That(s.Deviations.ProbeLatencyMs, Is.GreaterThan(0), "the loaded part is slightly slower");
            Assert.That(s.Deviations.ProbeRmsErrorDeg, Is.GreaterThan(0), "and less accurate");
            Assert.AreNotEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
            Assert.AreEqual(StrainLevel.Moderate, s.StrainIndicator.Level);
            Assert.That(s.Quality.RepeatedSampleFraction, Is.InRange(0.015, 0.035));
            Assert.That(s.Quality.MaxGapMs, Is.GreaterThanOrEqualTo(880), "the 0.9 s stall is a gap");
            Assert.That(engine.LastProbeStats.Baseline.Trials, Is.GreaterThan(55));
            Assert.That(engine.LastProbeStats.Task.Missed, Is.GreaterThan(0));
            Assert.IsFalse(s.Quality.Unavailable.Any(u => u.Signal == Signal.Probe), "probes were collected");
        }

        [Test]
        public void ReplaySpeedDoesNotChangeTheMetrics()
        {
            var slow = PlayDemo();
            var p = ReplayEyeProvider.FromFile(Path.Combine(TestFixtures.Root, "replay/demo_session.json"), ReplayOrigin.Synthetic);
            p.Speed = 10;
            var baseline = new BaselineData { SelfReport = slow.baseline.SelfReport };
            var task = new PhaseRecording();
            p.Start(0);
            double t = 0;
            Begin(p, baseline.Recording, t, 130);
            for (; t <= 13; t += 1 / 60.0) { p.ReadSamples(baseline.Recording.Samples, t); p.ReadProbes(baseline.Recording.Probes, t); }
            End(p, baseline.Recording, t);
            p.SetHold(true, t);
            p.SetHold(false, t + 3);
            Begin(p, task, t + 3, 60);
            for (t += 3; t <= 23; t += 1 / 60.0) { p.ReadSamples(task.Samples, t); p.ReadProbes(task.Probes, t); }
            End(p, task, t);
            var ctx = slow.ctx;
            var a = new SignalsMetricsEngine().Compute(slow.task, slow.baseline, ctx);
            var b = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.AreEqual(a.Metrics.ProbeLatencyMs.Value, b.Metrics.ProbeLatencyMs.Value, 20, "latency within one frame-ish");
            Assert.AreEqual(a.StrainIndicator.Level, b.StrainIndicator.Level);
        }

        // ----- hand-built streams -----

        static GazeSample Row(double t, Vector3 dir, bool valid = true) => new GazeSample
        {
            FrameTime = t, SensorTimestamp = t, Valid = valid, LeftValid = valid, RightValid = valid,
            LeftDirection = dir, RightDirection = dir, CyclopeanDirection = dir,
            HeadTracked = true, HeadPosition = new Vector3(0, 1.6f, 0), HeadRotation = System.Numerics.Quaternion.Identity,
        };

        /// <summary>Gaze that lands on every probe target after <paramref name="latency"/> seconds.</summary>
        static (BaselineData, PhaseRecording, MetricsContext) Stream(double baselineS, double taskS, double latency,
            Func<double, bool> drop = null, Func<double, bool> duplicate = null, bool taskProbes = true)
        {
            var baseline = new BaselineData { SelfReport = new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 } };
            var task = new PhaseRecording();
            void Fill(PhaseRecording r, string prefix, double start, double length, bool probes)
            {
                r.StartTime = start; r.EndTime = start + length;
                r.SensorStartTime = start; r.SensorEndTime = start + length;
                var trials = Enumerable.Range(0, (int)(length / ProbeSchedule.TrialSeconds)).Select(i => ProbeSchedule.Trial(prefix, i, start + i * 2)).ToList();
                if (probes) r.Probes.AddRange(trials);
                var current = Vector3.UnitZ;
                for (var k = 0; k < length * Hz; k++)
                {
                    var t = start + k / Hz;
                    var trial = trials.LastOrDefault(x => x.OnsetTime <= t);
                    if (trial.TrialId != null && t >= trial.OnsetTime + latency) current = trial.TargetDirection;
                    if (drop != null && drop(t)) continue;
                    r.Samples.Add(Row(t, current));
                    if (duplicate != null && duplicate(t)) r.Samples.Add(Row(t, current));
                }
            }
            Fill(baseline.Recording, "b", 0, baselineS, true);
            Fill(task, "l", baselineS, taskS, taskProbes);
            var ctx = new MetricsContext
            {
                SessionId = SessionContext.NewSessionId(), DataSource = DataSource.Synthetic,
                Capabilities = new Capabilities { GazePose = true }, Unavailable = new List<UnavailableSignal>(),
            };
            return (baseline, task, ctx);
        }

        [Test]
        public void StallWithValidFlagsIsTreatedAsAGap()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25, drop: t => t >= 150 && t < 150.9);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.That(s.Quality.MaxGapMs, Is.InRange(890, 920), "valid=true on both sides must not hide the stall");
            Assert.That(s.Quality.GapDurationMs, Is.GreaterThanOrEqualTo(890));
            Assert.AreEqual(s.DurationMs, s.Quality.ValidDurationMs + s.Quality.GapDurationMs);
        }

        [Test]
        public void RepeatedSensorTimestampsAreDroppedBeforeMetrics()
        {
            var clean = Stream(130, 60, 0.25);
            var noisy = Stream(130, 60, 0.25, duplicate: t => ((int)(t * Hz)) % 40 == 0);
            noisy.Item3.SessionId = clean.Item3.SessionId;
            var a = new SignalsMetricsEngine().Compute(clean.Item2, clean.Item1, clean.Item3);
            var b = new SignalsMetricsEngine().Compute(noisy.Item2, noisy.Item1, noisy.Item3);
            Assert.That(b.Quality.RepeatedSampleFraction, Is.GreaterThan(0.01));
            Assert.AreEqual(a.Metrics.ProbeLatencyMs, b.Metrics.ProbeLatencyMs);
            Assert.AreEqual(a.Metrics.GazeSpeedP95DegS, b.Metrics.GazeSpeedP95DegS);
        }

        [Test]
        public void ProbeLatencyFollowsTheGaze()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.That(s.Metrics.ProbeLatencyMs, Is.InRange(240, 265));
            Assert.AreEqual(0, s.Metrics.ProbeMissFraction);
        }

        [Test]
        public void LateGazeIsAMissAtTheStrictPolicy()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.6); // 600 ms > 500 ms deadline
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            // Every trial is missed except one whose target equals where the gaze already rests.
            Assert.That(s.Metrics.ProbeMissFraction, Is.GreaterThan(0.9));
        }

        [Test]
        public void MissingTaskProbesMakeTheIndicatorUnavailable()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25, taskProbes: false);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.IsNull(s.Metrics.ProbeLatencyMs);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
            Assert.IsNull(s.StrainIndicator.Value);
        }

        [Test]
        public void ShortSessionIsInsufficientEvenWithProbes()
        {
            var (baseline, task, ctx) = Stream(4, 4, 0.25, drop: t => t > 1 && t < 7);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
        }

        [Test]
        public void SyntheticProviderFollowsScheduledTargets()
        {
            var p = new SyntheticEyeProvider(5);
            var rec = new PhaseRecording();
            p.Start(0);
            for (double t = 0; t < 40; t += 1 / 72.0)
            {
                var i = ProbeSchedule.TrialIndex(t);
                if (rec.Probes.Count == i)
                {
                    var trial = ProbeSchedule.Trial("b", i, t);
                    rec.Probes.Add(trial);
                    p.ShowTarget(trial.TargetDirection, t);
                }
                p.ReadSamples(rec.Samples, t);
            }
            var hits = rec.Probes.Count(pr => rec.Samples.Any(s => s.FrameTime > pr.OnsetTime && s.FrameTime < pr.EndTime
                && Vector3.Dot(s.CyclopeanDirection, pr.TargetDirection) > Math.Cos(2 * Math.PI / 180)));
            Assert.That(hits, Is.GreaterThanOrEqualTo(rec.Probes.Count - 2));
        }

        [Test]
        public void ProbeLayoutJumpsAtLeastTenDegrees()
        {
            var layout = ProbeSchedule.Layout;
            for (var i = 0; i < layout.Length; i++)
            {
                var a = ProbeSchedule.Direction(layout[i].yaw, layout[i].pitch);
                var b = ProbeSchedule.Direction(layout[(i + 1) % layout.Length].yaw, layout[(i + 1) % layout.Length].pitch);
                var deg = Math.Acos(Math.Min(1, Vector3.Dot(a, b))) * 180 / Math.PI;
                Assert.That(deg, Is.InRange(10, 40), $"trial {i}");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingTaskEdgesRemainMissingUntilTheDeclaredEnd(bool leading)
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25,
                drop: t => leading ? t >= 130 && t < 180 : t >= 140);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(190000, s.DurationMs);
            Assert.AreEqual(130000, s.Baseline.DurationMs);
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
            Assert.That(s.Quality.GapDurationMs, Is.GreaterThanOrEqualTo(50000));
            Assert.That(s.Quality.ValidCoverage, Is.LessThan(0.8));
            foreach (MetricName metric in Enum.GetValues(typeof(MetricName)))
            { Assert.IsNull(s.Metrics.Get(metric)); Assert.IsNull(s.Deviations.Get(metric)); }
            Assert.IsNull(s.StrainIndicator.Value);
        }

        [Test]
        public void FrameBoundsPreserveALostTailForLegacyCallers()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25, drop: t => t >= 140);
            baseline.Recording.SensorStartTime = baseline.Recording.SensorEndTime = null;
            task.SensorStartTime = task.SensorEndTime = null;
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.AreEqual(190000, s.DurationMs);
            Assert.That(s.Quality.GapDurationMs, Is.GreaterThanOrEqualTo(50000));
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
        }

        [Test]
        public void AnEmptyTaskKeepsItsFullDeclaredDuration()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25, drop: t => t >= 130);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.AreEqual(190000, s.DurationMs);
            Assert.That(s.Quality.GapDurationMs, Is.GreaterThanOrEqualTo(60000));
            Assert.IsNull(s.StrainIndicator.Value);
        }

        [Test]
        public void NoSamplesAtAllStillReportsTheCapturePeriod()
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25, drop: t => true);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.AreEqual(190000, s.DurationMs);
            Assert.AreEqual(190000, s.Quality.GapDurationMs);
            Assert.AreEqual(0, s.Quality.ValidDurationMs);
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
        }

        [Test]
        public void ARecentInvalidWindowDoesNotResurrectAnOlderIndicator()
        {
            var good = Stream(130, 60, 0.25);
            Assert.IsNotNull(new SignalsMetricsEngine().Compute(good.Item2, good.Item1, good.Item3).StrainIndicator.Value);
            for (int i = 0; i < good.Item2.Samples.Count; i++)
            {
                var row = good.Item2.Samples[i];
                if (row.SensorTimestamp >= 182) { row.Valid = row.LeftValid = row.RightValid = false; good.Item2.Samples[i] = row; }
            }
            var s = new SignalsMetricsEngine().Compute(good.Item2, good.Item1, good.Item3);
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
            Assert.IsNull(s.StrainIndicator.Value);
        }

        [Test]
        public void APartialFinalWindowAlsoClearsTheCurrentIndicator()
        {
            var (baseline, task, ctx) = Stream(130, 55, 0.25);
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
            Assert.IsNull(s.StrainIndicator.Value);
        }

        [TestCase(DataSource.Synthetic)]
        [TestCase(DataSource.Replay)]
        [TestCase(DataSource.Live)]
        public void CalculationErrorsPreserveProvenanceWithoutInventingMeasurements(DataSource source)
        {
            var (baseline, task, ctx) = Stream(130, 60, 0.25);
            ctx.DataSource = source;
            ctx.ReplayOrigin = source == DataSource.Replay ? ReplayOrigin.Recorded : (ReplayOrigin?)null;
            ctx.Capabilities.NativeEyeGaze = source == DataSource.Live;
            var engine = new SignalsMetricsEngine();
            Assert.IsTrue(MetricsEngineFactory.TryCompute(engine, task, baseline, ctx, out var good));
            Assert.NotNull(engine.LastProbeStats);
            var invalid = task.Probes[0]; invalid.TargetDirection = Vector3.Zero; task.Probes[0] = invalid;
            Assert.IsFalse(MetricsEngineFactory.TryCompute(engine, task, baseline, ctx, out var s));
            Assert.IsNull(engine.LastProbeStats);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(ctx.SessionId, s.SessionId); Assert.AreEqual(source, s.DataSource);
            Assert.AreEqual(ctx.ReplayOrigin, s.ReplayOrigin);
            Assert.AreEqual("processing-error.v1", s.MetricVersion);
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
            Assert.AreEqual(190000, s.DurationMs);
            Assert.AreEqual(s.DurationMs, s.Quality.GapDurationMs);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
            Assert.IsNull(s.StrainIndicator.Value);
            foreach (MetricName metric in Enum.GetValues(typeof(MetricName)))
            { Assert.IsNull(s.Metrics.Get(metric)); Assert.IsNull(s.Deviations.Get(metric)); }
            var request = AnalyzeRequest.Create(s, baseline.SelfReport, null);
            CollectionAssert.IsEmpty(request.Validate());
            StringAssert.DoesNotContain("stub-0.1", ContractJson.Serialize(request));
        }

        [Test]
        public void ReplayBoundariesAreExactAtAnySpeedAndSurviveAHeldClock()
        {
            var p = ReplayEyeProvider.FromFile(Path.Combine(TestFixtures.Root, "replay/demo_session.json"), ReplayOrigin.Synthetic);
            p.Speed = 10; p.Start(0); p.BeginPhase(130, 0);
            var rows = new List<GazeSample>(); p.ReadSamples(rows, 13.7);
            Assert.AreEqual(130, p.SensorTime(13.7), 1e-9);
            Assert.That(rows.Max(r => r.SensorTimestamp), Is.LessThanOrEqualTo(130));
            p.SetHold(true, 13.7); p.SetHold(false, 20);
            p.BeginPhase(60, 20); p.ReadSamples(rows, 26.9);
            Assert.AreEqual(190, p.SensorTime(26.9), 1e-9);
        }

        [Test]
        public void ReplayDoesNotMoveTheDeclaredStartToTheFirstAvailableRow()
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(TestFixtures.Read("replay/steady_gaze.json"));
            var rows = (Newtonsoft.Json.Linq.JArray)json["samples"];
            foreach (var row in rows.Where(r => (long)r["timestamp_us"] < 500000).ToList()) row.Remove();
            var p = ReplayEyeProvider.FromEyeSessionJson(json.ToString(), ReplayOrigin.Synthetic);
            p.Start(0); p.BeginPhase(1, 0);
            var output = new List<GazeSample>();
            p.ReadSamples(output, 0.25); Assert.IsEmpty(output);
            Assert.AreEqual(0.25, p.SensorTime(0.25), 1e-9);
        }
    }
}
