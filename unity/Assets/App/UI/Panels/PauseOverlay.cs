using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Covers the flow while paused. Exit stays available.</summary>
    public sealed class PauseOverlay : MonoBehaviour
    {
        public Button ResumeButton;
        public Button ExitButton;

        public void Bind(SessionController controller)
        {
            ResumeButton.onClick.AddListener(controller.TogglePause);
            ExitButton.onClick.AddListener(controller.Exit);
        }
    }
}
