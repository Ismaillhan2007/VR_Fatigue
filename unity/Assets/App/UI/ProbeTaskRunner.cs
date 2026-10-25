using App.Core.Capture;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// Shows the head-locked probe target: a point <see cref="ProbeSchedule.DistanceMetres"/> in front of the head camera
    /// along the trial's head-local direction, drawn on an overlay canvas above the panels. On the headset this becomes
    /// a world-space object parented to the head; the direction and timing stay the same.
    /// </summary>
    public sealed class ProbeTaskRunner : MonoBehaviour
    {
        public RectTransform CanvasRect;
        public RectTransform Target;
        public Camera HeadCamera;

        public bool Visible => Target != null && Target.gameObject.activeSelf;

        public void Show(System.Numerics.Vector3 headLocalDirection)
        {
            var cam = HeadCamera != null ? HeadCamera : Camera.main;
            if (cam == null || Target == null) return;
            var dir = new Vector3(headLocalDirection.X, headLocalDirection.Y, headLocalDirection.Z);
            var world = cam.transform.position + cam.transform.rotation * (dir * ProbeSchedule.DistanceMetres);
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0) { Hide(); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRect, screen, null, out var local);
            Target.anchoredPosition = local;
            Target.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (Target != null) Target.gameObject.SetActive(false);
        }
    }
}
