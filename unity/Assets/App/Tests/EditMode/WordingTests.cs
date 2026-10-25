using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using App.Core.Metrics;
using NUnit.Framework;

namespace App.Tests
{
    /// <summary>CONCEPT 5.3: locally generated text must not claim fatigue detection, diagnosis or accuracy.</summary>
    public class WordingTests
    {
        static readonly string[] Forbidden = { "fatigue score", "you are fatigued", "detected", "diagnos", "clinical", "improves health", "%" };

        static IEnumerable<string> AllLocalStrings()
        {
            foreach (Reconciliation r in Enum.GetValues(typeof(Reconciliation)))
            foreach (DataSource d in Enum.GetValues(typeof(DataSource)))
                yield return ReportText.FallbackNarrative(r, d);
            foreach (ObservationCode c in Enum.GetValues(typeof(ObservationCode))) yield return ReportText.ObservationLabel(c);
            foreach (var l in ReportText.Limitations(TestFixtures.Load<SessionSummary>("session_synthetic_insufficient.json"))) yield return l;
            yield return ReportText.IndicatorLabel;
            yield return ReportText.IndicatorDisclaimer;
            yield return BaselinePolicy.LowConfidenceWording;
            yield return SafetyRules.BreakMessage;
            yield return ProvenanceLabels.Data(new SessionContext { DataSource = DataSource.Synthetic });
        }

        [Test]
        public void NoForbiddenClaims()
        {
            foreach (var s in AllLocalStrings())
            foreach (var f in Forbidden)
                Assert.IsFalse(s.ToLowerInvariant().Contains(f), $"'{f}' found in: {s}");
        }

        [Test]
        public void NarrativesFitTheContract() =>
            Assert.IsTrue(AllLocalStrings().All(s => s.Length <= 600));
    }
}
