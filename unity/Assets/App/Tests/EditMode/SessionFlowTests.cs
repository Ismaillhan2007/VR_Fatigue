using System;
using System.Collections.Generic;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using NUnit.Framework;

namespace App.Tests
{
    public class SessionFlowTests
    {
        static readonly Capabilities Synthetic = new Capabilities { GazePose = true };

        static SelfReport Report(ReportPhase phase, int eye = 2, int mind = 2, params Chip[] chips) =>
            new SelfReport { Phase = phase, EyeStrain = eye, MentalFatigue = mind, Chips = new List<Chip>(chips) };

        static SessionSummary SummaryFor(SessionFlow f)
        {
            var s = TestFixtures.Load<SessionSummary>("session_synthetic_usable.json");
            s.SessionId = f.Context.SessionId;
            return s;
        }

        static SessionFlow ToSummary(SelfReport baseline = null)
        {
            var f = new SessionFlow();
            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "synthetic");
            f.CompleteBaseline(baseline ?? Report(ReportPhase.Baseline));
            f.CompleteLoadTask();
            f.SubmitCheckIn(Report(ReportPhase.PostTask, 5, 5, Chip.TiredEyes), null);
            return f;
        }

        [Test]
        public void HappyPathVisitsEveryStateInOrder()
        {
            var f = new SessionFlow();
            var visited = new List<FlowState> { f.State };
            f.StateChanged += (_, next) => visited.Add(next);

            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "synthetic");
            f.CompleteBaseline(Report(ReportPhase.Baseline));
            f.CompleteLoadTask();
            f.SubmitCheckIn(Report(ReportPhase.PostTask, 5, 5, Chip.TiredEyes), null);
            var summary = SummaryFor(f);
            f.SetSummary(summary);
            var request = AnalyzeRequest.Create(summary, f.Context.BaselineReport, null);
            f.SetAnalysis(LocalFallbackAnalysis.Analyze(request), "test");
            f.ChooseRelaxation();
            f.EnterScene();
            f.MarkSceneParticipated();
            f.FinishScene();
            f.SubmitReRating(Report(ReportPhase.PostScene));

