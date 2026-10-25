using System;
using App.Capture;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using VRFatigue.QuestPro;

namespace App.Quest
{
    /// <summary>One native acquisition owner per app session; rendering remains owned by the existing controller.</summary>
    public static class QuestNativeEyeFactory
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            EyeProviderFactory.HardwareFactory = Application.platform == RuntimePlatform.Android && !Application.isEditor
                ? Create : (Func<HardwareEyeProvider>)null;
        }

        public static HardwareEyeProvider Create()
        {
            if (UnityEngine.Object.FindAnyObjectByType<QuestProCaptureController>() != null ||
                UnityEngine.Object.FindAnyObjectByType<NativeSessionGuard>() != null)
                throw new InvalidOperationException("A separate Quest capture controller already owns acquisition");
            var owner = new GameObject("Quest native eye session").AddComponent<NativeSessionGuard>();
            return owner.Create();
        }

        sealed class NativeSessionGuard : MonoBehaviour
        {
            HardwareEyeProvider _provider;
            QuestProRenderingController _rendering;
            OVRDisplay _display;
            RenderState _state;
            bool _locked;
            public HardwareEyeProvider Create()
            {
                _rendering = QuestProRenderingController.EnsureInstalled();
                _provider = new HardwareEyeProvider(new MetaQuestEyeSource(),
                    () => MetaQuestEyeSource.RequestPermissionAfterConsent(true),
                    () =>
                    {
                        if (_rendering == null || !_rendering.TryBeginAssessment()) return false;
                        _state = RenderState.Read(); _locked = true; return true;
                    },
                    () => { _locked = false; _rendering?.EndAssessment(); },
                    consent => _rendering?.SetParticipantConsent(consent),
                    () => { if (this != null) Destroy(gameObject); });
                if (_rendering != null) _rendering.AssessmentInvalidated += RenderingChanged;
                return _provider;
            }
            void Update()
            {
                if (_locked && !_state.Equals(RenderState.Read())) RenderingChanged();
                if (_display == OVRManager.display) return;
                if (_display != null) _display.RecenteredPose -= Recentered;
                _display = OVRManager.display;
                if (_display != null) _display.RecenteredPose += Recentered;
            }
            void RenderingChanged() => _provider?.Abort("rendering_conditions_changed");
            void Recentered() => _provider?.Abort("recentered");
            void OnApplicationFocus(bool focused) { if (!focused) _provider?.Abort("focus_lost"); }
            void OnApplicationPause(bool paused) { if (paused) _provider?.Abort("application_paused"); }
            void OnDisable() { _provider?.Abort("component_disabled"); }
            void OnDestroy()
            {
                if (_display != null) _display.RecenteredPose -= Recentered;
                if (_rendering != null) _rendering.AssessmentInvalidated -= RenderingChanged;
                _provider?.Dispose();
            }
        }

        // Snapshot stimulus-related state across baseline, the inter-phase check-in and task. The existing
        // rendering controller separately locks actual cadence and foveation. No render settings are repaired here.
        readonly struct RenderState : IEquatable<RenderState>
        {
            readonly float _scale, _viewport, _ambientIntensity, _fogDensity, _urpScale;
            readonly int _urpMsaa;
            readonly Color _ambient, _fogColor;
            readonly bool _fog;
            readonly int _quality, _aa;
            readonly Material _sky;
            readonly RenderPipelineAsset _pipeline;
            RenderState(bool unused)
            {
                _scale = XRSettings.eyeTextureResolutionScale; _viewport = XRSettings.renderViewportScale;
                _ambientIntensity = RenderSettings.ambientIntensity; _ambient = RenderSettings.ambientLight;
                _fogDensity = RenderSettings.fogDensity; _fogColor = RenderSettings.fogColor; _fog = RenderSettings.fog;
                _quality = QualitySettings.GetQualityLevel(); _aa = QualitySettings.antiAliasing;
                _sky = RenderSettings.skybox; _pipeline = GraphicsSettings.currentRenderPipeline;
                var urp = _pipeline as UniversalRenderPipelineAsset;
                _urpScale = urp != null ? urp.renderScale : 1;
                _urpMsaa = urp != null ? urp.msaaSampleCount : 1;
            }
            public static RenderState Read() => new RenderState(true);
            public bool Equals(RenderState other) => _scale == other._scale && _viewport == other._viewport &&
                _ambientIntensity == other._ambientIntensity && _ambient == other._ambient && _fogDensity == other._fogDensity &&
                _fogColor == other._fogColor && _fog == other._fog && _quality == other._quality && _aa == other._aa &&
                _sky == other._sky && _pipeline == other._pipeline && _urpScale == other._urpScale && _urpMsaa == other._urpMsaa;
        }
    }
}
