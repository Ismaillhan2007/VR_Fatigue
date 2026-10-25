using System.Collections.Generic;
using UnityEngine;

namespace VrFatigue.Relaxation
{
    // Deterministic, offline mapping from self-report chips and explicit preferences to a SceneConfig
    // (docs/CONCEPT.md section 7.3). Order: base preset -> chip modifiers -> explicit preferences -> clamp.
    public static class SceneConfigMapper
    {
        public static SceneConfig Build(SceneConfig basePreset, IEnumerable<string> chips, Preferences prefs)
        {
            var c = (basePreset ?? SceneConfig.CalmForestSunset()).Clone();
            var set = new HashSet<string>(chips ?? new string[0]);

            if (set.Contains("tired_eyes"))
            {
                c.brightness -= 0.15f;
                c.light_warmth += 0.15f;
                c.fog_density += 0.1f;
                c.motion_speed = 0f;
            }
            if (set.Contains("overstimulated"))
            {
                c.particles = new string[0];
                c.animals_enabled = false;
                c.sound_layers = new SoundLayers { wind = 0f, birds = 0f, water = 0f, pad = c.sound_layers.pad };
            }
            if (set.Contains("tense") || set.Contains("restless"))
            {
                c.orb.enabled = true;
                c.orb.breath_seconds = 6f;
            }
            if (set.Contains("low_energy"))
            {
                c.time_of_day = "day";
                c.sound_layers.birds += 0.2f;
                c.motion = "gentle";
            }
            if (set.Contains("want_quiet"))
            {
                // CONCEPT allows "silent, or master_volume 0.2". The chip is softer than the explicit
                // Preferences.sound = silent choice, so it caps the volume instead of muting.
                c.master_volume = Mathf.Min(c.master_volume, 0.2f);
            }
            if (set.Contains("want_dim")) c.brightness = 0.5f;
            if (set.Contains("want_nature"))
            {
                c.sound_layers.birds += 0.2f;
                c.sound_layers.water += 0.2f;
            }
            if (set.Contains("want_movement"))
            {
                c.motion = "gentle";
                c.motion_speed = 0.3f;
            }

            ApplyPreferences(c, prefs);

            // Quiet and animal-free always win, including over the chips above.
            if (c.sound == "silent")
            {
                c.master_volume = 0f;
                c.sound_layers = new SoundLayers();
            }
            c.Clamp();
            return c;
        }

        // Explicit participant choices override every earlier step. Unknown values are ignored.
        public static void ApplyPreferences(SceneConfig c, Preferences p)
        {
            if (p == null) return;
            c.animals_enabled = p.animals_enabled;
            if (p.time_of_day == "day" || p.time_of_day == "sunset") c.time_of_day = p.time_of_day;
            if (p.sound == "forest" || p.sound == "silent") c.sound = p.sound;
            if (p.motion == "none" || p.motion == "gentle")
            {
                c.motion = p.motion;
                if (p.motion == "none") c.motion_speed = 0f;
            }
        }
    }
}
