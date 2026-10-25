using System;
using System.Collections.Generic;
using UnityEngine;
using VRFatigue.Signals;

namespace VRFatigue.QuestPro
{
    public sealed class MetaQuestEyeSource : IHardwareEyeSource
    {
        public const string PackageVersion = "207.0.1";
        public string DataSource { get { return "live"; } }
        public string TimestampSource { get { return "ovr_eye_gazes_state.time"; } }
        public string SdkPackageVersion { get { return PackageVersion; } }
        private bool ownsTracking;
#if VR_FATIGUE_META_207
        private OVRPlugin.EyeGazesState eyes;
#endif
        public static string SetupStatus
        {
            get
            {
#if VR_FATIGUE_META_207
                if (Application.isEditor || Application.platform != RuntimePlatform.Android) return "quest_standalone_required";
                if (OVRManager.instance == null) return "ovr_manager_missing";
                return "runtime_check_required";
#else
                return "meta_sdk_version_unsupported";
#endif
            }
        }
        private static bool OnDevice { get { return SetupStatus == "runtime_check_required"; } }
        public HardwareReadiness Readiness
        {
            get
            {
#if VR_FATIGUE_META_207
                if (!OnDevice) return new HardwareReadiness(false, false, false, false);
                return new HardwareReadiness(OVRPlugin.eyeTrackingSupported,
                    OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.EyeTracking),
                    Application.isFocused && OVRManager.hasVrFocus && OVRManager.hasInputFocus, OVRPlugin.eyeTrackingEnabled);
#else
                return new HardwareReadiness(false, false, false, false);
#endif
            }
        }
        public static bool RequestPermissionAfterConsent(bool participantConsent)
        {
            if (!participantConsent || !OnDevice) return false;
#if VR_FATIGUE_META_207
            OVRPermissionsRequester.Request(new List<OVRPermissionsRequester.Permission> { OVRPermissionsRequester.Permission.EyeTracking });
            return true; // The OS decision is asynchronous; check Readiness before Start.
#else
            return false;
#endif
        }
        public bool TryGetClock(out double seconds)
        {
            seconds = 0;
#if VR_FATIGUE_META_207
            if (!OnDevice) return false;
            seconds = OVRPlugin.GetTimeInSeconds();
            // The SDK wrapper returns zero on failure; do not accept it as a valid clock.
            return !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0;
#else
            return false;
#endif
        }
        public bool StartTracking()
        {
#if VR_FATIGUE_META_207
            if (!OnDevice) return false;
            if (OVRPlugin.eyeTrackingEnabled) { ownsTracking = false; return true; }
            ownsTracking = OVRPlugin.StartEyeTracking();
            return ownsTracking;
#else
            return false;
#endif
        }
        public void StopTracking()
        {
#if VR_FATIGUE_META_207
            if (ownsTracking)
            {
                try { if (!OVRPlugin.StopEyeTracking()) throw new InvalidOperationException("Meta eye tracking stop failed."); }
                finally { ownsTracking = false; }
            }
#else
            ownsTracking = false;
#endif
        }
        public bool TryReadFrame(out HardwareEyeFrame frame)
        {
            frame = null;
#if VR_FATIGUE_META_207
            if (!OnDevice || !OVRPlugin.GetEyeGazesState(OVRPlugin.Step.Render, -1, ref eyes) || eyes.EyeGazes == null || eyes.EyeGazes.Length < 2) return false;
            if (double.IsNaN(eyes.Time) || double.IsInfinity(eyes.Time) || eyes.Time < 0)
            {
                frame = new HardwareEyeFrame(eyes.Time, Invalid(eyes.EyeGazes[0]), Invalid(eyes.EyeGazes[1]), null, null);
                return true; // The provider counts the bad timestamp; never pass it to native head-pose lookup.
            }
            var headState = OVRPlugin.GetNodePoseStateAtTime(eyes.Time, OVRPlugin.Node.Head);
            if (!OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.Head) ||
                !OVRPlugin.GetNodeOrientationValid(OVRPlugin.Node.Head) ||
                !OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head) ||
                !OVRPlugin.GetNodePositionValid(OVRPlugin.Node.Head))
            {
                frame = new HardwareEyeFrame(eyes.Time, Invalid(eyes.EyeGazes[0]), Invalid(eyes.EyeGazes[1]), null, null);
                return true;
            }
            // Meta's conversion flips handedness into Unity axes. Never subtract a scene Transform
            // or use a current predicted head pose to compensate an older eye sample.
            OVRPose head = headState.Pose.ToOVRPose();
            var pose = new HeadPose(new Direction3(head.position.x, head.position.y, head.position.z),
                head.orientation.x, head.orientation.y, head.orientation.z, head.orientation.w);
            frame = new HardwareEyeFrame(eyes.Time, Reading(eyes.EyeGazes[0], head.orientation), Reading(eyes.EyeGazes[1], head.orientation), pose, headState.Time);
            return true;
#else
            return false;
#endif
        }
#if VR_FATIGUE_META_207
        private static EyeReading Reading(OVRPlugin.EyeGazeState eye, Quaternion head)
        {
            Vector3 direction;
            if (!eye.IsValid || !QuestGazeGeometry.TryHeadLocalDirection(
                eye.Pose.ToOVRPose().orientation, head, out direction)) return Invalid(eye);
            return new EyeReading(true, new Direction3(direction.x, direction.y, direction.z), eye.Confidence);
        }
        private static EyeReading Invalid(OVRPlugin.EyeGazeState eye) { return new EyeReading(false, null, eye.Confidence); }
#endif
    }
}
