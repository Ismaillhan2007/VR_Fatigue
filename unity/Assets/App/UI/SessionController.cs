using System;
using System.Collections.Generic;
using System.Threading;
using App.Capture;
using App.Core.Analysis;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.Core.Metrics;
using App.Metrics;
using App.Relaxation;
using App.Reports;
using App.UI.Panels;
using UnityEngine;

namespace App.UI
{
    public enum AnalysisMode
    {
        /// <summary>Local deterministic rules only (offline demo).</summary>
        Local,
        /// <summary>Canned StreamingAssets fixture, for UI work.</summary>
        Mock,
        /// <summary>FastAPI backend; falls back to local rules on any failure.</summary>
        Http,
    }

    /// <summary>
    /// Integrator-owned glue: drives SessionFlow, captures samples per phase, calls the metrics engine and the analysis
    /// client, and hands the sanitised scene config to the relaxation scene. Panels only call public methods here.
    /// </summary>
    public sealed class SessionController : MonoBehaviour
    {
        [Header("Eye data")]
        public EyeSourceMode EyeSource = EyeSourceMode.Synthetic;
        [Tooltip("Replay file: eye-session.v1 .json (fixtures/replay, fixtures/metrics) or the CSV in docs/ARCHITECTURE.md. Relative paths start at the unity/ folder.")]
        public string ReplayCsvPath = "../fixtures/replay/steady_gaze.json";
        public ReplayOrigin ReplayOrigin = ReplayOrigin.Synthetic;
        public int SyntheticSeed = 1;

        [Header("Analysis")]
        public AnalysisMode Analysis = AnalysisMode.Http;
        [Tooltip("Override at runtime with the command-line argument -backendUrl http://<laptop-ip>:8000")]
        public string BackendBaseUrl = HttpAnalysisClient.DefaultBaseUrl;

        [Header("Durations (desktop defaults; pilot uses 120 s / 300 s)")]
        public float BaselineSeconds = 20;
        public float TaskSeconds = 20;

        [Header("UI (wired by App/Build Assessment Scene)")]
        public List<FlowPanel> Panels = new List<FlowPanel>();
        public ProvenanceBar ProvenanceBar;
        public PauseOverlay PauseOverlay;

        public SessionFlow Flow { get; private set; }
        public string ProviderNote { get; private set; }
        public bool IsCapturing => _recording != null;
        public float PhaseRemainingSeconds => Mathf.Max(0, _phaseDuration - _phaseElapsed);
        public int SampleCount => _recording?.Samples.Count ?? 0;
        public bool AnalysisInProgress { get; private set; }
        public string SceneStatus { get; private set; } = "";

        public string EyeSourceDescription => _provider != null
            ? $"{_provider.Name} ({_provider.DataSource.ToString().ToLowerInvariant()})"
            : EyeSource == EyeSourceMode.Synthetic ? "synthetic generator (no headset)" : EyeSource.ToString();

        readonly IMetricsEngine _metrics = MetricsEngineFactory.Create();
        readonly RelaxationSceneLoader _scene = new RelaxationSceneLoader();
        readonly BaselineData _baseline = new BaselineData();
        readonly PhaseRecording _task = new PhaseRecording();
        readonly List<GazeSample> _discard = new List<GazeSample>();
        IEyeTrackingProvider _provider;
        PhaseRecording _recording;
        float _phaseDuration, _phaseElapsed;
        CancellationTokenSource _cts = new CancellationTokenSource();

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            BackendBaseUrl = CommandLineValue("-backendUrl") ?? BackendBaseUrl;
            Flow = new SessionFlow();
            Flow.StateChanged += (_, __) => ShowCurrent();
            Flow.PauseChanged += _ => ShowCurrent();
            Flow.ContextChanged += RefreshVisible;
            foreach (var p in Panels) p.Bind(this);
            ProvenanceBar.Bind(this);
            PauseOverlay.Bind(this);
            ShowCurrent();
        }

        void OnDestroy()
        {
            _cts.Cancel();
            _provider?.Stop();
        }

        void Update()
        {
            if (_recording == null || _provider == null) return;
            if (Flow.IsPaused)
            {
                // Tracking during pause is not part of the session: drain and drop it.
                _provider.ReadSamples(_discard, Now);
                _discard.Clear();
                return;
            }
            _provider.ReadSamples(_recording.Samples, Now);
            _phaseElapsed += Time.unscaledDeltaTime;
            if (_phaseElapsed >= _phaseDuration) EndCapture();
            RefreshVisible();
        }

        // ----- called by panels -----

        public void AcceptConsent()
        {
            _provider?.Stop();
            _provider = EyeProviderFactory.CreateAndStart(EyeSource, Now, SyntheticSeed, ResolveProjectPath(ReplayCsvPath), ReplayOrigin, out var note);
            ProviderNote = note;
            Flow.AcceptConsent(_provider.DataSource, _provider.ReplayOrigin, _provider.Capabilities, _provider.Name);
            _baseline.Recording.Clear();
            _baseline.SelfReport = null;
            _task.Clear();
            BeginCapture(_baseline.Recording, BaselineSeconds);
        }

        public void EndCaptureEarly()
        {
            if (_recording != null) EndCapture();
        }

