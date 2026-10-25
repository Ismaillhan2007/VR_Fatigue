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
            var participation = !c.SceneParticipated ? "Report complete. No relaxation scene was entered."
                : c.PostSceneReport == null ? "Report complete. Optional relaxation ended; feedback was skipped."
                : $"Report complete. After optional relaxation you reported (1-7): eye strain {c.PostSceneReport.EyeStrain}, "
                    + $"mental fatigue {c.PostSceneReport.MentalFatigue}. A single rating is not evidence that the scene helps.";
            Body.text = participation + "\nYour report remains available until Exit or New session.\n\n"
                + (Controller.MetricsFailureMessage == null ? "" : Controller.MetricsFailureMessage + "\n\n")
                + SummaryFormatter.Format(c, false, Controller.ProbeStatsText);
        }
    }
}
