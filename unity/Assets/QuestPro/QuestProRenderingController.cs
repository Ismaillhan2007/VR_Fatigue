// Cadence and Meta ETFR activation adapted from QP_source_2.10.4
// EyeTrackedFoveationGfrActivation.cs, Copyright 2024 Texas State University.
// PolyForm Noncommercial 1.0.0; required notices: docs/licenses/ETRA2027-QP-LICENSE.md.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace VRFatigue.QuestPro
{
    // One persistent owner, installed only in an Android player. This renders using the
    // runtime's gaze; it neither samples nor records gaze and never requests permission.
    public sealed class QuestProRenderingController : MonoBehaviour
    {
        public static QuestProRenderingController Instance { get; private set; }
        public string Status { get; private set; } = "waiting_for_runtime";
        public QuestFoveationMode ActualMode { get; private set; } = QuestFoveationMode.Unavailable;
        public float ActualDisplayHz { get; private set; }
        public bool SupportsRequestedDisplayHz { get; private set; }
        public event Action AssessmentInvalidated;
        private readonly QuestRenderingPolicy policy = new QuestRenderingPolicy();
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private double nextPoll;
        private bool verified, ownsRendering;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() { EnsureInstalled(); }

        public static QuestProRenderingController EnsureInstalled()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android) return null;
            if (Instance == null)
            {
                var owner = new GameObject(nameof(QuestProRenderingController));
                owner.AddComponent<QuestProRenderingController>();
                DontDestroyOnLoad(owner);
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void SetParticipantConsent(bool consent)
        {
            policy.SetConsent(consent);
            if (!consent) DisableEyeFoveation();
            Tick(); // Withdrawal disables ETFR immediately, including during an assessment.
        }

        public bool TryBeginAssessment()
        {
            Tick();
            return isActiveAndEnabled && verified && policy.BeginAssessment(ActualMode, ActualDisplayHz);
        }

        public void EndAssessment() { policy.EndAssessment(); }

        private void Update()
        {
            if (Time.realtimeSinceStartupAsDouble < nextPoll) return;
            nextPoll = Time.realtimeSinceStartupAsDouble + 0.25;
            Tick();
        }

        private bool RuntimeReady()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android ||
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan ||
                !(XRGeneralSettings.Instance?.Manager?.activeLoader is OpenXRLoader)) return false;
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays) if (display.running) return true;
            return false;
        }

        private void Tick()
        {
            verified = false;
            if (!isActiveAndEnabled) { Unavailable("controller_disabled"); return; }
#if VR_FATIGUE_META_207
            if (!RuntimeReady() || OVRManager.instance == null)
            { Unavailable("waiting_for_quest_openxr_vulkan"); return; }
            var settings = OpenXRSettings.Instance;
            if (settings == null || settings.foveatedRenderingApi != OpenXRSettings.BackendFovationApi.Legacy ||
                settings.GetFeature<Meta.XR.MetaXRFoveationFeature>() == null ||
                settings.GetFeature<FoveatedRenderingFeature>()?.enabled == true)
            { Unavailable("incompatible_foveation_settings"); return; }
            try
            {
                Application.targetFrameRate = 90;
                QualitySettings.vSyncCount = 0;
                var availableRates = OVRPlugin.systemDisplayFrequenciesAvailable;
                SupportsRequestedDisplayHz = QuestRenderingPolicy.Supports90Hz(availableRates);
                // Do not change display cadence after an assessment has started.
                if (!policy.AssessmentActive && SupportsRequestedDisplayHz &&
                    Math.Abs(OVRPlugin.systemDisplayFrequency - 90) >= 0.01f)
                    OVRPlugin.systemDisplayFrequency = 90;

                // OpenXR disables unsupported features at initialization. A failed eye
                // feature is a normal FFR fallback, not a fatal configuration error.
                bool eyeSupported = settings.GetFeature<Meta.XR.MetaXREyeTrackedFoveationFeature>()?.enabled == true &&
                    OVRManager.eyeTrackedFoveatedRenderingSupported;
                bool fixedSupported = settings.GetFeature<Meta.XR.MetaXRFoveationFeature>().enabled &&
                    OVRManager.fixedFoveatedRenderingSupported;
                bool permission = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.EyeTracking);
                bool focused = Application.isFocused && OVRManager.hasVrFocus && OVRManager.hasInputFocus;
                // Observe drift before writing settings; a repair must not hide changed stimuli.
                if (policy.AssessmentActive)
                {
                    var observedLevel = OVRManager.foveatedRenderingLevel;
                    var observedMode = observedLevel == OVRManager.FoveatedRenderingLevel.Off
                        ? QuestFoveationMode.Off : eyeSupported && OVRManager.eyeTrackedFoveatedRenderingEnabled
                            ? QuestFoveationMode.EyeTracked : QuestFoveationMode.Fixed;
                    bool expectedLevel = observedLevel == OVRManager.FoveatedRenderingLevel.Off ||
                        observedLevel == OVRManager.FoveatedRenderingLevel.Medium;
                    if (!focused || !expectedLevel || OVRManager.useDynamicFoveatedRendering ||
                        !policy.ConditionsMatch(observedMode, OVRPlugin.systemDisplayFrequency))
                    {
                        InvalidateAssessment();
                        SetStatus("assessment_invalidated_rendering_changed");
                        return;
                    }
                }
                var desired = focused ? policy.Choose(fixedSupported,
                    eyeSupported, permission) : QuestFoveationMode.Off;
                ownsRendering = true;
                // Only one backend controls level/mode. No dynamic changes during baseline/task.
                if (OVRManager.useDynamicFoveatedRendering) OVRManager.useDynamicFoveatedRendering = false;
                if (eyeSupported && OVRManager.eyeTrackedFoveatedRenderingEnabled != (desired == QuestFoveationMode.EyeTracked))
                    OVRManager.eyeTrackedFoveatedRenderingEnabled = desired == QuestFoveationMode.EyeTracked;
                var level = desired == QuestFoveationMode.Off
                    ? OVRManager.FoveatedRenderingLevel.Off : OVRManager.FoveatedRenderingLevel.Medium;
                if (OVRManager.foveatedRenderingLevel != level) OVRManager.foveatedRenderingLevel = level;
                ActualDisplayHz = OVRPlugin.systemDisplayFrequency;
                bool levelMatches = OVRManager.foveatedRenderingLevel == level && !OVRManager.useDynamicFoveatedRendering;
                ActualMode = OVRManager.foveatedRenderingLevel == OVRManager.FoveatedRenderingLevel.Off
                    ? QuestFoveationMode.Off : eyeSupported && OVRManager.eyeTrackedFoveatedRenderingEnabled
                        ? QuestFoveationMode.EyeTracked : QuestFoveationMode.Fixed;
                verified = focused && levelMatches && ActualMode == desired &&
                    QuestRenderingPolicy.CadenceReady(availableRates, ActualDisplayHz);
                if (!verified || !policy.ConditionsMatch(ActualMode, ActualDisplayHz)) InvalidateAssessment();
                SetStatus($"mode={ActualMode} verified={verified} level={OVRManager.foveatedRenderingLevel} " +
                    $"dynamic={OVRManager.useDynamicFoveatedRendering} displayHz={ActualDisplayHz:R} " +
                    $"requestedHz=90 supported90={SupportsRequestedDisplayHz}");
            }
            catch (Exception error) { Unavailable("runtime_error_" + error.GetType().Name); }
