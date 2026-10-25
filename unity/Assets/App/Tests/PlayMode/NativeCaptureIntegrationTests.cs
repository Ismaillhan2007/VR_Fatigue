using System;
using System.Collections;
using App.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Sig = VRFatigue.Signals;

public class NativeCaptureIntegrationTests
{
    sealed class Source : Sig.IHardwareEyeSource
    {
        public string DataSource => "synthetic";
        public string TimestampSource => "synthetic-native-integration";
        public string SdkPackageVersion => "test";
        public bool Permission, Focus = true, Enabled;
        public int Requests, Starts;
        public double ClockOffset;
        public Sig.HardwareReadiness Readiness => new Sig.HardwareReadiness(true, Permission, Focus, Enabled);
        public bool TryGetClock(out double clock) { clock = 1000 + Time.realtimeSinceStartupAsDouble + ClockOffset; return true; }
        public bool StartTracking() { Starts++; Enabled = true; return true; }
        public void StopTracking() { Enabled = false; }
        public bool TryReadFrame(out Sig.HardwareEyeFrame frame)
        {
            TryGetClock(out var time); time -= .001;
            var eye = new Sig.EyeReading(true, new Sig.Direction3(0, 0, 1), 0);
            frame = new Sig.HardwareEyeFrame(time, eye, eye, new Sig.HeadPose(new Sig.Direction3(), 0, 0, 0, 1), time);
            return true;
        }
    }
    SessionController _controller;
    Source _source;
    int _locks, _releases;
    HardwareEyeProvider _provider;
    Func<HardwareEyeProvider> _oldFactory;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("Assessment");
        _controller = UnityEngine.Object.FindAnyObjectByType<SessionController>();
        _controller.BaselineSeconds = _controller.TaskSeconds = 2;
        _source = new Source(); _locks = _releases = 0;
        _oldFactory = EyeProviderFactory.HardwareFactory;
        EyeProviderFactory.HardwareFactory = () => _provider = new HardwareEyeProvider(_source,
            () => { _source.Requests++; return true; }, () => { _locks++; return true; }, () => _releases++, _ => { });
        _controller.SelectEyeSource(EyeSourceMode.Hardware);
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    { _controller.Exit(); EyeProviderFactory.HardwareFactory = _oldFactory; yield return null; }
    void Prepare()
    {
        _controller.RequestHardwarePermission(); _source.Permission = true;
        _controller.ConfirmHardwareCalibration(true); _controller.AcceptConsent();
    }
    static IEnumerator Until(Func<bool> condition)
    {
        var end = Time.realtimeSinceStartup + 8;
        while (!condition()) { if (Time.realtimeSinceStartup > end) Assert.Fail("timed out"); yield return null; }
    }

