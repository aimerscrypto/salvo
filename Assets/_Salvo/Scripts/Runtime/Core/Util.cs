using System.Collections.Generic;
using UnityEngine;

namespace Salvo
{
    /// <summary>Critically damped spring for procedural animation (camera kick, weapon sway...).</summary>
    public struct Spring3
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public void Step(Vector3 target, float frequency, float damping, float dt)
        {
            // semi-implicit damped spring, stable for large dt
            float w = frequency * 2f * Mathf.PI;
            Vector3 f = (target - Value) * (w * w) - Velocity * (2f * damping * w);
            Velocity += f * dt;
            Value += Velocity * dt;
        }

        public void Kick(Vector3 impulse) => Velocity += impulse;
    }

    public struct Spring1
    {
        public float Value;
        public float Velocity;

        public void Step(float target, float frequency, float damping, float dt)
        {
            float w = frequency * 2f * Mathf.PI;
            float f = (target - Value) * (w * w) - Velocity * (2f * damping * w);
            Velocity += f * dt;
            Value += Velocity * dt;
        }
    }

    public static class SalvoMath
    {
        public static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1f - Mathf.Exp(-lambda * dt));
        public static Vector3 Damp(Vector3 a, Vector3 b, float lambda, float dt) => Vector3.Lerp(a, b, 1f - Mathf.Exp(-lambda * dt));
        public static Quaternion Damp(Quaternion a, Quaternion b, float lambda, float dt) => Quaternion.Slerp(a, b, 1f - Mathf.Exp(-lambda * dt));

        /// <summary>Random direction inside a cone (degrees) around forward, with a gaussian-ish center bias.</summary>
        public static Vector3 Spread(Vector3 forward, float degrees, System.Random rng)
        {
            if (degrees <= 0.0001f) return forward;
            float r = (float)(rng.NextDouble() + rng.NextDouble()) * 0.5f; // triangular: tighter center
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float ang = r * degrees;
            Quaternion q = Quaternion.LookRotation(forward);
            Vector3 local = Quaternion.Euler(Mathf.Sin(a) * ang, Mathf.Cos(a) * ang, 0f) * Vector3.forward;
            return q * local;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }
    }

    /// <summary>Tiny GameObject pool keyed by prefab.</summary>
    public class SimplePool
    {
        readonly GameObject prefab;
        readonly Transform parent;
        readonly Stack<GameObject> free = new Stack<GameObject>();

        public SimplePool(GameObject prefab, Transform parent, int prewarm = 0)
        {
            this.prefab = prefab;
            this.parent = parent;
            for (int i = 0; i < prewarm; i++) Release(Object.Instantiate(prefab, parent));
        }

        public GameObject Get()
        {
            var go = free.Count > 0 ? free.Pop() : Object.Instantiate(prefab, parent);
            go.SetActive(true);
            return go;
        }

        public void Release(GameObject go)
        {
            go.SetActive(false);
            free.Push(go);
        }
    }

    public static class TransformExt
    {
        public static Transform FindDeep(this Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = t.GetChild(i).FindDeep(name);
                if (r != null) return r;
            }
            return null;
        }

        public static void SetLayerRecursive(this GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) c.gameObject.SetLayerRecursive(layer);
        }
    }
}
