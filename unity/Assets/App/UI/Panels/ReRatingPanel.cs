using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class ReRatingPanel : FlowPanel
    {
        public RatingRow EyeStrain;
        public RatingRow MentalFatigue;
        public Button SubmitButton;
        public Button SkipButton;

        public override FlowState State => FlowState.ReRating;

        void Awake()
        {
            SubmitButton.onClick.AddListener(() =>
            {
                if (EyeStrain.HasValue && MentalFatigue.HasValue) Controller.SubmitReRating(EyeStrain.Value, MentalFatigue.Value);
            });
            SkipButton.onClick.AddListener(() => Controller.SkipReRating());
            EyeStrain.Changed += Refresh;
            MentalFatigue.Changed += Refresh;
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            Refresh();
        }

        public override void Refresh() => SubmitButton.interactable = EyeStrain.HasValue && MentalFatigue.HasValue;
    }
}
