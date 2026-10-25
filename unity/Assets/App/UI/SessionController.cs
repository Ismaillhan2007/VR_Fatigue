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
        [Header("Eye data (desktop demo default: synthetic 130 s replay with probes)")]
        public EyeSourceMode EyeSource = EyeSourceMode.Replay;
        [Tooltip("Replay file: eye-session.v1 .json (fixtures/replay, fixtures/metrics) or the CSV in docs/ARCHITECTURE.md. Relative paths start at the unity/ folder; a missing file falls back to StreamingAssets/fixtures/replay.")]
        public string ReplayCsvPath = "../fixtures/replay/demo_session.json";
        [Tooltip("Replay seconds per real second. Metrics use sensor timestamps, so speed does not change them.")]
        public float ReplaySpeed = 4;
        public ReplayOrigin ReplayOrigin = ReplayOrigin.Synthetic;
        public int SyntheticSeed = 1;

        [Header("Analysis")]
        public AnalysisMode Analysis = AnalysisMode.Http;
        [Tooltip("Override at runtime with the command-line argument -backendUrl http://<laptop-ip>:8000")]
        public string BackendBaseUrl = HttpAnalysisClient.DefaultBaseUrl;

        [Header("Durations in replay/eye-data seconds (demo_session.json: 130 s baseline + 60 s task)")]
        public float BaselineSeconds = 130;
        public float TaskSeconds = 60;

        [Header("UI (wired by App/Build Assessment Scene)")]
        public List<FlowPanel> Panels = new List<FlowPanel>();
        public ProvenanceBar ProvenanceBar;
        public PauseOverlay PauseOverlay;
        public ProbeTaskRunner ProbeRunner;

        public SessionFlow Flow { get; private set; }
        public string ProviderNote { get; private set; }
        public string MetricsFailureMessage { get; private set; }
        public bool IsCapturing => _recording != null;
        /// <summary>Remaining phase time in real seconds (replay may run faster than real time).</summary>
        public float PhaseRemainingSeconds => Mathf.Max(0, (_phaseDuration - _phaseElapsed) / Mathf.Max(0.01f, PhaseSpeed));
        public int ProbeCount => _recording?.Probes.Count ?? 0;

        /// <summary>Per-trial probe outcomes of the last summary (diagnostics, not part of the contract).</summary>
        public string ProbeStatsText => (_metrics as SignalsMetricsEngine)?.LastProbeStats is ProbeTrialStats s
            ? $"Probe trials (2 deg within 500 ms): baseline {s.Baseline}; task {s.Task}"
            : null;

        float PhaseSpeed => _provider is ReplayEyeProvider r ? (float)r.Speed : 1f;
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
        string _probePrefix;
        int _lastTrial = -1;
        double _phaseStartReplay;
        CancellationTokenSource _cts = new CancellationTokenSource();

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            BackendBaseUrl = CommandLineValue("-backendUrl") ?? BackendBaseUrl;
            Flow = new SessionFlow();
            Flow.StateChanged += (_, __) => ShowCurrent();
            Flow.PauseChanged += paused =>
            {
                if (_recording != null && _provider is IHoldableProvider h) h.SetHold(paused, Now);
                ShowCurrent();
            };
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
                ProbeRunner?.Hide();
                return;
            }
            _provider.ReadSamples(_recording.Samples, Now);
            // A replay's own clock is the phase clock (frame deltas can include scene-load time).
            if (_provider is ReplayEyeProvider replay) _phaseElapsed = (float)(replay.ReplayedSeconds(Now) - _phaseStartReplay);
            else _phaseElapsed += Time.unscaledDeltaTime;
            UpdateProbes();
            if (_phaseElapsed >= _phaseDuration) EndCapture();
            RefreshVisible();
        }

        // ----- called by panels -----

        public void AcceptConsent()
        {
            MetricsFailureMessage = null;
            _provider?.Stop();
            _provider = EyeProviderFactory.CreateAndStart(EyeSource, Now, SyntheticSeed, ResolveReplayPath(ReplayCsvPath), ReplayOrigin, out var note, ReplaySpeed);
            if (_provider is ReplayEyeProvider replay)
            {
                // File parsing happens before capture. Start the replay clock only when the phase is ready.
                var ready = Now;
                replay.Start(ready);
                replay.SetHold(true, ready);
            }
            ProviderNote = note;
            Flow.AcceptConsent(_provider.DataSource, _provider.ReplayOrigin, _provider.Capabilities, _provider.Name);
            _baseline.Recording.Clear();
            _baseline.SelfReport = null;
            _task.Clear();
            BeginCapture(_baseline.Recording, BaselineSeconds, "b", loaded: false);
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
            BeginCapture(_task, TaskSeconds, "l", loaded: true);
        }

        public async void SubmitCheckIn(SelfReport report, Preferences preferences)
        {
            Flow.SubmitCheckIn(report, preferences);
            var sessionId = Flow.Context.SessionId;
            var succeeded = MetricsEngineFactory.TryCompute(_metrics, _task, _baseline, ContextForMetrics(report), out var summary);
            MetricsFailureMessage = succeeded ? null : "Measurements could not be calculated. Please repeat the session or continue using your check-in.";
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
            if (this == null || Flow.State != FlowState.Summary || Flow.Context.SessionId != sessionId) return;
            AnalysisInProgress = false;
            Flow.SetAnalysis(result.Response, result.FallbackReason);
        }

        public void FinishWithReport() => Flow.FinishWithReport();
        public void ChooseRelaxation() => Flow.ChooseRelaxation();
        public void CancelRelaxation() => Flow.CancelRelaxation();
        public void SkipReRating() => Flow.SkipReRating();

        public SceneConfig ScenePreview => Flow.Context.Analysis == null ? null
            : SceneConfigSanitizer.Sanitize(Flow.Context.Analysis.SceneConfig, Flow.Context.Preferences);

        public void EnterScene()
        {
            if (Flow.Context.Analysis?.SafetyNotice == SafetyNotice.StopSession) return;
            Flow.EnterScene();
            var config = Flow.Context.Analysis.SceneConfig;
            SceneStatus = "Loading scene...";
            StartCoroutine(EnterSceneRoutine(config, Flow.Context.SessionId));
        }

        System.Collections.IEnumerator EnterSceneRoutine(SceneConfig config, string sessionId)
        {
            yield return _scene.Enter(config, Flow.Context.Preferences);
            if (Flow.Context.SessionId != sessionId || Flow.State != FlowState.Scene)
            {
                yield return _scene.Exit();
                SceneConfigHandoff.Clear();
                yield break;
            }
            if (_scene.ForestLoaded) Flow.MarkSceneParticipated();
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
            MetricsFailureMessage = null;
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            StopCapture();
            StartCoroutine(_scene.Exit());
            SceneConfigHandoff.Clear();
            _baseline.Recording.Clear();
            _baseline.SelfReport = null;
            _task.Clear();
            AnalysisInProgress = false;
            SceneStatus = "";
            Flow.Exit();
        }

        public void Restart()
        {
            if (Flow.State != FlowState.Done && Flow.State != FlowState.Exited)
                throw new InvalidOperationException("finish or exit before restarting");
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            MetricsFailureMessage = null;
            StopCapture();
            _baseline.Recording.Clear();
            _baseline.SelfReport = null;
            _task.Clear();
            SceneConfigHandoff.Clear();
            SceneStatus = "";
            AnalysisInProgress = false;
            ProviderNote = null;
            Flow.Restart();
        }

        // ----- internals -----

        void BeginCapture(PhaseRecording recording, float seconds, string probePrefix, bool loaded)
        {
            var now = Now;
            recording.Clear();
            recording.StartTime = now;
            if (_provider is IHoldableProvider holdable) holdable.SetHold(false, now);
            _provider.ReadSamples(_discard, now); // drop anything produced before this phase
            _discard.Clear();
            if (_provider is IProbeFollower follower) follower.SetLoaded(loaded);
            _recording = recording;
            _probePrefix = probePrefix;
            _lastTrial = -1;
            _phaseDuration = Mathf.Max(1, seconds);
            _phaseElapsed = 0;
            _phaseStartReplay = _provider is ReplayEyeProvider replay ? replay.ReplayedSeconds(now) : 0;
            if (_provider is ReplayEyeProvider timed)
            {
                timed.BeginPhase(_phaseDuration, now);
                recording.SensorClockRate = Math.Max(0.01, timed.Speed);
                recording.SensorStartTime = timed.SensorTime(now);
            }
        }

        /// <summary>
        /// Same head-locked probe layout in both phases. A replay brings its own trials; otherwise the app runs
        /// ProbeSchedule and tells a synthetic provider where the target is.
        /// </summary>
        void UpdateProbes()
        {
            if (_provider is IProbeSource source) source.ReadProbes(_recording.Probes, Now);
            else
            {
                var index = ProbeSchedule.TrialIndex(_phaseElapsed);
                var fits = (index + 1) * ProbeSchedule.TrialSeconds <= _phaseDuration;
                if (index != _lastTrial && fits)
                {
                    _lastTrial = index;
                    var trial = ProbeSchedule.Trial(_probePrefix, index, Now);
                    _recording.Probes.Add(trial);
                    (_provider as IProbeFollower)?.ShowTarget(trial.TargetDirection, Now);
                }
            }
            var probes = _recording.Probes;
            var active = probes.Count > 0 && probes[probes.Count - 1].EndTime > Now ? probes[probes.Count - 1] : (ProbeEvent?)null;
            if (active.HasValue) ProbeRunner?.Show(active.Value.TargetDirection);
            else ProbeRunner?.Hide();
        }

        void EndCapture()
        {
            var now = Now;
            _recording.EndTime = now;
            if (_provider is ReplayEyeProvider replay) _recording.SensorEndTime = replay.SensorTime(now);
            _recording = null;
            ProbeRunner?.Hide();
            if (_provider is IHoldableProvider holdable) holdable.SetHold(true, now);
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
            ProbeRunner?.Hide();
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

        /// <summary>Relative to the unity/ folder; falls back to the StreamingAssets copy (builds have no repo).</summary>
        static string ResolveReplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var full = System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path));
            if (System.IO.File.Exists(full)) return full;
            var packaged = System.IO.Path.Combine(Application.streamingAssetsPath, "fixtures", "replay", System.IO.Path.GetFileName(path));
            return System.IO.File.Exists(packaged) ? packaged : full;
        }

        static string CommandLineValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
