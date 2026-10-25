using System;
using System.Collections;
using System.IO;
using App.Core.Contracts;
using App.Core.Flow;
using App.Reports;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace App.Tests
{
    public class PackagedReplayTests
    {
        static string FixtureUrl => new Uri(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "fixtures/replay/demo_session.json"))).AbsoluteUri;

        static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), "timed out");
        }

        static SessionController Setup()
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            controller.ReplayCsvPath = FixtureUrl;
            controller.ReplaySpeed = 40;
            controller.Analysis = AnalysisMode.Local;
            return controller;
        }

        [UnityTest]
        public IEnumerator UrlLoadedReplayKeepsProvenanceSpeedAndCompletesWithReport()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var controller = Setup();
            var consent = UnityEngine.Object.FindAnyObjectByType<ConsentPanel>();
            consent.StartButton.onClick.Invoke();
            Assert.IsTrue(controller.ReplayLoading);
            Assert.IsFalse(consent.StartButton.interactable);
            controller.AcceptConsent(); // A duplicate request cannot start a second load/capture.
            yield return WaitFor(() => !controller.ReplayLoading);
            Assert.AreEqual(DataSource.Replay, controller.Flow.Context.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, controller.Flow.Context.ReplayOrigin);
            Assert.IsNull(controller.ProviderNote);
            Assert.Less(controller.PhaseRemainingSeconds, 4, "the prepared replay must retain 40x speed");
            yield return WaitFor(() => !controller.IsCapturing);
            controller.SubmitBaseline(2, 2);
            yield return WaitFor(() => controller.Flow.State == FlowState.CheckIn);
            controller.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 3 }, null);
            yield return WaitFor(() => controller.Flow.Context.Analysis != null);
            Assert.AreEqual(DataSource.Replay, controller.Flow.Context.Summary.DataSource);
            Assert.AreNotEqual(StrainLevel.Unavailable, controller.Flow.Context.Summary.StrainIndicator.Level);
            controller.FinishWithReport();
            Assert.AreEqual(FlowState.Done, controller.Flow.State);
            Assert.IsFalse(SceneManager.GetSceneByName("ForestDemo").isLoaded);
            controller.Exit();
        }

        [UnityTest]
        public IEnumerator InvalidPackagedReplayStaysAtConsentAndCanRetry()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var controller = Setup();
            var file = Path.GetTempFileName();
            File.WriteAllText(file, "invented-private-parser-marker");
            try
            {
                controller.ReplayCsvPath = new Uri(file).AbsoluteUri;
                controller.AcceptConsent();
                yield return WaitFor(() => !controller.ReplayLoading);
                Assert.AreEqual(FlowState.Consent, controller.Flow.State);
                Assert.IsFalse(controller.IsCapturing);
                Assert.IsNull(controller.Flow.Context.DataSource);
                StringAssert.DoesNotContain("invented-private-parser-marker", controller.ReplayLoadStatus);
                Assert.IsTrue(UnityEngine.Object.FindAnyObjectByType<ConsentPanel>().StartButton.interactable);
                controller.ReplayCsvPath = FixtureUrl;
                controller.AcceptConsent();
                yield return WaitFor(() => !controller.ReplayLoading);
                Assert.AreEqual(DataSource.Replay, controller.Flow.Context.DataSource);
                controller.Exit();
            }
            finally { File.Delete(file); }
        }

        [UnityTest]
        public IEnumerator ExitAndRestartDuringLoadingCannotReviveCapture()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var controller = Setup();
            controller.AcceptConsent();
            Assert.IsTrue(controller.ReplayLoading);
            var previousId = controller.Flow.Context.SessionId;
            controller.Exit();
            controller.Restart();
            for (var i = 0; i < 20; i++) yield return null;
            Assert.AreNotEqual(previousId, controller.Flow.Context.SessionId);
            Assert.AreEqual(FlowState.Consent, controller.Flow.State);
            Assert.IsNull(controller.Flow.Context.DataSource);
            Assert.IsFalse(controller.IsCapturing);
            Assert.IsFalse(controller.ReplayLoading);
            Assert.IsNull(controller.ReplayLoadStatus);
            controller.Exit();
        }
    }
}
