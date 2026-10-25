using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
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
            SubmitButton.onClick.AddListener(() => Controller.SubmitReRating(EyeStrain.Value, MentalFatigue.Value));
            SkipButton.onClick.AddListener(() => Controller.SkipReRating());
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
        }
    }
}
