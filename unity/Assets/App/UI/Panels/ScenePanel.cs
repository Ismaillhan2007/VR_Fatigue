using System.Collections.Generic;
using System.Linq;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class ScenePanel : FlowPanel
    {
        public Text Body;
        public Button MuteButton;
        public Button FinishButton;

        public override FlowState State => FlowState.Scene;

        void Awake()
        {
            MuteButton.onClick.AddListener(() => Controller.MuteScene());
            FinishButton.onClick.AddListener(() => Controller.FinishScene());
        }

        public override void OnShow() => Refresh();

        public override void Refresh() => Body.text = Controller.SceneStatus;
    }
}
