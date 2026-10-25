using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using NUnit.Framework;

namespace App.Tests
{
    public class LocalFallbackTests
    {
        static AnalyzeRequest Request(Preferences prefs = null)
        {
            var s = TestFixtures.Load<SessionSummary>("session_synthetic_usable.json");
            return AnalyzeRequest.Create(s, TestFixtures.Load<SelfReport>("self_report_baseline.json"), prefs);
        }

        [Test]
        public void PostTaskChipsMapToExpectedScene()
        {
            var c = LocalFallbackAnalysis.SceneFor(TestFixtures.Load<SelfReport>("self_report_post_task.json"), null);
            const double eps = 1e-9;
            Assert.AreEqual(0.65, c.Brightness, eps);
            Assert.AreEqual(0.85, c.LightWarmth, eps);
            Assert.AreEqual(0.35, c.FogDensity, eps);
            Assert.AreEqual(0, c.MotionSpeed, eps);
            CollectionAssert.IsEmpty(c.Particles);
            Assert.IsFalse(c.AnimalsEnabled);
            Assert.AreEqual(0, c.SoundLayers.Wind + c.SoundLayers.Birds + c.SoundLayers.Water, eps);
            Assert.AreEqual(0.3, c.SoundLayers.Pad, eps);
            Assert.AreEqual(0.2, c.MasterVolume, eps);
            Assert.AreEqual(SoundMode.Forest, c.Sound);
        }

        [Test]
        public void NoChipsGivesThePreset()
        {
            var c = LocalFallbackAnalysis.SceneFor(TestFixtures.Load<SelfReport>("self_report_baseline.json"), null);
            Assert.AreEqual(ContractJson.Serialize(SceneConfigPresets.CalmForestSunset()), ContractJson.Serialize(c));
        }

        [Test]
        public void DimAndMovementOverrideEarlierRows()
        {
            var r = new SelfReport { Phase = ReportPhase.PostTask, Chips = new[] { Chip.TiredEyes, Chip.WantDim, Chip.WantMovement }.ToList() };
            var c = LocalFallbackAnalysis.SceneFor(r, null);
            Assert.AreEqual(0.5, c.Brightness, 1e-9);
            Assert.AreEqual(0.3, c.MotionSpeed, 1e-9);
        }

        [Test]
        public void ExplicitPreferencesWin()
        {
            var prefs = new Preferences { AnimalsEnabled = true, Sound = SoundMode.Silent, TimeOfDay = TimeOfDay.Day, Motion = MotionMode.None };
            var response = LocalFallbackAnalysis.Analyze(Request(prefs));
            Assert.IsTrue(response.SceneConfig.AnimalsEnabled);
            Assert.AreEqual(SoundMode.Silent, response.SceneConfig.Sound);
            Assert.AreEqual(MotionMode.None, response.SceneConfig.Motion);
        }

        [Test]
        public void FallbackResponseIsValidAndHonestlyLabelled()
        {
            var request = Request();
            var r = LocalFallbackAnalysis.Analyze(request);
            CollectionAssert.IsEmpty(r.Validate());
            Assert.AreEqual(AiSource.Fallback, r.AiSource);
            Assert.IsNull(r.ModelId);
            Assert.AreEqual(Reconciliation.AgreeHigh, r.Reconciliation);
            Assert.IsTrue(r.Observations.Any(o => o.Code == ObservationCode.ProbeSlower));
            StringAssert.Contains("synthetic", r.Narrative);
            CollectionAssert.IsEmpty(AnalysisResponseGuard.Check(request, r));
        }

        [Test]
        public void InsufficientDataReportsQualityOnly()
        {
            var s = TestFixtures.Load<SessionSummary>("session_synthetic_insufficient.json");
            var r = LocalFallbackAnalysis.Analyze(AnalyzeRequest.Create(s, null, null));
            Assert.AreEqual(Reconciliation.Unavailable, r.Reconciliation);
            Assert.AreEqual(ObservationCode.DataQualityLimited, r.Observations.Single().Code);
            CollectionAssert.IsEmpty(r.Validate());
        }

        [Test]
        public void GuardRejectsRelabelledOrWeakenedResponses()
        {
            var request = Request();
            request.SafetyNotice = SafetyNotice.TakeBreak;
            var ok = LocalFallbackAnalysis.Analyze(request);
            CollectionAssert.IsEmpty(AnalysisResponseGuard.Check(request, ok));

            var lowered = LocalFallbackAnalysis.Analyze(request);
            lowered.SafetyNotice = SafetyNotice.None;
            CollectionAssert.IsNotEmpty(AnalysisResponseGuard.Check(request, lowered));

            var relabelled = LocalFallbackAnalysis.Analyze(request);
            relabelled.DataSource = DataSource.Live;
            CollectionAssert.IsNotEmpty(AnalysisResponseGuard.Check(request, relabelled));

            var reconciled = LocalFallbackAnalysis.Analyze(request);
            reconciled.Reconciliation = Reconciliation.AgreeLow;
            CollectionAssert.IsNotEmpty(AnalysisResponseGuard.Check(request, reconciled));

            var liveNoModel = LocalFallbackAnalysis.Analyze(request);
            liveNoModel.AiSource = AiSource.Live;
            CollectionAssert.IsNotEmpty(AnalysisResponseGuard.Check(request, liveNoModel));
        }

        [Test]
        public void RequestNeverCarriesFreeText()
        {
            var summary = TestFixtures.Load<SessionSummary>("session_synthetic_usable.json");
            summary.SelfReport.FreeText = "I feel dizzy";
            var prefs = new Preferences { Text = "something personal" };
            var request = AnalyzeRequest.Create(summary, null, prefs);
            Assert.IsNull(request.SessionSummary.SelfReport.FreeText);
            Assert.IsNull(request.Preferences.Text);
            Assert.AreEqual(SafetyNotice.TakeBreak, request.SafetyNotice, "safety rules still see the local text");
            Assert.AreEqual("I feel dizzy", summary.SelfReport.FreeText, "caller's data stay intact");
            CollectionAssert.IsEmpty(request.Validate());
        }
    }
}
