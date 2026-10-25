using System;
using UnityEngine;
using VRFatigue.Signals;

namespace VRFatigue.QuestPro
{
    // No auto-start, raw-data logs, disk writes or automatic source substitution.
    public sealed class QuestProCaptureController : MonoBehaviour
    {
        [SerializeField, Range(1, 600)] private int maximumSeconds = 300;
        public HardwareEyeProvider Provider { get; private set; }
        public event Action<EyeTrackingBatch> BatchReady;
        private QuestProRenderingController rendering;
#if VR_FATIGUE_META_207
        private OVRDisplay boundDisplay;
#endif
        private void Awake() { Initialize(); }
        private void OnEnable() { BindRecenter(); }
        private void Initialize()
        {
            if (Provider == null)
                Provider = new HardwareEyeProvider(new MetaQuestEyeSource(),
                    new HardwareCaptureOptions(maximumDurationUs: (long)Mathf.Clamp(maximumSeconds, 1, 600) * 1000000));
        }
        public bool RequestPermission(bool participantConsent)
        {
            BindRendering(); rendering?.SetParticipantConsent(participantConsent);
            return MetaQuestEyeSource.RequestPermissionAfterConsent(participantConsent);
        }
        public bool BeginCapture(bool participantConsent, bool calibrationConfirmedByOperator)
        {
            Initialize(); BindRecenter();
            BindRendering(); rendering?.SetParticipantConsent(participantConsent);
            if (!Provider.Start(participantConsent, calibrationConfirmedByOperator)) return false;
            if (rendering != null && !rendering.TryBeginAssessment())
            { Provider.Abort("rendering_not_ready"); return false; }
            return true;
        }
        private void Update()
        {
            BindRecenter();
            if (Provider == null || Provider.State != "running") { rendering?.EndAssessment(); return; }
            var batch = Provider.ReadAvailable(Provider.ElapsedUs);
            if (Provider.State != "running") rendering?.EndAssessment();
            if (batch.Samples.Count > 0 || batch.ProbeEvents.Count > 0) BatchReady?.Invoke(batch);
        }
        public bool RecordProbe(string id, string kind, Vector3 headLocalTargetDirection)
        {
            Initialize();
            return Provider.RecordProbe(id, kind, new Direction3(headLocalTargetDirection.x, headLocalTargetDirection.y, headLocalTargetDirection.z));
        }
        public bool TryFinishCapture(out EyeSession session)
        {
            Initialize(); session = null;
            if (Provider.State == "idle" || Provider.State == "disposed") return false;
            var captured = Provider.Finish();
            rendering?.EndAssessment();
            if (Provider.State != "stopped" || Provider.SourceErrorType != null ||
                (Provider.StatusReason != "completed" && Provider.StatusReason != "duration_limit")) return false;
            session = captured;
            return true;
        }
        public string GetAggregateDiagnostics() { Initialize(); return Provider.DiagnosticsJson(); }
        public void ResetParticipant() { Initialize(); Provider.Reset(); rendering?.EndAssessment(); rendering?.SetParticipantConsent(false); }
        public void WithdrawConsent() { Initialize(); Provider.WithdrawConsent(); rendering?.SetParticipantConsent(false); rendering?.EndAssessment(); }
        private void BindRendering()
        {
            var current = QuestProRenderingController.EnsureInstalled();
            if (current == rendering) return;
            UnbindRendering(); rendering = current;
            if (rendering != null) rendering.AssessmentInvalidated += AbortForRenderingChange;
        }
        private void AbortForRenderingChange()
        { if (Provider != null && Provider.State == "running") Provider.Abort("rendering_conditions_changed"); }
        private void UnbindRendering()
        {
            if (rendering == null) return;
            rendering.AssessmentInvalidated -= AbortForRenderingChange;
            rendering.EndAssessment(); rendering.SetParticipantConsent(false); rendering = null;
        }
        public void AbortForRecenter() { if (Provider != null) Provider.Abort("recentered"); }
        private void OnApplicationPause(bool paused) { if (paused && Provider != null) Provider.Abort("application_paused"); }
        private void OnApplicationFocus(bool focused) { if (!focused && Provider != null) Provider.Abort("focus_lost"); }
        private void OnDisable() { UnbindRecenter(); UnbindRendering(); if (Provider != null) Provider.Abort("component_disabled"); }
        private void OnDestroy() { UnbindRecenter(); UnbindRendering(); if (Provider != null) Provider.Dispose(); }
        private void BindRecenter()
        {
#if VR_FATIGUE_META_207
            if (OVRManager.display == boundDisplay) return;
            UnbindRecenter(); boundDisplay = OVRManager.display;
            if (boundDisplay != null) boundDisplay.RecenteredPose += AbortForRecenter;
#endif
        }
        private void UnbindRecenter()
        {
#if VR_FATIGUE_META_207
            if (boundDisplay != null) boundDisplay.RecenteredPose -= AbortForRecenter;
            boundDisplay = null;
#endif
        }
    }
}
