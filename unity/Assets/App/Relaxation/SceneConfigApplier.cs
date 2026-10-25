using System;
using UnityEngine;

namespace VrFatigue.Relaxation
{
    // Implemented by anything that sways/moves ambiently (leaves, grass, squirrel idle). Keep it gentle.
    public interface IAmbientMotion
    {
        void SetMotion(bool enabled, float speed);
    }

    // Applies a validated SceneConfig to existing lights, fog, audio, particles, orb and the squirrel anchor.
    // It never creates geometry, loads assets, or reads paths/URLs from the config.
    // Lighting and sound change gradually; the camera is never touched.
    public class SceneConfigApplier : MonoBehaviour
    {
        [Serializable]
        public struct LightPreset
        {
            public Color coolColor;
            public Color warmColor;
            public float intensity;
            public Vector3 sunEuler;
            public Color fogTint;
        }

        [Header("Lighting")]
        [SerializeField] Light sun;
        [SerializeField] LightPreset day = new LightPreset
        {
            coolColor = new Color(0.90f, 0.95f, 1.00f), warmColor = new Color(1.00f, 0.95f, 0.82f),
            intensity = 1.0f, sunEuler = new Vector3(55f, 30f, 0f), fogTint = new Color(0.72f, 0.80f, 0.85f),
        };
        [SerializeField] LightPreset sunset = new LightPreset
        {
            coolColor = new Color(1.00f, 0.85f, 0.70f), warmColor = new Color(1.00f, 0.70f, 0.45f),
            intensity = 0.8f, sunEuler = new Vector3(12f, 30f, 0f), fogTint = new Color(0.85f, 0.70f, 0.60f),
        };
        [Tooltip("RenderSettings fog is exponential-squared; this scales fog_density 0..0.6 to a density. Tune in the editor.")]
        [SerializeField] float fogDensityScale = 0.05f;
        [Tooltip("Optional: skybox material with an _Exposure property (e.g. a downsized HDRI cubemap).")]
        [SerializeField] Material skyboxMaterial;
        [SerializeField] float lightTransitionSeconds = 3f;

        [Header("Audio (one looping source per layer)")]
        [SerializeField] AudioSource wind;
        [SerializeField] AudioSource birds;
        [SerializeField] AudioSource water;
        [SerializeField] AudioSource pad;
        [SerializeField] float audioTransitionSeconds = 2f;

        [Header("Scene objects")]
        [Tooltip("Empty transform that will host the real squirrel prefab. Active only when animals_enabled.")]
        [SerializeField] GameObject squirrelAnchor;
        [SerializeField] ParticleSystem fireflies;
        [SerializeField] ParticleSystem pollen;
        [SerializeField] Transform orb;
        [SerializeField] MonoBehaviour[] ambientMotionTargets; // each must implement IAmbientMotion

        [Header("Config")]
        [SerializeField] TextAsset savedPresetJson; // fixtures/scene_config_calm_forest_sunset.json copied into the project

        SceneConfig target;
        bool muted;

        // Smoothed state
        float warmth, brightness, fog, sunBlend; // sunBlend: 0 = day, 1 = sunset
        float wVol, bVol, waVol, pVol;
        float orbPhase;

        public SceneConfig Current => target;

        // RenderSettings belong to the active scene (Assessment when loaded additively): restore them on unload.
        bool savedRender, savedFog;
        FogMode savedFogMode;
        Color savedFogColor;
        float savedFogDensity, savedAmbientIntensity;

        void Awake()
        {
            CaptureRenderSettings();
            ApplyJson(savedPresetJson != null ? savedPresetJson.text : null, snap: true);
        }

        public void CaptureRenderSettings()
        {
            savedFog = RenderSettings.fog;
            savedFogMode = RenderSettings.fogMode;
            savedFogColor = RenderSettings.fogColor;
            savedFogDensity = RenderSettings.fogDensity;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedRender = true;
        }

        void OnDestroy() => RestoreRenderSettings();

        public void RestoreRenderSettings()
        {
            if (!savedRender) return;
            RenderSettings.fog = savedFog;
            RenderSettings.fogMode = savedFogMode;
            RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogDensity = savedFogDensity;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
        }

        // Safe entry point for text coming from the backend or a file. Invalid input -> saved preset.
        public bool ApplyJson(string json, bool snap = false)
        {
            if (SceneConfig.TryParse(json, out var cfg))
            {
                Apply(cfg, snap);
                return true;
            }
            // TryParse returns the built-in preset; prefer the project's saved preset when it is valid.
            if (savedPresetJson != null && SceneConfig.TryParse(savedPresetJson.text, out var saved)) cfg = saved;
            Apply(cfg, snap);
            return false;
        }