    [UnityTest]
    public IEnumerator AsynchronousPermissionAndCalibrationNeverStartOrSubstituteWithoutExplicitStart()
    {
        _controller.AcceptConsent();
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State); Assert.AreEqual(0, _source.Starts);
        _controller.RequestHardwarePermission();
        Assert.AreEqual(1, _source.Requests); Assert.AreEqual(0, _source.Starts);
        _controller.SendMessage("OnApplicationFocus", false);
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State, "permission prompt focus loss must not clear preparation");
        Assert.IsTrue(_provider.ConsentGranted);
        _controller.ConfirmHardwareCalibration(true); _controller.AcceptConsent();
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State); Assert.IsNull(_controller.Flow.Context.DataSource);
        _source.Permission = true; yield return null;
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State);
        _controller.AcceptConsent(); Assert.AreEqual(FlowState.Baseline, _controller.Flow.State);
        Assert.AreEqual(1, _source.Starts); Assert.AreEqual(1, _locks);
    }

    [UnityTest]
    public IEnumerator NativeAdapterCompletesLocallyWithHonestSyntheticTestProvenanceAndNoForest()
    {
        _controller.Analysis = AnalysisMode.Http;
        Prepare();
        _controller.EndCaptureEarly(); Assert.IsTrue(_controller.IsCapturing, "no desktop skip for native acceptance");
        yield return Until(() => !_controller.IsCapturing);
        Assert.AreEqual(0, _releases, "keep the rendering lock between phases");
        _controller.SubmitBaseline(2, 2);
        yield return Until(() => _controller.Flow.State == FlowState.CheckIn);
        Assert.AreEqual(1, _locks); Assert.AreEqual(1, _releases);
        Assert.IsTrue(_provider.Completed); Assert.IsFalse(_source.Enabled);
        Assert.AreEqual(AnalysisMode.Local, _controller.Analysis);
        _controller.Analysis = AnalysisMode.Http; // Changing an Inspector field cannot authorize real-eye transfer.
        _controller.BackendBaseUrl = "http://127.0.0.1:9";
        _controller.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 2, MentalFatigue = 2 }, null);
        yield return Until(() => _controller.Flow.Context.Analysis != null);
        Assert.AreEqual("local mode", _controller.Flow.Context.AnalysisFallbackReason);
        var summary = _controller.Flow.Context.Summary;
        Assert.AreEqual(DataSource.Synthetic, summary.DataSource); Assert.IsFalse(summary.Capabilities.NativeEyeGaze);
        Assert.AreEqual(QualityStatus.Usable, summary.Quality.Status);
        Assert.AreEqual(StrainLevel.Unavailable, summary.StrainIndicator.Level, "short baseline cannot invent an indicator");
        _controller.FinishWithReport();
        Assert.AreEqual(FlowState.Done, _controller.Flow.State);
        Assert.IsFalse(SceneManager.GetSceneByName("ForestDemo").isLoaded);
        Assert.NotNull(_controller.HardwareDiagnostics);
        _controller.Exit(); Assert.IsNull(_controller.HardwareDiagnostics);
        Assert.IsNull(_controller.Flow.Context.Summary); Assert.IsFalse(_provider.ConsentGranted);
    }

    [UnityTest]
    public IEnumerator PermissionLossBetweenPhasesClearsReferenceAndCannotProduceReport()
    {
        Prepare(); yield return Until(() => !_controller.IsCapturing);
        Assert.AreEqual(FlowState.Baseline, _controller.Flow.State);
        _source.Permission = false;
        yield return Until(() => _controller.Flow.State == FlowState.Exited);
        Assert.IsNull(_controller.Flow.Context.Summary); Assert.IsNull(_controller.Flow.Context.BaselineReport);
        Assert.IsNull(_controller.HardwareDiagnostics); Assert.AreEqual(1, _releases);
        _controller.Restart(); Assert.IsFalse(_controller.HardwareCalibrationConfirmed);
    }

    [UnityTest]
    public IEnumerator PauseRequiresFreshConsentAndBaselineInsteadOfResumingNativeComparison()
    {
        Prepare(); yield return null;
        _controller.TogglePause();
        Assert.AreEqual(FlowState.Exited, _controller.Flow.State);
        Assert.IsNull(_controller.Flow.Context.DataSource); Assert.IsFalse(_source.Enabled);
        Assert.IsNull(_controller.HardwareDiagnostics); Assert.AreEqual(1, _releases);
    }

    [UnityTest]
    public IEnumerator SeparateDiagnosticCannotBecomeAnAssessmentBaseline()
    {
        _controller.RequestHardwarePermission(); _source.Permission = true;
        _controller.ConfirmHardwareCalibration(true); _controller.StartNativeDiagnostic();
        Assert.IsTrue(_controller.NativeDiagnostic);
        _source.ClockOffset = 61; // Explicit synthetic time jump, not 60 seconds of physical device evidence.
        yield return Until(() => _controller.NativeDiagnosticComplete);
        Assert.IsTrue(_provider.Completed); Assert.AreEqual(1, _releases);
        _controller.SubmitBaseline(2, 2);
        Assert.AreEqual(FlowState.Baseline, _controller.Flow.State);
        Assert.IsNull(_controller.Flow.Context.Summary); Assert.IsNull(_controller.Flow.Context.BaselineReport);
        Assert.NotNull(_controller.HardwareDiagnostics);
        StringAssert.Contains("unique samples:", _controller.NativeDiagnosticSummary);
        StringAssert.Contains("Calibration: operator confirmation only", _controller.NativeDiagnosticSummary);
        _controller.Exit(); _controller.Restart();
        Assert.IsFalse(_controller.NativeDiagnostic); Assert.IsNull(_controller.HardwareDiagnostics);
    }

    [UnityTest]
    public IEnumerator SourceChangeDuringPermissionPromptCannotReviveNativeCapture()
    {
        _controller.RequestHardwarePermission(); _controller.ConfirmHardwareCalibration(true);
        var previous = _provider;
        _controller.SelectEyeSource(EyeSourceMode.Replay);
        _source.Permission = true; yield return null;
        Assert.IsFalse(previous.ConsentGranted); Assert.IsNull(_controller.HardwareDiagnostics);
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State); Assert.AreEqual(0, _source.Starts);
        Assert.IsFalse(_controller.HardwareCalibrationConfirmed);
    }
}