            CollectionAssert.AreEqual(new[]
            {
                FlowState.Consent, FlowState.Baseline, FlowState.LoadTask, FlowState.CheckIn, FlowState.Summary,
                FlowState.RelaxationSetup, FlowState.Scene, FlowState.ReRating, FlowState.Done,
            }, visited);
            Assert.AreEqual(DataSource.Synthetic, f.Context.DataSource);
            Assert.AreEqual(AiSource.Fallback, f.Context.AiSource);
            StringAssert.Contains("SYNTHETIC", ProvenanceLabels.Data(f.Context));
            StringAssert.Contains("LOCAL FALLBACK", ProvenanceLabels.Ai(f.Context));
        }

        [Test]
        public void SkippingAStateThrows()
        {
            var f = new SessionFlow();
            Assert.Throws<InvalidOperationException>(() => f.CompleteLoadTask());
            Assert.Throws<InvalidOperationException>(() => f.EnterScene());
            Assert.AreEqual(FlowState.Consent, f.State);
        }

        [Test]
        public void WrongReportPhaseThrows()
        {
            var f = new SessionFlow();
            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "synthetic");
            Assert.Throws<ArgumentException>(() => f.CompleteBaseline(Report(ReportPhase.PostTask)));
        }

        [Test]
        public void InvalidReportIsRejected()
        {
            var f = new SessionFlow();
            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "synthetic");
            Assert.Throws<ContractException>(() => f.CompleteBaseline(Report(ReportPhase.Baseline, eye: 9)));
        }

        [Test]
        public void SyntheticCannotClaimNativeGaze()
        {
            var f = new SessionFlow();
            Assert.Throws<ArgumentException>(() =>
                f.AcceptConsent(DataSource.Synthetic, null, new Capabilities { NativeEyeGaze = true }, "x"));
            Assert.Throws<ArgumentException>(() => f.AcceptConsent(DataSource.Replay, null, Synthetic, "x"));
        }

        [Test]
        public void PauseBlocksTransitionsUntilResume()
        {
            var f = new SessionFlow();
            f.Pause();
            Assert.IsTrue(f.IsPaused);
            Assert.Throws<InvalidOperationException>(() => f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "s"));
            f.Resume();
            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "s");
            Assert.AreEqual(FlowState.Baseline, f.State);
        }

        [Test]
        public void ExitWorksWhilePausedAndClearsData()
        {
            var f = ToSummary();
            var id = f.Context.SessionId;
            f.Pause();
            f.Exit();
            Assert.AreEqual(FlowState.Exited, f.State);
            Assert.IsFalse(f.IsPaused);
            Assert.IsNull(f.Context.PostTaskReport);
            Assert.IsNull(f.Context.DataSource);
            f.Restart();
            Assert.AreEqual(FlowState.Consent, f.State);
            Assert.AreNotEqual(id, f.Context.SessionId);
        }

        [Test]
        public void SummaryWithOtherProvenanceIsRejected()
        {
            var f = ToSummary();
            var s = SummaryFor(f);
            s.DataSource = DataSource.Replay;
            s.ReplayOrigin = ReplayOrigin.Recorded;
            Assert.Throws<InvalidOperationException>(() => f.SetSummary(s));
            var other = SummaryFor(f);
            other.SessionId = SessionContext.NewSessionId();
            Assert.Throws<InvalidOperationException>(() => f.SetSummary(other));
        }

        [Test]
        public void SceneNeedsAnalysis()
        {
            var f = ToSummary();
            f.SetSummary(SummaryFor(f));
            Assert.Throws<InvalidOperationException>(() => f.EnterScene());
        }

        [Test]
        public void StopSessionPreventsSceneEntryButAllowsExit()
        {
            var f = ToSummary();
            var summary = SummaryFor(f);
            f.SetSummary(summary);
            var request = AnalyzeRequest.Create(summary, null, null);
            request.SafetyNotice = SafetyNotice.StopSession;
            f.SetAnalysis(LocalFallbackAnalysis.Analyze(request), "test");
            Assert.Throws<InvalidOperationException>(() => f.EnterScene());
            f.Exit();
            Assert.AreEqual(FlowState.Exited, f.State);
        }

        [TestCase(4, 4, false)]
        [TestCase(5, 1, true)]
        [TestCase(1, 5, true)]
        [TestCase(7, 7, true)]
        public void BaselineTiredSelfReportFlagsLowConfidence(int eye, int mind, bool low)
        {
            var f = new SessionFlow();
            f.AcceptConsent(DataSource.Synthetic, null, Synthetic, "s");
            f.CompleteBaseline(Report(ReportPhase.Baseline, eye, mind));
            Assert.AreEqual(low, f.Context.BaselineLowConfidence);
            Assert.AreEqual(low ? LowConfidenceReason.SelfReportedTired : (LowConfidenceReason?)null, f.Context.BaselineLowConfidenceReason);
        }

        [Test]
        public void SessionIdsAreUuidV4()
        {
            var p = new Problems();
            p.Uuid("id", SessionContext.NewSessionId());
            Assert.IsFalse(p.Any);
        }

        static SessionFlow WithAnalysis(bool stop = false)
        {
            var flow = ToSummary();
            var summary = SummaryFor(flow);
            flow.SetSummary(summary);
            var request = AnalyzeRequest.Create(summary, flow.Context.BaselineReport, null);
            if (stop) request.SafetyNotice = SafetyNotice.StopSession;
            flow.SetAnalysis(LocalFallbackAnalysis.Analyze(request), "test");
            return flow;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReportOnlyCompletesAndRetainsReportUntilExplicitReset(bool stop)
        {
            var flow = WithAnalysis(stop);
            var context = flow.Context;
            flow.FinishWithReport();
            Assert.AreEqual(FlowState.Done, flow.State);
            Assert.AreSame(context, flow.Context);
            Assert.NotNull(context.Summary);
            Assert.NotNull(context.Analysis);
            Assert.IsNull(context.Preferences);
            Assert.IsNull(context.PostSceneReport);
            Assert.IsFalse(context.SceneParticipated);
            flow.Restart();
            Assert.IsNull(flow.Context.Summary);
            Assert.IsNull(flow.Context.Analysis);
            Assert.AreNotEqual(context.SessionId, flow.Context.SessionId);
        }

        [Test]
        public void ReportCompletionWaitsForAnalysisAndRespectsPause()
        {
            Assert.Throws<InvalidOperationException>(() => ToSummary().FinishWithReport());
            var flow = WithAnalysis();
            flow.Pause();
            Assert.Throws<InvalidOperationException>(() => flow.FinishWithReport());
            Assert.Throws<InvalidOperationException>(() => flow.ChooseRelaxation());
            flow.Resume();
            flow.FinishWithReport();
        }

        [Test]
        public void SceneNeedsSeparateChoiceAndCanBeDeclinedAfterPreview()
        {
            var flow = WithAnalysis();
            Assert.Throws<InvalidOperationException>(() => flow.EnterScene());
            Assert.Throws<InvalidOperationException>(() => flow.SetScenePreferences(new Preferences()));
            flow.ChooseRelaxation();
            Assert.AreEqual(FlowState.RelaxationSetup, flow.State);
            Assert.IsFalse(flow.Context.SceneParticipated);
            flow.CancelRelaxation();
            Assert.AreEqual(FlowState.Summary, flow.State);
            Assert.IsNull(flow.Context.Preferences);
            flow.FinishWithReport();
            Assert.IsFalse(flow.Context.SceneParticipated);
        }

        [Test]
        public void ScenePreferencesApplyOnlyAfterChoiceAndFeedbackCanBeSkipped()
        {
            var flow = WithAnalysis();
            Assert.Throws<InvalidOperationException>(() => flow.SkipReRating());
            flow.ChooseRelaxation();
            var preferences = new Preferences { Sound = SoundMode.Silent, AnimalsEnabled = false };
            flow.SetScenePreferences(preferences);
            flow.EnterScene();
            preferences.Sound = SoundMode.Forest;
            Assert.IsFalse(flow.Context.SceneParticipated, "entry is not participation until the forest actually loads");
            Assert.AreEqual(SoundMode.Silent, flow.Context.Preferences.Sound);
            flow.MarkSceneParticipated();
            Assert.IsTrue(flow.Context.SceneParticipated);
            flow.FinishScene();
            flow.SkipReRating();
            Assert.AreEqual(FlowState.Done, flow.State);
            Assert.IsNull(flow.Context.PostSceneReport);
            Assert.NotNull(flow.Context.Analysis);
        }

        [Test]
        public void UnavailableForestCompletesWithoutPostSceneFeedback()
        {
            var flow = WithAnalysis();
            flow.ChooseRelaxation();
            flow.EnterScene();
            flow.FinishScene();
            Assert.AreEqual(FlowState.Done, flow.State);
            Assert.IsFalse(flow.Context.SceneParticipated);
            Assert.IsNull(flow.Context.PostSceneReport);
        }

        [Test]
        public void StopSessionBlocksRelaxationChoiceAndExitClearsRetainedReport()
        {
            var flow = WithAnalysis(true);
            Assert.Throws<InvalidOperationException>(() => flow.ChooseRelaxation());
            flow.FinishWithReport();
            flow.Exit();
            Assert.IsNull(flow.Context.Summary);
            Assert.IsNull(flow.Context.Analysis);
            Assert.IsNull(flow.Context.PostTaskReport);
        }
    }
}
