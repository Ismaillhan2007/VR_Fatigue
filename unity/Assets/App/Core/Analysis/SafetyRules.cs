using App.Core.Contracts;

namespace App.Core.Analysis
{
    /// <summary>
    /// Local keyword rules for discomfort (CONCEPT 6.4). Not a medical judgement: it only tells the participant to
    /// take the headset off and rest.
    /// </summary>
    public static class SafetyRules
    {
        static readonly string[] Stop = { "nause", "vomit", "faint", "throw up" };
        static readonly string[] Break = { "dizz", "headache", "pain", "hurt", "sick", "unwell" };

        public const string BreakMessage = "Please take the headset off and rest for a while.";

        public static SafetyNotice Evaluate(SelfReport report)
        {
            var text = report?.FreeText?.ToLowerInvariant();
            if (string.IsNullOrEmpty(text)) return SafetyNotice.None;
            foreach (var w in Stop) if (text.Contains(w)) return SafetyNotice.StopSession;
            foreach (var w in Break) if (text.Contains(w)) return SafetyNotice.TakeBreak;
            return SafetyNotice.None;
        }

        public static SafetyNotice Max(SafetyNotice a, SafetyNotice b) => a >= b ? a : b;
    }
}
