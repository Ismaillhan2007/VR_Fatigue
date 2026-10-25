using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Shown for Done and Exited.</summary>
    public sealed class DonePanel : FlowPanel
    {
        public Text Body;
        public Button RestartButton;

        public override FlowState State => FlowState.Done;

        void Awake() => RestartButton.onClick.AddListener(() => Controller.Restart());

        public override void OnShow()
        {
            var flow = Controller.Flow;
            if (flow.State == FlowState.Exited)
            {
                Body.text = "Session ended. Session data were cleared from memory.";
                return;
            }
            var c = flow.Context;
            Body.text = (c.SceneParticipated ? "Session completed with optional relaxation." : "Session completed with your report.")
                + (c.PostSceneReport == null ? "" : $"\nAfter the scene (1-7): eye strain {c.PostSceneReport.EyeStrain}, mental fatigue {c.PostSceneReport.MentalFatigue}.\nA single before/after rating is not evidence that the scene helps.")
                + "\n\n" + (Controller.MetricsFailureMessage == null ? "" : Controller.MetricsFailureMessage + "\n\n")
                + SummaryFormatter.Format(c, false, Controller.ProbeStatsText);
        }
    }
}
