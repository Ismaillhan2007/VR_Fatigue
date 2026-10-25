using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    /// <summary>Forest controls. "Hide menu" collapses everything to one small button and the data/AI labels.</summary>
    public sealed class ScenePanel : FlowPanel
    {
        public Text Body;
        public Image Background;
        public GameObject Content;
        public Text HiddenLabel;
        public Button HideButton;
        public Button ShowButton;
        public Button MuteButton;
        public Button FinishButton;

        public override FlowState State => FlowState.Scene;
        public bool MenuHidden { get; private set; }

        void Awake()
        {
            MuteButton.onClick.AddListener(() => Controller.MuteScene());
            FinishButton.onClick.AddListener(() => Controller.FinishScene());
            HideButton.onClick.AddListener(() => SetMenuHidden(true));
            ShowButton.onClick.AddListener(() => SetMenuHidden(false));
        }

        public override void OnShow() => SetMenuHidden(false);

        void OnDisable()
        {
            // Leaving the scene always restores the full bar.
            if (Controller != null && Controller.ProvenanceBar != null) Controller.ProvenanceBar.gameObject.SetActive(true);
            MenuHidden = false;
        }

        public void SetMenuHidden(bool hidden)
        {
            MenuHidden = hidden;
            Content.SetActive(!hidden);
            Background.enabled = !hidden;
            ShowButton.gameObject.SetActive(hidden);
            HiddenLabel.gameObject.SetActive(hidden);
            if (Controller != null && Controller.ProvenanceBar != null) Controller.ProvenanceBar.gameObject.SetActive(!hidden);
            Refresh();
        }

        public override void Refresh()
        {
            if (Controller == null) return;
            Body.text = Controller.SceneStatus;
            var c = Controller.Flow.Context;
            // Data and AI provenance stay visible while the menu is hidden.
            HiddenLabel.text = ProvenanceLabels.Data(c) + "   ·   " + ProvenanceLabels.Ai(c);
        }
    }
}
