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
        }

        void Start()
        {
            ParseCommandLine();
            if (!autoStarted) GameFlow.GoToMenu();
        }

        bool autoStarted;

        /// <summary>Headless test hooks: -salvo-host / -salvo-join ip / -salvo-practice.</summary>
        void ParseCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int bots = 4;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-salvo-bots") int.TryParse(args[i + 1], out bots);
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-salvo-name" when i + 1 < args.Length: NetSession.NameOverride = NetSession.SanitizeName(args[i + 1]); break;
                    case "-salvo-host": autoStarted = true; GameFlow.StartLocal(new MatchSettings { fillWithBots = bots }, true); break;
                    case "-salvo-practice": autoStarted = true; GameFlow.StartLocal(new MatchSettings { fillWithBots = 6, offline = true }, false); break;
                    case "-salvo-join" when i + 1 < args.Length: autoStarted = true; GameFlow.JoinDirect(args[i + 1]); break;
                    case "-salvo-code" when i + 1 < args.Length: autoStarted = true; GameFlow.JoinCode(args[i + 1]); break;
                    case "-salvo-online-host": autoStarted = true; GameFlow.HostOnline(new MatchSettings { fillWithBots = bots }); break;
                }
            }
        }

#if DEVELOPMENT_BUILD
        float perfTimer; int perfFrames; float perfWorst;
        void Update()
        {
            perfTimer += Time.unscaledDeltaTime; perfFrames++;
            perfWorst = Mathf.Max(perfWorst, Time.unscaledDeltaTime);
            if (perfTimer >= 10f)
            {
                Debug.Log($"[SALVO/Perf] avg {perfFrames / perfTimer:0.0} fps, worst frame {perfWorst * 1000f:0} ms, combatants {Combatant.All.Count}, scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
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
