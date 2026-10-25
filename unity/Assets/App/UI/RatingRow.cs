using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>1-7 slider with its label (1 = not at all, 7 = extremely).</summary>
    public sealed class RatingRow : MonoBehaviour
    {
        public Slider Slider;
        public Text ValueLabel;

        public int Value => Mathf.RoundToInt(Slider.value);

        void Awake()
        {
            Slider.minValue = 1;
            Slider.maxValue = 7;
            Slider.wholeNumbers = true;
            Slider.onValueChanged.AddListener(_ => UpdateLabel());
            UpdateLabel();
        }

        public void ResetValue(int value = 1)
        {
            Slider.value = value;
            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (ValueLabel != null) ValueLabel.text = $"{Value} / 7";
        }
    }
}
