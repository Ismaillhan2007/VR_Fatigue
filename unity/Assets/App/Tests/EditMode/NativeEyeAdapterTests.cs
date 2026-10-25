using System;
using System.Collections.Generic;
using System.Numerics;
using App.Capture;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Metrics;
using NUnit.Framework;
using Sig = VRFatigue.Signals;

namespace App.Tests
{
    // All fake native hardware declares SYNTHETIC provenance. These tests do not establish hardware support.
    public class NativeEyeAdapterTests
    {
        public sealed class Source : Sig.IHardwareEyeSource
        {
            public string DataSource => "synthetic";
            public string TimestampSource => "synthetic-native-test";
            public string SdkPackageVersion => "test";
            public double Now = 1000, Stamp = 1000, HeadStamp = 1000;
            public bool Permission = true, Focus = true, Supported = true, Enabled, ReturnFrame = true;
            public bool Left = true, Right = true;
            public int Starts;
            public Sig.HardwareReadiness Readiness => new Sig.HardwareReadiness(Supported, Permission, Focus, Enabled);
            public bool StartTracking() { Starts++; Enabled = true; return true; }
            public void StopTracking() { Enabled = false; }
            public bool TryGetClock(out double time) { time = Now; return true; }
            public bool TryReadFrame(out Sig.HardwareEyeFrame frame)
            {
                frame = new Sig.HardwareEyeFrame(Stamp,
                    new Sig.EyeReading(Left, Left ? new Sig.Direction3(0, 0, 1) : (Sig.Direction3?)null, 0),
                    new Sig.EyeReading(Right, Right ? new Sig.Direction3(0, 0, 1) : (Sig.Direction3?)null, 0),
                    new Sig.HeadPose(new Sig.Direction3(1, 2, 3), 0, 0, 0, 1), HeadStamp);
                return ReturnFrame;
            }
            public void At(double elapsed) { Now = Stamp = HeadStamp = 1000 + elapsed; }
        }
        static HardwareEyeProvider Ready(Source source, Func<bool> begin = null, Action end = null)
        {
            var p = new HardwareEyeProvider(source, () => true, begin ?? (() => true), end, _ => { });
            p.RequestPermissionAfterConsent(); p.ConfirmCalibration(true); return p;
        }

        [Test]
        public void ConsentCalibrationAndPermissionGateTrackingAndRendering()
        {
            var source = new Source(); int locks = 0;
            using (var p = new HardwareEyeProvider(source, () => true, () => { locks++; return true; }, () => { }, _ => { }))
            {
                Assert.Throws<InvalidOperationException>(() => p.Start(5));
                p.RequestPermissionAfterConsent();
                Assert.Throws<InvalidOperationException>(() => p.Start(5));
                p.ConfirmCalibration(true); source.Permission = false;
                Assert.Throws<InvalidOperationException>(() => p.Start(5));
                Assert.AreEqual(0, locks); Assert.AreEqual(0, source.Starts);
                source.Permission = true; p.Start(5);
                Assert.AreEqual(1, locks); Assert.IsTrue(p.IsRunning);
                Assert.AreEqual(DataSource.Synthetic, p.DataSource); Assert.IsFalse(p.Capabilities.NativeEyeGaze);
                Assert.IsFalse(p.Capabilities.Pupil); Assert.IsFalse(p.Capabilities.Openness);
            }
        }

        [Test]
        public void RenderLockIsAcquiredOnceAndHeldBetweenBothPhases()
        {
            var source = new Source(); int begins = 0, ends = 0;
            using (var p = Ready(source, () => { begins++; return true; }, () => ends++))
            {
                p.Start(30); var baseline = new PhaseRecording(); p.BeginPhase(baseline, 30, 130);
                source.At(130); p.EndPhase(baseline);
                Assert.AreEqual(0, ends);
                source.At(135); p.ReadSamples(new List<GazeSample>(), 165);
                var task = new PhaseRecording(); p.BeginPhase(task, 165, 60);
                source.At(195); p.EndPhase(task); p.CompleteAssessment();
                Assert.AreEqual(1, begins); Assert.AreEqual(1, ends); Assert.IsTrue(p.Completed);
                Assert.AreEqual(130, baseline.SensorEndTime); Assert.AreEqual(135, task.SensorStartTime);
                Assert.AreEqual(195, task.SensorEndTime);
            }
            Assert.AreEqual(1, ends);
        }

        [Test]
        public void SourceTimestampAndAlignedHeadArePreservedDespiteDelayedReceipt()
        {
            var source = new Source();
            using (var p = Ready(source))
            {
                p.Start(25); var phase = new PhaseRecording(); p.BeginPhase(phase, 25, 2);
                source.At(0.2); source.Stamp = source.HeadStamp = 1000.15;
                var rows = new List<GazeSample>(); p.ReadSamples(rows, 99);
                Assert.AreEqual(0.15, rows[0].SensorTimestamp, 1e-6); Assert.AreEqual(99, rows[0].FrameTime);
                Assert.IsTrue(rows[0].HeadTracked); Assert.AreEqual(new Vector3(1, 2, 3), rows[0].HeadPosition);
                Assert.AreEqual(Quaternion.Identity, rows[0].HeadRotation); Assert.AreEqual(0, rows[0].Confidence);
                source.At(0.3); source.HeadStamp = 1000.25; p.ReadSamples(rows, 100);
                Assert.IsFalse(rows[1].Valid); Assert.IsFalse(rows[1].HeadTracked);
                Assert.AreEqual(0.3, rows[1].SensorTimestamp, 1e-6, "misalignment stays an invalid barrier");
            }
        }

