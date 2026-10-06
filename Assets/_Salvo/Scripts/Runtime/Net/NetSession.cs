using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Salvo
{
    [Serializable]
    public class MatchSettings
    {
        public GameMode mode = GameMode.FreeForAll;
        public int maxPlayers = 8;
        public int fillWithBots = 6;      // keep total combatants at least this high using bots
        public BotSkill botSkill = BotSkill.Normal;
        public int scoreLimit;            // 0 = mode default
        public float timeLimit = GameRules.MatchLength;
        public string map = "Outpost";
        public bool isPrivate;
        public string sessionName = "";
        public bool offline;
        public bool rotateMaps = true;

        public int ScoreLimit => scoreLimit > 0 ? scoreLimit : (mode == GameMode.FreeForAll ? GameRules.FfaScoreLimit : GameRules.TdmScoreLimit);
    }

    /// <summary>Data a client sends when connecting (approval payload).</summary>
    [Serializable]
    public class ConnectionPayload
    {
        public string version;
        public string name;
        public int primary, secondary, primarySkin, secondarySkin, color;

        public static ConnectionPayload FromProfile()
        {
            var d = Profile.Data;
            return new ConnectionPayload
            {
                version = GameRules.NetVersion,
                name = string.IsNullOrEmpty(NetSession.NameOverride) ? d.playerName : NetSession.NameOverride,
                primary = d.primary,
                secondary = d.secondary,
                primarySkin = Profile.EquippedSkin(d.primary),
                secondarySkin = Profile.EquippedSkin(d.secondary),
                color = d.colorIndex
            };
        }
    }

    /// <summary>
    /// Front door for every way of starting/joining a game:
    ///  - Online (Unity Multiplayer Services sessions over Relay: host, join-by-code, quick match)
    ///  - LAN / direct IP (Unity Transport)
    ///  - Offline practice (local host + bots)
    /// The NetworkManager lives in the persistent Boot scene.
    /// </summary>
    public static class NetSession
    {
        /// <summary>Session-only display name (command line testing); never written to the profile.</summary>
        public static string NameOverride;
        public static MatchSettings Settings = new MatchSettings();
        public static ISession Session { get; private set; }
        public static string JoinCode => Session?.Code;
        public static bool Busy { get; private set; }
        public static bool IsOnline => Session != null;

        public static event Action<string> Status;
        public static event Action<string> Failed;
        public static event Action Started;

        /// <summary>Approved payloads by clientId (server only).</summary>
        public static readonly Dictionary<ulong, ConnectionPayload> Payloads = new Dictionary<ulong, ConnectionPayload>();

        const string SessionType = "salvo-arena";
        static bool hooked;
        static bool servicesReady;

        static NetworkManager NM => NetworkManager.Singleton;
        static UnityTransport Transport => NM.GetComponent<UnityTransport>();

        static void Hook()
        {
            if (hooked || NM == null) return;
            hooked = true;
            NM.ConnectionApprovalCallback = Approve;
            NM.OnClientDisconnectCallback += OnDisconnect;
            NM.OnServerStopped += _ => Payloads.Clear();
        }

        static void Report(string s) { Debug.Log("[SALVO/Net] " + s); Status?.Invoke(s); }

        static void Fail(string s)
        {
            Debug.LogWarning("[SALVO/Net] " + s);
            Busy = false;
            Failed?.Invoke(s);
        }

        static void PreparePayload()
        {
            Hook();
            var json = JsonUtility.ToJson(ConnectionPayload.FromProfile());
            NM.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(json);
        }

        // ------------------------------------------------------------------ approval (server)
        static void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            ConnectionPayload p = null;
            try { p = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(req.Payload)); } catch { }

            res.CreatePlayerObject = false; // MatchManager spawns players once their scene is loaded
            if (p == null || p.version != GameRules.NetVersion)
            {
                res.Approved = false;
                res.Reason = "Version mismatch - please update SALVO.";
                return;
            }
            int humans = NM.ConnectedClientsIds.Count;
            if (humans >= Settings.maxPlayers || (Settings.offline && req.ClientNetworkId != NetworkManager.ServerClientId))
            {
                res.Approved = false;
                res.Reason = "Match is full.";
                return;
            }
            p.name = SanitizeName(p.name);
            Payloads[req.ClientNetworkId] = p;
            res.Approved = true;
        }

        public static string SanitizeName(string n)
        {
            n = string.IsNullOrWhiteSpace(n) ? "Player" : n.Trim();
            var sb = new StringBuilder();
            foreach (char c in n) if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ') sb.Append(c);
            n = sb.Length == 0 ? "Player" : sb.ToString();
            return n.Length > 16 ? n.Substring(0, 16) : n;
        }

        static void OnDisconnect(ulong clientId)
        {
            if (NM.IsServer)
            {
                Payloads.Remove(clientId);
                return;
            }
            // we are a client and lost the connection
            if (clientId == NM.LocalClientId || !NM.IsConnectedClient)
            {
                string reason = string.IsNullOrEmpty(NM.DisconnectReason) ? "Disconnected from host." : NM.DisconnectReason;
                GameFlow.ReturnToMenu(reason);
            }
        }

        // ------------------------------------------------------------------ services
        static async Task<bool> EnsureServices()
        {
            if (servicesReady && AuthenticationService.Instance.IsSignedIn) return true;
            try
            {
                Report("Connecting to online services...");
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var opts = new InitializationOptions();
#if UNITY_EDITOR
                    // separate identity per editor/clone so local multi-instance testing works
                    opts.SetProfile("editor" + Mathf.Abs(Application.dataPath.GetHashCode() % 1000));
#endif
                    await UnityServices.InitializeAsync(opts);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                servicesReady = true;
                return true;
            }
            catch (Exception e)
            {
                Fail("Online services unavailable. Check your connection.\n" + Short(e));
                return false;
            }
        }

        static string Short(Exception e)
        {
            var m = e.InnerException != null ? e.InnerException.Message : e.Message;
            return m.Length > 140 ? m.Substring(0, 140) : m;
        }

        static SessionOptions BuildOptions(MatchSettings s)
        {
            var o = new SessionOptions
            {
                MaxPlayers = Mathf.Clamp(s.maxPlayers, 2, GameRules.MaxPlayers),
                IsPrivate = s.isPrivate,
                Name = string.IsNullOrEmpty(s.sessionName) ? Profile.Data.playerName + "'s match" : s.sessionName,
                Type = SessionType,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { "ver", new SessionProperty(GameRules.NetVersion, VisibilityPropertyOptions.Public, PropertyIndex.String1) },
                    { "mode", new SessionProperty(((int)s.mode).ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String2) },
                    { "map", new SessionProperty(s.map, VisibilityPropertyOptions.Public) }
                }
            };
            return o.WithRelayNetwork();
        }

        // ------------------------------------------------------------------ public API
        public static async void HostOnline(MatchSettings s)
        {
            if (Busy) return;
            Busy = true;
            Settings = s;
            s.offline = false;
            if (!await EnsureServices()) return;
            try
            {
                PreparePayload();
                Report("Creating match...");
                Session = await MultiplayerService.Instance.CreateSessionAsync(BuildOptions(s));
                Report("Match created. Code: " + Session.Code);
                OnHostStarted();
            }
            catch (Exception e) { Session = null; Fail("Could not create match.\n" + Short(e)); }
        }

        public static async void QuickMatch(GameMode preferred)
        {
            if (Busy) return;
            Busy = true;
            if (!await EnsureServices()) return;
            try
            {
                PreparePayload();
                Report("Searching for a match...");
                var settings = new MatchSettings { mode = preferred, maxPlayers = 8, fillWithBots = 6 };
                Settings = settings;
                var quick = new QuickJoinOptions
                {
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(FilterField.StringIndex1, GameRules.NetVersion, FilterOperation.Equal),
                        new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater)
                    },
                    Timeout = TimeSpan.FromSeconds(4),
                    CreateSession = true
                };
                Session = await MultiplayerService.Instance.MatchmakeSessionAsync(quick, BuildOptions(settings));
                if (Session.IsHost)
                {
                    Report("No open matches - hosting a new one.");
                    OnHostStarted();
                }
                else
                {
                    Report("Joined match. Loading...");
                    Busy = false;
                    Started?.Invoke();
                }
            }
            catch (Exception e) { Session = null; Fail("Matchmaking failed.\n" + Short(e)); }
        }

        public static async void JoinByCode(string code)
        {
            if (Busy) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) { Failed?.Invoke("Enter a valid match code."); return; }
            Busy = true;
            if (!await EnsureServices()) return;
            try
            {
                PreparePayload();
                Report("Joining " + code + "...");
                Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Report("Joined. Loading match...");
                Busy = false;
                Started?.Invoke();
            }
            catch (Exception e) { Session = null; Fail("Could not join match " + code + ".\n" + Short(e)); }
        }

        /// <summary>Offline practice (localhost only) or LAN host (listens on all interfaces).</summary>
        public static void HostLocal(MatchSettings s, bool lan)
        {
            if (Busy) return;
            Busy = true;
            Settings = s;
            s.offline = !lan;
            PreparePayload();
            Transport.SetConnectionData(lan ? "0.0.0.0" : "127.0.0.1", GameRules.DefaultPort, lan ? "0.0.0.0" : "127.0.0.1");
            if (!NM.StartHost())
            {
                Fail(lan ? "Could not open port " + GameRules.DefaultPort + " for LAN hosting." : "Could not start practice match.");
                return;
            }
            Report(lan ? "LAN match hosted on port " + GameRules.DefaultPort : "Starting practice...");
            OnHostStarted();
        }

        public static void JoinDirect(string address)
        {
            if (Busy) return;
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            ushort port = GameRules.DefaultPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(address.Substring(colon + 1), out var p)) { port = p; address = address.Substring(0, colon); }
            Busy = true;
            PreparePayload();
            Transport.SetConnectionData(address, port);
            Report($"Connecting to {address}:{port}...");
            if (!NM.StartClient()) { Fail("Could not start client."); return; }
            Busy = false;
            Started?.Invoke();
        }

        static void OnHostStarted()
        {
            Busy = false;
            Started?.Invoke();
            var status = NM.SceneManager.LoadScene(Settings.map, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started) Debug.LogWarning("[SALVO/Net] Scene load status " + status);
        }

        public static async Task Leave()
        {
            Busy = false;
            var s = Session;
            Session = null;
            try { if (s != null) await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[SALVO/Net] Leave session: " + e.Message); }
            if (NM != null && (NM.IsServer || NM.IsClient)) NM.Shutdown();
            Payloads.Clear();
        }
    }
}
