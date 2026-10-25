using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Scene preview and preferences are offered only after a separate relaxation choice.</summary>
    public sealed class RelaxationOptionsPanel : FlowPanel
    {
        public Text Preview;
        public Toggle ChooseSettings;
        public GameObject PreferenceGroup;
        public Toggle Animals, Silent, Sunset, GentleMotion;
        public Button EnterButton, BackButton;
        public override FlowState State => FlowState.RelaxationOptions;

        void Awake()
        {
            ChooseSettings.onValueChanged.AddListener(_ => Refresh());
            foreach (var toggle in new[] { Animals, Silent, Sunset, GentleMotion })
                toggle.onValueChanged.AddListener(_ => Refresh());
            EnterButton.onClick.AddListener(() => Controller.EnterScene(SelectedPreferences()));
            BackButton.onClick.AddListener(() => Controller.CancelRelaxation());
        }

        public override void OnShow()
        {
            ChooseSettings.SetIsOnWithoutNotify(false);
            Animals.SetIsOnWithoutNotify(true);
            Silent.SetIsOnWithoutNotify(false);
            Sunset.SetIsOnWithoutNotify(true);
            GentleMotion.SetIsOnWithoutNotify(true);
            Refresh();
        }

        Preferences SelectedPreferences() => !ChooseSettings.isOn ? null : new Preferences
        {
            AnimalsEnabled = Animals.isOn,
            Sound = Silent.isOn ? SoundMode.Silent : SoundMode.Forest,
            TimeOfDay = Sunset.isOn ? TimeOfDay.Sunset : TimeOfDay.Day,
            Motion = GentleMotion.isOn ? MotionMode.Gentle : MotionMode.None,
        };

        public override void Refresh()
        {
            PreferenceGroup.SetActive(ChooseSettings.isOn);
            if (Controller == null) return;
            var analysis = Controller.Flow.Context.Analysis;
            EnterButton.interactable = analysis != null && analysis.SafetyNotice != SafetyNotice.StopSession;
            if (analysis == null) { Preview.text = "Preparing preview..."; return; }
            var config = SceneConfigSanitizer.Sanitize(analysis.SceneConfig, SelectedPreferences());
            Preview.text = "Optional forest preview\n\n"
                + $"Lighting: {config.TimeOfDay.ToString().ToLowerInvariant()}\n"
                + $"Sound: {(config.Sound == SoundMode.Silent ? "silent" : "forest ambience")}\n"
                + $"Ambient movement: {(config.Motion == MotionMode.None ? "off" : "gentle")}\n"
                + $"Animals: {(config.AnimalsEnabled ? "shown" : "hidden")}"
                + "\n\n" + analysis.SceneRationale + "\n\nYou may return to your report without entering.";
        }
    }
}
