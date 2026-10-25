using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class LoadTaskPanel : FlowPanel
    {
        public Text Status;
        public Button SkipButton;

        public override FlowState State => FlowState.LoadTask;

        void Awake() => SkipButton.onClick.AddListener(() => Controller.EndCaptureEarly());

        public override void Refresh() => Status.text =
            "Task: keep following the yellow target (same layout as the baseline; the reading/visual-search load is a placeholder).\n"
            + $"{Controller.PhaseRemainingSeconds:0} s left ({Controller.SampleCount} samples, {Controller.ProbeCount} targets, {Controller.EyeSourceDescription})";
    }
}
