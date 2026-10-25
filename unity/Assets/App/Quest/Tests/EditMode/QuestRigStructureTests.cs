using System.Linq;
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
}