        [Test]
        public void OneValidEyeSurvivesAndDuplicatesAndPrePhaseFramesAreNotRestamped()
        {
            var source = new Source();
            using (var p = Ready(source))
            {
                p.Start(0); source.At(1); var phase = new PhaseRecording(); p.BeginPhase(phase, 1, 2);
                var rows = new List<GazeSample>(); source.Stamp = source.HeadStamp = 1000.5;
                Assert.AreEqual(0, p.ReadSamples(rows, 1));
                source.At(1.1); source.Right = false; p.ReadSamples(rows, 1.1); p.ReadSamples(rows, 1.2);
                Assert.AreEqual(1, rows.Count); Assert.IsTrue(rows[0].Valid); Assert.IsTrue(rows[0].LeftValid);
                Assert.IsFalse(rows[0].RightValid); Assert.AreEqual(Vector3.UnitZ, rows[0].CyclopeanDirection);
                p.EndPhase(phase);
                Assert.AreEqual(3, phase.NativeFramesReceived); Assert.AreEqual(1, phase.NativeDuplicateFrames);
            }
        }

        [Test]
        public void EmptyPhaseRetainsNativeAcquisitionBoundsAndOvershootDoesNotExtendIt()
        {
            var source = new Source { ReturnFrame = false };
            using (var p = Ready(source))
            {
                p.Start(500); source.At(2); var phase = new PhaseRecording(); p.BeginPhase(phase, 900, 10);
                source.At(12.1); p.EndPhase(phase);
                Assert.AreEqual(2, phase.SensorStartTime); Assert.AreEqual(12, phase.SensorEndTime);
                Assert.IsEmpty(phase.Samples);
            }
        }

        [Test]
        public void NativeProbeClockDoesNotDependOnGazeAvailabilityOrAppFrameClock()
        {
            var source = new Source { ReturnFrame = false };
            using (var p = Ready(source))
            {
                p.Start(800); source.At(.1);
                Assert.AreEqual(.1, p.StampProbe("b000", "target_onset", Vector3.UnitZ), 1e-6);
                source.At(.65); Assert.AreEqual(.65, p.StampProbe("b000", "hold_start", Vector3.UnitZ), 1e-6);
                source.At(2.2); Assert.AreEqual(2.2, p.StampProbe("b000", "target_end", Vector3.UnitZ), 1e-6);
            }
        }

        [TestCase("permission")]
        [TestCase("focus")]
        [TestCase("tracking")]
        [TestCase("clock")]
        public void FaultsNeverCompleteAssessmentOrProduceReplacementRows(string fault)
        {
            var source = new Source(); int releases = 0;
            using (var p = Ready(source, end: () => releases++))
            {
                p.Start(0); var phase = new PhaseRecording(); p.BeginPhase(phase, 0, 130);
                if (fault == "permission") source.Permission = false;
                if (fault == "focus") source.Focus = false;
                if (fault == "tracking") source.Enabled = false;
                if (fault == "clock") source.Now = 999;
                var rows = new List<GazeSample>(); p.ReadSamples(rows, 1);
                Assert.IsFalse(p.IsRunning); Assert.IsEmpty(rows); Assert.AreEqual(1, releases);
                Assert.Throws<InvalidOperationException>(() => p.CompleteAssessment()); Assert.IsFalse(p.Completed);
            }
        }

        [Test]
        public void OwnerReleaseCanSynchronouslyDisableAndDisposeAgain()
        {
            var source = new Source(); HardwareEyeProvider p = null;
            p = new HardwareEyeProvider(source, () => true, () => true, () => { }, _ => { },
                () => { p.Abort("component_disabled"); p.Dispose(); });
            p.RequestPermissionAfterConsent(); p.ConfirmCalibration(true); p.Start(0);
            Assert.DoesNotThrow(() => p.Dispose());
            Assert.IsFalse(source.Enabled); Assert.IsNull(p.AggregateDiagnostics);
        }

        [Test]
        public void RenderingRejectedCannotStartAndExitDisposesDiagnosticsAndBuffers()
        {
            var source = new Source(); var p = Ready(source, () => false);
            Assert.Throws<InvalidOperationException>(() => p.Start(0)); Assert.IsFalse(source.Enabled);
            p.Stop(); Assert.IsNull(p.AggregateDiagnostics); Assert.IsFalse(p.ConsentGranted);
            Assert.Throws<ObjectDisposedException>(() => p.ReadSamples(new List<GazeSample>(), 1));
        }
    }
}
