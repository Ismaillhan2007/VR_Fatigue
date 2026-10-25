using System.Collections;
using System.Linq;
using App.Capture;
using App.Core.Contracts;
using App.Core.Flow;
using App.Quest;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

public class QuestPresentationTests
{
    QuestAssessmentPresentation _view;
    SessionController _controller;
    XRUIInputModule _module;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("Assessment");
        _controller = Object.FindAnyObjectByType<SessionController>();
        _controller.ShowPhaseInstructions = false; // flow and capture tests; PhaseInstructionTests cover the gate
        var head = Camera.main;
        _view = new GameObject("Simulated Quest presentation").AddComponent<QuestAssessmentPresentation>();
        _view.Configure(_controller, head);
        _module = Object.FindAnyObjectByType<XRUIInputModule>();
        yield return null;
        Canvas.ForceUpdateCanvases();
    }

    [Test]
    public void OneInputModuleAndWorldLockedPanelWithHeadRelativeProbes()
    {
        Assert.AreEqual(1, Object.FindAnyObjectByType<EventSystem>().GetComponents<BaseInputModule>().Count(m => m.enabled));
        Assert.IsTrue(_module.enableXRInput);
        Assert.AreEqual(RenderMode.WorldSpace, _view.Canvas.renderMode);
        var before = _view.Canvas.transform.position;
        var head = _view.Head.transform;
        head.position += Vector3.right;
        head.rotation = Quaternion.Euler(10, 45, 0);
        Assert.AreEqual(before, _view.Canvas.transform.position, "panel stays still until explicitly brought into view");
        var direction = new System.Numerics.Vector3(0.3f, 0.2f, 0.9327379f);
        _controller.ProbeRunner.Show(direction);
        var world = _controller.ProbeRunner.Target.position;
        var local = Quaternion.Inverse(head.rotation) * (world - head.position);
        Assert.That(local.x, Is.EqualTo(direction.X * 2).Within(0.0001));
        Assert.That(local.y, Is.EqualTo(direction.Y * 2).Within(0.0001));
        Assert.That(local.z, Is.EqualTo(direction.Z * 2).Within(0.0001));
        _view.PlaceInFrontOfHead();
        Assert.That(Vector3.Distance(head.position, _view.Canvas.transform.position), Is.EqualTo(2).Within(0.0001));
        // Placed along the horizontal view direction, upright: head pitch (10 deg) must not tilt the panel.
        Assert.That(_view.Canvas.transform.position.y, Is.EqualTo(head.position.y).Within(0.0001));
        Assert.That(Vector3.Angle(_view.Canvas.transform.up, Vector3.up), Is.LessThan(0.01f));
        Assert.That(_view.Canvas.transform.eulerAngles.y, Is.EqualTo(45).Within(0.01));
        Assert.GreaterOrEqual(_view.Canvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit, 3f, "text must be rasterized above 1 texel per unit");
    }

    [UnityTest]
    public IEnumerator TrackedRayCanClickConsentPauseAndExit()
    {
        _controller.EyeSource = EyeSourceMode.Synthetic;
        _controller.BaselineSeconds = 100;
        var pointer = new Pointer(_module, _view.Head);
        _module.RegisterInteractor(pointer);
        try
        {
            var consent = Object.FindAnyObjectByType<ConsentPanel>();
            yield return Click(pointer, consent.StartButton);
            Assert.AreEqual(FlowState.Baseline, _controller.Flow.State);
            yield return Click(pointer, _controller.ProvenanceBar.PauseButton);
            Assert.IsTrue(_controller.Flow.IsPaused);
            yield return Click(pointer, _controller.PauseOverlay.ExitButton);
            Assert.AreEqual(FlowState.Exited, _controller.Flow.State);
            Assert.IsNull(_controller.Flow.Context.Summary);
        }
        finally { _module.UnregisterInteractor(pointer); }
    }

    [UnityTest]
    public IEnumerator ReportScrollsAtReadableSizeThroughTrackedInput()
    {
        var flow = _controller.Flow;
        flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities { GazePose = true }, "synthetic UI test");
        flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
        flow.CompleteLoadTask();
        flow.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 2, MentalFatigue = 2 }, null);
        var body = Object.FindAnyObjectByType<SummaryPanel>().Body;
        body.text = string.Join("\n", Enumerable.Repeat("Synthetic layout test: measured observations and limitations.", 70));
        yield return null;
        Canvas.ForceUpdateCanvases();
        var scroll = body.GetComponentInParent<ScrollRect>();
        Assert.IsFalse(body.resizeTextForBestFit);
        Assert.AreEqual(24, body.fontSize);
        Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
        var pointer = new Pointer(_module, _view.Head);
        pointer.Target = scroll.viewport.TransformPoint(scroll.viewport.rect.center);
        pointer.Scroll = new Vector2(0, -1);
        _module.RegisterInteractor(pointer);
        try
        {
            var before = scroll.content.anchoredPosition.y;
            for (int i = 0; i < 4; i++) yield return null;
            Assert.Greater(scroll.content.anchoredPosition.y, before);
        }
        finally { _module.UnregisterInteractor(pointer); }
    }

    [UnityTest]
    public IEnumerator RecenterBetweenPhasesClearsBaselineAndRequiresFreshConsent()
    {
        _controller.EyeSource = EyeSourceMode.Synthetic;
        _controller.AcceptConsent();
        _controller.EndCaptureEarly(); // baseline task finished, but the next phase has not started
        Assert.IsFalse(_controller.IsCapturing);
        var owner = new GameObject("Lifecycle owner").AddComponent<QuestAssessmentRig>();
        typeof(QuestAssessmentRig).GetProperty(nameof(QuestAssessmentRig.Controller)).SetValue(owner, _controller);
        typeof(QuestAssessmentRig).GetProperty(nameof(QuestAssessmentRig.Presentation)).SetValue(owner, _view);
        owner.HandleRecenter();
        Assert.AreEqual(FlowState.Exited, _controller.Flow.State);
        Assert.IsNull(_controller.Flow.Context.DataSource);
        Assert.IsFalse(_controller.IsCapturing);
        _controller.Restart();
        Assert.AreEqual(FlowState.Consent, _controller.Flow.State);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PendingReportCanCompleteWhileFlowRemainsPaused()
    {
        _controller.EyeSource = EyeSourceMode.Synthetic;
        _controller.Analysis = AnalysisMode.Http;
        _controller.ConfigureBackend("http://127.0.0.1:9", System.Environment.GetEnvironmentVariable("VR_BACKEND_ACCESS_TOKEN") ?? "", true, true, true);
        _controller.AcceptConsent();
        _controller.EndCaptureEarly();
        _controller.SubmitBaseline(2, 2);
        _controller.EndCaptureEarly();
        _controller.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 2, MentalFatigue = 2 }, null);
        _controller.Flow.Pause();
        var deadline = Time.realtimeSinceStartup + 5;
        while ((_controller.Flow.Context.Analysis == null || _controller.AnalysisInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.NotNull(_controller.Flow.Context.Analysis);
        Assert.IsFalse(_controller.AnalysisInProgress);
        Assert.IsTrue(_controller.Flow.IsPaused);
        Assert.AreEqual(FlowState.Summary, _controller.Flow.State);
        Assert.Throws<System.InvalidOperationException>(() => _controller.EnterScene());
        _controller.Flow.Resume();
        _controller.Exit();
    }

    [UnityTest]
    public IEnumerator ReportOnlyCompletesThroughTrackedRayWithReportDependency()
    {
        if (typeof(SessionController).GetMethod("FinishWithReport") == null)
            Assert.Ignore("Requires report-only dependency #16 / PR #26; run the combined integration check");
        _controller.ReplaySpeed = 40;
        _controller.Analysis = AnalysisMode.Local;
        var pointer = new Pointer(_module, _view.Head);
        _module.RegisterInteractor(pointer);
        try
        {
            yield return Click(pointer, Object.FindAnyObjectByType<ConsentPanel>().StartButton);
            var deadline = Time.realtimeSinceStartup + 15;
            while (_controller.IsCapturing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(_controller.IsCapturing);
            yield return Click(pointer, Object.FindAnyObjectByType<BaselinePanel>().ContinueButton);
            deadline = Time.realtimeSinceStartup + 15;
            while (_controller.Flow.State != FlowState.CheckIn && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.CheckIn, _controller.Flow.State);
            yield return Click(pointer, Object.FindAnyObjectByType<CheckInPanel>().SubmitButton);
            var summary = Object.FindAnyObjectByType<SummaryPanel>();
            var finish = summary.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "Finish with report");
            yield return Click(pointer, finish);
            Assert.AreEqual(FlowState.Done, _controller.Flow.State);
            Assert.NotNull(_controller.Flow.Context.Analysis);
            Assert.IsNull(_controller.Flow.Context.PostSceneReport);
            Assert.IsNull(_controller.Flow.Context.Preferences);
            Assert.IsFalse(SceneManager.GetSceneByName("ForestDemo").isLoaded);
            StringAssert.Contains("REPLAY", _controller.ProvenanceBar.DataLabel.text);
            StringAssert.Contains("Limitations:", Object.FindAnyObjectByType<DonePanel>().Body.text);
        }
        finally { _module.UnregisterInteractor(pointer); }
    }

    [UnityTest]
    public IEnumerator ProductionRayAndTriggerBindingReachUi()
    {
        const string layout = "QuestUiSyntheticController";
        InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>(layout);
        var device = InputSystem.AddDevice(layout);
        InputSystem.SetDeviceUsage(device, UnityEngine.InputSystem.CommonUsages.RightHand);
        var anchor = new GameObject("Synthetic controller pose");
        var manager = new GameObject("Synthetic XRI manager").AddComponent<XRInteractionManager>();
        try
        {
            _controller.EyeSource = EyeSourceMode.Synthetic;
            _controller.BaselineSeconds = 100;
            var button = Object.FindAnyObjectByType<ConsentPanel>().StartButton;
            var target = button.transform.TransformPoint(((RectTransform)button.transform).rect.center);
            anchor.transform.position = _view.Head.transform.position;
            anchor.transform.rotation = Quaternion.LookRotation(target - anchor.transform.position);
            typeof(QuestAssessmentRig).GetMethod("ConfigureRay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { anchor.transform, "RightHand" });
            var ray = anchor.GetComponentInChildren<XRRayInteractor>();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(ray.TryGetCurrentUIRaycastResult(out var hit), "production ray must hit the UI layer");
            Assert.NotNull(hit.gameObject.GetComponentInParent<Button>());
            using (StateEvent.From(device, out var pressEvent))
            {
                ((ButtonControl)device["triggerPressed"]).WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);
            }
            yield return null;
            Assert.IsTrue(ray.uiPressInput.ReadIsPerformed(), "the production OpenXR trigger binding must be enabled and resolve");
            Assert.Greater(ray.uiScrollInput.inputAction.controls.Count, 0, "thumbstick scroll binding must resolve");
            yield return null;
            using (StateEvent.From(device, out var releaseEvent))
            {
                ((ButtonControl)device["triggerPressed"]).WriteValueIntoEvent(0f, releaseEvent);
                InputSystem.QueueEvent(releaseEvent);
            }
            yield return null; yield return null;
            Assert.AreEqual(FlowState.Baseline, _controller.Flow.State);
            _controller.Exit();
        }
        finally
        {
            InputSystem.RemoveDevice(device); InputSystem.RemoveLayout(layout);
            Object.Destroy(anchor); Object.Destroy(manager.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator TrackedRayOpensBackendSettingsAndReturnsToConsent()
    {
        var panel = Object.FindAnyObjectByType<ConsentPanel>();
        var setup = new GameObject("Combined native and backend controls").AddComponent<QuestEyeSetupPanel>();
        setup.Configure(_controller);
        yield return null;
        var pointer = new Pointer(_module, _view.Head);
        _module.RegisterInteractor(pointer);
        try
        {
            Canvas.ForceUpdateCanvases();
            var bounds = new Vector3[4];
            foreach (var button in panel.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(bounds);
                foreach (var corner in bounds)
                    Assert.IsTrue(((RectTransform)_view.Canvas.transform).rect.Contains(
                        _view.Canvas.transform.InverseTransformPoint(corner)), "combined consent control stays on canvas: " + button.name);
            }
            yield return Click(pointer, panel.OpenSettingsButton);
            Assert.IsTrue(panel.SettingsRoot.activeSelf);
            Assert.AreEqual(InputField.ContentType.Password, panel.BackendToken.contentType);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var field in new[] { panel.BackendUrl, panel.BackendToken })
            {
                ((RectTransform)field.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = _view.Canvas.transform.InverseTransformPoint(corner);
                    Assert.IsTrue(((RectTransform)_view.Canvas.transform).rect.Contains(point), "settings input stays within the tracked canvas");
                }
            }
            yield return Click(pointer, panel.SaveSettingsButton);
            Assert.IsFalse(panel.SettingsRoot.activeSelf);
            Assert.AreEqual(FlowState.Consent, _controller.Flow.State);
        }
        finally { _module.UnregisterInteractor(pointer); Object.Destroy(setup.gameObject); }
    }

    [UnityTest]
    public IEnumerator NativeSetupControlsReachConsentAndUnavailableHardwareDoesNotBecomeReplay()
    {
        var setup = new GameObject("Native setup controls").AddComponent<QuestEyeSetupPanel>();
        setup.Configure(_controller);
        var consent = Object.FindAnyObjectByType<ConsentPanel>();
        var buttons = consent.GetComponentsInChildren<Button>();
        buttons.Single(b => b.name == "Use native eyes").onClick.Invoke();
        Assert.AreEqual(EyeSourceMode.Hardware, _controller.EyeSource);
        var original = EyeProviderFactory.HardwareFactory;
        EyeProviderFactory.HardwareFactory = null; // No physical headset in this test.
        try
        {
            buttons.Single(b => b.name == "I consent: request eye permission").onClick.Invoke();
            consent.StartButton.onClick.Invoke();
            Assert.AreEqual(FlowState.Consent, _controller.Flow.State);
            Assert.IsNull(_controller.Flow.Context.DataSource);
            StringAssert.Contains("Grant eye permission", _controller.HardwareSetupStatus);
            buttons.Single(b => b.name == "Use replay").onClick.Invoke();
            Assert.AreEqual(EyeSourceMode.Replay, _controller.EyeSource);
            Assert.IsFalse(_controller.HardwareCalibrationConfirmed);
        }
        finally { EyeProviderFactory.HardwareFactory = original; Object.Destroy(setup.gameObject); }
        yield return null;
    }

    [UnityTest]
    public IEnumerator ProductionNativeFactoryIsUnavailableInEditorAndReleasesItsOwner()
    {
        var provider = QuestNativeEyeFactory.Create();
        Assert.AreEqual(DataSource.Live, provider.DataSource, "declared production source, not successful live acquisition");
        Assert.IsFalse(provider.Capabilities.GazePose, "capability is not claimed until Start succeeds");
        Assert.IsFalse(provider.RequestPermissionAfterConsent());
        provider.ConfirmCalibration(true);
        Assert.Throws<System.InvalidOperationException>(() => provider.Start(0));
        Assert.IsFalse(provider.IsRunning);
        provider.Dispose(); yield return null;
        Assert.IsNull(provider.AggregateDiagnostics);
    }

    static IEnumerator Click(Pointer pointer, Button button)
    {
        Canvas.ForceUpdateCanvases();
        pointer.Target = button.transform.TransformPoint(((RectTransform)button.transform).rect.center);
        pointer.Pressed = false; yield return null;
        pointer.Pressed = true; yield return null;
        pointer.Pressed = false; yield return null;
    }

    // A clearly synthetic tracked controller model exercises XRI raycasting/event dispatch, not device tracking.
    sealed class Pointer : IUIInteractor
    {
        readonly XRUIInputModule _module;
        readonly Camera _head;
        public Vector3 Target;
        public bool Pressed;
        public Vector2 Scroll;
        public Pointer(XRUIInputModule module, Camera head) { _module = module; _head = head; }
        public bool TryGetUIModel(out TrackedDeviceModel model) => _module.GetTrackedDeviceModel(this, out model);
        public void UpdateUIModel(ref TrackedDeviceModel model)
        {
            var origin = _head.transform.position;
            var dir = (Target - origin).normalized;
            model.position = origin;
            model.orientation = Quaternion.LookRotation(dir);
            model.raycastPoints.Clear(); model.raycastPoints.Add(origin); model.raycastPoints.Add(Target + dir * 0.1f);
            model.raycastLayerMask = ~0;
            model.select = Pressed;
            model.scrollDelta = Scroll;
        }
    }
}
