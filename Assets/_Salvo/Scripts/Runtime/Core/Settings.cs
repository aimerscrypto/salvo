using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Salvo
{
    public enum GraphicsPreset { Performance = 0, Balanced = 1, Quality = 2, Custom = 3 }

    [Serializable]
    public class GameSettings
    {
        // graphics
        public int preset = (int)GraphicsPreset.Balanced;
        public float renderScale = 1f;
        public int msaa = 2;               // 1, 2, 4
        public int shadows = 2;            // 0 off, 1 hard, 2 soft
        public float shadowDistance = 45f;
        public int fpsCap = 144;           // 0 = unlimited
        public bool vsync;
        public int fullscreen = 1;         // 0 windowed, 1 borderless, 2 exclusive
        // controls
        public float sensitivity = 1.6f;
        public float adsSensitivity = 0.85f;
        public bool invertY;
        public bool toggleAim;
        public bool toggleCrouch;
        // gameplay / view
        public float fov = 85f;
        public bool showFps;
        public int crosshairColor;         // index into palette
        public float crosshairSize = 1f;
        public bool viewBob = true;
        public bool damageNumbers = true;
        // audio (0..1)
        public float master = 0.8f;
        public float sfx = 1f;
        public float music = 0.5f;
        public float ui = 0.8f;
    }

    /// <summary>Persistent settings and their application to URP / QualitySettings / audio.</summary>
    public static class Settings
    {
        public static GameSettings Data { get; private set; } = new GameSettings();
        public static event Action Changed;
        const string Key = "salvo.settings.v1";

        public static readonly Color[] CrosshairColors =
        {
            new Color(1f, 1f, 1f), new Color(0.55f, 1f, 0.45f), new Color(1f, 0.85f, 0.3f), new Color(0.35f, 0.95f, 1f), new Color(1f, 0.45f, 0.75f)
        };
        public static readonly string[] CrosshairColorNames = { "White", "Green", "Yellow", "Cyan", "Pink" };

        public static void Load()
        {
            var json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { Data = JsonUtility.FromJson<GameSettings>(json) ?? new GameSettings(); }
                catch { Data = new GameSettings(); }
            }
            else
            {
                Data = new GameSettings();
                ApplyPreset(AutoDetectPreset(), false);
            }
        }

        public static void ResetDefaults()
        {
            Data = new GameSettings();
            ApplyPreset(AutoDetectPreset());
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        static GraphicsPreset AutoDetectPreset()
        {
            // integrated GPUs and low memory devices start on Performance
            bool integrated = SystemInfo.graphicsDeviceName.Contains("Intel") || SystemInfo.graphicsMemorySize < 1500;
            if (integrated || SystemInfo.processorCount <= 2) return GraphicsPreset.Performance;
            return SystemInfo.graphicsMemorySize >= 4000 ? GraphicsPreset.Quality : GraphicsPreset.Balanced;
        }

        public static void ApplyPreset(GraphicsPreset p, bool apply = true)
        {
            var d = Data;
            d.preset = (int)p;
            switch (p)
            {
                case GraphicsPreset.Performance:
                    d.renderScale = 0.85f; d.msaa = 1; d.shadows = 1; d.shadowDistance = 30f; break;
                case GraphicsPreset.Balanced:
                    d.renderScale = 1f; d.msaa = 2; d.shadows = 2; d.shadowDistance = 45f; break;
                case GraphicsPreset.Quality:
                    d.renderScale = 1f; d.msaa = 4; d.shadows = 2; d.shadowDistance = 60f; break;
            }
            if (apply) Apply();
        }

        public static void Apply()
        {
            var d = Data;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Mathf.Clamp(d.renderScale, 0.5f, 1f);
                // plain bilinear: FSR's extra passes cost more than they save on integrated GPUs (measured +3 ms)
                urp.upscalingFilter = UpscalingFilterSelection.Linear;
                urp.msaaSampleCount = d.msaa >= 4 ? 4 : d.msaa >= 2 ? 2 : 1;
                urp.shadowDistance = d.shadows == 0 ? 0f : d.shadowDistance;
                // hard = one 1024 cascade (~3 ms cheaper on integrated GPUs), soft = two 2048 cascades
                urp.shadowCascadeCount = d.shadows >= 2 ? 2 : 1;
                urp.mainLightShadowmapResolution = d.shadows >= 2 ? 2048 : 1024;
                // the grade runs in the Salvo shaders (ShaderGrade), so no HDR target / post pass is needed
                urp.supportsHDR = false;
            }
            ApplySun();
            QualitySettings.vSyncCount = d.vsync ? 1 : 0;
            Application.targetFrameRate = d.vsync ? -1 : (d.fpsCap <= 0 ? -1 : d.fpsCap);

#if !UNITY_EDITOR
            ApplyDisplay(d.fullscreen == 0 ? FullScreenMode.Windowed : d.fullscreen == 2 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow);
#endif
            AudioListener.volume = d.master;
            Changed?.Invoke();
        }

        /// <summary>
        /// Full screen always runs at the monitor's native resolution: Unity remembers the last window
        /// size and would otherwise stretch e.g. a stored 800x450 across a 1080p panel (blurry UI and
        /// world). Performance scaling is the job of the render scale, which leaves the UI sharp.
        /// </summary>
        static void ApplyDisplay(FullScreenMode mode)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-screen-width") >= 0) return; // explicit size (tests)
            var info = Screen.mainWindowDisplayInfo;
            int nw = info.width > 0 ? info.width : Screen.currentResolution.width;
            int nh = info.height > 0 ? info.height : Screen.currentResolution.height;
            if (mode == FullScreenMode.Windowed)
            {
                if (Screen.fullScreenMode == mode) return;
                int w = Mathf.Min(1600, Mathf.RoundToInt(nw * 0.85f));
                Screen.SetResolution(w, Mathf.RoundToInt(w * nh / (float)nw), mode);
            }
            else
            {
                // exclusive full screen may switch the display mode: use the panel's largest mode
                if (mode == FullScreenMode.ExclusiveFullScreen)
                    foreach (var r in Screen.resolutions)
                        if (r.width * r.height > nw * nh) { nw = r.width; nh = r.height; }
                if (Screen.fullScreenMode != mode || Screen.width != nw || Screen.height != nh)
                    Screen.SetResolution(nw, nh, mode);
            }
        }

        /// <summary>Called when a map loads (its sun is the only light).</summary>
        public static void ApplySun()
        {
            var sun = RenderSettings.sun;
            if (sun != null) sun.shadows = Data.shadows == 0 ? LightShadows.None : Data.shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
        }

#if UNITY_EDITOR
        // Runtime tweaks mutate the shared URP asset; restore authoring values when leaving play mode.
        [UnityEditor.InitializeOnLoadMethod]
        static void HookEditor()
        {
            UnityEditor.EditorApplication.playModeStateChanged += s =>
            {
                if (s != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                {
                    urp.renderScale = 1f; urp.msaaSampleCount = 2; urp.shadowDistance = 55f;
                    urp.shadowCascadeCount = 2; urp.mainLightShadowmapResolution = 2048; urp.upscalingFilter = UpscalingFilterSelection.Auto; urp.supportsHDR = true;
                }
            };
        }
#endif
    }
}
