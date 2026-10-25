using UnityEngine;
using VrFatigue.Relaxation;

namespace VrFatigue.Squirrel
{
    // Very gentle idle for the placeholder squirrel: slow breathing scale, tail sway, a slight head-turn of the
    // whole body. No travel, no jumps, never moves toward the viewer. Driven by SceneConfig motion via IAmbientMotion.
    public class SquirrelIdle : MonoBehaviour, IAmbientMotion
    {
        // Matches TAIL_BASE in tools/generate_squirrel.py (model space, metres).
        [SerializeField] Vector3 tailBase = new Vector3(0f, 0.07f, -0.07f);
        [SerializeField] Transform tail; // optional; found by name "Tail" if empty

        Transform pivot;
        bool active;
        float speed;      // 0..0.5 from SceneConfig.motion_speed
        float amount;     // smoothed 0..1 so motion eases in/out
        Vector3 baseScale;
        Quaternion baseRot;

        void Awake()
        {
            baseScale = transform.localScale;
            baseRot = transform.localRotation;
            if (tail == null) tail = FindDeep(transform, "Tail");
            if (tail != null)
            {
                pivot = new GameObject("TailPivot").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = tailBase;
                tail.SetParent(pivot, true);
            }
        }

        public void SetMotion(bool enabled, float motionSpeed)
        {
            active = enabled;
            speed = Mathf.Clamp(motionSpeed, 0f, 0.5f);
        }

        void Update()
        {
            amount = Mathf.MoveTowards(amount, active ? 1f : 0f, Time.deltaTime * 0.5f);
            float t = Time.time * (0.4f + speed * 2f);
            float a = amount;
            if (pivot != null) pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t) * 8f * a);
            transform.localRotation = baseRot * Quaternion.Euler(0f, Mathf.Sin(t * 0.37f) * 6f * a, 0f);
            transform.localScale = baseScale * (1f + Mathf.Sin(t * 1.3f) * 0.01f * a);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.name == name) return c;
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
