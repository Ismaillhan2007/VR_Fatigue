using System.Collections;
using App.Core.Contracts;
using App.Core.Scene;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace App.Relaxation
{
    /// <summary>
    /// Loads the forest scene additively when it is in Build Settings and hands it the config through the first
    /// MonoBehaviour that implements ISceneApplier. Without the scene, the logging stub is used and the UI says so.
    /// </summary>
    public sealed class RelaxationSceneLoader
    {
        public const string ForestSceneName = "ForestDemo";

        public bool ForestLoaded { get; private set; }
        public ISceneApplier Applier { get; private set; }

        public static bool ForestAvailable => Application.CanStreamedLevelBeLoaded(ForestSceneName);

        public IEnumerator Enter(SceneConfig config, Preferences preferences)
        {
            SceneConfigHandoff.Set(config, preferences);
            Applier = null;
            if (ForestAvailable && !SceneManager.GetSceneByName(ForestSceneName).isLoaded)
            {
                yield return SceneManager.LoadSceneAsync(ForestSceneName, LoadSceneMode.Additive);
                ForestLoaded = true;
            }
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                if (b is ISceneApplier a) { Applier = a; break; }
            if (Applier == null) Applier = new LoggingSceneApplier();
            Applier.Apply(SceneConfigHandoff.Current);
        }

        public IEnumerator Exit()
        {
            if (ForestLoaded && SceneManager.GetSceneByName(ForestSceneName).isLoaded)
                yield return SceneManager.UnloadSceneAsync(ForestSceneName);
            ForestLoaded = false;
            Applier = null;
        }
    }
}
