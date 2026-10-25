using System.Collections.Generic;
using System.Linq;
using App.Capture;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.Core.Metrics;
using App.Metrics;
using NUnit.Framework;

namespace App.Tests
{
    public class CaptureAndMetricsTests
    {
        sealed class InvalidEngine : IMetricsEngine
        {
            public string MetricVersion => "invalid-test";
            public SessionSummary Compute(PhaseRecording task, BaselineData baseline, MetricsContext context)
                => new SessionSummary { SessionId = context.SessionId, DurationMs = -1 };
        }

        [Test]
        public void InvalidMetricsResultStillProducesValidInsufficientReportWithOriginalProvenance()
        {
            var context = new MetricsContext
            {
                SessionId = SessionContext.NewSessionId(), DataSource = DataSource.Live,
                Capabilities = new Capabilities { NativeEyeGaze = true, GazePose = true },
                PostTaskReport = new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 4 },
            };
            Assert.IsFalse(MetricsEngineFactory.TryCompute(new InvalidEngine(), new PhaseRecording(),
                new BaselineData(), context, out var summary));
            CollectionAssert.IsEmpty(summary.Validate());
            Assert.AreEqual(DataSource.Live, summary.DataSource);
            Assert.AreEqual(context.SessionId, summary.SessionId);
            Assert.AreEqual(QualityStatus.InsufficientData, summary.Quality.Status);
            Assert.IsNull(summary.Metrics.GazeSpeedMedianDegS);
        }

        static List<GazeSample> Run(IEyeTrackingProvider p, double seconds)
        {
            var buffer = new List<GazeSample>();
            p.Start(0);
            for (var t = 0.0; t <= seconds; t += 1.0 / 72) p.ReadSamples(buffer, t);
            p.Stop();
            return buffer;
        }

        [Test]
        public void SyntheticProviderIsLabelledAndDeterministic()
        {
            var a = Run(new SyntheticEyeProvider(7), 3);
            var b = Run(new SyntheticEyeProvider(7), 3);
            var p = new SyntheticEyeProvider(7);
            Assert.AreEqual(DataSource.Synthetic, p.DataSource);
            Assert.IsNull(p.ReplayOrigin);
            Assert.IsFalse(p.Capabilities.NativeEyeGaze);
            Assert.That(a.Count, Is.InRange(260, 280));
            CollectionAssert.AreEqual(a.Select(s => s.CyclopeanDirection), b.Select(s => s.CyclopeanDirection));
            Assert.IsTrue(a.Zip(a.Skip(1), (x, y) => y.SensorTimestamp == x.SensorTimestamp).Any(), "expects repeated sensor timestamps");
        }

        [Test]
        public void ReplayProviderParsesCsvAndIsLabelled()
        {
            var csv = ReplayEyeProvider.Header + "\n"
                + "0.000,0.000,1,1,1,0,0,1,0,0,1,0,1.6,0,0,0,0,1,\n"
                + "0.011,0.011,1,1,1,0.1,0,0.995,0.1,0,0.995,0,1.6,0,0,0,0,1,0.5\n"
                + "0.022,0.011,0,0,0,0,0,1,0,0,1,0,1.6,0,0,0,0,1,\n";
            var p = ReplayEyeProvider.FromCsv(csv, ReplayOrigin.Synthetic);
            Assert.AreEqual(DataSource.Replay, p.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, p.ReplayOrigin);
            Assert.IsFalse(p.Capabilities.NativeEyeGaze);
            var rows = Run(p, 1);
            Assert.AreEqual(3, rows.Count);
            Assert.IsNull(rows[0].Confidence);
            Assert.AreEqual(0.5f, rows[1].Confidence);
            Assert.IsFalse(rows[2].Valid);
        }

        [Test]
        public void ReplayRejectsUnknownHeader() =>
            Assert.Throws<System.FormatException>(() => ReplayEyeProvider.Parse("time,x,y\n0,0,0"));

        [Test]
        public void HardwareNeverSilentlySubstitutesSynthetic()
        {
            Assert.Throws<System.InvalidOperationException>(() => EyeProviderFactory.CreateAndStart(
                EyeSourceMode.Hardware, 0, 1, null, ReplayOrigin.Recorded, out var note));
        }

        static (PhaseRecording task, BaselineData baseline, MetricsContext ctx) Record(double baselineSeconds, double taskSeconds, SelfReport baselineReport)
        {
            var provider = new SyntheticEyeProvider(3);
            var baseline = new BaselineData { SelfReport = baselineReport };
            baseline.Recording.StartTime = 0;
            provider.Start(0);
            provider.ReadSamples(baseline.Recording.Samples, baselineSeconds);
            baseline.Recording.EndTime = baselineSeconds;
            var task = new PhaseRecording { StartTime = baselineSeconds };
            provider.ReadSamples(task.Samples, baselineSeconds + taskSeconds);
            task.EndTime = baselineSeconds + taskSeconds;
            var ctx = new MetricsContext
            {
                SessionId = SessionContext.NewSessionId(),
                DataSource = provider.DataSource,
                Capabilities = provider.Capabilities,
                Unavailable = provider.Unavailable,
                PostTaskReport = TestFixtures.Load<SelfReport>("self_report_post_task.json"),
            };
            return (task, baseline, ctx);
        }

