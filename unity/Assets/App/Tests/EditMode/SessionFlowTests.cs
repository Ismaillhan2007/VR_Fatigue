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

        static SessionFlow WithReport(bool stop = false)
        {
            var f = ToSummary();
            var s = SummaryFor(f);
            f.SetSummary(s);
            var request = AnalyzeRequest.Create(s, f.Context.BaselineReport, null);
            if (stop) request.SafetyNotice = SafetyNotice.StopSession;
            f.SetAnalysis(LocalFallbackAnalysis.Analyze(request), "offline test");
            return f;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReportOnlyKeepsFullReportUntilResetOrExit(bool stop)
        {
            var f = WithReport(stop);
            var context = f.Context;
            f.FinishWithReport();
            Assert.AreEqual(FlowState.Done, f.State);
            Assert.AreSame(context, f.Context);
            Assert.NotNull(f.Context.Analysis);
            Assert.IsNull(f.Context.Preferences);
            Assert.IsNull(f.Context.PostSceneReport);
            Assert.IsFalse(f.Context.SceneParticipated);
            f.Restart();
            Assert.IsNull(f.Context.Summary);
            Assert.AreNotEqual(context.SessionId, f.Context.SessionId);
        }

        [Test]
        public void ReportNeedsAnalysisAndCannotFinishWhilePaused()
        {
            var f = ToSummary();
            Assert.Throws<InvalidOperationException>(() => f.FinishWithReport());
            f = WithReport();
            f.Pause();
            Assert.Throws<InvalidOperationException>(() => f.FinishWithReport());
            f.Exit();
            Assert.IsNull(f.Context.Analysis);
            Assert.IsNull(f.Context.Summary);
        }

        [Test]
        public void ExplicitChoicePrecedesPreferencesAndSceneEntry()
        {
            var f = WithReport();
            var summary = f.Context.Summary;
            Assert.Throws<InvalidOperationException>(() => f.EnterScene());
            Assert.Throws<InvalidOperationException>(() => f.SetScenePreferences(new Preferences()));
            f.ChooseRelaxation();
            Assert.IsFalse(f.Context.SceneParticipated);
            f.SetScenePreferences(new Preferences { AnimalsEnabled = false });
            f.CancelRelaxation();
            Assert.AreSame(summary, f.Context.Summary);
            Assert.IsNull(f.Context.Preferences);
            f.FinishWithReport();
        }

        [Test]
        public void SceneFeedbackCanBeSkippedWithoutInventedValues()
        {
            var f = WithReport();
            f.ChooseRelaxation();
            f.EnterScene();
            f.MarkSceneParticipated();
            f.FinishScene();
            f.SkipReRating();
            Assert.AreEqual(FlowState.Done, f.State);
            Assert.IsTrue(f.Context.SceneParticipated);
            Assert.IsNull(f.Context.PostSceneReport);
        }

        [Test]
        public void UnavailableForestCompletesWithoutPostSceneFeedback()
        {
            var f = WithReport();
            f.ChooseRelaxation();
            f.EnterScene();
            f.FinishScene();
            Assert.AreEqual(FlowState.Done, f.State);
            Assert.IsFalse(f.Context.SceneParticipated);
            Assert.IsNull(f.Context.PostSceneReport);
        }

        [Test]
        public void StopSessionBlocksOptionalChoice()
        {
            var f = WithReport(true);
            Assert.Throws<InvalidOperationException>(() => f.ChooseRelaxation());
            f.FinishWithReport();
            Assert.NotNull(f.Context.Analysis);
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
    }
}
