using App.Core.Analysis;
using App.Core.Contracts;
using App.Relaxation;
using NUnit.Framework;

namespace App.Tests
{
    public class ForestBridgeTests
    {
        [Test]
        public void ForestDtoMirrorsTheSanitisedConfig()
        {
            var c = LocalFallbackAnalysis.SceneFor(TestFixtures.Load<SelfReport>("self_report_post_task.json"), null);
            var f = ForestSceneApplier.ToForest(c);
            Assert.IsTrue(f.IsStructurallyValid());
            Assert.AreEqual("sunset", f.time_of_day);
            Assert.AreEqual((float)c.Brightness, f.brightness);
            Assert.AreEqual(c.AnimalsEnabled, f.animals_enabled);
            Assert.AreEqual(0, f.particles.Length);
            Assert.AreEqual((float)c.SoundLayers.Pad, f.sound_layers.pad);
        }

        [Test]
        public void ForestPresetMatchesTheContractPreset()
        {
            var ours = ForestSceneApplier.ToForest(SceneConfigPresets.CalmForestSunset());
            var theirs = VrFatigue.Relaxation.SceneConfig.CalmForestSunset();
            Assert.AreEqual(UnityEngine.JsonUtility.ToJson(theirs), UnityEngine.JsonUtility.ToJson(ours));
        }
    }
}
