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
            RatingGroup.SetActive(!capturing);
            SkipButton.gameObject.SetActive(capturing);
            ContinueButton.gameObject.SetActive(!capturing);
            Status.text = capturing
                ? $"Baseline: follow the yellow target with your eyes.\n{Controller.PhaseRemainingSeconds:0} s left ({Controller.SampleCount} samples, {Controller.ProbeCount} targets, {Controller.EyeSourceDescription})"
                : "Baseline done. How do you feel right now?";
        }
    }
}
