using System;
using App.Core.Contracts;

namespace App.Core.Analysis
{
    /// <summary>docs/CONCEPT.md section 6.2. Computed by the app, never by the AI. Words decide the scene.</summary>
    public static class ReconciliationRules
    {
        /// <summary>max(slider) >= 5, or either slider rose by at least 2 over its baseline rating.</summary>
        public static bool WordsHigh(SelfReport post, SelfReport baseline)
        {
            if (post == null) return false;
            if (Math.Max(post.EyeStrain, post.MentalFatigue) >= 5) return true;
            return baseline != null
                && (post.EyeStrain - baseline.EyeStrain >= 2 || post.MentalFatigue - baseline.MentalFatigue >= 2);
        }

        public static Reconciliation Compute(SelfReport post, SelfReport baseline, StrainLevel level)
        {
            if (level == StrainLevel.Unavailable) return Reconciliation.Unavailable;
            var words = WordsHigh(post, baseline);
            var signals = level == StrainLevel.High || level == StrainLevel.Moderate;
            if (words) return signals ? Reconciliation.AgreeHigh : Reconciliation.WordsOnly;
            return signals ? Reconciliation.SignalsOnly : Reconciliation.AgreeLow;
        }
    }
}
