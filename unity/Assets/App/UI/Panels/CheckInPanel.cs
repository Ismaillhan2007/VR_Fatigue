using System.Collections.Generic;
using System.Linq;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Three short steps: the two 1-7 ratings, optional feeling chips, optional own words (typed or dictated).</summary>
    public sealed class CheckInPanel : FlowPanel
    {
        public RatingRow EyeStrain;
        public RatingRow MentalFatigue;
        /// <summary>One toggle per Chip value, in enum order.</summary>
        public List<Toggle> ChipToggles = new List<Toggle>();
        public InputField FreeText;
        public Text Hint;
        public Text StepLabel;
        public List<GameObject> Steps = new List<GameObject>();
        public Button BackButton;
        public Button NextButton;
        public Button SubmitButton;
        /// <summary>Opt-in: aggregate measurements and ratings (never raw gaze or own words) go to the backend and Gemini.</summary>
        public Toggle CloudExplanation;

        public const int MaxChips = 6;
        int _step;

        public override FlowState State => FlowState.CheckIn;
        public int Step => _step;

        void Awake()
        {
            FreeText.characterLimit = SelfReport.MaxFreeText;
            foreach (var t in ChipToggles) t.onValueChanged.AddListener(_ => EnforceChipLimit());
            BackButton.onClick.AddListener(() => ShowStep(_step - 1));
            NextButton.onClick.AddListener(() => ShowStep(_step + 1));
            SubmitButton.onClick.AddListener(Submit);
            EyeStrain.Changed += Refresh;
            MentalFatigue.Changed += Refresh;
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            foreach (var t in ChipToggles) t.isOn = false;
            FreeText.text = "";
            CloudExplanation.SetIsOnWithoutNotify(false);
            ShowStep(0);
        }

        public void ShowStep(int step)
        {
            if (step > 0 && !(EyeStrain.HasValue && MentalFatigue.HasValue)) step = 0;
            _step = Mathf.Clamp(step, 0, Steps.Count - 1);
            for (var i = 0; i < Steps.Count; i++) Steps[i].SetActive(i == _step);
            Refresh();
        }

        public override void Refresh()
        {
            if (Controller == null) return;
            var last = _step == Steps.Count - 1;
            StepLabel.text = $"STEP 3 OF 4  ·  CHECK-IN {_step + 1}/{Steps.Count}";
            BackButton.gameObject.SetActive(_step > 0);
            NextButton.gameObject.SetActive(!last);
            NextButton.interactable = _step > 0 || (EyeStrain.HasValue && MentalFatigue.HasValue);
            SubmitButton.gameObject.SetActive(last);
            Hint.text = "Your words stay on this headset for the local report. They are never sent to the backend or Gemini.";
        }

        void EnforceChipLimit()
        {
            var on = ChipToggles.Count(t => t.isOn);
            foreach (var t in ChipToggles) t.interactable = t.isOn || on < MaxChips;
        }

        void Submit()
        {
            if (!(EyeStrain.HasValue && MentalFatigue.HasValue)) { ShowStep(0); return; }
            var chips = new List<Chip>();
            for (var i = 0; i < ChipToggles.Count; i++) if (ChipToggles[i].isOn) chips.Add((Chip)i);
            var report = new SelfReport
            {
                Phase = ReportPhase.PostTask,
                EyeStrain = EyeStrain.Value,
                MentalFatigue = MentalFatigue.Value,
                Chips = chips,
                FreeText = string.IsNullOrWhiteSpace(FreeText.text) ? null : FreeText.text.Trim(),
            };
            Controller.SubmitCheckIn(report, null, CloudExplanation.isOn);
        }
    }
}
