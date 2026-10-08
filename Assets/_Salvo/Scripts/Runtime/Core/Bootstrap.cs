using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Salvo
{
    /// <summary>
    /// Persistent root living in the Boot scene: services (profile, settings, input, audio, FX, UI)
    /// and the NetworkManager. Survives every scene change.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap I { get; private set; }
        public const string TransitionScene = "Transition";

        void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            // everything authored in the Boot scene (NetworkManager, volume, UI) is persistent
            foreach (var go in gameObject.scene.GetRootGameObjects()) DontDestroyOnLoad(go);
            Application.runInBackground = true;
            QualitySettings.maxQueuedFrames = 1; // lower input latency
            Profile.Load();
            Settings.Load();
            SalvoInput.Init();
            Settings.Apply();
            // compile the game's shader variants up front (behind the menu) instead of mid-fight
            var svc = GameConfig.I != null ? GameConfig.I.shaderVariants : null;
            if (svc != null && !svc.isWarmedUp) svc.WarmUp();
        }

        void Start()
        {
            Debug.Log($"[SALVO] Display {Screen.width}x{Screen.height} (monitor {Screen.currentResolution.width}x{Screen.currentResolution.height}, {Screen.dpi:0} dpi, {Screen.fullScreenMode}), render scale {Settings.Data.renderScale:0.00}, MSAA {Settings.Data.msaa}x");
            ParseCommandLine();
            if (!autoStarted) GameFlow.GoToMenu();
        }

        bool autoStarted;

        /// <summary>-salvo-perflog: log the average frame rate every 10 s in any build (web testing).</summary>
        static bool PerfLog;
        float plTimer; int plFrames;

        void LateUpdate()
        {
            if (!PerfLog) return;
            plTimer += Time.unscaledDeltaTime; plFrames++;
            if (plTimer < 10f) return;
            Debug.Log($"[SALVO/FPS] {plFrames / plTimer:0.0} fps at {Screen.width}x{Screen.height}, scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, combatants {Combatant.All.Count}");
            plTimer = 0f; plFrames = 0;
        }

        /// <summary>
        /// Browsers have no command line: the page URL's query stands in for it, e.g.
        /// index.html?salvo-practice&salvo-primary=4 -> "-salvo-practice", "-salvo-primary", "4".
        /// </summary>
        static string[] WebArgs()
        {
            var list = new System.Collections.Generic.List<string> { "salvo" };
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q < 0) return list.ToArray();
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var kv = part.Split(new[] { '=' }, 2);
                list.Add("-" + System.Uri.UnescapeDataString(kv[0]));
                if (kv.Length > 1) list.Add(System.Uri.UnescapeDataString(kv[1]));
            }
            return list.ToArray();
        }

        /// <summary>Headless test hooks: -salvo-host / -salvo-join ip / -salvo-practice.</summary>
        void ParseCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
#if UNITY_WEBGL && !UNITY_EDITOR
            args = WebArgs();
#endif
            int bots = 4;
            string map = "Outpost";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-salvo-bots") int.TryParse(args[i + 1], out bots);
                if (args[i] == "-salvo-map") map = args[i + 1];
                if (args[i] == "-salvo-primary" && int.TryParse(args[i + 1], out int prim)) Profile.Data.primary = prim; // session test hook
            }
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-salvo-preset" when i + 1 < args.Length && System.Enum.TryParse(args[i + 1], true, out GraphicsPreset gp): Settings.ApplyPreset(gp); break; // session only
#if DEVELOPMENT_BUILD
                    case "-salvo-gfx" when i + 1 < args.Length: StartCoroutine(DevGraphics(args[i + 1])); break;
#endif
                    case "-salvo-perflog": PerfLog = true; break;
                    case "-salvo-name" when i + 1 < args.Length: NetSession.NameOverride = NetSession.SanitizeName(args[i + 1]); break;
                    case "-salvo-host": autoStarted = true; GameFlow.StartLocal(new MatchSettings { fillWithBots = bots, map = map, rotateMaps = false }, true); break;
                    case "-salvo-practice": autoStarted = true; GameFlow.StartLocal(new MatchSettings { fillWithBots = 6, offline = true, map = map, rotateMaps = false }, false); break;
                    case "-salvo-join" when i + 1 < args.Length: autoStarted = true; GameFlow.JoinDirect(args[i + 1]); break;
                    case "-salvo-code" when i + 1 < args.Length: autoStarted = true; GameFlow.JoinCode(args[i + 1]); break;
                    case "-salvo-online-host": autoStarted = true; GameFlow.HostOnline(new MatchSettings { fillWithBots = bots }); break;
                }
            }
            if (autoStarted) Hud.SkipPicker = true; // test / benchmark runs play without the weapon picker
        }

