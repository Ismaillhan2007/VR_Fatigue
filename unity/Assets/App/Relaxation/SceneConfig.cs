using System;
using UnityEngine;

namespace VrFatigue.Relaxation
{
    // Mirrors contracts/v1/scene_config.schema.json (v1.0). Do not add or rename fields without approval.
    // Field names are snake_case on purpose so JsonUtility can read the contract JSON directly.
    [Serializable]
    public class SoundLayers
    {
        public float wind;
        public float birds;
        public float water;
        public float pad;
    }

    [Serializable]
    public class OrbConfig
    {
        public bool enabled;
        public float breath_seconds = 6f;
    }

    [Serializable]
    public class SceneConfig
    {
        public const string SchemaVersion = "1.0";
        public const string ForestSceneId = "forest_v1";

        public string schema_version = SchemaVersion;
        public string scene_id = ForestSceneId;
        public bool animals_enabled;
        public string time_of_day = "day";   // day | sunset
        public float light_warmth;           // 0..1
        public float brightness = 1f;        // 0.4..1
        public float fog_density;            // 0..0.6
        public string sound = "forest";      // forest | silent
        public float master_volume;          // 0..1
        public SoundLayers sound_layers = new SoundLayers();
        public string motion = "none";       // none | gentle
        public float motion_speed;           // 0..0.5
        public string[] particles = new string[0]; // subset of fireflies, pollen (max 2)
        public OrbConfig orb = new OrbConfig();

        // Same values as fixtures/scene_config_calm_forest_sunset.json. Used when no valid config exists.
        public static SceneConfig CalmForestSunset()
        {
            return new SceneConfig
            {
                animals_enabled = true,
                time_of_day = "sunset",
                light_warmth = 0.7f,
                brightness = 0.8f,
                fog_density = 0.25f,
                sound = "forest",
                master_volume = 0.6f,
                sound_layers = new SoundLayers { wind = 0.2f, birds = 0.3f, water = 0.4f, pad = 0.3f },
                motion = "gentle",
                motion_speed = 0.2f,
                particles = new[] { "fireflies" },
                orb = new OrbConfig { enabled = true, breath_seconds = 5.5f },
            };
        }

        public SceneConfig Clone()
        {
            return JsonUtility.FromJson<SceneConfig>(JsonUtility.ToJson(this));
        }

        // Parses untrusted JSON. Returns false (and the saved preset) for anything structurally invalid:
        // unparsable JSON, wrong version/scene id, unknown enum values, missing objects.
        // Numeric values out of range are clamped instead of rejected, as the scene must stay usable.
        // JsonUtility ignores unknown fields; strict unknown-field rejection is enforced by the backend schema.
        public static bool TryParse(string json, out SceneConfig config)
        {
            config = CalmForestSunset();
            if (string.IsNullOrWhiteSpace(json)) return false;

            SceneConfig parsed;
            try { parsed = JsonUtility.FromJson<SceneConfig>(json); }
            catch (Exception) { return false; }

            if (parsed == null || !parsed.IsStructurallyValid()) return false;
            parsed.Clamp();
            config = parsed;
            return true;
        }

        public bool IsStructurallyValid()
        {
            if (schema_version != SchemaVersion || scene_id != ForestSceneId) return false;
            if (time_of_day != "day" && time_of_day != "sunset") return false;
            if (sound != "forest" && sound != "silent") return false;
            if (motion != "none" && motion != "gentle") return false;
            if (sound_layers == null || orb == null || particles == null) return false;
            if (particles.Length > 2) return false;
            foreach (var p in particles)
                if (p != "fireflies" && p != "pollen") return false;
            if (particles.Length == 2 && particles[0] == particles[1]) return false;
            return true;
        }

        public void Clamp()
        {
            light_warmth = Sane(light_warmth, 0f, 1f);
            brightness = Sane(brightness, 0.4f, 1f);
            fog_density = Sane(fog_density, 0f, 0.6f);
            master_volume = Sane(master_volume, 0f, 1f);
            motion_speed = Sane(motion_speed, 0f, 0.5f);
            sound_layers.wind = Sane(sound_layers.wind, 0f, 1f);
            sound_layers.birds = Sane(sound_layers.birds, 0f, 1f);
            sound_layers.water = Sane(sound_layers.water, 0f, 1f);
            sound_layers.pad = Sane(sound_layers.pad, 0f, 1f);
            orb.breath_seconds = Sane(orb.breath_seconds, 4f, 8f);
        }

        // NaN/Infinity would otherwise survive Mathf.Clamp; fall back to the lower bound.
        static float Sane(float v, float min, float max)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return min;
            return Mathf.Clamp(v, min, max);
        }
    }

    // Mirrors contracts/v1/preferences.schema.json. These are explicit participant choices and always win.
    [Serializable]
    public class Preferences
    {
        public string schema_version = SceneConfig.SchemaVersion;
        public string theme = "forest";
        public bool animals_enabled = true;
        public string time_of_day = "sunset";
        public string sound = "forest";
        public string motion = "gentle";
        public string text;
    }
}
