using System.Collections.Generic;
using UnityEngine;

namespace Salvo
{
    /// <summary>Launch pad. Motors poll the static registry (no trigger callbacks, no physics cost).</summary>
    public class JumpPad : MonoBehaviour
    {
        public static readonly List<JumpPad> All = new List<JumpPad>();

        public Vector3 launchVelocity = new Vector3(0, 14f, 6f);
        public float radius = 1.1f;
        public float height = 0.6f;
        public Transform ring;   // visual that pulses
        public Vector3 LaunchWorld => transform.rotation * launchVelocity;
        float pulse;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public bool Inside(Vector3 feet)
        {
            Vector3 d = feet - transform.position;
            if (d.y < -0.3f || d.y > height) return false;
            d.y = 0;
            return d.sqrMagnitude < radius * radius;
        }

        public void PlayEffect()
        {
            pulse = 1f;
            AudioManager.Play3D(AudioManager.Bank != null ? AudioManager.Bank.jumpPad : null, transform.position, 0.9f);
            FxManager.I?.Dust(transform.position + Vector3.up * 0.2f, Vector3.up, 6, new Color(0.95f, 0.9f, 0.75f));
        }

        void Update()
        {
            if (ring == null) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 3f);
            float idle = 1f + Mathf.Sin(Time.time * 4f) * 0.04f;
            ring.localScale = Vector3.one * (idle + pulse * 0.35f);
            ring.localPosition = new Vector3(0, 0.08f + pulse * 0.15f, 0);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 p = transform.position + Vector3.up;
            Vector3 v = launchVelocity;
            for (int i = 0; i < 40; i++)
            {
                Vector3 n = p + v * 0.05f;
                v.y -= GameRules.Gravity * 0.05f;
                Gizmos.DrawLine(p, n);
                p = n;
            }
        }
#endif
    }
}