#if DEVELOPMENT_BUILD
        /// <summary>Profiling hook: -salvo-gfx "hdr=0,post=0,msaa=1,scale=0.8,fsr=0,shadowres=1024,cascades=1,shadowdist=30,shadows=1"</summary>
        System.Collections.IEnumerator DevGraphics(string spec)
        {
            yield return new WaitForSeconds(8f); // after the match scene and settings are applied
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var camData = Camera.main != null ? Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            foreach (var kv in spec.Split(','))
            {
                var p = kv.Split('=');
                if (p.Length != 2 || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
                switch (p[0])
                {
                    case "hdr": urp.supportsHDR = v > 0; break;
                    case "msaa": urp.msaaSampleCount = (int)v; break;
                    case "scale": urp.renderScale = v; break;
                    case "fsr": urp.upscalingFilter = v > 0 ? UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR : UnityEngine.Rendering.Universal.UpscalingFilterSelection.Linear; break;
                    case "shadowres": urp.mainLightShadowmapResolution = (int)v; break;
                    case "cascades": urp.shadowCascadeCount = (int)v; break;
                    case "shadowdist": urp.shadowDistance = v; break;
                    case "shadows": if (RenderSettings.sun != null) RenderSettings.sun.shadows = v <= 0 ? LightShadows.None : v < 2 ? LightShadows.Hard : LightShadows.Soft; break;
                    case "post": if (camData != null) camData.renderPostProcessing = v > 0; break;
                    case "grade": ShaderGrade.DevDisabled = v <= 0; break;
                    case "aim": WeaponController.DebugForceAim = v > 0; break;
                }
            }
            Debug.Log("[SALVO] dev gfx " + spec + $" (scale {urp.renderScale}, msaa {urp.msaaSampleCount}, hdr {urp.supportsHDR}, cascades {urp.shadowCascadeCount}, res {urp.mainLightShadowmapResolution})");
        }

        float perfTimer; int perfFrames; float perfWorst; int lastGc;
        public static int FrameRebuilds, FrameBreaks, FrameShots;
        // frame budget split: C# script time (what the scripting backend affects) vs GPU time
        static readonly string[] scriptMarkers = { "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate", "CoroutinesDelayedCalls" };
        Unity.Profiling.ProfilerRecorder[] scriptRec;
        readonly FrameTiming[] timing = new FrameTiming[1];
        double sumScripts, sumGpu, sumMain, sumRender; int timedFrames;
        // where the main thread goes (ms per frame), and how much the GPU is asked to draw
        static readonly (string label, Unity.Profiling.ProfilerCategory cat, string marker)[] systems =
        {
            ("physics", Unity.Profiling.ProfilerCategory.Physics, "Physics.Simulate"),
            ("anim", Unity.Profiling.ProfilerCategory.Animation, "Animators.Update"),
            ("particles", Unity.Profiling.ProfilerCategory.Particles, "ParticleSystem.Update"),
            ("ui", Unity.Profiling.ProfilerCategory.Gui, "UIElementsUpdatePanels"),
            ("uiRepaint", Unity.Profiling.ProfilerCategory.Gui, "UIR.DrawChain"),
            ("audio", Unity.Profiling.ProfilerCategory.Audio, "AudioManager.Update"),
            ("render", Unity.Profiling.ProfilerCategory.Render, "UniversalRenderPipeline.RenderSingleCameraInternal"),
            ("shadowsCpu", Unity.Profiling.ProfilerCategory.Render, "MainLightShadow"),
            ("netcode", Unity.Profiling.ProfilerCategory.Network, "NetworkManager.NetworkUpdate"),
            ("nav", Unity.Profiling.ProfilerCategory.Ai, "NavMeshManager"),
            ("gcAlloc", Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc"),
        };
        static readonly string[] counters = { "Batches Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count" };
        Unity.Profiling.ProfilerRecorder[] sysRec, cntRec;
        double[] sysSum = new double[systems.Length], cntSum = new double[counters.Length];
        void OnEnable()
        {
            scriptRec = new Unity.Profiling.ProfilerRecorder[scriptMarkers.Length];
            for (int i = 0; i < scriptMarkers.Length; i++) scriptRec[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, scriptMarkers[i]);
            sysRec = new Unity.Profiling.ProfilerRecorder[systems.Length];
            for (int i = 0; i < systems.Length; i++) sysRec[i] = Unity.Profiling.ProfilerRecorder.StartNew(systems[i].cat, systems[i].marker);
            cntRec = new Unity.Profiling.ProfilerRecorder[counters.Length];
            for (int i = 0; i < counters.Length; i++) cntRec[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, counters[i]);
        }
        void OnDisable()
        {
            if (scriptRec != null) foreach (var r in scriptRec) r.Dispose();
            if (sysRec != null) foreach (var r in sysRec) r.Dispose();
            if (cntRec != null) foreach (var r in cntRec) r.Dispose();
        }
        void SampleBudget()
        {
            double scripts = 0;
            for (int i = 0; i < scriptRec.Length; i++) if (scriptRec[i].Valid) scripts += scriptRec[i].LastValue * 1e-6;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) == 0) return;
            sumScripts += scripts;
            sumGpu += timing[0].gpuFrameTime;
            sumMain += timing[0].cpuMainThreadFrameTime;
            sumRender += timing[0].cpuRenderThreadFrameTime;
            for (int i = 0; i < sysRec.Length; i++) if (sysRec[i].Valid) sysSum[i] += sysRec[i].LastValue * (systems[i].label == "gcAlloc" ? 1.0 / 1024 : 1e-6);
            for (int i = 0; i < cntRec.Length; i++) if (cntRec[i].Valid) cntSum[i] += cntRec[i].LastValue;
            timedFrames++;
        }
        void Update()
        {
            SampleBudget();
            int gc = System.GC.CollectionCount(0);
            if (Time.unscaledDeltaTime > 0.03f && Time.frameCount > 300)
                Debug.Log($"[SALVO/Hitch] {Time.unscaledDeltaTime * 1000f:0} ms rebuilds={FrameRebuilds} breaks={FrameBreaks} shots={FrameShots} gc={(gc != lastGc)}");
            lastGc = gc; FrameRebuilds = FrameBreaks = FrameShots = 0;
            perfTimer += Time.unscaledDeltaTime; perfFrames++;
            perfWorst = Mathf.Max(perfWorst, Time.unscaledDeltaTime);
            if (perfTimer >= 10f)
            {
                Debug.Log($"[SALVO/Perf] avg {perfFrames / perfTimer:0.0} fps, worst frame {perfWorst * 1000f:0} ms, combatants {Combatant.All.Count}, scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                if (timedFrames > 0)
                {
                    Debug.Log($"[SALVO/Budget] frame {1000f * perfTimer / perfFrames:0.00} ms | C# scripts {sumScripts / timedFrames:0.00} ms | GPU {sumGpu / timedFrames:0.00} ms");
                    var sb = new System.Text.StringBuilder($"[SALVO/Split] cpuMain {sumMain / timedFrames:0.00} cpuRender {sumRender / timedFrames:0.00} gpu {sumGpu / timedFrames:0.00} scripts {sumScripts / timedFrames:0.00}");
                    for (int i = 0; i < systems.Length; i++) sb.Append($" {systems[i].label} {(sysRec[i].Valid ? (sysSum[i] / timedFrames).ToString("0.00") : "n/a")}");
                    for (int i = 0; i < counters.Length; i++) sb.Append($" | {counters[i].Replace(" Count", "")} {(cntRec[i].Valid ? (cntSum[i] / timedFrames).ToString("0") : "n/a")}");
                    sb.Append($" | res {Screen.width}x{Screen.height}");
                    Debug.Log(sb.ToString());
                }
                sumScripts = sumGpu = sumMain = sumRender = 0; timedFrames = 0;
                System.Array.Clear(sysSum, 0, sysSum.Length); System.Array.Clear(cntSum, 0, cntSum.Length);
                perfTimer = 0; perfFrames = 0; perfWorst = 0;
            }
        }
#endif

        void OnApplicationQuit()
        {
            Profile.Save();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        }
    }

    /// <summary>High level navigation between menu and matches.</summary>
    public static class GameFlow
    {
        public static bool InMenu { get; private set; }
        public static string MenuMap = "Outpost";
        public static string PendingMessage;
        public static event System.Action EnteredMenu;
        public static event System.Action EnteredMatch;

        public static bool InMatch => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !InMenu;

        public static void GoToMenu(string message = null)
        {
            InMenu = true;
            PendingMessage = message;
            SalvoInput.GameplayEnabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // a different map behind the menu each time keeps the front end fresh
            var maps = GameConfig.I != null ? GameConfig.I.maps : null;
            if (maps != null && maps.Length > 0) MenuMap = maps[Random.Range(0, maps.Length)].sceneName;
            var op = SceneManager.LoadSceneAsync(MenuMap, LoadSceneMode.Single);
            if (op != null) op.completed += _ => EnteredMenu?.Invoke();
        }

        public static Task LoadTransition()
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = SceneManager.LoadSceneAsync(Bootstrap.TransitionScene, LoadSceneMode.Single);
            op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        /// <summary>Called by the network layer once the local client is in a match scene.</summary>
        public static void OnMatchSceneReady()
        {
            InMenu = false;
            EnteredMatch?.Invoke();
        }

        public static async void StartLocal(MatchSettings s, bool lan)
        {
            InMenu = false;
            await LoadTransition();
            NetSession.HostLocal(s, lan);
        }

        public static async void HostOnline(MatchSettings s)
        {
            InMenu = false;
            await LoadTransition();
            NetSession.HostOnline(s);
        }

        public static async void QuickMatch(GameMode mode)
        {
            InMenu = false;
            await LoadTransition();
            NetSession.QuickMatch(mode);
        }

        public static async void JoinCode(string code)
        {
            InMenu = false;
            await LoadTransition();
            NetSession.JoinByCode(code);
        }

        public static async void JoinDirect(string address)
        {
            InMenu = false;
            await LoadTransition();
            NetSession.JoinDirect(address);
        }

        static bool leaving;

        public static async void ReturnToMenu(string message = null)
        {
            if (leaving) return;
            leaving = true;
            try
            {
                MatchRewards.ApplyIfPending(false);
                await NetSession.Leave();
            }
            finally { leaving = false; }
            GoToMenu(message);
        }
    }
}
