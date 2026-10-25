using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class SummaryPanel : FlowPanel
    {
        public Text Body;
        public Button EnterSceneButton;

        public override FlowState State => FlowState.Summary;

        void Awake() => EnterSceneButton.onClick.AddListener(() => Controller.EnterScene());

        public override void OnShow() => Refresh();

        public override void Refresh()
        {
            var c = Controller.Flow.Context;
            // stop_session: show the take-a-break message instead of offering the scene.
            EnterSceneButton.interactable = c.Analysis != null && c.Analysis.SafetyNotice != SafetyNotice.StopSession;
            Body.text = SummaryFormatter.Format(c, Controller.AnalysisInProgress);
        }
    }
}
