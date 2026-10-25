using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace VRFatigue.Signals.Tests
{
    public sealed class HardwareProviderTests
    {
        private sealed class Source : IHardwareEyeSource
        {
            public string DataSource { get { return "synthetic"; } }
            public string TimestampSource { get { return "synthetic_runtime_clock"; } }
            public string SdkPackageVersion { get { return "mock"; } }
            public bool Supported = true, Permission = true, Focus = true, Enabled, ClockAvailable = true, StartResult = true, ReturnFrame = true, ThrowRead;
            public double Now = 1000;
            public int Starts, Stops;
            public HardwareEyeFrame Frame = MakeFrame(1000);
            public Action OnRead;
            public HardwareReadiness Readiness { get { return new HardwareReadiness(Supported, Permission, Focus, Enabled); } }
            public bool TryGetClock(out double seconds) { seconds = Now; return ClockAvailable; }
            public bool StartTracking() { Starts++; Enabled = StartResult; return StartResult; }
            public void StopTracking() { Stops++; Enabled = false; }
            public bool TryReadFrame(out HardwareEyeFrame frame)
            {
                if (ThrowRead) throw new InvalidOperationException("synthetic_source_failure");
                if (OnRead != null) OnRead();
                frame = Frame; return ReturnFrame;
            }
        }
        private static Direction3 Direction(double degrees = 0)
        { return new Direction3(Math.Sin(degrees * Math.PI / 180), 0, Math.Cos(degrees * Math.PI / 180)); }
        private static HardwareEyeFrame MakeFrame(double time, bool valid = true, double angle = 0, double? headTime = null, HeadPose head = null)
        {
            var eye = new EyeReading(valid, valid ? (Direction3?)Direction(angle) : null, 0);
            return new HardwareEyeFrame(time, eye, eye, head ?? new HeadPose(new Direction3(), 0, 0, 0, 1), headTime ?? time);
        }
        private static EyeTrackingBatch Poll(Source source, HardwareEyeProvider provider, double elapsed, bool valid = true)
        {
            source.Now = 1000 + elapsed; source.Frame = MakeFrame(source.Now, valid);
            return provider.ReadAvailable((long)Math.Round(elapsed * 1000000));
        }
        private static void WriteReport(string name, HardwareEyeProvider provider)
        {
            string path = Environment.GetEnvironmentVariable("VR_FATIGUE_REPORTS_DIR");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(Path.Combine(path, name + ".hardware.json"), provider.DiagnosticsJson());
        }

        [Test] public void ConsentCalibrationPermissionSupportAndFocusAreRequiredBeforeCapture()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source);
            Assert.That(p.Start(false, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("consent_required"));
            Assert.That(p.Start(true, false), Is.False); Assert.That(p.StatusReason, Is.EqualTo("calibration_confirmation_required"));
            source.Permission = false;
            Assert.That(p.Start(true, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("permission_required"));
            source.Permission = true; source.Supported = false;
            Assert.That(p.Start(true, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("hardware_unsupported"));
            source.Supported = true; source.Focus = false;
            Assert.That(p.Start(true, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("focus_required"));
            Assert.That(source.Starts, Is.Zero);
            Assert.That(p.Finish().Samples.Count, Is.Zero);
        }
        [Test] public void StartFailuresDoNotCreateAUsableSession()
        {
            var source = new Source { StartResult = false }; var p = new HardwareEyeProvider(source);
            Assert.That(p.Start(true, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("tracking_start_failed"));
            source.StartResult = true; source.ClockAvailable = false;
            Assert.That(p.Start(true, true), Is.False); Assert.That(p.StatusReason, Is.EqualTo("runtime_clock_unavailable"));
            Assert.That(p.Info.Capabilities.Gaze.Available, Is.False);
        }
        [Test] public void CapturedTimestampsComeFromTheSourceClockNotTheCaller()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            source.Now = 1000.2; source.Frame = MakeFrame(1000.15);
            var batch = p.ReadAvailable(0);
            Assert.That(batch.Samples.Single().TimestampUs, Is.EqualTo(150000));
            Assert.That(p.ElapsedUs, Is.EqualTo(200000));
            Assert.That(p.Info.DataSource, Is.EqualTo("synthetic"));
        }
        [Test] public void ClockIsReadAfterFrameAcquisitionBeforeCallingItFutureData()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            source.Frame = MakeFrame(1000.01); source.OnRead = () => source.Now = 1000.02;
            Assert.That(p.ReadAvailable(0).Samples.Single().TimestampUs, Is.EqualTo(10000));
        }
        [Test] public void RepeatedAndOutOfOrderFramesDoNotInflateSamples()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            Poll(source, p, 0.01);
            Assert.That(p.ReadAvailable(10000).Samples, Is.Empty);
            source.Now = 1000.02; source.Frame = MakeFrame(1000.005);
            Assert.That(p.ReadAvailable(20000).Samples, Is.Empty);
            var report = JObject.Parse(p.DiagnosticsJson());
            Assert.That(report["counts"]["duplicate_timestamps"].Value<int>(), Is.EqualTo(1));
            Assert.That(report["counts"]["out_of_order_timestamps"].Value<int>(), Is.EqualTo(1));
            Assert.That(report["counts"]["accepted_samples"].Value<int>(), Is.EqualTo(1));
        }
        [Test] public void SubMicrosecondCollisionsAreDroppedInsteadOfRestamped()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true); Poll(source, p, 0);
            source.Now += 0.00001; source.Frame = MakeFrame(1000.0000001);
            Assert.That(p.ReadAvailable(1).Samples, Is.Empty);
            Assert.That(JObject.Parse(p.DiagnosticsJson())["counts"]["quantized_collisions"].Value<int>(), Is.EqualTo(1));
        }
        [Test] public void PreStartFutureAndNonfiniteFramesAreRejected()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            foreach (double stamp in new[] { 999.99, 1000.1, double.NaN })
            { source.Frame = MakeFrame(stamp); Assert.That(p.ReadAvailable(0).Samples, Is.Empty); }
            var counts = JObject.Parse(p.DiagnosticsJson())["counts"];
            Assert.That(counts["pre_start_frames"].Value<int>(), Is.EqualTo(1));
            Assert.That(counts["future_frames"].Value<int>(), Is.EqualTo(1));
            Assert.That(counts["invalid_timestamps"].Value<int>(), Is.EqualTo(1));
        }
        [Test] public void QueryFailureLeavesAGapWithoutInventingASample()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            Poll(source, p, 0); Poll(source, p, 0.01);
            source.ReturnFrame = false; source.Now = 1000.3;
            Assert.That(p.ReadAvailable(300000).Samples, Is.Empty);
            source.ReturnFrame = true; Poll(source, p, 0.5); Poll(source, p, 0.51);
            source.Now = 1000.6; var capture = p.Finish();
            Assert.That(capture.Samples.Count, Is.EqualTo(4));
            var quality = QualityEvaluator.Evaluate(capture.Info, capture.Samples);
            Assert.That(quality.ValidDurationUs, Is.EqualTo(20000));
            Assert.That(quality.LongestGapUs, Is.EqualTo(490000));
        }
        [Test] public void InvalidGazeIsARealTimestampedBarrierAndConfidenceDoesNotFilter()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            Assert.That(Poll(source, p, 0).Samples.Single().HasUsableGaze, Is.True);
            var bad = Poll(source, p, 0.01, false).Samples.Single();
            Assert.That(bad.TimestampUs, Is.EqualTo(10000)); Assert.That(bad.HasUsableGaze, Is.False);
            Assert.That(Poll(source, p, 0.02).Samples.Single().HasUsableGaze, Is.True);
        }
        [Test] public void MissingOrMisalignedHeadPoseCannotMasqueradeAsHeadLocalGaze()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            source.Now = 1000.01; source.Frame = MakeFrame(source.Now, headTime: 1000.02);
            var sample = p.ReadAvailable(10000).Samples.Single();
            Assert.That(sample.HasUsableGaze, Is.False); Assert.That(sample.Head, Is.Null);
            source.Now = 1000.02; source.Frame = new HardwareEyeFrame(source.Now, new EyeReading(true, Direction()), new EyeReading(true, Direction()), null, null);
            Assert.That(p.ReadAvailable(20000).Samples.Single().HasUsableGaze, Is.False);
        }
        [Test] public void RuntimeClockResetFaultsInsteadOfRebasingTheSession()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            Poll(source, p, 0); Poll(source, p, 0.02); source.Now = 999;
            Assert.That(p.ReadAvailable(20000).Samples, Is.Empty);
            Assert.That(p.StatusReason, Is.EqualTo("runtime_clock_reset")); Assert.That(p.State, Is.EqualTo("faulted"));
            Assert.That(p.Finish().Info.DurationUs, Is.EqualTo(20000));
            WriteReport("clock_reset", p);
        }
        [Test] public void PermissionAndFocusLossStopWithoutSilentReplayFallback()
        {
            foreach (string kind in new[] { "permission", "focus", "tracking" })
            {
                var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true); Poll(source, p, 0);
                if (kind == "permission") source.Permission = false;
                if (kind == "focus") source.Focus = false;
                if (kind == "tracking") source.Enabled = false;
                source.Now = 1000.1; Assert.That(p.ReadAvailable(100000).Samples, Is.Empty);
                Assert.That(p.State, Is.EqualTo("faulted")); Assert.That(p.Info.DataSource, Is.EqualTo("synthetic"));
                Assert.That(p.Finish().Samples.Count, Is.EqualTo(1));
            }
        }
        [Test] public void PauseOrRecenterRequiresAnExplicitFreshSession()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true); string old = p.Info.SessionId;
            Poll(source, p, 0.01); p.Abort("recentered");
            Assert.That(p.RecordProbe("a", "target_onset", Direction()), Is.False);
            Assert.That(p.Start(true, true), Is.True); Assert.That(p.Info.SessionId, Is.Not.EqualTo(old));
            Assert.That(p.ElapsedUs, Is.Zero); source.Now += 0.1;
            Assert.That(p.Finish().Samples, Is.Empty);
        }
        [Test] public void SampleAndProbeBuffersAreBounded()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source, new HardwareCaptureOptions(maximumSamples: 2)); p.Start(true, true);
            Poll(source, p, 0); Poll(source, p, 0.01); Poll(source, p, 0.02);
            Assert.That(p.StatusReason, Is.EqualTo("sample_buffer_limit")); Assert.That(p.Finish().Samples.Count, Is.EqualTo(2));
            p = new HardwareEyeProvider(source, new HardwareCaptureOptions(maximumProbeEvents: 3)); p.Start(true, true);
            foreach (string kind in new[] { "target_onset", "hold_start", "target_end" })
            { source.Now += 0.1; Assert.That(p.RecordProbe("a", kind, Direction()), Is.True); }
            source.Now += 0.1; Assert.That(p.RecordProbe("b", "target_onset", Direction()), Is.False);
            Assert.That(p.StatusReason, Is.EqualTo("probe_buffer_limit")); Assert.That(p.Finish().ProbeEvents.Count, Is.EqualTo(3));
        }
        [Test] public void DurationLimitNeverClampsALateFrameIntoASyntheticBoundarySample()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source, new HardwareCaptureOptions(maximumDurationUs: 1000000)); p.Start(true, true);
            Poll(source, p, 0);
            source.Now = 1000.99; source.Frame = MakeFrame(1001.01); source.OnRead = () => source.Now = 1001.1;
            Assert.That(p.ReadAvailable(990000).Samples, Is.Empty);
            Assert.That(p.State, Is.EqualTo("stopped")); Assert.That(p.Finish().Info.DurationUs, Is.EqualTo(1000000));
            Assert.That(p.Finish().Samples.Count, Is.EqualTo(1));
        }
        [Test] public void ProbeEventsUseTheSameRuntimeClockAndEmitOnlyOnce()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true);
            source.Now = 1000.123;
            Assert.That(p.RecordProbe("a", "target_onset", Direction()), Is.True);
            Assert.That(p.ReadAvailable(0).ProbeEvents.Single().TimestampUs, Is.EqualTo(123000));
            Assert.That(p.ReadAvailable(0).ProbeEvents, Is.Empty);
            source.Now = 1000.5; Assert.That(p.Finish().ProbeEvents.Count, Is.EqualTo(1));
        }
        [Test] public void ResetDisposeAndConsentWithdrawalClearRetainedCaptureData()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source); p.Start(true, true); Poll(source, p, 0);
            string old = p.Info.SessionId; p.Reset(); Assert.That(p.Info.SessionId, Is.Not.EqualTo(old));
            Assert.That(JObject.Parse(p.DiagnosticsJson())["counts"]["accepted_samples"].Value<int>(), Is.Zero);
            p.Start(true, true); Poll(source, p, 0.01); p.WithdrawConsent();
            Assert.That(p.StatusReason, Is.EqualTo("consent_withdrawn")); Assert.That(p.Finish().Samples, Is.Empty);
            p.Dispose(); Assert.Throws<ObjectDisposedException>(() => p.Start(true, true));
            Assert.Throws<ObjectDisposedException>(() => p.ReadAvailable(0));
        }
        [Test] public void SourceExceptionsBecomeAnExplicitFaultAndDoNotLeakExceptionText()
        {
            var source = new Source { ThrowRead = true }; var p = new HardwareEyeProvider(source); p.Start(true, true);
            Assert.That(p.ReadAvailable(0).Samples, Is.Empty); Assert.That(p.StatusReason, Is.EqualTo("source_error"));
            Assert.That(p.SourceErrorType, Is.EqualTo("InvalidOperationException"));
            Assert.That(p.DiagnosticsJson().Contains("synthetic_source_failure"), Is.False);
        }
        [Test] public void CallerTimeErrorsAndInvalidOptionsAreRejected()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source);
            Assert.Throws<InvalidOperationException>(() => p.Finish()); p.Start(true, true);
            p.ReadAvailable(100); Assert.Throws<ArgumentOutOfRangeException>(() => p.ReadAvailable(99));
            Assert.Throws<InvalidOperationException>(() => p.Start(true, true));
            Assert.Throws<ArgumentException>(() => new HardwareCaptureOptions(maximumSamples: 1));
            Assert.Throws<ArgumentException>(() => new HardwareCaptureOptions(maximumDurationUs: 600000001));
        }
        [Test] public void SyntheticHardwareBoundaryFeedsTheUnchangedBaselineAndIndicatorPipeline()
        {
            var source = new Source(); var p = new HardwareEyeProvider(source, new HardwareCaptureOptions(maximumDurationUs: 201000000)); p.Start(true, true);
            for (long t = 0; t < 200000000; t += 100000)
            {
                source.Now = 1000 + t / 1000000.0; long index = t / 10000000, within = t % 10000000;
                double target = index % 2 == 0 ? 0 : 12;
                if (within == 0 || within == 1000000 || within == 2000000)
                    Assert.That(p.RecordProbe("probe-" + index, within == 0 ? "target_onset" : within == 1000000 ? "hold_start" : "target_end", Direction(target)), Is.True);
                bool baseline = t < 120000000;
                double error = within < (baseline ? 100000 : 200000) ? 6 : baseline ? 1 : 2;
                source.Frame = MakeFrame(source.Now, angle: target + error); p.ReadAvailable(t);
            }
            source.Now = 1200; var session = p.Finish();
            Assert.That(session.Samples.Count, Is.EqualTo(2000)); Assert.That(session.Info.DataSource, Is.EqualTo("synthetic"));
            var plan = new BaselinePlan(session.Info.SessionId, new AssessmentPhase(0, 120000000, "synthetic-probes"),
                new AssessmentPhase(120000000, 200000000, "synthetic-probes"), new BaselineSelfReport(2, 2));
            var indicator = IndicatorEvaluator.Evaluate(BaselineEvaluator.Evaluate(session, plan));
            Assert.That(indicator.Windows[2].State.NudgeEvent, Is.EqualTo("offer_break"));
            var diagnostic = JObject.Parse(p.DiagnosticsJson());
            Assert.That(diagnostic["recording_period_complete"].Value<bool>(), Is.False);
            Assert.That(diagnostic["headset_review_required"].Value<bool>(), Is.True);
            Assert.That(diagnostic["median_dt_ms"].Value<double>(), Is.EqualTo(100));
            WriteReport("capture_pipeline", p);
        }
    }
}
