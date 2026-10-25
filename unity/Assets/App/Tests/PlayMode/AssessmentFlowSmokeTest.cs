using System;
using System.Collections;
using System.Collections.Generic;
using App.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace App.Tests
{
    /// <summary>Desktop smoke test: the generated Assessment scene completes one synthetic session offline.</summary>
    public class AssessmentFlowSmokeTest
    {
        static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds = 10)
        {
            var end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SyntheticSessionCompletesWithLabelledFallback()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            Assert.NotNull(c, "SessionController missing: run App > Build Assessment Scene");
            c.EyeSource = EyeSourceMode.Synthetic;
            c.BaselineSeconds = 1;
            c.TaskSeconds = 1;
            c.Analysis = AnalysisMode.Http;
            c.BackendBaseUrl = "http://127.0.0.1:9"; // nothing listens here: must fall back locally
            Assert.AreEqual(FlowState.Consent, c.Flow.State);

            c.AcceptConsent();
            Assert.AreEqual(FlowState.Baseline, c.Flow.State);
            yield return WaitUntil(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn);

            c.SubmitCheckIn(new SelfReport
            {
                Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 4,
                Chips = new List<Chip> { Chip.TiredEyes, Chip.WantQuiet }, FreeText = "a bit dizzy",
            }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var ctx = c.Flow.Context;
            Assert.AreEqual(DataSource.Synthetic, ctx.Summary.DataSource);
            Assert.AreEqual(AiSource.Fallback, ctx.Analysis.AiSource);
            Assert.AreEqual(SafetyNotice.TakeBreak, ctx.Analysis.SafetyNotice);
            StringAssert.Contains("backend", ctx.AnalysisFallbackReason);
            var bar = UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>();
            StringAssert.Contains("SYNTHETIC", bar.DataLabel.text);
            StringAssert.Contains("LOCAL FALLBACK", bar.AiLabel.text);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            StringAssert.Contains("Forest scene loaded", c.SceneStatus, "ForestDemo must be in Build Settings");
            Assert.NotNull(UnityEngine.Object.FindAnyObjectByType<App.Relaxation.ForestSceneApplier>());
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);

            c.Exit();
            Assert.AreEqual(FlowState.Exited, c.Flow.State);
            Assert.IsNull(c.Flow.Context.Summary);
        }

        /// <summary>
        /// End-to-end with the real backend in offline (mock) mode. Set VR_SMOKE_BACKEND_URL=http://127.0.0.1:8000
        /// and start the backend first; otherwise the test is ignored.
        /// </summary>
        [UnityTest]
        public IEnumerator SessionThroughRunningBackendReachesForest()
        {
            var url = Environment.GetEnvironmentVariable("VR_SMOKE_BACKEND_URL");
            if (string.IsNullOrEmpty(url)) Assert.Ignore("VR_SMOKE_BACKEND_URL not set (backend not running for this run)");
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.EyeSource = EyeSourceMode.Synthetic;
            c.BaselineSeconds = 2;
            c.TaskSeconds = 2;
            c.Analysis = AnalysisMode.Http;
            c.BackendBaseUrl = url;

            c.AcceptConsent();
            yield return WaitUntil(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn);
            c.SubmitCheckIn(new SelfReport
            {
                Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 5,
                Chips = new List<Chip> { Chip.TiredEyes, Chip.WantQuiet }, FreeText = "private text stays local",
            }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var a = c.Flow.Context.Analysis;
            Assert.IsNull(c.Flow.Context.AnalysisFallbackReason, "the backend must have answered: " + c.Flow.Context.AnalysisFallbackReason);
            StringAssert.StartsWith("local-", a.PromptVersion, "backend offline rules");
            Assert.AreEqual(AiSource.Fallback, a.AiSource);
            Assert.AreEqual(DataSource.Synthetic, a.DataSource);
            StringAssert.Contains("backend rules", UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>().AiLabel.text);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            StringAssert.Contains("Forest scene loaded", c.SceneStatus);
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);
        }

        /// <summary>Desktop demo default: the synthetic 130 s replay with probes, played fast, offline, to the forest.</summary>
        [UnityTest]
        public IEnumerator DemoReplayShowsDeviationsAndReachesForest()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            Assert.AreEqual(EyeSourceMode.Replay, c.EyeSource, "desktop demo default");
            c.ReplaySpeed = 40;
            c.Analysis = AnalysisMode.Local;

            c.AcceptConsent();
            StringAssert.Contains("REPLAY of synthetic data", UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>().DataLabel.text);
            yield return WaitUntil(() => c.ProbeRunner.Visible, 5);
            yield return WaitUntil(() => !c.IsCapturing, 20);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn, 20);
            c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 4, Chips = new List<Chip> { Chip.TiredEyes } }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var s = c.Flow.Context.Summary;
            Assert.AreEqual(ReplayOrigin.Synthetic, s.ReplayOrigin);
            Assert.IsNotNull(s.Metrics.ProbeLatencyMs);
            Assert.IsNotNull(s.Deviations.ProbeLatencyMs);
            Assert.AreNotEqual(StrainLevel.Unavailable, s.StrainIndicator.Level, "the demo must show the experimental indicator");
            StringAssert.Contains("Probe trials", c.ProbeStatsText);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            StringAssert.Contains("Forest scene loaded", c.SceneStatus);
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);
        }
    }
}
