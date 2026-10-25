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
        /// <summary>Head tracking must be stable this long before the start screen is placed.</summary>
        public const float PlacementSettleSeconds = 0.5f;
        OVRDisplay _display;
        InputAction _pause;
        bool _placed;
        float _trackedFor;

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
            // UI and its XRUIInputModule first: the rays register with that module when they are enabled.
            owner.Presentation = root.AddComponent<QuestAssessmentPresentation>();
            owner.Presentation.Configure(controller, owner.Head);
            root.AddComponent<QuestEyeSetupPanel>().Configure(controller);
            ConfigureRay(rig.leftControllerAnchor, "LeftHand");
            ConfigureRay(rig.rightControllerAnchor, "RightHand");
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
            var press = new InputAction("UI press", InputActionType.Button, $"<XRController>{{{hand}}}/triggerPressed");
            press.AddBinding($"<XRController>{{{hand}}}/{{TriggerButton}}"); // same trigger through its standard usage
            ray.uiPressInput = new XRInputButtonReader("UI press", inputSourceMode: XRInputButtonReader.InputSourceMode.InputAction)
            { inputActionPerformed = press };
            ray.uiScrollInput = new XRInputValueReader<Vector2>("UI scroll", XRInputValueReader.InputSourceMode.InputAction)
            { inputAction = new InputAction("UI scroll", InputActionType.Value, $"<XRController>{{{hand}}}/primary2DAxis", expectedControlType: "Vector2") };
            var visual = go.AddComponent<XRInteractorLineVisual>();
            go.GetComponent<LineRenderer>().sharedMaterial = Resources.Load<Material>("QuestRayMaterial");
            visual.lineWidth = 0.003f;
            visual.smoothMovement = false;
            visual.stopLineAtFirstRaycastHit = true;
            go.SetActive(true);
        }

        // Re-place the start screen whenever the flow returns to Consent (new session) or the headset is put on.
        // Subscribed at runtime only: SessionController creates its Flow in Awake.
        void Start()
        {
            if (Controller != null && Controller.Flow != null) Controller.Flow.StateChanged += HandleStateChanged;
            OVRManager.HMDMounted += RequestPlacement;
        }

        void HandleStateChanged(FlowState previous, FlowState next)
        {
            if (next == FlowState.Consent) RequestPlacement();
        }

        void Update()
        {
            if (OVRManager.display != _display)
            {
                if (_display != null) _display.RecenteredPose -= HandleRecenter;
                _display = OVRManager.display;
                if (_display != null) _display.RecenteredPose += HandleRecenter;
            }
        }

        // After OVRCameraRig has moved the head anchor this frame; wait for stable tracking so the start screen
        // lands where the person is actually looking, not where the headset pointed while being put on.
        void LateUpdate()
        {
            if (_placed || Head == null) return;
            var tracked = InputDevices.GetDeviceAtXRNode(XRNode.Head)
                .TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var isTracked) && isTracked;
            _trackedFor = tracked ? _trackedFor + Time.unscaledDeltaTime : 0;
            if (_trackedFor < PlacementSettleSeconds) return;
            Presentation.PlaceInFrontOfHead();
            _placed = true;
        }

        /// <summary>Places the panel in view again once tracking is stable, except during a measured phase.</summary>
        public void RequestPlacement()
        {
            var state = Controller != null && Controller.Flow != null ? Controller.Flow.State : FlowState.Consent;
            if (state == FlowState.Baseline || state == FlowState.LoadTask) return; // same conditions for both phases
            _placed = false;
            _trackedFor = 0;
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
            OVRManager.HMDMounted -= RequestPlacement;
            if (Controller != null && Controller.Flow != null) Controller.Flow.StateChanged -= HandleStateChanged;
            if (_display != null) _display.RecenteredPose -= HandleRecenter;
            _pause?.Dispose();
        }
    }
}
