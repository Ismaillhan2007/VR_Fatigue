using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class SummaryPanel : FlowPanel
    {
        public Text Body;
        public Button EnterSceneButton;
        public Button FinishReportButton;
        public Toggle CloudExplanation { get; private set; }
        public Button GenerateGeminiButton { get; private set; }

        public override FlowState State => FlowState.Summary;

        void Awake()
        {
            EnterSceneButton.onClick.AddListener(() => Controller.ChooseRelaxation());
            FinishReportButton.onClick.AddListener(() => Controller.FinishWithReport());
        }

        public override void OnShow()
        {
            // Reuse the check-in's readable consent control, including its reviewed wording.
            if (CloudExplanation == null)
            {
                var checkIn = Controller.Panels.OfType<CheckInPanel>().First();
                CloudExplanation = Instantiate(checkIn.CloudExplanation, FinishReportButton.transform.parent.parent);
                CloudExplanation.name = "Report Gemini consent";
                CloudExplanation.transform.SetSiblingIndex(FinishReportButton.transform.parent.GetSiblingIndex());
                GenerateGeminiButton = Instantiate(FinishReportButton, FinishReportButton.transform.parent);
                GenerateGeminiButton.name = "Generate Gemini explanation";
                GenerateGeminiButton.GetComponentInChildren<Text>().text = "Generate Gemini explanation";
                GenerateGeminiButton.onClick = new Button.ButtonClickedEvent();
                GenerateGeminiButton.onClick.AddListener(() => Controller.RequestGeminiExplanation(CloudExplanation.isOn));
                CloudExplanation.onValueChanged.AddListener(_ => Refresh());
            }
            CloudExplanation.SetIsOnWithoutNotify(false);
            Refresh();
        }

        public override void Refresh()
        {
            var c = Controller.Flow.Context;
            // stop_session: show the take-a-break message instead of offering the scene.
            FinishReportButton.interactable = c.Analysis != null;
            FinishReportButton.GetComponentInChildren<Text>().text = Controller.AnalysisInProgress
                ? "Finish with local report (cancel Gemini)" : "Finish with report";
            if (GenerateGeminiButton != null)
                GenerateGeminiButton.interactable = CloudExplanation.isOn && !Controller.AnalysisInProgress
                    && c.Analysis != null && c.Analysis.AiSource != AiSource.Live
                    && c.Analysis.SafetyNotice != SafetyNotice.StopSession;
            EnterSceneButton.interactable = c.Analysis != null && c.Analysis.SafetyNotice != SafetyNotice.StopSession;
            Body.text = (Controller.MetricsFailureMessage == null ? "" : Controller.MetricsFailureMessage + "\n\n")
                + SummaryFormatter.Format(c, Controller.AnalysisInProgress, Controller.ProbeStatsText);
        }
    }
}
