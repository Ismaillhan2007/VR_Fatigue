using App.Core.Contracts;
using App.Core.Scene;
using UnityEngine;
using Forest = VrFatigue.Relaxation;

namespace App.Relaxation
{
    /// <summary>
    /// ISceneApplier seam for the forest owner's SceneConfigApplier (VrFatigue.Relaxation). Receives the already
    /// sanitised v1 config, copies it field by field into the forest DTO and applies it. Lives on the same object as
    /// SceneConfigApplier in ForestDemo; RelaxationSceneLoader finds it after the additive load.
    /// </summary>
    [RequireComponent(typeof(Forest.SceneConfigApplier))]
    public sealed class ForestSceneApplier : MonoBehaviour, ISceneApplier
    {
        public void Apply(SceneConfig config)
        {
            var c = SceneConfigSanitizer.Sanitize(config);
            GetComponent<Forest.SceneConfigApplier>().Apply(ToForest(c));
        }

        public static Forest.SceneConfig ToForest(SceneConfig c) => new Forest.SceneConfig
        {
            animals_enabled = c.AnimalsEnabled,
            time_of_day = c.TimeOfDay == TimeOfDay.Day ? "day" : "sunset",
            light_warmth = (float)c.LightWarmth,
            brightness = (float)c.Brightness,
            fog_density = (float)c.FogDensity,
            sound = c.Sound == SoundMode.Silent ? "silent" : "forest",
            master_volume = (float)c.MasterVolume,
            sound_layers = new Forest.SoundLayers
            {
                wind = (float)c.SoundLayers.Wind, birds = (float)c.SoundLayers.Birds,
                water = (float)c.SoundLayers.Water, pad = (float)c.SoundLayers.Pad,
            },
            motion = c.Motion == MotionMode.Gentle ? "gentle" : "none",
            motion_speed = (float)c.MotionSpeed,
            particles = c.Particles.ConvertAll(p => p == Particle.Pollen ? "pollen" : "fireflies").ToArray(),
            orb = new Forest.OrbConfig { enabled = c.Orb.Enabled, breath_seconds = (float)c.Orb.BreathSeconds },
        };
    }
}
