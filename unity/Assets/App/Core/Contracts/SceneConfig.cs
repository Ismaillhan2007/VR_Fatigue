using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace App.Core.Contracts
{
    /// <summary>
    /// contracts/v1/scene_config.schema.json: allowlisted, bounded scene settings only (no URLs, paths, code or free text).
    /// Anything applied to a scene must first pass <see cref="SceneConfigSanitizer.Sanitize"/>.
    /// </summary>
    public sealed class SceneConfig : IContract
    {
        public const string ForestSceneId = "forest_v1";

        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        [JsonProperty("scene_id", Required = Required.Always)] public string SceneId = ForestSceneId;
        [JsonProperty("animals_enabled", Required = Required.Always)] public bool AnimalsEnabled;
        [JsonProperty("time_of_day", Required = Required.Always)] public TimeOfDay TimeOfDay;
        /// <summary>0 = cool, 1 = warm.</summary>
        [JsonProperty("light_warmth", Required = Required.Always)] public double LightWarmth;
        /// <summary>0.4..1.0; the floor keeps the scene usable.</summary>
        [JsonProperty("brightness", Required = Required.Always)] public double Brightness;
        [JsonProperty("fog_density", Required = Required.Always)] public double FogDensity;
        [JsonProperty("sound", Required = Required.Always)] public SoundMode Sound;
        [JsonProperty("master_volume", Required = Required.Always)] public double MasterVolume;
        /// <summary>Treated as 0 when <see cref="Sound"/> is silent.</summary>
        [JsonProperty("sound_layers", Required = Required.Always)] public SoundLayers SoundLayers = new SoundLayers();
        [JsonProperty("motion", Required = Required.Always)] public MotionMode Motion;
        /// <summary>0..0.5; 0.5 is the hard ceiling.</summary>
        [JsonProperty("motion_speed", Required = Required.Always)] public double MotionSpeed;
        [JsonProperty("particles", Required = Required.Always)] public List<Particle> Particles = new List<Particle>();
        [JsonProperty("orb", Required = Required.Always)] public Orb Orb = new Orb();

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Const(path + ".scene_id", SceneId, ForestSceneId);
            p.Defined(path + ".time_of_day", TimeOfDay);
            p.Range(path + ".light_warmth", LightWarmth, 0, 1);
            p.Range(path + ".brightness", Brightness, 0.4, 1.0);
            p.Range(path + ".fog_density", FogDensity, 0, 0.6);
            p.Defined(path + ".sound", Sound);
            p.Range(path + ".master_volume", MasterVolume, 0, 1);
            p.Nested(path + ".sound_layers", SoundLayers);
            p.Defined(path + ".motion", Motion);
            p.Range(path + ".motion_speed", MotionSpeed, 0, 0.5);
            p.List(path + ".particles", Particles, 0, 2, unique: true);
            if (Particles != null) foreach (var x in Particles) p.Defined(path + ".particles[]", x);
            p.Nested(path + ".orb", Orb);
        }

        public SceneConfig Copy() => new SceneConfig
        {
            SchemaVersion = SchemaVersion, SceneId = SceneId, AnimalsEnabled = AnimalsEnabled, TimeOfDay = TimeOfDay,
            LightWarmth = LightWarmth, Brightness = Brightness, FogDensity = FogDensity, Sound = Sound,
            MasterVolume = MasterVolume,
            SoundLayers = SoundLayers == null ? null : new SoundLayers
                { Wind = SoundLayers.Wind, Birds = SoundLayers.Birds, Water = SoundLayers.Water, Pad = SoundLayers.Pad },
            Motion = Motion, MotionSpeed = MotionSpeed,
            Particles = Particles == null ? null : new List<Particle>(Particles),
            Orb = Orb == null ? null : new Orb { Enabled = Orb.Enabled, BreathSeconds = Orb.BreathSeconds },
        };

        /// <summary>
        /// Returns a copy forced into the v1 bounds: numbers clamped, non-finite numbers replaced by the preset value,
        /// unknown enum values and particles dropped, constants restored. The result always validates.
        /// </summary>
        public SceneConfig Clamped()
        {
            var d = SceneConfigPresets.CalmForestSunset();
            var c = Copy();
            c.SchemaVersion = Problems.SchemaVersion;
            c.SceneId = ForestSceneId;
            if (!Enum.IsDefined(typeof(TimeOfDay), c.TimeOfDay)) c.TimeOfDay = d.TimeOfDay;
            if (!Enum.IsDefined(typeof(SoundMode), c.Sound)) c.Sound = d.Sound;
            if (!Enum.IsDefined(typeof(MotionMode), c.Motion)) c.Motion = d.Motion;
            c.LightWarmth = Clamp(c.LightWarmth, 0, 1, d.LightWarmth);
            c.Brightness = Clamp(c.Brightness, 0.4, 1.0, d.Brightness);
            c.FogDensity = Clamp(c.FogDensity, 0, 0.6, d.FogDensity);
            c.MasterVolume = Clamp(c.MasterVolume, 0, 1, d.MasterVolume);
            c.MotionSpeed = Clamp(c.MotionSpeed, 0, 0.5, d.MotionSpeed);
            var l = c.SoundLayers ?? d.SoundLayers;
            c.SoundLayers = new SoundLayers
            {
                Wind = Clamp(l.Wind, 0, 1, d.SoundLayers.Wind),
                Birds = Clamp(l.Birds, 0, 1, d.SoundLayers.Birds),
                Water = Clamp(l.Water, 0, 1, d.SoundLayers.Water),
                Pad = Clamp(l.Pad, 0, 1, d.SoundLayers.Pad),
            };
            c.Particles = (c.Particles ?? new List<Particle>())
                .Where(x => Enum.IsDefined(typeof(Particle), x)).Distinct().Take(2).ToList();
            var o = c.Orb ?? d.Orb;
            c.Orb = new Orb { Enabled = o.Enabled, BreathSeconds = Clamp(o.BreathSeconds, 4, 8, d.Orb.BreathSeconds) };
            return c;
        }

        static double Clamp(double v, double min, double max, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            return v < min ? min : v > max ? max : v;
        }
    }

    public sealed class SoundLayers : IContract
    {
        [JsonProperty("wind", Required = Required.Always)] public double Wind;
        [JsonProperty("birds", Required = Required.Always)] public double Birds;
        [JsonProperty("water", Required = Required.Always)] public double Water;
        [JsonProperty("pad", Required = Required.Always)] public double Pad;

        public void Validate(Problems p, string path)
        {
            p.Range(path + ".wind", Wind, 0, 1);
            p.Range(path + ".birds", Birds, 0, 1);
            p.Range(path + ".water", Water, 0, 1);
            p.Range(path + ".pad", Pad, 0, 1);
        }
    }

    /// <summary>Optional slow breathing/pursuit orb. Smooth, non-flashing, never moves the camera.</summary>
    public sealed class Orb : IContract
    {
        [JsonProperty("enabled", Required = Required.Always)] public bool Enabled;
        /// <summary>Full inhale+exhale cycle, 4..8 s.</summary>
        [JsonProperty("breath_seconds", Required = Required.Always)] public double BreathSeconds = 6;

        public void Validate(Problems p, string path) => p.Range(path + ".breath_seconds", BreathSeconds, 4, 8);
    }

    public static class SceneConfigPresets
    {
        /// <summary>Same values as fixtures/scene_config_calm_forest_sunset.json (checked by a test). The offline demo preset.</summary>
        public static SceneConfig CalmForestSunset() => new SceneConfig
        {
            AnimalsEnabled = true,
            TimeOfDay = TimeOfDay.Sunset,
            LightWarmth = 0.7,
            Brightness = 0.8,
            FogDensity = 0.25,
            Sound = SoundMode.Forest,
            MasterVolume = 0.6,
            SoundLayers = new SoundLayers { Wind = 0.2, Birds = 0.3, Water = 0.4, Pad = 0.3 },
            Motion = MotionMode.Gentle,
            MotionSpeed = 0.2,
            Particles = new List<Particle> { Particle.Fireflies },
            Orb = new Orb { Enabled = true, BreathSeconds = 5.5 },
        };
    }

    public static class SceneConfigSanitizer
    {
        /// <summary>
        /// Clamp, then let explicit participant choices win, then re-validate. Only the result may reach an ISceneApplier.
        /// A null config falls back to the offline preset.
        /// </summary>
        public static SceneConfig Sanitize(SceneConfig config, Preferences preferences = null)
        {
            var c = (config ?? SceneConfigPresets.CalmForestSunset()).Clamped();
            if (preferences != null)
            {
                c.AnimalsEnabled = preferences.AnimalsEnabled;
                c.TimeOfDay = preferences.TimeOfDay;
                c.Sound = preferences.Sound;
                c.Motion = preferences.Motion;
            }
            if (c.Motion == MotionMode.None) c.MotionSpeed = 0;
            var problems = c.Validate();
            if (problems.Count > 0) throw new ContractException("SceneConfig failed validation after clamping", problems);
            return c;
        }
    }
}
