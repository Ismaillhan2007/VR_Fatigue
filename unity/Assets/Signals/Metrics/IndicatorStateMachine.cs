using System;

namespace VRFatigue.Signals
{
    // Pure temporal math on already validated normalized inputs. Production callers use IndicatorEvaluator.
    public sealed class IndicatorStateMachine
    {
        private readonly IndicatorPolicy policy;
        private long? previousEnd;
        private double? smoothed;
        private int highWindows;
        private bool active;
        private string level = "unavailable";

        public IndicatorStateMachine(IndicatorPolicy policy = null) { this.policy = policy ?? new IndicatorPolicy(); }

        public IndicatorState Advance(long startUs, long endUs, double? rawValue, string unavailableReason = null)
        {
            if (startUs < 0 || endUs <= startUs || (previousEnd.HasValue && startUs < previousEnd.Value))
                throw new ArgumentException("Indicator windows must be nonoverlapping and chronological.");
            if (rawValue.HasValue ? (!Direction3.Finite(rawValue.Value) || rawValue < 0 || rawValue > 1 || unavailableReason != null || endUs - startUs != IndicatorPolicy.ExpectedWindowUs)
                : string.IsNullOrWhiteSpace(unavailableReason))
                throw new ArgumentException("Valid inputs require a full 20 s window and a finite value in 0..1; unavailable inputs require a reason.");
            bool gap = previousEnd.HasValue && startUs != previousEnd.Value;
            string reset = !previousEnd.HasValue ? "initial" : gap ? "time_gap" : null;
            string before = level;
            previousEnd = endUs;
            if (gap) ClearValues();
            if (!rawValue.HasValue)
            {
                ClearValues(); level = "unavailable";
                return new IndicatorState { StartUs = startUs, EndUs = endUs, Level = level, PreviousLevel = before,
                    NudgeEvent = "none", StateReset = true, ResetReason = "unavailable_window", UnavailableReason = unavailableReason };
            }
            smoothed = smoothed.HasValue ? policy.Alpha * rawValue.Value + (1 - policy.Alpha) * smoothed.Value : rawValue.Value;
            smoothed = Math.Max(0, Math.Min(1, smoothed.Value));
            level = smoothed < policy.LowThreshold ? "low" : smoothed < policy.HighThreshold ? "moderate" : "high";
            highWindows = smoothed > policy.HighThreshold ? Math.Min(policy.RequiredHighWindows, highWindows + 1) : 0;
            string change = "none";
            if (!active && highWindows >= policy.RequiredHighWindows) { active = true; change = "offer_break"; }
            else if (active && smoothed < policy.ReleaseThreshold) { active = false; change = "released"; }
            return new IndicatorState { StartUs = startUs, EndUs = endUs, RawValue = rawValue, Value = smoothed,
                Level = level, PreviousLevel = before, NudgeActive = active, NudgeEvent = change,
                ConsecutiveHighWindows = highWindows, StateReset = reset != null, ResetReason = reset };
        }

        public void Reset() { previousEnd = null; level = "unavailable"; ClearValues(); }
        private void ClearValues() { smoothed = null; highWindows = 0; active = false; }
    }
}
