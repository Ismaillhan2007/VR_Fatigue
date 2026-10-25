using App.Core.Contracts;

namespace App.Core.Scene
{
    /// <summary>
    /// Seam owned by feat/forest-scene. Maps an already sanitised SceneConfig onto prebuilt lighting, audio,
    /// particles and animals. Never loads URLs, files or code named by the config. Changes must fade slowly
    /// (no flashing, no camera motion).
    /// </summary>
    public interface ISceneApplier
    {
        void Apply(SceneConfig config);
    }
}
