using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Forest = VrFatigue.Relaxation;

namespace App.Tests
{
    /// <summary>
    /// The forest owner's SceneConfigMapper against the same fixtures/integration/scene_rules.json as Unity
    /// LocalFallbackAnalysis and the backend. stop_session cases are skipped: the forest mapper has no safety input
    /// (the app applies safety before the scene).
    /// </summary>
    public class ForestMapperRulesTests
    {
        static IEnumerable<TestCaseData> Cases()
        {
            foreach (JObject c in JArray.Parse(TestFixtures.Read("integration/scene_rules.json")))
                if ((string)c["safety_notice"] != "stop_session")
                    yield return new TestCaseData(c).SetName("ForestMapper_" + c["name"]);
        }

        [TestCaseSource(nameof(Cases))]
        public void ForestMapperMatchesSharedRules(JObject c)
        {
            var chips = c["chips"].Select(t => (string)t);
            Forest.Preferences prefs = null;
            if (c["preferences"].Type != JTokenType.Null)
            {
                var p = (JObject)c["preferences"];
                prefs = new Forest.Preferences
                {
                    animals_enabled = (bool)p["animals_enabled"], time_of_day = (string)p["time_of_day"],
                    sound = (string)p["sound"], motion = (string)p["motion"],
                };
            }
            var actual = JObject.Parse(JsonUtility.ToJson(Forest.SceneConfigMapper.Build(Forest.SceneConfig.CalmForestSunset(), chips, prefs)));
            var expected = c["expected_fixture"] != null ? JToken.Parse(TestFixtures.Read((string)c["expected_fixture"])) : c["expected"];
            AssertSubset(actual, expected, (string)c["name"]);
        }

        static void AssertSubset(JToken actual, JToken expected, string path)
        {
            if (expected is JObject fields)
            {
                foreach (var f in fields.Properties()) AssertSubset(actual[f.Name], f.Value, path + "." + f.Name);
            }
            else if (expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float)
                Assert.AreEqual(expected.Value<double>(), actual.Value<double>(), 1e-5, path);
            else Assert.IsTrue(JToken.DeepEquals(expected, actual), $"{path}: expected {expected}, got {actual}");
        }

        [Test]
        public void ForestRestoresRenderSettingsOnUnload()
        {
            RenderSettings.fog = false;
            RenderSettings.fogDensity = 0.001f;
            var go = new GameObject("applier");
            var applier = go.AddComponent<Forest.SceneConfigApplier>();
            applier.CaptureRenderSettings(); // Awake does not run in EditMode
            RenderSettings.fog = true; // what the forest does while running
            RenderSettings.fogDensity = 0.03f;
            applier.RestoreRenderSettings();
            Object.DestroyImmediate(go);
            Assert.IsFalse(RenderSettings.fog);
            Assert.AreEqual(0.001f, RenderSettings.fogDensity, 1e-6);
        }
    }
}
