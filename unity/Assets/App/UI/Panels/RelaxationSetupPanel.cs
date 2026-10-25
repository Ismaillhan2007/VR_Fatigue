using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Shown only after a deliberate optional-relaxation choice; never collects assessment consent.</summary>
    public sealed class RelaxationSetupPanel : FlowPanel
    {
        public Text Body;
        public Toggle ChooseSettings, Animals, Silent, Sunset, GentleMotion;
        public GameObject PreferenceGroup;
        public Button BackButton, EnterButton;
        public override FlowState State => FlowState.RelaxationSetup;
        bool _initializing;

        void Awake()
        {
            foreach (var toggle in new[] { ChooseSettings, Animals, Silent, Sunset, GentleMotion })
                toggle.onValueChanged.AddListener(_ => UpdatePreferences());
            BackButton.onClick.AddListener(() => Controller.CancelRelaxation());
            EnterButton.onClick.AddListener(() => Controller.EnterScene());
        }

        public override void OnShow()
        {
            _initializing = true;
            ChooseSettings.isOn = false;
            Animals.isOn = Sunset.isOn = GentleMotion.isOn = true;
            Silent.isOn = false;
            _initializing = false;
            UpdatePreferences();
        }

        void UpdatePreferences()
        {
            if (_initializing) return;
            PreferenceGroup.SetActive(ChooseSettings.isOn);
            Controller.Flow.SetScenePreferences(!ChooseSettings.isOn ? null : new Preferences
            {
                AnimalsEnabled = Animals.isOn,
                Sound = Silent.isOn ? SoundMode.Silent : SoundMode.Forest,
                TimeOfDay = Sunset.isOn ? TimeOfDay.Sunset : TimeOfDay.Day,
                Motion = GentleMotion.isOn ? MotionMode.Gentle : MotionMode.None,
            });
            Refresh();
        }

        public override void Refresh()
        {
            var a = Controller.Flow.Context.Analysis;
            EnterButton.interactable = a != null && a.SafetyNotice != SafetyNotice.StopSession;
            Body.text = "You can return to the report without entering.\n\n"
                + (Controller.ScenePreview == null ? "" : SummaryFormatter.SceneLine(Controller.ScenePreview))
                + "\n\n" + a?.SceneRationale;
        }
    }
}
