using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Instructions, then the recording with a countdown, then the two baseline ratings.</summary>
    public sealed class BaselinePanel : FlowPanel
    {
        public Text Title;
        public Text Status;
        public Text Countdown;
        public RatingRow EyeStrain;
        public RatingRow MentalFatigue;
        public UnityEngine.GameObject RatingGroup;
        public Button StartButton;
        public Button SkipButton;
        public Button ContinueButton;

        public override FlowState State => FlowState.Baseline;

        void Awake()
        {
            StartButton.onClick.AddListener(() => Controller.StartPhase());
            SkipButton.onClick.AddListener(() => Controller.EndCaptureEarly());
            ContinueButton.onClick.AddListener(() =>
            {
                if (EyeStrain.HasValue && MentalFatigue.HasValue) Controller.SubmitBaseline(EyeStrain.Value, MentalFatigue.Value);
            });
            EyeStrain.Changed += Refresh;
            MentalFatigue.Changed += Refresh;
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            Refresh();
        }

        public override void Refresh()
        {
            if (Controller == null) return;
            var awaiting = Controller.AwaitingPhaseStart;
            var capturing = Controller.IsCapturing;
            var diagnostic = Controller.NativeDiagnostic;
            var rating = !awaiting && !capturing && !diagnostic;
            RatingGroup.SetActive(rating);
            StartButton.gameObject.SetActive(awaiting);
            StartButton.interactable = !Controller.Flow.IsPaused;
            Countdown.gameObject.SetActive(capturing);
            SkipButton.gameObject.SetActive(capturing && Controller.EyeSource != App.Capture.EyeSourceMode.Hardware);
            ContinueButton.gameObject.SetActive(rating);
            ContinueButton.interactable = EyeStrain.HasValue && MentalFatigue.HasValue;
            Countdown.text = $"{UnityEngine.Mathf.CeilToInt(Controller.PhaseRemainingSeconds)} s";
            if (Controller.NativeDiagnosticComplete)
            {
                Title.text = "Diagnostic complete";
                Status.text = "60-second native diagnostic completed. Aggregate diagnostics are available locally. Start a new session for the full assessment; this diagnostic is not an eligible baseline.\n\n" + Controller.NativeDiagnosticSummary;
            }
            else if (awaiting)
            {
                Title.text = diagnostic ? "Before the eye diagnostic" : "Before we start";
                Status.text = (diagnostic ? "A separate 60-second check of native eye tracking. It is not a baseline.\n\n"
                        : "This first recording is your personal baseline for today.\n\n")
                    + "•  Sit comfortably and keep your head still.\n"
                    + "•  A yellow dot jumps to different places. Look at it as quickly as you can and keep looking until it moves.\n"
                    + $"•  Blink normally. It takes {PhaseInstructions.Duration(Controller.PendingPhaseSeconds)}.\n"
                    + "•  Press Y or B on a controller to pause at any time.";
            }
            else if (capturing)
            {
                Title.text = "Follow the yellow dot";
                Status.text = "Keep your head still and look at the dot until it moves.\n\n"
                    + $"{Controller.SampleCount} samples, {Controller.ProbeCount} targets  ·  {Controller.EyeSourceDescription}";
            }
            else
            {
                Title.text = "How do you feel right now?";
                Status.text = "Baseline recorded. Choose a number for each line, then continue to the task.";
            }
        }
    }
}
