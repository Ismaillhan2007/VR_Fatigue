using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class CheckInPanel : FlowPanel
    {
        public RatingRow EyeStrain;
        public RatingRow MentalFatigue;
        /// <summary>One toggle per Chip value, in enum order.</summary>
        public List<Toggle> ChipToggles = new List<Toggle>();
        public InputField FreeText;
        public Toggle ChooseSettings;
        public UnityEngine.GameObject PreferenceGroup;
        public Toggle Animals;
        public Toggle Silent;
        public Toggle Sunset;
        public Toggle GentleMotion;
        public Text Hint;
        public Button SubmitButton;

        public override FlowState State => FlowState.CheckIn;

        void Awake()
        {
            FreeText.characterLimit = SelfReport.MaxFreeText;
            ChooseSettings.onValueChanged.AddListener(on => PreferenceGroup.SetActive(on));
            foreach (var t in ChipToggles) t.onValueChanged.AddListener(_ => EnforceChipLimit());
            SubmitButton.onClick.AddListener(Submit);
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            foreach (var t in ChipToggles) t.isOn = false;
            FreeText.text = "";
            ChooseSettings.isOn = false;
            PreferenceGroup.SetActive(false);
            Animals.isOn = true;
            Silent.isOn = false;
            Sunset.isOn = true;
            GentleMotion.isOn = true;
            Hint.text = "Text stays on this device (it is not sent to the AI).";
        }

        void EnforceChipLimit()
        {
            var on = ChipToggles.Count(t => t.isOn);
            foreach (var t in ChipToggles) t.interactable = t.isOn || on < 6;
        }

        void Submit()
        {
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
            Preferences prefs = null;
            if (ChooseSettings.isOn)
                prefs = new Preferences
                {
                    AnimalsEnabled = Animals.isOn,
                    Sound = Silent.isOn ? SoundMode.Silent : SoundMode.Forest,
                    TimeOfDay = Sunset.isOn ? TimeOfDay.Sunset : TimeOfDay.Day,
                    Motion = GentleMotion.isOn ? MotionMode.Gentle : MotionMode.None,
                };
            Controller.SubmitCheckIn(report, prefs);
        }
    }
}
