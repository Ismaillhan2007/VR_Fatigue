using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Metrics;
using NUnit.Framework;

namespace App.Tests
{
    public class RulesTests
    {
        static SelfReport R(ReportPhase phase, int eye, int mind, string text = null, params Chip[] chips) =>
            new SelfReport { Phase = phase, EyeStrain = eye, MentalFatigue = mind, Chips = chips.ToList(), FreeText = text };

        [TestCase(5, 2, StrainLevel.Moderate, Reconciliation.AgreeHigh)]
        [TestCase(5, 2, StrainLevel.High, Reconciliation.AgreeHigh)]
        [TestCase(6, 2, StrainLevel.Low, Reconciliation.WordsOnly)]
        [TestCase(2, 2, StrainLevel.High, Reconciliation.SignalsOnly)]
        [TestCase(2, 2, StrainLevel.Low, Reconciliation.AgreeLow)]
        [TestCase(7, 7, StrainLevel.Unavailable, Reconciliation.Unavailable)]
        public void ReconciliationTable(int eye, int mind, StrainLevel level, Reconciliation expected)
        {
            var post = R(ReportPhase.PostTask, eye, mind);
            var baseline = R(ReportPhase.Baseline, 2, 2);
            Assert.AreEqual(expected, ReconciliationRules.Compute(post, baseline, level));
        }

        [Test]
        public void RiseOfTwoOverBaselineCountsAsWordsHigh()
        {
            Assert.IsTrue(ReconciliationRules.WordsHigh(R(ReportPhase.PostTask, 3, 1), R(ReportPhase.Baseline, 1, 1)));
            Assert.IsFalse(ReconciliationRules.WordsHigh(R(ReportPhase.PostTask, 2, 1), R(ReportPhase.Baseline, 1, 1)));
        }

        [TestCase(2, 2, 6, false, false, null)]
        [TestCase(5, 2, 6, false, true, LowConfidenceReason.SelfReportedTired)]
        [TestCase(5, 2, 2, true, true, LowConfidenceReason.SelfReportedTired)]
        [TestCase(2, 2, 5, false, true, LowConfidenceReason.TooShort)]
        [TestCase(2, 2, 6, true, true, LowConfidenceReason.PoorQuality)]
        public void BaselinePolicyPriorities(int eye, int mind, int windows, bool poor, bool low, LowConfidenceReason? reason)
        {
            var (l, r) = BaselinePolicy.Evaluate(R(ReportPhase.Baseline, eye, mind), windows, poor);
            Assert.AreEqual(low, l);
            Assert.AreEqual(reason, r);
        }

        [TestCase(null, SafetyNotice.None)]
        [TestCase("My eyes feel dry and I want something calm.", SafetyNotice.None)]
        [TestCase("I feel a bit dizzy", SafetyNotice.TakeBreak)]
        [TestCase("nauseous and dizzy", SafetyNotice.StopSession)]
        public void SafetyKeywords(string text, SafetyNotice expected) =>
            Assert.AreEqual(expected, SafetyRules.Evaluate(R(ReportPhase.PostTask, 3, 3, text)));
    }
}
