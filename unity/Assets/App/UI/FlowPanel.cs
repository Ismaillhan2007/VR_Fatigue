using App.Core.Flow;
using UnityEngine;

namespace App.UI
{
    /// <summary>Base for one screen of the assessment flow. Panels are prefabs so they can move to a world-space canvas.</summary>
    public abstract class FlowPanel : MonoBehaviour
    {
        protected SessionController Controller { get; private set; }

        /// <summary>The flow state this panel is shown for.</summary>
        public abstract FlowState State { get; }

        public void Bind(SessionController controller) => Controller = controller;

        /// <summary>Called each time the panel becomes visible.</summary>
        public virtual void OnShow() { }

        /// <summary>Called while visible when session data change.</summary>
        public virtual void Refresh() { }
    }
}
