using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class BaselinePanel : FlowPanel
    {
        public Text Status;
        public RatingRow EyeStrain;
        public RatingRow MentalFatigue;
        public UnityEngine.GameObject RatingGroup;
        public Button SkipButton;
        public Button ContinueButton;

        public override FlowState State => FlowState.Baseline;

        void Awake()
        {
            SkipButton.onClick.AddListener(() => Controller.EndCaptureEarly());
            ContinueButton.onClick.AddListener(() => Controller.SubmitBaseline(EyeStrain.Value, MentalFatigue.Value));
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            Refresh();
        }

        public override void Refresh()
        {
            var capturing = Controller.IsCapturing;
            RatingGroup.SetActive(!capturing && !Controller.NativeDiagnostic);
            SkipButton.gameObject.SetActive(capturing && Controller.EyeSource != App.Capture.EyeSourceMode.Hardware);
            ContinueButton.gameObject.SetActive(!capturing && !Controller.NativeDiagnostic);
            Status.text = Controller.NativeDiagnosticComplete
                ? "60-second native diagnostic completed. Aggregate diagnostics are available locally. Start a new session for the full assessment; this diagnostic is not an eligible baseline.\n\n" + Controller.NativeDiagnosticSummary
                : capturing
                ? $"{(Controller.NativeDiagnostic ? "Diagnostic" : "Baseline")}: follow the yellow target with your eyes.\n{Controller.PhaseRemainingSeconds:0} s left ({Controller.SampleCount} samples, {Controller.ProbeCount} targets, {Controller.EyeSourceDescription})"
                : "Baseline done. How do you feel right now?";
        }
    }
}
