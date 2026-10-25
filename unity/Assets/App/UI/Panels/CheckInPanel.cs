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
        public Text Hint;
        public Button SubmitButton;
        public Toggle CloudExplanation { get; private set; }

        public override FlowState State => FlowState.CheckIn;

        void Awake()
        {
            FreeText.characterLimit = SelfReport.MaxFreeText;
            foreach (var t in ChipToggles) t.onValueChanged.AddListener(_ => EnforceChipLimit());
            SubmitButton.onClick.AddListener(Submit);
            var choice = DefaultControls.CreateToggle(new DefaultControls.Resources());
            choice.name = "Optional Gemini summary consent";
            choice.transform.SetParent(SubmitButton.transform.parent.parent, false);
            choice.transform.SetSiblingIndex(SubmitButton.transform.parent.GetSiblingIndex());
            choice.AddComponent<LayoutElement>().preferredHeight = 90;
            CloudExplanation = choice.GetComponent<Toggle>();
            CloudExplanation.isOn = false;
            if (ChipToggles.Count > 0)
            {
                MatchImage(CloudExplanation.targetGraphic, ChipToggles[0].targetGraphic);
                MatchImage(CloudExplanation.graphic, ChipToggles[0].graphic);
            }
            ((UnityEngine.RectTransform)CloudExplanation.targetGraphic.transform).sizeDelta = new UnityEngine.Vector2(36, 36);
            var label = choice.GetComponentInChildren<Text>();
            label.font = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
            label.fontSize = 22; label.supportRichText = false; label.color = Hint.color;
            label.rectTransform.offsetMin = new UnityEngine.Vector2(48, 0);
            label.text = "Optional: send aggregate eye measurements and ratings to the configured backend and Google Gemini for an experimental explanation. Raw gaze and free text stay on-device. Google processes the summary under its Gemini terms. You can finish with a local report.";
            foreach (var node in choice.GetComponentsInChildren<UnityEngine.Transform>(true))
                node.gameObject.layer = UnityEngine.LayerMask.NameToLayer("UI");
        }

        static void MatchImage(Graphic target, Graphic source)
        {
            if (target is Image destination && source is Image template)
            { destination.sprite = template.sprite; destination.type = template.type; destination.color = template.color; }
        }

        public override void OnShow()
        {
            EyeStrain.ResetValue();
            MentalFatigue.ResetValue();
            foreach (var t in ChipToggles) t.isOn = false;
            FreeText.text = "";
            CloudExplanation.SetIsOnWithoutNotify(false);
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
            Controller.SubmitCheckIn(report, null, CloudExplanation.isOn);
        }
    }
}
