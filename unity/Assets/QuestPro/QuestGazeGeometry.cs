// Adapted from QP_source_2.10.4 MqpHeadPose.cs / GazeGeometry.cs.
// Copyright Texas State University. See docs/licenses/ETRA2027-QP-LICENSE.md
// for PolyForm Noncommercial 1.0.0 terms and required notices.
using UnityEngine;

namespace VRFatigue.QuestPro
{
    public static class QuestGazeGeometry
    {
        public static bool TryHeadLocalDirection(Quaternion eye, Quaternion head, out Vector3 direction)
        {
            direction = default;
            if (!Valid(eye) || !Valid(head)) return false;
            direction = (Quaternion.Inverse(head.normalized) * eye.normalized) * Vector3.forward;
            return true;
        }

        public static bool Valid(Quaternion rotation)
        {
            var norm = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            return !float.IsNaN(norm) && !float.IsInfinity(norm) && norm > Mathf.Epsilon;
        }
    }
}
