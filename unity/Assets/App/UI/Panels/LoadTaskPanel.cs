using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Task instructions, then the recording with a countdown.</summary>
    public sealed class LoadTaskPanel : FlowPanel
    {
        public Text Title;
        public Text Status;
        public Text Countdown;
        public Button StartButton;
        public Button SkipButton;

        public override FlowState State => FlowState.LoadTask;

        void Awake()
        {
            StartButton.onClick.AddListener(() => Controller.StartPhase());
            SkipButton.onClick.AddListener(() => Controller.EndCaptureEarly());
        }

        public override void OnShow() => Refresh();

        public override void Refresh()
        {
            if (Controller == null) return;
            var awaiting = Controller.AwaitingPhaseStart;
            StartButton.gameObject.SetActive(awaiting);
            StartButton.interactable = !Controller.Flow.IsPaused;
            Countdown.gameObject.SetActive(Controller.IsCapturing);
            SkipButton.gameObject.SetActive(Controller.IsCapturing && Controller.EyeSource != App.Capture.EyeSourceMode.Hardware);
            Countdown.text = $"{UnityEngine.Mathf.CeilToInt(Controller.PhaseRemainingSeconds)} s";
            if (awaiting)
            {
                Title.text = "Now the task";
                Status.text = "It works the same way as the baseline.\n\n"
                    + "•  Keep your head still and follow the yellow dot with your eyes.\n"
                    + $"•  It takes {PhaseInstructions.Duration(Controller.PendingPhaseSeconds)}.\n"
                    + "•  Press Y or B to pause at any time.\n\n"
                    + "In this demo the task uses the same dot layout as the baseline (the reading load is a placeholder).";
            }
            else
            {
                Title.text = "Follow the yellow dot";
                Status.text = "Keep your head still and look at the dot until it moves.\n\n"
                    + $"{Controller.SampleCount} samples, {Controller.ProbeCount} targets  ·  {Controller.EyeSourceDescription}";
            }
        }
    }

    public static class PhaseInstructions
    {
        /// <summary>"about 2 minutes (130 s)" or "about 45 seconds".</summary>
        public static string Duration(float seconds)
        {
            var s = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(seconds));
            if (s < 90) return $"about {s} second{(s == 1 ? "" : "s")}";
            var minutes = UnityEngine.Mathf.RoundToInt(s / 60f);
            return $"about {minutes} minute{(minutes == 1 ? "" : "s")} ({s} s)";
        }
    }
}
