using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Salvo
{
    public struct LoadoutData : INetworkSerializable, IEquatable<LoadoutData>
    {
        public byte primary, secondary, primarySkin, secondarySkin;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref primary); s.SerializeValue(ref secondary);
            s.SerializeValue(ref primarySkin); s.SerializeValue(ref secondarySkin);
        }

        public bool Equals(LoadoutData o) => primary == o.primary && secondary == o.secondary && primarySkin == o.primarySkin && secondarySkin == o.secondarySkin;
    }

    /// <summary>
    /// A fighter in the arena - human (owner-authoritative movement) or bot (server simulated).
    /// Health, death and respawn are always server-authoritative.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(WeaponController))]
    public class Combatant : NetworkBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();
        public static Combatant Local { get; private set; }
        public static event Action<Combatant> LocalSpawned;
        public static event Action<Combatant> AnyDied;

        public readonly NetworkVariable<FixedString32Bytes> DisplayName = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<byte> TeamNV = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> ColorNV = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> IsBotNV = new NetworkVariable<bool>();
        public readonly NetworkVariable<short> Health = new NetworkVariable<short>(GameRules.MaxHealth);
        public readonly NetworkVariable<bool> AliveNV = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<bool> FrozenNV = new NetworkVariable<bool>();
        public readonly NetworkVariable<LoadoutData> Loadout = new NetworkVariable<LoadoutData>();
        public readonly NetworkVariable<double> SpawnTime = new NetworkVariable<double>();
        // owner-written presentation state
        public readonly NetworkVariable<byte> Slot = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<short> AimPitch = new NetworkVariable<short>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> CrouchNV = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public struct InitData
        {
            public string name;
            public Team team;
            public byte color;
            public bool bot;
            public BotSkill skill;
            public LoadoutData loadout;
            public SpawnSelector.Spawn spawn;
        }
        public InitData PendingInit;

        public PlayerMotor Motor { get; private set; }
        public WeaponController Weapons { get; private set; }
        public CharacterVisual Visual { get; private set; }
        public BotBrain Brain { get; private set; }
        public NetworkTransform NetTransform { get; private set; }

        public string Name => DisplayName.Value.ToString();
        public Team Team => (Team)TeamNV.Value;
        public bool IsBot => IsBotNV.Value;
        public bool Alive => AliveNV.Value;
        public bool Frozen => FrozenNV.Value;
        public bool IsLocalHuman => IsOwner && !IsBot;
        /// <summary>True on the peer that simulates this combatant (owner for humans, server for bots).</summary>
        public bool HasControl => IsSpawned && (IsBot ? IsServer : IsOwner);
        public Vector3 EyePosition => transform.position + Vector3.up * Motor.EyeHeight;
        public Vector3 CenterMass => transform.position + Vector3.up * (Motor.Crouching ? 0.75f : 1.1f);
        public Color TeamColor => GameConfig.TeamColor(Team, ColorNV.Value);
        public bool SpawnProtected => NetworkManager != null && NetworkManager.ServerTime.Time - SpawnTime.Value < GameRules.SpawnProtection;

        // server-only bookkeeping
        struct DamageRecord { public ulong attacker; public float amount; public float time; }
        readonly List<DamageRecord> damageLog = new List<DamageRecord>();
        float lastDamageTime = -99f;
        float regenAccum;

        public static Combatant Find(ulong networkObjectId)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].NetworkObjectId == networkObjectId) return All[i];
            return null;
        }

        public bool IsEnemyOf(Combatant other)
        {
            if (other == null || other == this) return false;
            if (Team == Team.None || other.Team == Team.None) return true;
            return Team != other.Team;
        }

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Weapons = GetComponent<WeaponController>();
            Visual = GetComponent<CharacterVisual>();
            Brain = GetComponent<BotBrain>();
            NetTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                var d = PendingInit;
                DisplayName.Value = string.IsNullOrEmpty(d.name) ? "Player" : d.name;
                TeamNV.Value = (byte)d.team;
                ColorNV.Value = d.color;
                IsBotNV.Value = d.bot;
                Loadout.Value = d.loadout;
                Health.Value = GameRules.MaxHealth;
                AliveNV.Value = true;
                SpawnTime.Value = NetworkManager.ServerTime.Time;
                if (Brain != null) Brain.Skill = d.skill;
            }
            All.Add(this);
            if (Brain != null) Brain.enabled = IsServer && IsBot;
            AliveNV.OnValueChanged += OnAliveChanged;
            Weapons.OnSpawned(this);
            Motor.OnSpawned(this);
            Visual?.Build(this);
            gameObject.layer = Layers.Player;
            if (IsOwner && !IsBotNV.Value)
            {
                Local = this;
                GetComponent<LocalPlayerInput>()?.Bind(this);
                GetComponent<ViewModel>()?.Bind(this);
                GetComponent<LocalFeedback>()?.Bind(this);
                LocalSpawned?.Invoke(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            AliveNV.OnValueChanged -= OnAliveChanged;
            if (Local == this) Local = null;
        }

        void OnAliveChanged(bool prev, bool now)
        {
            if (!now) AnyDied?.Invoke(this);
        }

        // ---------------------------------------------------------------- server: damage
        public void ServerApplyDamage(Combatant attacker, float amount, HitZone zone, byte weaponId, Vector3 fromPos)
        {
            if (!IsServer || !Alive || Frozen) return;
            if (SpawnProtected && attacker != null && attacker != this) return;
            if (attacker != null && attacker != this && !attacker.IsEnemyOf(this)) return; // no friendly fire
            int dmg = Mathf.Max(1, Mathf.RoundToInt(amount));
            int newHealth = Mathf.Max(0, Health.Value - dmg);
            Health.Value = (short)newHealth;
            lastDamageTime = Time.time;
            regenAccum = 0f;
            if (attacker != null && attacker != this)
                damageLog.Add(new DamageRecord { attacker = attacker.NetworkObjectId, amount = dmg, time = Time.time });

            DamagedRpc((short)newHealth, (short)dmg, fromPos, attacker != null ? attacker.NetworkObjectId : 0, (byte)zone);
            if (attacker != null && attacker != this) attacker.Weapons.ServerConfirmHit(this, dmg, zone, newHealth <= 0);
            if (newHealth <= 0) ServerDie(attacker, weaponId, zone == HitZone.Head, transform.position - fromPos);
        }

        void ServerDie(Combatant killer, byte weaponId, bool headshot, Vector3 dir)
        {
            AliveNV.Value = false;
            var assisters = new List<ulong>();
            float cutoff = Time.time - GameRules.AssistWindow;
            foreach (var r in damageLog)
                if (r.time >= cutoff && r.amount >= 20 && !assisters.Contains(r.attacker)) assisters.Add(r.attacker);
            damageLog.Clear();
            DiedRpc(killer != null ? killer.NetworkObjectId : NetworkObjectId, weaponId, headshot, dir.normalized);
            if (MatchManager.I != null) MatchManager.I.ServerOnKill(this, killer, weaponId, headshot, assisters);
        }

        public void ServerKillVolume()
        {
            if (Alive) ServerApplyDamage(null, 999, HitZone.Body, 255, transform.position + Vector3.up);
        }

        public void ServerFreeze() { FrozenNV.Value = true; }

        public void ServerRespawn(Vector3 pos, float yaw)
        {
            if (!IsServer) return;
            Health.Value = GameRules.MaxHealth;
            AliveNV.Value = true;
            FrozenNV.Value = false;
            SpawnTime.Value = NetworkManager.ServerTime.Time;
            damageLog.Clear();
            RespawnRpc(pos, yaw);
        }

        public void ServerHeal(int amount)
        {
            if (!Alive) return;
            Health.Value = (short)Mathf.Min(GameRules.MaxHealth, Health.Value + amount);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !Alive) return;
            float kill = MapInfo.I != null ? MapInfo.I.killHeight : GameRules.KillHeight;
            if (transform.position.y < kill) { ServerKillVolume(); return; }
            if (Health.Value < GameRules.MaxHealth && Time.time - lastDamageTime > GameRules.RegenDelay)
            {
                regenAccum += GameRules.RegenPerSecond * Time.deltaTime;
                if (regenAccum >= 1f)
                {
                    int add = Mathf.FloorToInt(regenAccum);
                    regenAccum -= add;
                    Health.Value = (short)Mathf.Min(GameRules.MaxHealth, Health.Value + add);
                }
            }
        }

        // ---------------------------------------------------------------- RPCs
        public event Action<int, Vector3, ulong, HitZone> Damaged;   // damage, from, attacker, zone
        public event Action<ulong, byte, bool, Vector3> Died;
        public event Action Respawned;

        [Rpc(SendTo.Everyone)]
        void DamagedRpc(short newHealth, short dmg, Vector3 from, ulong attackerId, byte zone)
        {
            Damaged?.Invoke(dmg, from, attackerId, (HitZone)zone);
        }

        [Rpc(SendTo.Everyone)]
        void DiedRpc(ulong killerId, byte weaponId, bool headshot, Vector3 dir)
        {
            Died?.Invoke(killerId, weaponId, headshot, dir);
            Visual?.BreakApart(dir, headshot);
            Weapons.OnDied();
            if (IsOwner && !IsBot) GameCamera.I?.EnterDeathCam(this, Find(killerId));
        }

        [Rpc(SendTo.Everyone)]
        void RespawnRpc(Vector3 pos, float yaw)
        {
            if (HasControl) Motor.Teleport(pos, yaw);
            Weapons.OnRespawned();
            Visual?.Rebuild();
            if (IsOwner && !IsBot) GetComponent<ViewModel>()?.Build();
            Respawned?.Invoke();
            if (IsOwner && !IsBot) GameCamera.I?.EnterFirstPerson(this);
        }
    }
}
