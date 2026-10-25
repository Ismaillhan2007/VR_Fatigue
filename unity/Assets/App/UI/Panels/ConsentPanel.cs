using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class ConsentPanel : FlowPanel
    {
        public Text Body;
        public Button StartButton;

        public override FlowState State => FlowState.Consent;

        void Awake() => StartButton.onClick.AddListener(() => Controller.AcceptConsent());

        public override void OnShow() => Body.text =
            "Experimental wellness prototype. Not a medical device and not a diagnosis.\n\n"
            + "We record eye-movement aggregates for this session only. Nothing leaves this computer except a summary "
            + "without your free text (and only if a backend is configured). Your report stays in memory until Exit or New session.\n\n"
            + $"Eye data source: {Controller.EyeSourceDescription}\n\nYou can pause or exit at any time.";
    }
}