        [Test]
        public void StubEngineProducesValidLabelledSummary()
        {
            var (task, baseline, ctx) = Record(120, 60, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            var s = new StubMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(s.DurationMs, s.Quality.ValidDurationMs + s.Quality.GapDurationMs, "backend requires valid + gap = duration");
            Assert.AreEqual(DataSource.Synthetic, s.DataSource);
            Assert.AreEqual(ctx.SessionId, s.SessionId);
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.That(s.Quality.RepeatedSampleFraction, Is.GreaterThan(0));
            Assert.AreEqual(6, s.Baseline.Windows);
            Assert.AreEqual(60000, s.DurationMs, "session quality describes the task, not task plus baseline");
            Assert.AreEqual(s.DurationMs, s.Quality.ValidDurationMs + s.Quality.GapDurationMs);
            Assert.AreEqual(120000, s.Baseline.DurationMs);
            Assert.IsFalse(s.Baseline.LowConfidence);
            Assert.AreEqual(StrainLevel.Moderate, s.StrainIndicator.Level);
            StringAssert.Contains("stub", s.MetricVersion);
        }

        [Test]
        public void FactoryReturnsTheSignalsEngine() => Assert.IsInstanceOf<SignalsMetricsEngine>(MetricsEngineFactory.Create());

        [Test]
        public void SignalsEngineProducesValidLabelledSummary()
        {
            var (task, baseline, ctx) = Record(130, 60, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(DataSource.Synthetic, s.DataSource);
            Assert.IsFalse(s.Capabilities.NativeEyeGaze);
            Assert.AreEqual(ctx.SessionId, s.SessionId);
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.AreEqual(s.DurationMs, s.Quality.ValidDurationMs + s.Quality.GapDurationMs);
            Assert.That(s.Quality.RepeatedSampleFraction, Is.GreaterThan(0), "synthetic repeats must be counted and dropped");
            Assert.That(s.Baseline.Windows, Is.GreaterThanOrEqualTo(6));
            Assert.IsFalse(s.Baseline.LowConfidence);
            Assert.IsNotNull(s.Metrics.GazeSpeedMedianDegS);
            Assert.IsNotNull(s.Deviations.GazeSpeedP95DegS);
            Assert.IsNull(s.Metrics.ProbeLatencyMs, "no probes were shown: probe metrics stay null, never 0");
            Assert.IsTrue(s.Quality.Unavailable.Any(u => u.Signal == Signal.Probe && u.Reason == UnavailableReason.NotCollected));
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level, "indicator needs probes");
            Assert.AreEqual(s.StrainIndicator.Level == StrainLevel.Unavailable, s.StrainIndicator.Value == null);
            StringAssert.Contains("gaze-metrics", s.MetricVersion);
        }

        [Test]
        public void SignalsEngineIsDeterministic()
        {
            var a = Record(130, 30, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            var b = Record(130, 30, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            b.ctx.SessionId = a.ctx.SessionId;
            Assert.AreEqual(ContractJson.Serialize(new SignalsMetricsEngine().Compute(a.task, a.baseline, a.ctx)),
                ContractJson.Serialize(new SignalsMetricsEngine().Compute(b.task, b.baseline, b.ctx)));
        }

        [Test]
        public void SignalsEngineFlagsShortAndTiredBaselines()
        {
            var (task, baseline, ctx) = Record(20, 20, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            var s = new SignalsMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(LowConfidenceReason.TooShort, s.Baseline.LowConfidenceReason);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);

            var tired = Record(130, 20, new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 5, MentalFatigue = 2 });
            var t = new SignalsMetricsEngine().Compute(tired.task, tired.baseline, tired.ctx);
            Assert.AreEqual(LowConfidenceReason.SelfReportedTired, t.Baseline.LowConfidenceReason);
        }

        [Test]
        public void SignalsEngineWithNoSamplesIsInsufficient()
        {
            var ctx = new MetricsContext
            {
                SessionId = SessionContext.NewSessionId(), DataSource = DataSource.Synthetic,
                Capabilities = new Capabilities(), Unavailable = new List<UnavailableSignal>(),
            };
            var s = new SignalsMetricsEngine().Compute(new PhaseRecording(), new BaselineData(), ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
        }

        [TestCase("replay/steady_gaze.json")]
        [TestCase("metrics/steady_acquired.json")]
        public void ReplayLoadsSignalFixturesAndStaysSynthetic(string fixture)
        {
            var p = ReplayEyeProvider.FromFile(System.IO.Path.Combine(TestFixtures.Root, fixture), ReplayOrigin.Recorded);
            Assert.AreEqual(DataSource.Replay, p.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, p.ReplayOrigin, "a synthetic file must never be relabelled as recorded");
            Assert.IsFalse(p.Capabilities.NativeEyeGaze);
            Assert.That(p.RowCount, Is.GreaterThan(0));
        }

        [Test]
        public void ShortBaselineMakesIndicatorUnavailable()
        {
            var (task, baseline, ctx) = Record(15, 20, TestFixtures.Load<SelfReport>("self_report_baseline.json"));
            var s = new StubMetricsEngine().Compute(task, baseline, ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(LowConfidenceReason.TooShort, s.Baseline.LowConfidenceReason);
            Assert.AreEqual(StrainLevel.Unavailable, s.StrainIndicator.Level);
        }

        [Test]
        public void NoSamplesIsInsufficientData()
        {
            var ctx = new MetricsContext
            {
                SessionId = SessionContext.NewSessionId(), DataSource = DataSource.Synthetic,
                Capabilities = new Capabilities(), Unavailable = new List<UnavailableSignal>(),
            };
            var s = new StubMetricsEngine().Compute(new PhaseRecording { EndTime = 30 }, new BaselineData(), ctx);
            CollectionAssert.IsEmpty(s.Validate());
            Assert.AreEqual(QualityStatus.InsufficientData, s.Quality.Status);
            Assert.IsNull(s.StrainIndicator.Value);
            Assert.IsNull(s.Metrics.GazeSpeedMedianDegS);
        }
    }
}