        public void SubmitBaseline(int eyeStrain, int mentalFatigue)
        {
            var report = new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = eyeStrain, MentalFatigue = mentalFatigue };
            Flow.CompleteBaseline(report);
            _baseline.SelfReport = report;
            BeginCapture(_task, TaskSeconds);
        }

        public async void SubmitCheckIn(SelfReport report, Preferences preferences)
        {
            Flow.SubmitCheckIn(report, preferences);
            var sessionId = Flow.Context.SessionId;
            SessionSummary summary;
            try
            {
                summary = _metrics.Compute(_task, _baseline, ContextForMetrics(report));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionController] metrics engine failed, using stub: {e}");
                summary = new StubMetricsEngine().Compute(_task, _baseline, ContextForMetrics(report));
            }
            Flow.SetSummary(summary);

            var request = AnalyzeRequest.Create(summary, Flow.Context.BaselineReport, preferences);
            AnalysisInProgress = true;
            RefreshVisible();
            AnalysisResult result;
            try
            {
                result = await CreateClient().AnalyzeAsync(request, _cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionController] analysis client threw: {e.Message}");
                result = new AnalysisResult { Response = LocalFallbackAnalysis.Analyze(request), FallbackReason = "client error" };
            }
            AnalysisInProgress = false;
            if (this == null || Flow.State != FlowState.Summary || Flow.Context.SessionId != sessionId) return;
            Flow.SetAnalysis(result.Response, result.FallbackReason);
        }

        public void EnterScene()
        {
            if (Flow.Context.Analysis?.SafetyNotice == SafetyNotice.StopSession) return;
            Flow.EnterScene();
            var config = Flow.Context.Analysis.SceneConfig;
            SceneStatus = "Loading scene...";
            StartCoroutine(EnterSceneRoutine(config));
        }

        System.Collections.IEnumerator EnterSceneRoutine(SceneConfig config)
        {
            yield return _scene.Enter(config, Flow.Context.Preferences);
            SceneStatus = (_scene.ForestLoaded ? "Forest scene loaded." : "Forest scene not available yet (stub applier): showing the config only.")
                + "\n\n" + SummaryFormatter.SceneLine(SceneConfigHandoff.Current)
                + "\n\nYou can mute, pause or exit at any time.";
            RefreshVisible();
        }

        public void MuteScene()
        {
            var muted = SceneConfigHandoff.Current.Copy();
            muted.Sound = SoundMode.Silent;
            SceneConfigHandoff.Set(muted);
            _scene.Applier?.Apply(SceneConfigHandoff.Current);
            SceneStatus = "Muted.\n\n" + SummaryFormatter.SceneLine(SceneConfigHandoff.Current);
            RefreshVisible();
        }

        public void FinishScene()
        {
            Flow.FinishScene();
            StartCoroutine(_scene.Exit());
        }

        public void SubmitReRating(int eyeStrain, int mentalFatigue) =>
            Flow.SubmitReRating(new SelfReport { Phase = ReportPhase.PostScene, EyeStrain = eyeStrain, MentalFatigue = mentalFatigue });

        public void TogglePause()
        {
            if (Flow.IsPaused) Flow.Resume();
            else Flow.Pause();
        }

        public void Exit()
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            StopCapture();
            StartCoroutine(_scene.Exit());
            SceneConfigHandoff.Clear();
            _baseline.Recording.Clear();
            _baseline.SelfReport = null;
            _task.Clear();
            AnalysisInProgress = false;
            Flow.Exit();
        }

        public void Restart()
        {
            StopCapture();
            ProviderNote = null;
            Flow.Restart();
        }

        // ----- internals -----

        void BeginCapture(PhaseRecording recording, float seconds)
        {
            recording.Clear();
            recording.StartTime = Now;
            _provider.ReadSamples(_discard, Now); // drop anything produced before this phase
            _discard.Clear();
            _recording = recording;
            _phaseDuration = Mathf.Max(1, seconds);
            _phaseElapsed = 0;
        }

        void EndCapture()
        {
            _recording.EndTime = Now;
            _recording = null;
            if (Flow.State == FlowState.LoadTask)
            {
                _provider.Stop();
                Flow.CompleteLoadTask();
            }
            RefreshVisible();
        }

        void StopCapture()
        {
            _recording = null;
            _provider?.Stop();
            _provider = null;
        }

        MetricsContext ContextForMetrics(SelfReport postTask) => new MetricsContext
        {
            SessionId = Flow.Context.SessionId,
            DataSource = Flow.Context.DataSource.Value,
            ReplayOrigin = Flow.Context.ReplayOrigin,
            Capabilities = Flow.Context.Capabilities,
            Unavailable = _provider?.Unavailable,
            PostTaskReport = postTask,
        };

        IAnalysisClient CreateClient()
        {
            switch (Analysis)
            {
                case AnalysisMode.Mock: return new MockAnalysisClient();
                // The optional backend token comes only from the VR_BACKEND_ACCESS_TOKEN environment variable.
                case AnalysisMode.Http: return new HttpAnalysisClient(BackendBaseUrl);
                default: return new LocalAnalysisClient();
            }
        }

        void ShowCurrent()
        {
            var state = Flow.State == FlowState.Exited ? FlowState.Done : Flow.State;
            foreach (var p in Panels)
            {
                var show = p.State == state;
                var wasActive = p.gameObject.activeSelf;
                p.gameObject.SetActive(show);
                if (show && (!wasActive || state == FlowState.Done)) p.OnShow();
            }
            PauseOverlay.gameObject.SetActive(Flow.IsPaused);
            ProvenanceBar.Show(this);
        }

        void RefreshVisible()
        {
            foreach (var p in Panels) if (p.gameObject.activeSelf) p.Refresh();
            ProvenanceBar.Show(this);
        }

        static string ResolveProjectPath(string path) =>
            string.IsNullOrWhiteSpace(path) || System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path));

        static string CommandLineValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
