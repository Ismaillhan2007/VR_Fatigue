using System;
using System.Collections;
using System.Linq;
using App.Capture;
using App.Core.Flow;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace App.Tests
{
    /// <summary>Instruction screens gate recording; 1-7 number buttons; the forest menu can be hidden.</summary>
    public class PhaseInstructionTests
    {
        SessionController _c;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            _c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            _c.EyeSource = EyeSourceMode.Synthetic;
            _c.BaselineSeconds = _c.TaskSeconds = 1;
            _c.ShowPhaseInstructions = true;
        }

        static IEnumerator Until(Func<bool> condition)
        {
            var end = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator NothingIsRecordedUntilTheInstructionsAreConfirmed()
        {
            _c.AcceptConsent();
            var baseline = _c.Panels.OfType<BaselinePanel>().Single();
            Assert.AreEqual(FlowState.Baseline, _c.Flow.State);
            Assert.IsTrue(_c.AwaitingPhaseStart);
            Assert.IsTrue(baseline.StartButton.gameObject.activeSelf);
            Assert.IsFalse(baseline.RatingGroup.activeSelf, "no ratings before the baseline is recorded");
            StringAssert.Contains("yellow dot", baseline.Status.text);
            StringAssert.Contains("It takes about 1 second.", baseline.Status.text);
            yield return AssessmentFlowSmokeTest.CaptureEvidence("baseline-instructions");
            // Waiting on the instructions records nothing, however long it takes to read them.
            for (var i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(0, _c.SampleCount);
            Assert.IsFalse(_c.IsCapturing);
            _c.SubmitBaseline(2, 2);
            Assert.AreEqual(FlowState.Baseline, _c.Flow.State, "cannot rate a baseline that was never recorded");

            baseline.StartButton.onClick.Invoke();
            Assert.IsTrue(_c.IsCapturing);
            Assert.IsFalse(_c.AwaitingPhaseStart);
            Assert.IsFalse(baseline.StartButton.gameObject.activeSelf);
            Assert.IsTrue(baseline.Countdown.gameObject.activeSelf);
            yield return Until(() => !_c.IsCapturing);

            Assert.IsTrue(baseline.RatingGroup.activeSelf);
            Assert.IsFalse(baseline.ContinueButton.interactable, "both numbers must be chosen");
            baseline.EyeStrain.Select(2);
            Assert.IsFalse(baseline.ContinueButton.interactable);
            baseline.MentalFatigue.Select(5);
            Assert.IsTrue(baseline.ContinueButton.interactable);
            yield return AssessmentFlowSmokeTest.CaptureEvidence("baseline-rating");
            baseline.ContinueButton.onClick.Invoke();
            Assert.AreEqual(5, _c.Flow.Context.BaselineReport.MentalFatigue);

            var task = _c.Panels.OfType<LoadTaskPanel>().Single();
            Assert.AreEqual(FlowState.LoadTask, _c.Flow.State);
            Assert.IsTrue(_c.AwaitingPhaseStart);
            Assert.IsTrue(task.StartButton.gameObject.activeSelf);
            yield return AssessmentFlowSmokeTest.CaptureEvidence("task-instructions");
            for (var i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(FlowState.LoadTask, _c.Flow.State, "the task waits for Start");
            task.StartButton.onClick.Invoke();
            yield return Until(() => _c.Flow.State == FlowState.CheckIn);
        }

        [UnityTest]
        public IEnumerator PauseBlocksStartAndExitDiscardsTheWaitingPhase()
        {
            _c.AcceptConsent();
            _c.TogglePause();
            _c.StartPhase();
            Assert.IsFalse(_c.IsCapturing, "Start is ignored while paused");
            Assert.IsTrue(_c.AwaitingPhaseStart);
            _c.TogglePause();
            _c.Exit();
            Assert.IsFalse(_c.AwaitingPhaseStart);
            _c.StartPhase();
            Assert.IsFalse(_c.IsCapturing);
            Assert.AreEqual(FlowState.Exited, _c.Flow.State);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NumberButtonsSelectOneValueAndStartEmpty()
        {
            _c.ShowPhaseInstructions = false;
            _c.AcceptConsent();
            yield return Until(() => !_c.IsCapturing);
            var row = _c.Panels.OfType<BaselinePanel>().Single().EyeStrain;
            Assert.AreEqual(RatingRow.Max, row.Options.Count);
            Assert.IsFalse(row.HasValue);
            Assert.IsFalse(row.Options.Any(o => o.isOn));
            row.Options[3].isOn = true; // a press on "4"
            Assert.AreEqual(4, row.Value);
            row.Options[6].isOn = true; // then "7"
            Assert.AreEqual(7, row.Value);
            Assert.AreEqual(1, row.Options.Count(o => o.isOn), "one number at a time");
            row.Options[6].isOn = false; // pressing the chosen number again keeps it
            Assert.AreEqual(7, row.Value);
            Assert.IsTrue(row.Options[6].isOn);
            var visual = row.Options[6].GetComponent<ToggleVisual>();
            Assert.AreEqual(visual.OnFill, visual.Background.color, "the chosen number is highlighted");
            Assert.AreEqual(visual.OffFill, row.Options[0].GetComponent<ToggleVisual>().Background.color);
        }

        [UnityTest]
        public IEnumerator ForestMenuHidesToOneButtonAndKeepsProvenanceVisible()
        {
            _c.ShowPhaseInstructions = false;
            _c.AcceptConsent();
            yield return Until(() => !_c.IsCapturing);
            _c.SubmitBaseline(2, 2);
            yield return Until(() => _c.Flow.State == FlowState.CheckIn);
            _c.SubmitCheckIn(new App.Core.Contracts.SelfReport
            {
                Phase = App.Core.Contracts.ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 3,
                Chips = new System.Collections.Generic.List<App.Core.Contracts.Chip>(), FreeText = null,
            }, null);
            yield return Until(() => _c.Flow.Context.Analysis != null);
            _c.ChooseRelaxation();
            _c.EnterScene();
            yield return Until(() => _c.Flow.State == FlowState.Scene);
            var scene = _c.Panels.OfType<ScenePanel>().Single();
            Assert.IsTrue(scene.Content.activeSelf);
            scene.HideButton.onClick.Invoke();
            Assert.IsTrue(scene.MenuHidden);
            Assert.IsFalse(scene.Content.activeSelf);
            Assert.IsFalse(_c.ProvenanceBar.gameObject.activeSelf);
            Assert.IsTrue(scene.ShowButton.gameObject.activeSelf);
            StringAssert.Contains("Data:", scene.HiddenLabel.text);
            StringAssert.Contains("AI:", scene.HiddenLabel.text);
            scene.ShowButton.onClick.Invoke();
            Assert.IsTrue(scene.Content.activeSelf);
            Assert.IsTrue(_c.ProvenanceBar.gameObject.activeSelf);
            scene.HideButton.onClick.Invoke();
            _c.FinishScene();
            yield return null;
            Assert.IsTrue(_c.ProvenanceBar.gameObject.activeSelf, "leaving the scene restores the bar");
        }
    }
}
