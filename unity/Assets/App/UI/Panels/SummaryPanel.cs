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
        public Button FinishReportButton;

        public override FlowState State => FlowState.Summary;

        void Awake()
        {
            EnterSceneButton.onClick.AddListener(() => Controller.ChooseRelaxation());
            FinishReportButton.onClick.AddListener(() => Controller.FinishWithReport());
        }

        public override void OnShow() => Refresh();

        public override void Refresh()
        {
            var c = Controller.Flow.Context;
            // stop_session: show the take-a-break message instead of offering the scene.
            FinishReportButton.interactable = c.Analysis != null && !Controller.AnalysisInProgress;
            EnterSceneButton.interactable = c.Analysis != null && c.Analysis.SafetyNotice != SafetyNotice.StopSession;
            FinishReportButton.interactable = c.Analysis != null && !Controller.AnalysisInProgress;
            Body.text = (Controller.MetricsFailureMessage == null ? "" : Controller.MetricsFailureMessage + "\n\n")
                + SummaryFormatter.Format(c, Controller.AnalysisInProgress, Controller.ProbeStatsText);
        }
    }
}
