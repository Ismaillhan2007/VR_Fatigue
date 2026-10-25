using System.Collections;
using System.Linq;
using App.Capture;
using App.Core.Flow;
using App.Quest;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR.Features.Interactions;

/// <summary>
/// Device bug from the first Quest Pro run: a ray enabled before the world-space UI was configured registered with an
/// XRUIInputModule that the UI then disabled, so the ray was visible but the trigger clicked nothing.
/// </summary>
public class QuestRigOrderTests
{
    [UnityTest]
    public IEnumerator RayEnabledBeforeUiConfigurationStillClicksWithTrigger()
    {
        yield return SceneManager.LoadSceneAsync("Assessment");
        var controller = Object.FindAnyObjectByType<SessionController>();
        controller.EyeSource = EyeSourceMode.Synthetic;
        controller.BaselineSeconds = 100;
        const string layout = "QuestUiOrderController";
        InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>(layout);
        var device = InputSystem.AddDevice(layout);
        InputSystem.SetDeviceUsage(device, UnityEngine.InputSystem.CommonUsages.RightHand);
        var anchor = new GameObject("Synthetic controller pose");
        var manager = new GameObject("Synthetic XRI manager").AddComponent<XRInteractionManager>();
        try
        {
            // Old production order: ray first (XRI registers it with an XRUIInputModule), UI configuration second.
            typeof(QuestAssessmentRig).GetMethod("ConfigureRay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { anchor.transform, "RightHand" });
            var view = new GameObject("Quest presentation").AddComponent<QuestAssessmentPresentation>();
            view.Configure(controller, Camera.main);
            var events = Object.FindAnyObjectByType<EventSystem>();
            Assert.AreEqual(1, events.GetComponents<BaseInputModule>().Count(m => m.enabled), "exactly one active input module");
            Assert.IsTrue(events.GetComponent<XRUIInputModule>().enabled, "the module the ray registered with stays active");
            yield return null;
            Canvas.ForceUpdateCanvases();

            var button = Object.FindAnyObjectByType<ConsentPanel>().StartButton;
            var target = button.transform.TransformPoint(((RectTransform)button.transform).rect.center);
            anchor.transform.position = view.Head.transform.position;
            anchor.transform.rotation = Quaternion.LookRotation(target - anchor.transform.position);
            var ray = anchor.GetComponentInChildren<XRRayInteractor>();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(ray.TryGetCurrentUIRaycastResult(out _), "ray must hit the UI");
            using (StateEvent.From(device, out var pressEvent))
            {
                ((ButtonControl)device["triggerPressed"]).WriteValueIntoEvent(1f, pressEvent);
                InputSystem.QueueEvent(pressEvent);
            }
            yield return null; yield return null;
            using (StateEvent.From(device, out var releaseEvent))
            {
                ((ButtonControl)device["triggerPressed"]).WriteValueIntoEvent(0f, releaseEvent);
                InputSystem.QueueEvent(releaseEvent);
            }
            yield return null; yield return null;
            Assert.AreEqual(FlowState.Baseline, controller.Flow.State, "trigger on Start must leave Consent");
            controller.Exit();
        }
        finally
        {
            InputSystem.RemoveDevice(device); InputSystem.RemoveLayout(layout);
            Object.Destroy(anchor); Object.Destroy(manager.gameObject);
        }
    }
}
