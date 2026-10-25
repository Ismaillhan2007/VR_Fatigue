using App.Capture;
using App.Core.Flow;
using App.UI;
using App.UI.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace App.Quest
{
    /// <summary>Explicit native setup controls, composed inside the existing consent panel without scene YAML edits.</summary>
    public sealed class QuestEyeSetupPanel : MonoBehaviour
    {
        SessionController _controller;
        Toggle _calibration;
        GameObject _diagnosticReset;
        // Styled templates from the generated consent panel, so these runtime controls match the rest of the UI.
        Button _buttonTemplate;
        public void Configure(SessionController controller)
        {
            _controller = controller;
            var consent = controller.Panels.Find(p => p is ConsentPanel);
            _buttonTemplate = ((ConsentPanel)consent).OpenSettingsButton;
            var row = new GameObject("Eye source and permission", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(consent.transform, false);
            row.transform.SetSiblingIndex(consent.transform.childCount - 2);
            row.GetComponent<LayoutElement>().preferredHeight = 64;
            row.GetComponent<HorizontalLayoutGroup>().spacing = 8;
            Button(row.transform, "Use native eyes", () => { controller.SelectEyeSource(EyeSourceMode.Hardware); _calibration.isOn = false; });
            Button(row.transform, "Use replay", () => { controller.SelectEyeSource(EyeSourceMode.Replay); _calibration.isOn = false; });
            Button(row.transform, "I consent: request eye permission", controller.RequestHardwarePermission);
            var diagnostic = new GameObject("Separate native diagnostic", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            diagnostic.transform.SetParent(consent.transform, false);
            diagnostic.transform.SetSiblingIndex(consent.transform.childCount - 2);
            diagnostic.GetComponent<LayoutElement>().preferredHeight = 60;
            Button(diagnostic.transform, "Run separate 60-second eye diagnostic", controller.StartNativeDiagnostic);
            var baseline = controller.Panels.Find(p => p is BaselinePanel);
            _diagnosticReset = new GameObject("Diagnostic completion", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            _diagnosticReset.transform.SetParent(baseline.transform, false);
            _diagnosticReset.GetComponent<LayoutElement>().preferredHeight = 60;
            Button(_diagnosticReset.transform, "Clear diagnostic and start a new session", () => { controller.Exit(); controller.Restart(); });
            _diagnosticReset.SetActive(false);
            var go = Instantiate(((ConsentPanel)consent).LocalDevelopment.gameObject, consent.transform, false);
            go.name = "Operator calibration confirmation";
            go.SetActive(true);
            go.transform.SetSiblingIndex(consent.transform.childCount - 2);
            _calibration = go.GetComponent<Toggle>(); _calibration.onValueChanged.RemoveAllListeners(); _calibration.isOn = false;
            var visual = go.GetComponent<ToggleVisual>();
            if (visual != null) _calibration.onValueChanged.AddListener(_ => visual.Apply(_calibration.isOn));
            var label = go.GetComponentInChildren<Text>();
            label.fontSize = 24;
            label.text = "I completed headset eye calibration for this wearer (operator confirmation, not a measured calibration result).";
            _calibration.onValueChanged.AddListener(controller.ConfirmHardwareCalibration);
            foreach (var node in diagnostic.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = LayerMask.NameToLayer("UI");
            foreach (var node in _diagnosticReset.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = LayerMask.NameToLayer("UI");
            foreach (var node in row.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = LayerMask.NameToLayer("UI");
            foreach (var node in go.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        void Update()
        {
            if (_controller == null || _calibration == null) return;
            _diagnosticReset.SetActive(_controller.NativeDiagnosticComplete);
            _calibration.interactable = _controller.EyeSource == EyeSourceMode.Hardware && _controller.Flow.State == FlowState.Consent;
            if (!_controller.HardwareCalibrationConfirmed) _calibration.SetIsOnWithoutNotify(false);
        }
        void Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = Instantiate(_buttonTemplate.gameObject, parent, false); go.name = text;
            go.SetActive(true);
            var label = go.GetComponentInChildren<Text>();
            label.text = text; label.fontSize = 26;
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null) layout = go.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1; layout.minWidth = 250; layout.preferredHeight = 64;
            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
