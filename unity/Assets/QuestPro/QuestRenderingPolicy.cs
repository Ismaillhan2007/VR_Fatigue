using System;

namespace VRFatigue.QuestPro
{
    public enum QuestFoveationMode { Off, Fixed, EyeTracked, Unavailable }

    // Deterministic policy; no permission prompts or native calls here.
    public sealed class QuestRenderingPolicy
    {
        public const float RequestedDisplayHz = 90;
        public bool Consent { get; private set; }
        public bool AssessmentActive { get; private set; }
        private QuestFoveationMode lockedMode;
        private float lockedHz;

        public void SetConsent(bool consent) { Consent = consent; }

        public QuestFoveationMode Choose(bool fixedSupported, bool eyeSupported, bool permissionGranted)
        {
            if (!fixedSupported) return QuestFoveationMode.Off;
            if (AssessmentActive)
            {
                // An eye-tracked session cannot continue after permission/consent is lost.
                if (lockedMode == QuestFoveationMode.EyeTracked &&
                    (!Consent || !permissionGranted || !eyeSupported)) return QuestFoveationMode.Fixed;
                return lockedMode;
            }
            return Consent && permissionGranted && eyeSupported
                ? QuestFoveationMode.EyeTracked : QuestFoveationMode.Fixed;
        }

        public bool BeginAssessment(QuestFoveationMode verifiedMode, float verifiedHz)
        {
            if (AssessmentActive || verifiedMode == QuestFoveationMode.Unavailable ||
                !Finite(verifiedHz) || verifiedHz <= 0) return false;
            lockedMode = verifiedMode; lockedHz = verifiedHz; AssessmentActive = true;
            return true;
        }

        public bool ConditionsMatch(QuestFoveationMode actualMode, float actualHz)
        {
            return !AssessmentActive || (actualMode == lockedMode && Finite(actualHz) &&
                Math.Abs(actualHz - lockedHz) < 0.1f);
        }

        public void EndAssessment() { AssessmentActive = false; }

        public static bool Supports90Hz(float[] rates)
        {
            if (rates == null) return false;
            foreach (var hz in rates) if (Finite(hz) && Math.Abs(hz - RequestedDisplayHz) < 0.01f) return true;
            return false;
        }

        public static bool CadenceReady(float[] available, float actualHz)
        {
            if (available == null || !Finite(actualHz) || actualHz <= 0) return false;
            if (Supports90Hz(available) && Math.Abs(actualHz - RequestedDisplayHz) >= 0.1f) return false;
            foreach (var hz in available) if (Finite(hz) && Math.Abs(hz - actualHz) < 0.1f) return true;
            return false;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
