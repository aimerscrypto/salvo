using System.IO;
using UnityEngine;

namespace Salvo.EditorTools
{
    /// <summary>Editor-side helpers used by tooling/automation (render arbitrary viewpoints to PNG).</summary>
    public static class SalvoDebug
    {
        /// <summary>Render from an explicit pose to a PNG file. Works in edit and play mode.</summary>
        public static string Capture(string path, Vector3 pos, Quaternion rot, float fov = 60f, int width = 960, int height = 540, float near = 0.01f, int mask = ~0)
        {
            var go = new GameObject("__DebugCapture");
            try
            {
                var cam = go.AddComponent<Camera>();
                go.transform.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = fov;
                cam.nearClipPlane = near;
                cam.farClipPlane = 800f;
                cam.cullingMask = mask;
                cam.clearFlags = CameraClearFlags.Skybox;
                var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return path;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Side view of the local player's first-person rig.</summary>
        public static string CaptureViewModel(string path, float side = 0.35f)
        {
            var cam = GameCamera.I.transform;
            Vector3 center = cam.position + cam.forward * 0.1f - cam.up * 0.04f + cam.right * 0.05f;
            Vector3 pos = center + cam.right * side + cam.forward * 0.02f;
            return Capture(path, pos, Quaternion.LookRotation(center - pos, cam.up), 50f, 960, 540, 0.005f);
        }
    }
}
