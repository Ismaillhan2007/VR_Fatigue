using System;
using App.Core.Flow;
using App.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace App.Quest
{
    /// <summary>Android assessment composition only. No eye permission requests, capture, locomotion or cadence owner.</summary>
    public sealed class QuestAssessmentRig : MonoBehaviour
    {
        public Camera Head { get; private set; }
        public QuestAssessmentPresentation Presentation { get; private set; }
        public SessionController Controller { get; private set; }
        OVRDisplay _display;
        InputAction _pause;
        bool _placed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android) return;
            SceneManager.sceneLoaded -= InstallForScene;
            SceneManager.sceneLoaded += InstallForScene;
        }

        static void InstallForScene(Scene scene, LoadSceneMode mode)
        {
            // Assessment is the existing entry scene. The desktop asset remains untouched.
            var controller = FindAnyObjectByType<SessionController>();
            if (controller != null && FindAnyObjectByType<QuestAssessmentRig>() == null)
                Create(controller);
        }

        public static QuestAssessmentRig Create(SessionController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (FindAnyObjectByType<OVRCameraRig>() != null || FindAnyObjectByType<OVRManager>() != null)
                throw new InvalidOperationException("Assessment already has a Meta rig/manager; use one composition path.");
            var root = new GameObject("QuestAssessmentRig");
            var owner = root.AddComponent<QuestAssessmentRig>();
            owner.Controller = controller;
            var rig = root.AddComponent<OVRCameraRig>();
            rig.usePerEyeCameras = false;
            rig.EnsureGameObjectIntegrity();
            // The SDK's startup permission flags default to false. The builder validates them, too.
            var manager = root.AddComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            owner.Head = rig.centerEyeAnchor.GetComponent<Camera>();
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (camera == owner.Head) continue;
                camera.enabled = false;
                camera.tag = "Untagged";
            }
            owner.Head.tag = "MainCamera";
            owner.Head.nearClipPlane = 0.05f;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                listener.enabled = false;
            owner.Head.gameObject.AddComponent<AudioListener>();
            root.AddComponent<XRInteractionManager>();
            ConfigureRay(rig.leftControllerAnchor, "LeftHand");
            ConfigureRay(rig.rightControllerAnchor, "RightHand");
            owner.Presentation = root.AddComponent<QuestAssessmentPresentation>();
            owner.Presentation.Configure(controller, owner.Head);
            root.AddComponent<QuestEyeSetupPanel>().Configure(controller);
            owner._pause = new InputAction("Pause assessment", InputActionType.Button);
            owner._pause.AddBinding("<XRController>{LeftHand}/secondaryButton");
            owner._pause.AddBinding("<XRController>{RightHand}/secondaryButton");
            owner._pause.performed += _ => owner.PauseAndBringPanel();
            owner._pause.Enable();
            return owner;
        }

        static void ConfigureRay(Transform anchor, string hand)
        {
            var go = new GameObject(hand + " UI ray");
            go.SetActive(false);
            go.transform.SetParent(anchor, false);
            var ray = go.AddComponent<XRRayInteractor>();
            ray.maxRaycastDistance = 5;
            ray.referenceFrame = anchor.root;
            ray.raycastMask = 1 << LayerMask.NameToLayer("UI"); // UI only; never move/select the forest or the camera.
            ray.enableUIInteraction = true;
            ray.uiPressInput = new XRInputButtonReader("UI press", inputSourceMode: XRInputButtonReader.InputSourceMode.InputAction)
            { inputActionPerformed = new InputAction("UI press", InputActionType.Button, $"<XRController>{{{hand}}}/triggerPressed") };
            ray.uiScrollInput = new XRInputValueReader<Vector2>("UI scroll", XRInputValueReader.InputSourceMode.InputAction)
            { inputAction = new InputAction("UI scroll", InputActionType.Value, $"<XRController>{{{hand}}}/primary2DAxis", expectedControlType: "Vector2") };
            var visual = go.AddComponent<XRInteractorLineVisual>();
            go.GetComponent<LineRenderer>().sharedMaterial = Resources.Load<Material>("QuestRayMaterial");
            visual.lineWidth = 0.003f;
            visual.smoothMovement = false;
            visual.stopLineAtFirstRaycastHit = true;
            go.SetActive(true);
        }

        void Update()
        {
            if (OVRManager.display != _display)
            {
                if (_display != null) _display.RecenteredPose -= HandleRecenter;
                _display = OVRManager.display;
                if (_display != null) _display.RecenteredPose += HandleRecenter;
            }
            if (!_placed && Head != null && InputDevices.GetDeviceAtXRNode(XRNode.Head)
                .TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var tracked) && tracked)
            { Presentation.PlaceInFrontOfHead(); _placed = true; }
        }

        public void PauseAndBringPanel()
        {
            if (Controller == null) return;
            Presentation.PlaceInFrontOfHead();
            if (!Controller.Flow.IsPaused) Controller.Flow.Pause();
        }

        public void HandleRecenter()
        {
            Interrupt("View recentered. Start a new baseline before another assessment.");
            Presentation?.PlaceInFrontOfHead();
        }

        public void Interrupt(string reason)
        {
            if (Controller == null) return;
            var state = Controller.Flow.State;
            // Recenter/focus changes between phases invalidate the baseline too; never auto-resume it.
            if (state == FlowState.Baseline || state == FlowState.LoadTask)
                Controller.Exit();
            else if (state != FlowState.Consent && state != FlowState.Done && state != FlowState.Exited)
                Controller.Flow.Pause();
            Presentation?.SetStatus(reason);
        }

        void OnApplicationPause(bool paused) { if (paused) Interrupt("Headset paused. Review the session before continuing."); }
        void OnApplicationFocus(bool focused) { if (!focused) Interrupt("Headset focus lost. Review the session before continuing."); }
        void OnDestroy()
        {
            if (_display != null) _display.RecenteredPose -= HandleRecenter;
            _pause?.Dispose();
        }
    }
}
