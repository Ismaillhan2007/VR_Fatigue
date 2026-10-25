using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 1-7 rating as seven large number buttons (1 = not at all, 7 = extremely): point and press one. Nothing is
    /// preselected, so a rating is always the person's own choice; pressing the selected number again keeps it.
    /// </summary>
    public sealed class RatingRow : MonoBehaviour
    {
        public const int Max = 7;

        /// <summary>Options[i] is the number i + 1.</summary>
        public List<Toggle> Options = new List<Toggle>();

        /// <summary>The selected number, or 0 when nothing is selected yet.</summary>
        public int Value { get; private set; }
        public bool HasValue => Value > 0;
        public event Action Changed;

        void Awake()
        {
            for (var i = 0; i < Options.Count; i++)
            {
                var number = i + 1;
                Options[i].onValueChanged.AddListener(on =>
                {
                    if (on) Select(number);
                    else if (Value == number) Show(); // a second press keeps the choice
                });
            }
            Show();
        }

        public void Select(int value)
        {
            if (value < 1 || value > Max) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
            Show();
            Changed?.Invoke();
        }

        public void ResetValue()
        {
            Value = 0;
            Show();
            Changed?.Invoke();
        }

        void Show()
        {
            for (var i = 0; i < Options.Count; i++)
            {
                var on = i + 1 == Value;
                Options[i].SetIsOnWithoutNotify(on);
                var visual = Options[i].GetComponent<ToggleVisual>();
                if (visual != null) visual.Apply(on);
            }
        }
    }
}