        public void Apply(SceneConfig config, bool snap = false)
        {
            if (config == null || !config.IsStructurallyValid()) config = SceneConfig.CalmForestSunset();
            config = config.Clone();
            config.Clamp();
            target = config;

            // Discrete switches. Do these while the scene is not yet visible (confirmation screen) to avoid pop-in.
            if (squirrelAnchor != null) squirrelAnchor.SetActive(config.animals_enabled);
            SetParticles(fireflies, Has(config.particles, "fireflies"));
            SetParticles(pollen, Has(config.particles, "pollen"));
            bool moving = config.motion == "gentle" && config.motion_speed > 0f;
            foreach (var m in ambientMotionTargets)
                if (m is IAmbientMotion a) a.SetMotion(moving, moving ? config.motion_speed : 0f);
            if (orb != null) orb.gameObject.SetActive(config.orb.enabled);

            if (snap) Snap();
        }

        // Mute overrides all sound output, regardless of config. Wire this to the always-visible mute control.
        public void SetMuted(bool value)
        {
            muted = value;
            if (value) foreach (var s in Sources()) if (s != null) s.mute = true;
        }

        public bool IsMuted => muted;

        void Update()
        {
            if (target == null) return;
            float lt = Step(lightTransitionSeconds);
            warmth = Mathf.Lerp(warmth, target.light_warmth, lt);
            brightness = Mathf.Lerp(brightness, target.brightness, lt);
            fog = Mathf.Lerp(fog, target.fog_density, lt);
            sunBlend = Mathf.MoveTowards(sunBlend, target.time_of_day == "sunset" ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, lightTransitionSeconds));
            ApplyLighting();

            float at = Step(audioTransitionSeconds);
            bool silent = muted || target.sound == "silent";
            float master = silent ? 0f : target.master_volume;
            wVol = Mathf.Lerp(wVol, master * target.sound_layers.wind, at);
            bVol = Mathf.Lerp(bVol, master * target.sound_layers.birds, at);
            waVol = Mathf.Lerp(waVol, master * target.sound_layers.water, at);
            pVol = Mathf.Lerp(pVol, master * target.sound_layers.pad, at);
            Drive(wind, wVol, silent);
            Drive(birds, bVol, silent);
            Drive(water, waVol, silent);
            Drive(pad, pVol, silent);

            if (orb != null && target.orb.enabled) UpdateOrb();
        }

        void ApplyLighting()
        {
            var a = Blend(day, sunset, sunBlend);
            if (sun != null)
            {
                sun.color = Color.Lerp(a.coolColor, a.warmColor, warmth);
                sun.intensity = a.intensity * brightness;
                sun.transform.rotation = Quaternion.Euler(a.sunEuler);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(a.fogTint * 0.9f, a.fogTint, warmth) * Mathf.Lerp(0.7f, 1f, brightness);
            RenderSettings.fogDensity = fog * fogDensityScale;
            RenderSettings.ambientIntensity = brightness;
            if (skyboxMaterial != null && skyboxMaterial.HasProperty("_Exposure"))
                skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(0.6f, 1.1f, brightness) * Mathf.Lerp(1f, 0.7f, sunBlend));
        }

        // Slow sinusoidal scale, one full inhale+exhale per breath_seconds. No flashing, no camera motion.
        void UpdateOrb()
        {
            orbPhase += Time.deltaTime / Mathf.Max(4f, target.orb.breath_seconds);
            float s = Mathf.Lerp(0.85f, 1.15f, 0.5f - 0.5f * Mathf.Cos(orbPhase * Mathf.PI * 2f));
            orb.localScale = Vector3.one * s;
        }

        void Snap()
        {
            warmth = target.light_warmth; brightness = target.brightness; fog = target.fog_density;
            sunBlend = target.time_of_day == "sunset" ? 1f : 0f;
            bool silent = muted || target.sound == "silent";
            float m = silent ? 0f : target.master_volume;
            wVol = m * target.sound_layers.wind; bVol = m * target.sound_layers.birds;
            waVol = m * target.sound_layers.water; pVol = m * target.sound_layers.pad;
        }

        void Drive(AudioSource s, float volume, bool silent)
        {
            if (s == null) return;
            s.volume = volume;
            // Mute / silent cuts output immediately so nothing leaks through; other changes fade via volume.
            s.mute = silent;
            if (!silent && volume > 0f && !s.isPlaying) s.Play();
        }

        static LightPreset Blend(LightPreset a, LightPreset b, float t)
        {
            return new LightPreset
            {
                coolColor = Color.Lerp(a.coolColor, b.coolColor, t),
                warmColor = Color.Lerp(a.warmColor, b.warmColor, t),
                intensity = Mathf.Lerp(a.intensity, b.intensity, t),
                sunEuler = new Vector3(Mathf.LerpAngle(a.sunEuler.x, b.sunEuler.x, t), Mathf.LerpAngle(a.sunEuler.y, b.sunEuler.y, t), 0f),
                fogTint = Color.Lerp(a.fogTint, b.fogTint, t),
            };
        }

        static float Step(float seconds) => 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.1f, seconds));

        static bool Has(string[] list, string name) => Array.IndexOf(list, name) >= 0;

        static void SetParticles(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            var e = ps.emission;
            e.enabled = on;
            if (on && !ps.isPlaying) ps.Play();
        }

        System.Collections.Generic.IEnumerable<AudioSource> Sources()
        {
            yield return wind; yield return birds; yield return water; yield return pad;
        }
    }
}
