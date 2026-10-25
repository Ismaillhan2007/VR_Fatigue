using App.Core.Contracts;
using App.Core.Scene;
using UnityEngine;

namespace App.Relaxation
{
    /// <summary>
    /// STUB used while the forest scene is missing: logs the config and keeps it for the UI preview.
    /// feat/forest-scene implements ISceneApplier on a MonoBehaviour inside ForestDemo instead.
    /// </summary>
    public sealed class LoggingSceneApplier : ISceneApplier
    {
        public SceneConfig LastApplied { get; private set; }

        public void Apply(SceneConfig config)
        {
            LastApplied = config;
            Debug.Log($"[LoggingSceneApplier] {config.TimeOfDay}, brightness {config.Brightness:0.00}, sound {config.Sound}, animals {config.AnimalsEnabled}");
        }
    }
}
