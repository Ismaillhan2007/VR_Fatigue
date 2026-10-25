using App.Core.Flow;
using UnityEngine.UI;

namespace App.UI.Panels
{
    public sealed class ConsentPanel : FlowPanel
    {
        public Text Body, ConnectionStatus;
        public Button StartButton, OpenSettingsButton, SaveSettingsButton, CheckConnectionButton;
        public UnityEngine.GameObject SettingsRoot;
        public InputField BackendUrl, BackendToken;
        public Toggle LocalDevelopment, InventedDemo, AllowUpload;

        public override FlowState State => FlowState.Consent;

        void Awake()
        {
            StartButton.onClick.AddListener(() => { ApplySettings(); Controller.AcceptConsent(); });
            OpenSettingsButton.onClick.AddListener(() => SettingsRoot.SetActive(true));
            SaveSettingsButton.onClick.AddListener(() => { ApplySettings(); SettingsRoot.SetActive(false); });
            CheckConnectionButton.onClick.AddListener(() => { ApplySettings(); Controller.CheckBackendConnection(); });
        }

        void ApplySettings() => Controller.ConfigureBackend(BackendUrl.text, BackendToken.text,
            LocalDevelopment.isOn, InventedDemo.isOn, AllowUpload.isOn);

        public void ClearCredentials()
        {
            if (BackendToken != null) BackendToken.text = "";
            if (InventedDemo != null) InventedDemo.isOn = false;
            if (AllowUpload != null) AllowUpload.isOn = false;
        }

        public override void OnShow()
        {
            BackendUrl.SetTextWithoutNotify(Controller.BackendBaseUrl);
            BackendToken.SetTextWithoutNotify(Controller.BackendToken);
            LocalDevelopment.SetIsOnWithoutNotify(Controller.BackendLocalDevelopment);
            InventedDemo.SetIsOnWithoutNotify(Controller.InventedDemoSession);
            AllowUpload.SetIsOnWithoutNotify(Controller.AllowBackendUpload);
            SettingsRoot.SetActive(false);
            Refresh();
        }

        public override void Refresh()
        {
            Body.text = "Experimental wellness prototype. Not a medical device or diagnosis. Pause or exit at any time. "
                + "Reports stay in memory until Exit or New session. Free text stays on-device. "
                + "Backend upload sends aggregates and ratings to the configured server. Networked demos accept wholly invented data only; "
                + "participant sessions use the on-device report. Gemini stays disabled for interactive check-ins.\n\n"
                + $"Eye data source: {Controller.EyeSourceDescription}"
                + (Controller.ReplayLoadStatus == null ? "" : "\n\n" + Controller.ReplayLoadStatus);
            ConnectionStatus.text = Controller.BackendConnectionStatus;
            StartButton.interactable = !Controller.ReplayLoading;
            OpenSettingsButton.interactable = !Controller.ReplayLoading;
            CheckConnectionButton.interactable = !Controller.BackendCheckInProgress && !Controller.ReplayLoading;
            SaveSettingsButton.interactable = !Controller.ReplayLoading;
        }
    }
}
