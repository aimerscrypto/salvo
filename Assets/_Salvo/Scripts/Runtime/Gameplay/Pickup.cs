using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    /// <summary>Health / ammo pickup. Server polls distances (no trigger physics), clients animate.</summary>
    public class Pickup : NetworkBehaviour
    {
        public enum Kind : byte { Health, Ammo }
        public static readonly List<Pickup> All = new List<Pickup>();

        public Kind kind;
        public int amount = 50;
        public float respawnTime = 18f;
        public float radius = 1.1f;
        public Transform visual;

        public readonly NetworkVariable<bool> Available = new NetworkVariable<bool>(true);
        float respawnAt, checkTimer, spin;
        Vector3 visualBase;
        bool local = true; // menu mode (not networked) still shows the pickup

        void Awake()
        {
            if (visual != null) visualBase = visual.localPosition;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public override void OnNetworkSpawn()
        {
            local = false;
            Available.OnValueChanged += (a, b) =>
            {
                if (!b) return;
                FxManager.I?.Dust(transform.position + Vector3.up * 0.4f, Vector3.up, 4, Color.white);
            };
        }

        public bool IsAvailable => local || Available.Value;

        public static bool NearestAvailable(Vector3 from, Kind k, out Vector3 pos)
        {
            pos = default;
            float best = float.MaxValue;
            foreach (var p in All)
            {
                if (p.kind != k || !p.IsAvailable) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < best) { best = d; pos = p.transform.position; }
            }
            return best < float.MaxValue;
        }

        void Update()
        {
            if (visual != null)
            {
                bool show = IsAvailable;
                if (visual.gameObject.activeSelf != show) visual.gameObject.SetActive(show);
                spin += Time.deltaTime * 90f;
                visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * 2.2f) * 0.08f);
                visual.localRotation = Quaternion.Euler(0, spin, 0);
            }
            if (!IsSpawned || !IsServer) return;
            if (!Available.Value)
            {
                if (Time.time >= respawnAt) Available.Value = true;
                return;
            }
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 0.1f;
            foreach (var c in Combatant.All)
            {
                if (!c.Alive) continue;
                Vector3 d = c.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1.6f) continue;
                d.y = 0;
                if (d.sqrMagnitude > radius * radius) continue;
                if (kind == Kind.Health && c.Health.Value >= GameRules.MaxHealth) continue;
                Consume(c);
                break;
            }
        }

        void Consume(Combatant c)
        {
            Available.Value = false;
            respawnAt = Time.time + respawnTime;
            if (kind == Kind.Health) c.ServerHeal(amount);
            else
            {
                if (c.HasControl) c.Weapons.RefillAmmo();
                else c.Weapons.RefillAmmoRpc();
            }
            PickedRpc(c.NetworkObjectId);
        }

        [Rpc(SendTo.Everyone)]
        void PickedRpc(ulong who)
        {
            var bank = AudioManager.Bank;
            var clip = kind == Kind.Health ? bank?.pickupHealth : bank?.pickupAmmo;
            var me = Combatant.Local;
            if (me != null && me.NetworkObjectId == who) AudioManager.Play2D(clip, 0.8f);
            else AudioManager.Play3D(clip, transform.position, 0.7f);
            if (me != null && me.NetworkObjectId == who) Hud.I?.Toast(kind == Kind.Health ? "+" + amount + " HEALTH" : "AMMO REFILLED");
        }
    }
}
