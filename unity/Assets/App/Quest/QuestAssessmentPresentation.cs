using System;
using App.UI;
using App.UI.Panels;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace App.Quest
{
    /// <summary>One world-locked UI at a comfortable distance; only probes follow the tracked head.</summary>
    public sealed class QuestAssessmentPresentation : MonoBehaviour
    {
        public const float PanelDistance = 2f;
        public const float MetresPerPixel = 0.001f;
        public const float TextPixelsPerUnit = 3f;
        public Canvas Canvas { get; private set; }
        public Camera Head { get; private set; }
        Text _status;

        public void Configure(SessionController controller, Camera head)
        {
            if (Canvas != null) throw new InvalidOperationException("Quest UI is already configured");
            Head = head != null ? head : throw new ArgumentNullException(nameof(head));
            Canvas = controller.ProvenanceBar.GetComponentInParent<Canvas>();
            var rect = (RectTransform)Canvas.transform;
            Canvas.renderMode = RenderMode.WorldSpace;
            Canvas.worldCamera = Head;
            rect.sizeDelta = new Vector2(1920, 1080);
            rect.localScale = Vector3.one * MetresPerPixel;
            var scaler = Canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            // Legacy Text rasterizes glyphs at fontSize * dynamicPixelsPerUnit texels; at 1 they were blurry in the headset.
            scaler.dynamicPixelsPerUnit = TextPixelsPerUnit;
            foreach (var raycaster in Canvas.GetComponents<GraphicRaycaster>()) raycaster.enabled = false;
            Canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            var events = FindAnyObjectByType<EventSystem>();
            // Reuse an XRUIInputModule that XRI may already have created for enabled ray interactors: disabling it
            // would leave those rays registered with a dead module (visible ray, trigger does nothing).
            var input = events.GetComponent<XRUIInputModule>();
            foreach (var module in events.GetComponents<BaseInputModule>()) module.enabled = module == input;
            if (input == null) input = events.gameObject.AddComponent<XRUIInputModule>();
            input.enabled = true;
            input.enableXRInput = true;
            input.enableMouseInput = false;
            input.enableTouchInput = input.enableGamepadInput = input.enableJoystickInput = false;
            foreach (var text in Canvas.GetComponentsInChildren<Text>(true))
            {
                text.resizeTextForBestFit = false;
                text.fontSize = Mathf.Max(text.fontSize, 22);
            }
            foreach (var panel in controller.Panels)
            {
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = new Vector2(panelRect.anchorMin.x, Mathf.Max(panelRect.anchorMin.y, 0.08f));
                // Builder-defined flexible text bodies include report, Done and optional preview.
                // Adapt by layout role so newly generated panels also retain readable text.
                foreach (var body in panel.GetComponentsInChildren<Text>(true))
                {
                    var layout = body.GetComponent<LayoutElement>();
                    if (layout != null && layout.flexibleHeight > 0) MakeScrollable(body);
                }
                if (panel is BaselinePanel baseline) MakeScrollable(baseline.Status);
                if (panel is ScenePanel) panelRect.anchorMax = new Vector2(panelRect.anchorMax.x, 0.58f);
            }
            var pauseRect = (RectTransform)controller.PauseOverlay.transform;
            pauseRect.anchorMin = new Vector2(pauseRect.anchorMin.x, 0.08f);
            foreach (var button in Canvas.GetComponentsInChildren<Button>(true))
            {
                var size = button.GetComponent<LayoutElement>();
                if (size != null) { size.preferredWidth = Mathf.Max(330, size.preferredWidth); size.preferredHeight = 60; }
                var row = button.transform.parent.GetComponent<LayoutElement>();
                if (row != null) row.preferredHeight = 64;
            }
            _status = new GameObject("Quest status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            _status.transform.SetParent(Canvas.transform, false);
            var statusRect = (RectTransform)_status.transform;
            statusRect.anchorMin = new Vector2(0.05f, 0);
            statusRect.anchorMax = new Vector2(0.95f, 0.07f);
            statusRect.offsetMin = statusRect.offsetMax = Vector2.zero;
            _status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _status.fontSize = 22;
            _status.color = Color.white;
            _status.alignment = TextAnchor.MiddleLeft;
            _status.raycastTarget = false;
            SetStatus("Point and press trigger. Thumbstick scrolls. Y / B brings Pause into view. Replay requires no eye permission.");
            var probe = controller.ProbeRunner;
            probe.HeadCamera = Head;
            probe.WorldSpaceHeadRelative = true;
            var probeCanvas = probe.CanvasRect.GetComponent<Canvas>();
            probeCanvas.renderMode = RenderMode.WorldSpace;
            probeCanvas.worldCamera = Head;
            probeCanvas.sortingOrder = 50;
            probeCanvas.overrideSorting = true;
            probe.CanvasRect.SetParent(Head.transform, false);
            probe.CanvasRect.localScale = Vector3.one * MetresPerPixel;
            probe.CanvasRect.localPosition = Vector3.forward * PanelDistance;
            probe.CanvasRect.sizeDelta = new Vector2(4000, 3000);
            foreach (var node in Canvas.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = LayerMask.NameToLayer("UI");
            PlaceInFrontOfHead();
        }

        /// <summary>
        /// Puts the panel 2 m along the current horizontal view direction, upright at eye height (no pitch or roll),
        /// so it appears where the person is looking. The panel then stays world-locked.
        /// </summary>
        public void PlaceInFrontOfHead()
        {
            if (Canvas == null || Head == null) return;
            var head = Head.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(-head.up * Mathf.Sign(head.forward.y), Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Canvas.transform.SetPositionAndRotation(head.position + forward * PanelDistance, Quaternion.LookRotation(forward, Vector3.up));
        }

        public void SetStatus(string message) { if (_status != null) _status.text = message; }

        static void MakeScrollable(Text body)
        {
            var parent = body.transform.parent;
            var index = body.transform.GetSiblingIndex();
            var scrollGo = new GameObject("Readable report", typeof(RectTransform), typeof(LayoutElement), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            scrollGo.transform.SetSiblingIndex(index);
            scrollGo.GetComponent<LayoutElement>().flexibleHeight = 1;
            scrollGo.GetComponent<LayoutElement>().minHeight = 120;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch((RectTransform)viewport.transform);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1); contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.transform.SetParent(content.transform, false);
            body.GetComponent<LayoutElement>().flexibleHeight = 0;
            body.fontSize = 24;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform; scroll.content = contentRect;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45;
        }

        static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