#else
            Unavailable("meta_sdk_version_unsupported");
#endif
        }

        private void Unavailable(string reason)
        {
            DisableEyeFoveation();
            ActualMode = QuestFoveationMode.Unavailable; ActualDisplayHz = 0; SupportsRequestedDisplayHz = false;
            InvalidateAssessment(); SetStatus(reason);
        }

        private void InvalidateAssessment()
        {
            if (!policy.AssessmentActive) return;
            policy.EndAssessment(); AssessmentInvalidated?.Invoke();
        }

        private void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value; Debug.Log("[QuestRendering] " + value);
        }

        private void OnDisable()
        {
            verified = false; InvalidateAssessment();
            ActualMode = QuestFoveationMode.Unavailable; ActualDisplayHz = 0; SupportsRequestedDisplayHz = false;
            SetStatus("controller_disabled");
#if VR_FATIGUE_META_207
            if (ownsRendering)
            {
                try
                {
                    if (OVRManager.eyeTrackedFoveatedRenderingSupported)
                        OVRManager.eyeTrackedFoveatedRenderingEnabled = false;
                    OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.Off;
                }
                catch (Exception error) { SetStatus("stop_error_" + error.GetType().Name); }
            }
#endif
        }
        private void DisableEyeFoveation()
        {
#if VR_FATIGUE_META_207
            if (!ownsRendering) return;
            try
            {
                if (OVRManager.eyeTrackedFoveatedRenderingSupported)
                    OVRManager.eyeTrackedFoveatedRenderingEnabled = false;
            }
            catch (Exception error) { SetStatus("eye_disable_error_" + error.GetType().Name); }
#endif
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
