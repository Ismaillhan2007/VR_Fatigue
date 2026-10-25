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
            Body.text = "Thank you.\n\nYou reported (1-7):\n"
                + $"  after the task:  eye strain {c.PostTaskReport?.EyeStrain}, mental fatigue {c.PostTaskReport?.MentalFatigue}\n"
                + $"  after the scene: eye strain {c.PostSceneReport?.EyeStrain}, mental fatigue {c.PostSceneReport?.MentalFatigue}\n\n"
                + "A single before/after rating is not evidence that the scene helps.";
        }
    }
}
