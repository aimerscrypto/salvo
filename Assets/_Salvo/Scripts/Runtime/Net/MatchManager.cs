using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    public struct ScoreEntry : INetworkSerializable, IEquatable<ScoreEntry>
    {
        public ulong id;          // combatant NetworkObjectId
        public ulong clientId;    // owner (server id for bots)
        public FixedString32Bytes name;
        public byte team;
        public byte color;
        public bool bot;
        public short kills, deaths, assists, streak, headshots;
        public int score;
        public ushort ping;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref id); s.SerializeValue(ref clientId); s.SerializeValue(ref name);
            s.SerializeValue(ref team); s.SerializeValue(ref color); s.SerializeValue(ref bot);
            s.SerializeValue(ref kills); s.SerializeValue(ref deaths); s.SerializeValue(ref assists);
            s.SerializeValue(ref streak); s.SerializeValue(ref headshots); s.SerializeValue(ref score); s.SerializeValue(ref ping);
        }

        public bool Equals(ScoreEntry o) =>
            id == o.id && clientId == o.clientId && name.Equals(o.name) && team == o.team && color == o.color && bot == o.bot &&
            kills == o.kills && deaths == o.deaths && assists == o.assists && streak == o.streak && headshots == o.headshots &&
            score == o.score && ping == o.ping;
    }

    public struct KillEvent
    {
        public string killer, victim;
        public Color killerColor, victimColor;
        public byte weapon;
        public bool headshot;
        public ulong killerId, victimId;
        public float time;
    }

    /// <summary>
    /// Server-authoritative match flow, scoring and population (players + bots). Lives in each
    /// map scene as an in-scene NetworkObject; replicates state to clients through
    /// NetworkVariables / NetworkList and pushes discrete events through RPCs.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager I { get; private set; }

        public readonly NetworkVariable<byte> Mode = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Phase = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> PhaseEnd = new NetworkVariable<double>();
        public readonly NetworkVariable<short> ScoreLimit = new NetworkVariable<short>(25);
        public readonly NetworkVariable<short> RedScore = new NetworkVariable<short>();
        public readonly NetworkVariable<short> BlueScore = new NetworkVariable<short>();
        public readonly NetworkVariable<FixedString64Bytes> Winner = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<byte> WinnerTeam = new NetworkVariable<byte>();
        public NetworkList<ScoreEntry> Scores;

        public static event Action<KillEvent> OnKill;
        public static event Action OnMatchEnded;
        public static event Action OnMatchStarted;

        public GameMode GameMode => (GameMode)Mode.Value;
        public MatchPhase MatchPhase => (MatchPhase)Phase.Value;
        public float TimeLeft => (float)(PhaseEnd.Value - (NetworkManager != null ? NetworkManager.ServerTime.Time : 0));
        public bool IsTeamMode => GameMode == GameMode.TeamDeathmatch;

        // ---------------------------------------------------------------- server state
        MatchSettings settings;
        readonly List<Combatant> bots = new List<Combatant>();
        readonly Dictionary<ulong, float> respawnAt = new Dictionary<ulong, float>(); // combatant id -> time
        readonly HashSet<ulong> spawnedClients = new HashSet<ulong>();
        int botNameCursor;
        bool rotating;
        float populationTimer;
        float pingTimer;

        void Awake()
        {
            I = this;
            Scores = new NetworkList<ScoreEntry>();
        }

        public override void OnDestroy()
        {
            if (I == this) I = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                settings = NetSession.Settings ?? new MatchSettings();
                Mode.Value = (byte)settings.mode;
                ScoreLimit.Value = (short)settings.ScoreLimit;
                NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
                NetworkManager.SceneManager.OnSynchronizeComplete += OnSynchronizeComplete;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                StartLive();
                // the host's own client has already loaded the scene
                if (IsHost) SpawnPlayer(NetworkManager.LocalClientId);
                UpdatePopulation();
            }
            Phase.OnValueChanged += OnPhaseChanged;
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseChanged;
            if (IsServer && NetworkManager != null)
            {
                if (NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
                    NetworkManager.SceneManager.OnSynchronizeComplete -= OnSynchronizeComplete;
                }
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        void OnPhaseChanged(byte prev, byte now)
        {
            if (now == (byte)MatchPhase.Ended) OnMatchEnded?.Invoke();
            if (now == (byte)MatchPhase.Live && prev == (byte)MatchPhase.Ended) OnMatchStarted?.Invoke();
        }

        // ---------------------------------------------------------------- population
        void OnLoadEventCompleted(string scene, UnityEngine.SceneManagement.LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            foreach (var id in completed) SpawnPlayer(id);
            UpdatePopulation();
        }

        void OnSynchronizeComplete(ulong clientId)
        {
            SpawnPlayer(clientId);
            UpdatePopulation();
        }

        void OnClientDisconnected(ulong clientId)
        {
            spawnedClients.Remove(clientId);
            for (int i = Scores.Count - 1; i >= 0; i--)
                if (!Scores[i].bot && Scores[i].clientId == clientId) Scores.RemoveAt(i);
            UpdatePopulation();
        }

        void SpawnPlayer(ulong clientId)
        {
            if (spawnedClients.Contains(clientId)) return;
            if (!NetworkManager.ConnectedClients.ContainsKey(clientId)) return;
            spawnedClients.Add(clientId);

            NetSession.Payloads.TryGetValue(clientId, out var p);
            if (p == null) p = ConnectionPayload.FromProfile();
            var team = PickTeam();
            var spawn = SpawnSelector.Pick(team, IsTeamMode);
            var go = Instantiate(GameConfig.I.combatantPrefab, spawn.position, Quaternion.Euler(0, spawn.yaw, 0));
            var c = go.GetComponent<Combatant>();
            c.PendingInit = new Combatant.InitData
            {
                name = p.name, team = team, color = (byte)Mathf.Abs(p.color % 16), bot = false,
                loadout = new LoadoutData
                {
                    primary = (byte)ValidWeapon(p.primary, WeaponSlot.Primary), secondary = (byte)ValidWeapon(p.secondary, WeaponSlot.Secondary),
                    primarySkin = (byte)p.primarySkin, secondarySkin = (byte)p.secondarySkin
                },
                spawn = spawn
            };
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            AddScore(c, clientId, false);
        }

        static int ValidWeapon(int id, WeaponSlot slot)
        {
            var w = GameConfig.I.Weapon(id);
            if (w != null && w.slot == slot) return w.id;
            foreach (var x in GameConfig.I.weapons) if (x.slot == slot && x.ownedByDefault) return x.id;
            return 0;
        }

        void SpawnBot()
        {
            var names = GameConfig.I.botNames;
            string n = names != null && names.Length > 0 ? names[botNameCursor++ % names.Length] : "Bot" + botNameCursor++;
            var team = PickTeam();
            var spawn = SpawnSelector.Pick(team, IsTeamMode);
            var go = Instantiate(GameConfig.I.combatantPrefab, spawn.position, Quaternion.Euler(0, spawn.yaw, 0));
            var c = go.GetComponent<Combatant>();
            // bots pick from the primary pool with a bias towards rifles
            var primaries = new List<WeaponDef>();
            foreach (var w in GameConfig.I.weapons) if (w.slot == WeaponSlot.Primary) { primaries.Add(w); if (w.ownedByDefault) primaries.Add(w); }
            var prim = primaries[UnityEngine.Random.Range(0, primaries.Count)];
            int skinCount = GameConfig.I.skins.Length;
            c.PendingInit = new Combatant.InitData
            {
                name = n, team = team, color = (byte)UnityEngine.Random.Range(0, 16), bot = true, skill = settings.botSkill,
                loadout = new LoadoutData { primary = prim.id, secondary = (byte)ValidWeapon(0, WeaponSlot.Secondary), primarySkin = (byte)UnityEngine.Random.Range(0, Mathf.Min(skinCount, 6)) },
                spawn = spawn
            };
            go.GetComponent<NetworkObject>().Spawn(true);
            bots.Add(c);
            AddScore(c, NetworkManager.ServerClientId, true);
        }

        void RemoveBot(Combatant c)
        {
            bots.Remove(c);
            RemoveScore(c.NetworkObjectId);
            if (c != null && c.NetworkObject.IsSpawned) c.NetworkObject.Despawn(true);
        }

        Team PickTeam()
        {
            if (!IsTeamMode) return Team.None;
            int red = 0, blue = 0;
            foreach (var e in Scores) { if (e.team == (byte)Team.Red) red++; else if (e.team == (byte)Team.Blue) blue++; }
            return red <= blue ? Team.Red : Team.Blue;
        }

        void UpdatePopulation()
        {
            if (!IsServer) return;
            int humans = spawnedClients.Count;
            int target = Mathf.Clamp(settings.fillWithBots - humans, 0, GameRules.MaxPlayers - humans);
            bots.RemoveAll(b => b == null);
            while (bots.Count < target) SpawnBot();
            while (bots.Count > target)
            {
                // remove from the bigger team first to keep TDM balanced
                Combatant pick = bots[bots.Count - 1];
                if (IsTeamMode)
                {
                    int red = 0, blue = 0;
                    foreach (var e in Scores) { if (e.team == 1) red++; else if (e.team == 2) blue++; }
                    var big = red > blue ? Team.Red : Team.Blue;
                    foreach (var b in bots) if (b.Team == big) { pick = b; break; }
                }
                RemoveBot(pick);
            }
        }

        // ---------------------------------------------------------------- scores
        void AddScore(Combatant c, ulong clientId, bool bot)
        {
            var d = c.PendingInit;
            Scores.Add(new ScoreEntry { id = c.NetworkObjectId, clientId = clientId, name = d.name, team = (byte)d.team, color = d.color, bot = bot });
        }

        void RemoveScore(ulong id)
        {
            for (int i = Scores.Count - 1; i >= 0; i--) if (Scores[i].id == id) Scores.RemoveAt(i);
        }

        int IndexOf(ulong id)
        {
            for (int i = 0; i < Scores.Count; i++) if (Scores[i].id == id) return i;
            return -1;
        }

        public bool TryGetScore(ulong id, out ScoreEntry e)
        {
            int i = IndexOf(id);
            e = i >= 0 ? Scores[i] : default;
            return i >= 0;
        }

        /// <summary>Server: called by Combatant when it dies.</summary>
        public void ServerOnKill(Combatant victim, Combatant killer, byte weapon, bool headshot, List<ulong> assisters)
        {
            if (MatchPhase != MatchPhase.Live) return;
            int vi = IndexOf(victim.NetworkObjectId);
            if (vi >= 0) { var v = Scores[vi]; v.deaths++; v.streak = 0; Scores[vi] = v; }

            bool suicide = killer == null || killer == victim;
            if (!suicide)
            {
                int ki = IndexOf(killer.NetworkObjectId);
                if (ki >= 0)
                {
                    var k = Scores[ki];
                    k.kills++; k.streak++;
                    k.score += GameRules.ScoreKill + (headshot ? GameRules.ScoreHeadshotBonus : 0);
                    if (headshot) k.headshots++;
                    Scores[ki] = k;
                }
                if (IsTeamMode)
                {
                    if (killer.Team == Team.Red) RedScore.Value++;
                    else if (killer.Team == Team.Blue) BlueScore.Value++;
                }
            }
            foreach (var a in assisters)
            {
                if (killer != null && a == killer.NetworkObjectId) continue;
                int ai = IndexOf(a);
                if (ai >= 0) { var e = Scores[ai]; e.assists++; e.score += GameRules.ScoreAssist; Scores[ai] = e; }
            }

            KillFeedRpc(killer != null ? killer.NetworkObjectId : victim.NetworkObjectId, victim.NetworkObjectId, weapon, headshot, suicide);
            respawnAt[victim.NetworkObjectId] = Time.time + GameRules.RespawnDelay;
            CheckWin();
        }

        [Rpc(SendTo.Everyone)]
        void KillFeedRpc(ulong killerId, ulong victimId, byte weapon, bool headshot, bool suicide)
        {
            TryGetScore(killerId, out var k);
            TryGetScore(victimId, out var v);
            var ev = new KillEvent
            {
                killer = suicide ? "" : k.name.ToString(),
                victim = v.name.ToString(),
                killerColor = GameConfig.TeamColor((Team)k.team, k.color),
                victimColor = GameConfig.TeamColor((Team)v.team, v.color),
                weapon = weapon,
                headshot = headshot,
                killerId = suicide ? 0 : killerId,
                victimId = victimId,
                time = Time.time
            };
            OnKill?.Invoke(ev);
        }

        void CheckWin()
        {
            int limit = ScoreLimit.Value;
            if (IsTeamMode)
            {
                if (RedScore.Value >= limit) EndMatch();
                else if (BlueScore.Value >= limit) EndMatch();
            }
            else
            {
                foreach (var e in Scores) if (e.kills >= limit) { EndMatch(); break; }
            }
        }

        // ---------------------------------------------------------------- phases
        void StartLive()
        {
            Phase.Value = (byte)MatchPhase.Live;
            PhaseEnd.Value = NetworkManager.ServerTime.Time + settings.timeLimit;
            RedScore.Value = 0;
            BlueScore.Value = 0;
            Winner.Value = default;
            WinnerTeam.Value = 0;
        }

        void EndMatch()
        {
            if (MatchPhase != MatchPhase.Live) return;
            // decide winner
            if (IsTeamMode)
            {
                var t = RedScore.Value > BlueScore.Value ? Team.Red : BlueScore.Value > RedScore.Value ? Team.Blue : Team.None;
                WinnerTeam.Value = (byte)t;
                Winner.Value = t == Team.Red ? "RED TEAM" : t == Team.Blue ? "BLUE TEAM" : "DRAW";
            }
            else
            {
                var best = BestEntry();
                Winner.Value = best.name;
                WinnerTeam.Value = 0;
            }
            Phase.Value = (byte)MatchPhase.Ended;
            PhaseEnd.Value = NetworkManager.ServerTime.Time + GameRules.EndScreenTime;
            foreach (var c in Combatant.All) c.ServerFreeze();
        }

        public ScoreEntry BestEntry()
        {
            ScoreEntry best = default;
            bool any = false;
            foreach (var e in Scores)
                if (!any || e.kills > best.kills || (e.kills == best.kills && e.score > best.score)) { best = e; any = true; }
            return best;
        }

        void RestartMatch()
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                var e = Scores[i];
                e.kills = e.deaths = e.assists = e.streak = e.headshots = 0; e.score = 0;
                Scores[i] = e;
            }
            StartLive();
            respawnAt.Clear();
            foreach (var c in Combatant.All)
            {
                var sp = SpawnSelector.Pick(c.Team, IsTeamMode);
                c.ServerRespawn(sp.position, sp.yaw);
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            double now = NetworkManager.ServerTime.Time;
            if (MatchPhase == MatchPhase.Live)
            {
                if (now >= PhaseEnd.Value) EndMatch();
                // respawns
                if (respawnAt.Count > 0)
                {
                    List<ulong> ready = null;
                    foreach (var kv in respawnAt) if (Time.time >= kv.Value) (ready ??= new List<ulong>()).Add(kv.Key);
                    if (ready != null)
                        foreach (var id in ready)
                        {
                            respawnAt.Remove(id);
                            var c = Combatant.Find(id);
                            if (c == null) continue;
                            var sp = SpawnSelector.Pick(c.Team, IsTeamMode);
                            c.ServerRespawn(sp.position, sp.yaw);
                        }
                }
            }
            else if (MatchPhase == MatchPhase.Ended && now >= PhaseEnd.Value)
            {
                // rotate to the next map when there is more than one; otherwise replay this one
                var maps = GameConfig.I.maps;
                if (settings.rotateMaps && maps != null && maps.Length > 1 && !rotating)
                {
                    rotating = true;
                    int cur = System.Array.FindIndex(maps, m => m.sceneName == gameObject.scene.name);
                    settings.map = maps[(cur + 1 + maps.Length) % maps.Length].sceneName;
                    NetworkManager.SceneManager.LoadScene(settings.map, UnityEngine.SceneManagement.LoadSceneMode.Single);
                    return;
                }
                if (!rotating) RestartMatch();
            }

            populationTimer += Time.deltaTime;
            if (populationTimer > 3f) { populationTimer = 0; UpdatePopulation(); }

            pingTimer += Time.deltaTime;
            if (pingTimer > 2f)
            {
                pingTimer = 0;
                var transport = NetworkManager.NetworkConfig.NetworkTransport;
                for (int i = 0; i < Scores.Count; i++)
                {
                    var e = Scores[i];
                    if (e.bot || e.clientId == NetworkManager.ServerClientId) continue;
                    ushort ping = (ushort)Mathf.Min(999, transport.GetCurrentRtt(e.clientId));
                    if (ping != e.ping) { e.ping = ping; Scores[i] = e; }
                }
            }
        }

        public float RespawnRemaining(ulong id) => respawnAt.TryGetValue(id, out var t) ? Mathf.Max(0, t - Time.time) : 0f;

        /// <summary>1-based placement of an entry (FFA by kills, TDM by team result then score).</summary>
        public int PlacementOf(ulong id)
        {
            if (!TryGetScore(id, out var me)) return 0;
            int place = 1;
            foreach (var e in Scores)
            {
                if (e.id == id) continue;
                if (e.kills > me.kills || (e.kills == me.kills && e.score > me.score)) place++;
            }
            return place;
        }
    }
}
