using System;
using App.Core.Contracts;
using App.Core.Metrics;

namespace App.Core.Flow
{
    public enum FlowState
    {
        Consent,
        Baseline,
        LoadTask,
        CheckIn,
        Summary,
        Scene,
        ReRating,
        Done,
        Exited,
        RelaxationSetup, // appended to preserve serialized values of the existing states
    }

    /// <summary>Everything one session knows. Lives in memory only and is cleared on exit/restart.</summary>
    public sealed class SessionContext
    {
        public string SessionId = NewSessionId();
        public DataSource? DataSource;
        public ReplayOrigin? ReplayOrigin;
        public Capabilities Capabilities;
        public string ProviderName;

        public SelfReport BaselineReport;
        public bool BaselineLowConfidence;
        public LowConfidenceReason? BaselineLowConfidenceReason;
        public SelfReport PostTaskReport;
        public Preferences Preferences;
        public SessionSummary Summary;
        public AnalysisResponse Analysis;
        public string AnalysisFallbackReason;
        public SelfReport PostSceneReport;
        public bool SceneParticipated;

        public AiSource? AiSource => Analysis?.AiSource;

        /// <summary>UUIDv4 from a random Guid; never derived from a person, device or account.</summary>
        public static string NewSessionId() => Guid.NewGuid().ToString("D");
    }

    /// <summary>
    /// Pure session flow: assessment -> report -> Done, or explicitly chosen relaxation -> optional feedback -> Done,
    /// with pause (blocks transitions) and exit (from anywhere; clears session data). Invalid transitions throw.
    /// Provenance set at consent cannot be relabelled later.
    /// </summary>
    public sealed class SessionFlow
    {
        public FlowState State { get; private set; } = FlowState.Consent;
        public bool IsPaused { get; private set; }
        public SessionContext Context { get; private set; } = new SessionContext();

        /// <summary>(previous, next)</summary>
        public event Action<FlowState, FlowState> StateChanged;
        public event Action<bool> PauseChanged;
        /// <summary>Raised when summary or analysis data change within a state.</summary>
        public event Action ContextChanged;

        public void AcceptConsent(DataSource source, ReplayOrigin? origin, Capabilities capabilities, string providerName)
        {
            Require(FlowState.Consent);
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            if (source == DataSource.Replay && origin == null) throw new ArgumentException("replay data needs a replay origin");
            if (source != DataSource.Replay && origin != null) throw new ArgumentException("replay origin is only for replay data");
            if (source == DataSource.Synthetic && capabilities.NativeEyeGaze)
                throw new ArgumentException("synthetic data cannot claim native eye gaze");
            Context.DataSource = source;
            Context.ReplayOrigin = origin;
            Context.Capabilities = capabilities;
            Context.ProviderName = providerName;
            MoveTo(FlowState.Baseline);
        }

        /// <summary>Finishes the baseline gaze task and stores the baseline sliders (CONCEPT 4, rule 1).</summary>
        public void CompleteBaseline(SelfReport baselineReport)
        {
            Require(FlowState.Baseline);
            RequireReport(baselineReport, ReportPhase.Baseline);
            Context.BaselineReport = baselineReport.Copy();
            Context.BaselineLowConfidence = BaselinePolicy.SelfReportedTired(baselineReport);
            Context.BaselineLowConfidenceReason = Context.BaselineLowConfidence ? LowConfidenceReason.SelfReportedTired : (LowConfidenceReason?)null;
            MoveTo(FlowState.LoadTask);
        }

        public void CompleteLoadTask()
        {
            Require(FlowState.LoadTask);
            MoveTo(FlowState.CheckIn);
        }

        public void SubmitCheckIn(SelfReport postTaskReport, Preferences preferences)
        {
            Require(FlowState.CheckIn);
            RequireReport(postTaskReport, ReportPhase.PostTask);
            if (preferences != null) throw new InvalidOperationException("choose scene preferences only after opting into relaxation");
            Context.PostTaskReport = postTaskReport.Copy();
            Context.Preferences = preferences;
            MoveTo(FlowState.Summary);
        }

        public void SetSummary(SessionSummary summary)
        {
            Require(FlowState.Summary);
            ThrowIfInvalid(summary);
            if (summary.SessionId != Context.SessionId) throw new InvalidOperationException("summary belongs to another session");
            if (summary.DataSource != Context.DataSource || summary.ReplayOrigin != Context.ReplayOrigin)
                throw new InvalidOperationException("summary provenance differs from the active provider");
            Context.Summary = summary;
            Context.BaselineLowConfidence = summary.Baseline.LowConfidence;
            Context.BaselineLowConfidenceReason = summary.Baseline.LowConfidenceReason;
            ContextChanged?.Invoke();
        }

