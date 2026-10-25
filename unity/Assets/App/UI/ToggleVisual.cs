using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Pill-style toggle: the whole background and label change colour when selected, so a choice is visible from
    /// across the panel in a headset (a small checkmark is not). Hover and press still tint through the ColorBlock.
    /// </summary>
    public sealed class ToggleVisual : MonoBehaviour
    {
        public Toggle Toggle;
        public Image Background;
        public Text Label;
        public Color OnFill = Color.white, OffFill = Color.gray, OnText = Color.black, OffText = Color.white;

        // Read isOn rather than the event value: another listener (RatingRow) may restore the toggle in the same event.
        void Awake() => Toggle.onValueChanged.AddListener(_ => Apply(Toggle.isOn));

        void OnEnable() => Apply(Toggle.isOn);

        public void Apply(bool on)
        {
            Background.color = on ? OnFill : OffFill;
            if (Label != null) Label.color = on ? OnText : OffText;
        }
    }
}
