using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using App.Core.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace App.Tests
{
    public class ContractFixtureTests
    {
        static IEnumerable<string> ValidFiles() =>
            Directory.GetFiles(TestFixtures.Root, "*.json").Select(Path.GetFileName).OrderBy(x => x);

        static IEnumerable<string> InvalidFiles() =>
            Directory.GetFiles(Path.Combine(TestFixtures.Root, "invalid"), "*.json").Select(Path.GetFileName).OrderBy(x => x);

        static IContract ParseFile(string relative)
        {
            var type = TestFixtures.TypeFor(Path.GetFileName(relative));
            Assert.NotNull(type, $"no contract type for {relative}");
            try { return TestFixtures.Parse(type, TestFixtures.Read(relative)); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        [Test]
        public void FixtureFoldersAreNotEmpty()
        {
            Assert.That(ValidFiles().Count(), Is.GreaterThanOrEqualTo(7));
            Assert.That(InvalidFiles().Count(), Is.GreaterThanOrEqualTo(8));
        }

        [TestCaseSource(nameof(ValidFiles))]
        public void ValidFixtureParsesAndValidates(string file)
        {
            var value = ParseFile(file);
            CollectionAssert.IsEmpty(value.Validate());
        }

        [TestCaseSource(nameof(ValidFiles))]
        public void ValidFixtureRoundTripsExactly(string file)
        {
            var value = ParseFile(file);
            var original = JToken.Parse(TestFixtures.Read(file));
            var written = JToken.Parse(ContractJson.Serialize(value));
            Assert.IsTrue(JToken.DeepEquals(original, written), $"round trip differs:\n{written}");
        }

        [TestCaseSource(nameof(InvalidFiles))]
        public void InvalidFixtureIsRejected(string file)
        {
            Assert.Throws<ContractException>(() => ParseFile(Path.Combine("invalid", file)));
        }

        [Test]
        public void OfflinePresetMatchesFixture()
        {
            var fixture = JToken.Parse(TestFixtures.Read("scene_config_calm_forest_sunset.json"));
            var preset = JToken.Parse(ContractJson.Serialize(SceneConfigPresets.CalmForestSunset()));
            Assert.IsTrue(JToken.DeepEquals(fixture, preset));
        }

        [TestCase("analysis_fallback.json")]
        [TestCase("scene_config_calm_forest_sunset.json")]
        [TestCase("replay/demo_session.json")]
        public void StreamingAssetsCopyMatchesRepoFixture(string file)
        {
            var copy = Path.Combine(Application.streamingAssetsPath, "fixtures", file);
            FileAssert.AreEqual(Path.Combine(TestFixtures.Root, file), copy, "run: cp fixtures/" + file + " unity/Assets/StreamingAssets/fixtures/");
        }

        // ----- mutations of a valid session (no new committed files) -----

        static string Usable => TestFixtures.Read("session_synthetic_usable.json");

        static void Rejects<T>(string json) where T : class, IContract =>
            Assert.Throws<ContractException>(() => ContractJson.Parse<T>(json));

        static string Mutate(string json, System.Action<JObject> change)
        {
            var o = JObject.Parse(json);
            change(o);
            return o.ToString();
        }

        [Test] public void RejectsUnknownField() => Rejects<SessionSummary>(Mutate(Usable, o => o["extra"] = 1));
        [Test] public void RejectsMissingRequiredField() => Rejects<SessionSummary>(Mutate(Usable, o => o.Remove("metric_version")));
        [Test] public void RejectsMissingNullableField() => Rejects<SessionSummary>(Mutate(Usable, o => o.Remove("replay_origin")));
        [Test] public void RejectsUnknownEnum() => Rejects<SessionSummary>(Mutate(Usable, o => o["data_source"] = "head_gaze"));
        [Test] public void RejectsIntegerEnum() => Rejects<SessionSummary>(Mutate(Usable, o => o["data_source"] = 2));
        [Test] public void RejectsStringNumber() => Rejects<SessionSummary>(Mutate(Usable, o => o["quality"]["valid_coverage"] = "0.93"));
        [Test] public void RejectsFractionalInteger() => Rejects<SessionSummary>(Mutate(Usable, o => o["duration_ms"] = 1.5));
        [Test] public void RejectsNaN() => Rejects<SessionSummary>(Usable.Replace("\"valid_coverage\": 0.93", "\"valid_coverage\": NaN"));
        [Test] public void RejectsDuplicateKey() => Rejects<SessionSummary>(Usable.Replace("\"duration_ms\": 300000,", "\"duration_ms\": 300000, \"duration_ms\": 1,"));
        [Test] public void RejectsBadSessionId() => Rejects<SessionSummary>(Mutate(Usable, o => o["session_id"] = "participant-42"));
        [Test] public void RejectsDeviationOutOfRange() => Rejects<SessionSummary>(Mutate(Usable, o => o["deviations"]["probe_latency_ms"] = 9));
        [Test] public void RejectsReplayWithoutOrigin() => Rejects<SessionSummary>(Mutate(Usable, o => o["data_source"] = "replay"));
        [Test] public void RejectsOriginWithoutReplay() => Rejects<SessionSummary>(Mutate(Usable, o => o["replay_origin"] = "recorded"));
        [Test] public void RejectsWrongIndicatorKind() => Rejects<SessionSummary>(Mutate(Usable, o => o["strain_indicator"]["kind"] = "fatigue_score"));
        [Test] public void RejectsDuplicateChips() => Rejects<SessionSummary>(Mutate(Usable, o => o["self_report"]["chips"] = new JArray("tense", "tense")));
        [Test] public void RejectsLongFreeText() => Rejects<SelfReport>(Mutate(TestFixtures.Read("self_report_post_task.json"), o => o["free_text"] = new string('a', 301)));
        [Test] public void RejectsNonObjectRoot() => Rejects<SessionSummary>("[1,2]");

        [Test]
        public void AcceptsReplayWithOrigin()
        {
            var s = ContractJson.Parse<SessionSummary>(Mutate(Usable, o => { o["data_source"] = "replay"; o["replay_origin"] = "synthetic"; }));
            Assert.AreEqual(ReplayOrigin.Synthetic, s.ReplayOrigin);
        }

        [Test]
        public void FallbackNamingModelIsRejected() =>
            Rejects<AnalysisResponse>(Mutate(TestFixtures.Read("analysis_fallback.json"), o => o["model_id"] = "gemini"));

        [Test]
        public void SerializeRefusesInvalidValues()
        {
            var c = SceneConfigPresets.CalmForestSunset();
            c.Brightness = double.NaN;
            Assert.Throws<ContractException>(() => ContractJson.Serialize(c));
        }

        // ----- clamp -----

        [Test]
        public void ClampBringsOutOfRangeFixtureIntoBounds()
        {
            var raw = JsonConvert.DeserializeObject<SceneConfig>(TestFixtures.Read("invalid/scene_config_out_of_range.json"));
            CollectionAssert.IsNotEmpty(raw.Validate());
            var c = raw.Clamped();
            CollectionAssert.IsEmpty(c.Validate());
            Assert.AreEqual(0.4, c.Brightness);
            Assert.AreEqual(0.6, c.FogDensity);
            Assert.AreEqual(0.5, c.MotionSpeed);
        }

        [Test]
        public void ClampReplacesNonFiniteAndDedupesParticles()
        {
            var c = SceneConfigPresets.CalmForestSunset();
            c.LightWarmth = double.PositiveInfinity;
            c.Orb.BreathSeconds = double.NaN;
            c.Particles = new List<Particle> { Particle.Pollen, Particle.Pollen, (Particle)99, Particle.Fireflies };
            c.SceneId = "other";
            var k = c.Clamped();
            CollectionAssert.IsEmpty(k.Validate());
            Assert.AreEqual(0.7, k.LightWarmth);
            Assert.AreEqual(5.5, k.Orb.BreathSeconds);
            CollectionAssert.AreEqual(new[] { Particle.Pollen, Particle.Fireflies }, k.Particles);
            Assert.AreEqual(SceneConfig.ForestSceneId, k.SceneId);
        }

        [Test]
        public void PreferencesWinInSanitize()
        {
            var prefs = new Preferences { AnimalsEnabled = false, Sound = SoundMode.Silent, Motion = MotionMode.None, TimeOfDay = TimeOfDay.Day };
            var c = SceneConfigSanitizer.Sanitize(SceneConfigPresets.CalmForestSunset(), prefs);
            Assert.IsFalse(c.AnimalsEnabled);
            Assert.AreEqual(SoundMode.Silent, c.Sound);
            Assert.AreEqual(MotionMode.None, c.Motion);
            Assert.AreEqual(0, c.MotionSpeed);
            Assert.AreEqual(TimeOfDay.Day, c.TimeOfDay);
        }
    }
}
