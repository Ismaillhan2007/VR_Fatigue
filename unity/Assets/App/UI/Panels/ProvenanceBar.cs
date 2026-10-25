using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Always-visible data/AI source labels plus pause and exit controls.</summary>
    public sealed class ProvenanceBar : MonoBehaviour
    {
        public Text DataLabel;
        public Text AiLabel;
        public Text StateLabel;
        public Button PauseButton;
        public Button ExitButton;

        public void Bind(SessionController controller)
        {
            PauseButton.onClick.AddListener(controller.TogglePause);
            ExitButton.onClick.AddListener(controller.Exit);
        }

        public void Show(SessionController controller)
        {
            var c = controller.Flow.Context;
            DataLabel.text = ProvenanceLabels.Data(c) + (controller.ProviderNote != null ? $"  [{controller.ProviderNote}]" : "");
            AiLabel.text = ProvenanceLabels.Ai(c);
            StateLabel.text = $"Step: {controller.Flow.State}" + (controller.Flow.IsPaused ? " (paused)" : "");
        }
    }
}