        public void SetAnalysis(AnalysisResponse analysis, string fallbackReason)
        {
            // Completing an in-flight explanation updates context, not the flow state. Keep the
            // pause overlay active; subsequent participant transitions still require Resume.
            if (State != FlowState.Summary) throw new InvalidOperationException("analysis requires Summary");
            if (Context.Summary == null) throw new InvalidOperationException("set the summary first");
            ThrowIfInvalid(analysis);
            if (analysis.SessionId != Context.SessionId || analysis.DataSource != Context.DataSource
                || analysis.ReplayOrigin != Context.ReplayOrigin)
                throw new InvalidOperationException("analysis provenance differs from the session");
            Context.Analysis = analysis;
            Context.AnalysisFallbackReason = fallbackReason;
            ContextChanged?.Invoke();
        }

        public void FinishWithReport()
        {
            Require(FlowState.Summary);
            RequireAnalysis();
            MoveTo(FlowState.Done);
        }

        public void ChooseRelaxation()
        {
            Require(FlowState.Summary);
            RequireSceneAllowed();
            MoveTo(FlowState.RelaxationSetup);
        }

        public void CancelRelaxation()
        {
            Require(FlowState.RelaxationSetup);
            Context.Preferences = null;
            MoveTo(FlowState.Summary);
        }

        public void SetScenePreferences(Preferences preferences)
        {
            Require(FlowState.RelaxationSetup);
            if (preferences != null) ThrowIfInvalid(preferences);
            Context.Preferences = preferences == null ? null :
                ContractJson.Parse<Preferences>(ContractJson.Serialize(preferences));
            ContextChanged?.Invoke();
        }

        public void EnterScene()
        {
            Require(FlowState.RelaxationSetup);
            RequireSceneAllowed();
            MoveTo(FlowState.Scene);
        }

        void RequireAnalysis()
        {
            if (Context.Summary == null || Context.Analysis == null)
                throw new InvalidOperationException("wait for the report before completing");
        }

        void RequireSceneAllowed()
        {
            if (Context.Analysis == null) throw new InvalidOperationException("no analysis yet");
            if (Context.Analysis.SafetyNotice == SafetyNotice.StopSession)
                throw new InvalidOperationException("stop-session notice requires exiting and resting");
        }

        public void MarkSceneParticipated()
        {
            if (State != FlowState.Scene) throw new InvalidOperationException("no scene is being entered");
            Context.SceneParticipated = true;
        }

        public void FinishScene()
        {
            Require(FlowState.Scene);
            MoveTo(Context.SceneParticipated ? FlowState.ReRating : FlowState.Done);
        }

        public void SkipReRating()
        {
            Require(FlowState.ReRating);
            MoveTo(FlowState.Done);
        }

        public void SubmitReRating(SelfReport postSceneReport)
        {
            Require(FlowState.ReRating);
            RequireReport(postSceneReport, ReportPhase.PostScene);
            Context.PostSceneReport = postSceneReport.Copy();
            MoveTo(FlowState.Done);
        }

        public void Pause()
        {
            if (IsPaused || State == FlowState.Done || State == FlowState.Exited) return;
            IsPaused = true;
            PauseChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            PauseChanged?.Invoke(false);
        }

        /// <summary>Always allowed, also while paused. Drops all session data.</summary>
        public void Exit()
        {
            if (State == FlowState.Exited) return;
            Resume();
            Context = new SessionContext();
            MoveTo(FlowState.Exited);
        }

        /// <summary>Starts a fresh session with a new id.</summary>
        public void Restart()
        {
            if (State != FlowState.Done && State != FlowState.Exited)
                throw new InvalidOperationException($"cannot restart from {State}");
            Context = new SessionContext();
            MoveTo(FlowState.Consent);
        }

        void Require(FlowState expected)
        {
            if (IsPaused) throw new InvalidOperationException("session is paused");
            if (State != expected) throw new InvalidOperationException($"expected state {expected}, current {State}");
        }

        static void RequireReport(SelfReport report, ReportPhase phase)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (report.Phase != phase) throw new ArgumentException($"self-report phase must be {phase}");
            ThrowIfInvalid(report);
        }

        static void ThrowIfInvalid(IContract value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var problems = value.Validate();
            if (problems.Count > 0) throw new ContractException($"Invalid {value.GetType().Name}", problems);
        }

        void MoveTo(FlowState next)
        {
            var previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }
    }
}
