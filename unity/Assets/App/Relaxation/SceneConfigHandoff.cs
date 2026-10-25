using App.Core.Contracts;

namespace App.Relaxation
{
    /// <summary>
    /// Carries the sanitised config from the assessment flow to the forest scene. Defaults to the offline preset so
    /// ForestDemo can be played on its own.
    /// </summary>
    public static class SceneConfigHandoff
    {
        static SceneConfig _current;

        public static SceneConfig Current => _current ?? SceneConfigSanitizer.Sanitize(SceneConfigPresets.CalmForestSunset());

        /// <summary>Re-sanitises on the way in; nothing unchecked reaches the scene.</summary>
        public static void Set(SceneConfig config, Preferences preferences = null) =>
            _current = SceneConfigSanitizer.Sanitize(config, preferences);

        public static void Clear() => _current = null;
    }
}
