using System.Linq;
using App.Core.Flow;
using App.Quest;
using App.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

public class QuestRigStructureTests
{
    [Test]
    public void RigHasOneRenderingCameraAudioListenerAndTwoTrackedControllerRays()
    {
        EditorSceneManager.OpenScene("Assets/App/Scenes/Assessment.unity");
        try
        {
            var controller = Object.FindAnyObjectByType<SessionController>();
            foreach (var panel in controller.Panels)
                PrefabUtility.UnpackPrefabInstance(panel.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var owner = QuestAssessmentRig.Create(controller);
            var cameras = Object.FindObjectsByType<Camera>();
            Assert.AreEqual(1, cameras.Count(c => c.enabled));
            Assert.AreSame(owner.Head, Camera.main);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>().Count(a => a.enabled));
            Assert.AreEqual(1, Object.FindObjectsByType<OVRManager>().Length);
            Assert.AreEqual(1, Object.FindObjectsByType<OVRCameraRig>().Length);
            var rays = owner.GetComponentsInChildren<XRRayInteractor>();
            Assert.AreEqual(2, rays.Length);
            Assert.AreEqual("<XRController>{LeftHand}/triggerPressed", rays[0].uiPressInput.inputActionPerformed.bindings[0].path);
            Assert.AreEqual("<XRController>{RightHand}/triggerPressed", rays[1].uiPressInput.inputActionPerformed.bindings[0].path);
            Assert.AreEqual("<XRController>{RightHand}/{TriggerButton}", rays[1].uiPressInput.inputActionPerformed.bindings[1].path);
            foreach (var ray in rays)
            {
                Assert.AreEqual(XRInputButtonReader.InputSourceMode.InputAction, ray.uiPressInput.inputSourceMode);
                Assert.IsTrue(ray.enableUIInteraction);
                Assert.AreEqual(1 << LayerMask.NameToLayer("UI"), ray.raycastMask.value);
                Assert.NotNull(ray.GetComponent<LineRenderer>().sharedMaterial);
            }
            Assert.Throws<System.InvalidOperationException>(() => QuestAssessmentRig.Create(controller));
            var manager = new SerializedObject(Object.FindAnyObjectByType<OVRManager>());
            foreach (var name in new[] { "requestEyeTrackingPermissionOnStartup", "requestFaceTrackingPermissionOnStartup", "requestBodyTrackingPermissionOnStartup" })
                Assert.IsFalse(manager.FindProperty(name).boolValue, "no permission request before explicit native consent");
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
    }

    [Test]
    public void PlacementWaitsForStableTrackingAndHappensOnce()
    {
        var gate = new PlacementGate();
        Assert.IsTrue(gate.Pending, "the first start screen must be placed");
        Assert.IsFalse(gate.Tick(true, 0.3f, FlowState.Consent));
        Assert.IsFalse(gate.Tick(false, 0.3f, FlowState.Consent), "tracking loss restarts the settle time");
        Assert.IsFalse(gate.Tick(true, 0.3f, FlowState.Consent));
        Assert.IsTrue(gate.Tick(true, 0.3f, FlowState.Consent));
        Assert.IsFalse(gate.Tick(true, 1f, FlowState.Consent), "placed exactly once");
    }

    [Test]
    public void StartDuringSettleDelayNeverMovesThePanelInsideACapture()
    {
        // Review finding on #35: Start pressed during the 0.5 s delay let LateUpdate move the panel after recording began.
        var gate = new PlacementGate();
        Assert.IsFalse(gate.Tick(true, 0.3f, FlowState.Consent));
        Assert.IsFalse(gate.Tick(true, 0.3f, FlowState.Baseline), "capture started: placement is cancelled, not performed");
        Assert.IsFalse(gate.Pending);
        Assert.IsFalse(gate.Tick(true, 5f, FlowState.Baseline));
        Assert.IsFalse(gate.Tick(true, 5f, FlowState.LoadTask));

        gate.Request(FlowState.Baseline);
        gate.Request(FlowState.LoadTask);
        Assert.IsFalse(gate.Pending, "requests during baseline/task are ignored");
    }

    [Test]
    public void CancelledWhenCaptureStartsAndPlacedAgainOnTheNextStartScreen()
    {
        var gate = new PlacementGate();
        gate.Cancel(); // flow entered Baseline
        Assert.IsFalse(gate.Tick(true, 1f, FlowState.Baseline));
        gate.Request(FlowState.Consent); // New session
        Assert.IsFalse(gate.Tick(true, 0.2f, FlowState.Consent));
        Assert.IsTrue(gate.Tick(true, 0.4f, FlowState.Consent));
    }
}
