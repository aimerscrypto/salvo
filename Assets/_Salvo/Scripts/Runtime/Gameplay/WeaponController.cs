using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    public struct HitClaim : INetworkSerializable
    {
        public ulong target;
        public byte zone;
        public byte pellets;
        public Vector3 point;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref target); s.SerializeValue(ref zone); s.SerializeValue(ref pellets); s.SerializeValue(ref point);
        }
    }

    /// <summary>One trigger pull: where it came from, where (up to 4) rays ended, and who it claims to have hit.</summary>
    public struct ShotData : INetworkSerializable
    {
        public byte weapon;
        public Vector3 origin;
        public Vector3[] ends;
        public byte[] endKinds;       // 0 = world (impact fx), 1 = character, 2 = miss (no impact)
        public Vector3[] normals;
        public HitClaim[] claims;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref weapon);
            s.SerializeValue(ref origin);
            int n = ends?.Length ?? 0;
            s.SerializeValue(ref n);
            if (s.IsReader) { ends = new Vector3[n]; endKinds = new byte[n]; normals = new Vector3[n]; }
            for (int i = 0; i < n; i++) { s.SerializeValue(ref ends[i]); s.SerializeValue(ref endKinds[i]); s.SerializeValue(ref normals[i]); }
            int m = claims?.Length ?? 0;
            s.SerializeValue(ref m);
            if (s.IsReader) claims = new HitClaim[m];
            for (int i = 0; i < m; i++) claims[i].NetworkSerialize(s);
        }
    }

    /// <summary>
    /// Loadout, ammo, firing, reloading, switching and melee for a combatant.
    /// The controlling peer simulates and raycasts ("favor the shooter"); the server validates
    /// every claim (rate, range, line of sight, plausibility) before applying damage.
    /// </summary>
    public class WeaponController : NetworkBehaviour
    {
        public struct WeaponInput
        {
            public bool fireHeld, firePressed, aimHeld, reload, melee, lastWeapon;
            public int selectSlot; // -1 none
            public int scroll;
        }

        public class WeaponState
        {
            public WeaponDef def;
            public int mag, reserve;
            public byte skin;
        }

        public WeaponInput In;
        public readonly WeaponState[] States = new WeaponState[3];
        public int CurrentSlot { get; private set; }
        public WeaponDef Current => States[CurrentSlot]?.def;
        public WeaponState CurrentState => States[CurrentSlot];
        public float AimT { get; private set; }
        public float Bloom { get; private set; }
        public bool Reloading { get; private set; }
        public float ReloadProgress => Reloading && reloadDuration > 0 ? 1f - Mathf.Clamp01((reloadEnd - Time.time) / reloadDuration) : 0f;
        public bool Equipping => Time.time < equipEnd;

        // local presentation events (fire on every peer that simulates or receives FX)
        public event Action<WeaponDef> Fired;              // controlling peer
        public event Action<WeaponDef, float> ReloadStarted;
        public event Action<WeaponDef> ReloadShell;
        public event Action<WeaponDef> ReloadFinished;
        public event Action<WeaponDef> Equipped;
        public event Action<WeaponDef> DryFired;
        public event Action<WeaponDef> Meleed;
        public event Action<WeaponDef> ActionCycled;       // pump / bolt
        public event Action<ShotData> RemoteShot;          // other peers' shots
        public event Action<int, HitZone, bool> HitConfirmed; // dmg, zone, kill (owner)

        Combatant c;
        int lastSlot = 1;
        float nextFire, equipEnd, reloadEnd, reloadDuration, nextShell, actionAt = -1f;
        bool cancelReload, meleeQueued;
        readonly System.Random rng = new System.Random();
        readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        readonly List<HitClaim> claimScratch = new List<HitClaim>();
        float serverLastShot;
        public int ShotsFired, ShotsHit;

        public static readonly int MeleeWeaponId = 5;
        /// <summary>Automation/test hook: hold ADS on the local player.</summary>
        public static bool DebugForceAim;

        public void OnSpawned(Combatant combatant)
        {
            c = combatant;
            c.Loadout.OnValueChanged += (_, __) => { if (!c.Alive) BuildStates(); };
            BuildStates();
            Select(0, true);
        }

        void BuildStates()
        {
            var cfg = GameConfig.I;
            var l = c.Loadout.Value;
            States[0] = Make(cfg.Weapon(l.primary), l.primarySkin);
            States[1] = Make(cfg.Weapon(l.secondary), l.secondarySkin);
            States[2] = Make(cfg.Weapon(MeleeWeaponId), 0);
        }

        static WeaponState Make(WeaponDef d, byte skin) => new WeaponState { def = d, mag = d.magSize, reserve = d.reserveAmmo, skin = skin };

        public void OnDied()
        {
            Reloading = false;
            AimT = 0f;
            if (c.IsLocalHuman && GameCamera.I != null) GameCamera.I.ZoomT = 0f;
        }

        public void OnRespawned()
        {
            BuildStates();
            Bloom = 0f;
            Select(0, true);
        }

        public void RefillAmmo()
        {
            for (int i = 0; i < 2; i++) if (States[i] != null) States[i].reserve = States[i].def.reserveAmmo;
        }

        void Select(int slot, bool instant = false)
        {
            if (slot < 0 || slot > 2 || States[slot] == null) return;
            if (slot != CurrentSlot) lastSlot = CurrentSlot;
            CurrentSlot = slot;
            Reloading = false;
            actionAt = -1f;
            var d = Current;
            equipEnd = Time.time + (instant ? 0.05f : d.equipTime);
            nextFire = Mathf.Max(nextFire, equipEnd);
            if (IsSpawned && IsOwner) c.Slot.Value = (byte)slot;
            Equipped?.Invoke(d);
        }

        // ------------------------------------------------------------------ simulation
        void Update()
        {
            if (c == null || !c.HasControl) return;
            if (!c.Alive || c.Frozen) { In = default; return; }
            float dt = Time.deltaTime;
            var d = Current;
            if (d == null) return;

            // switching
            if (In.selectSlot >= 0 && In.selectSlot != CurrentSlot) Select(In.selectSlot);
            else if (In.lastWeapon) Select(lastSlot);
            else if (In.scroll != 0) Select((CurrentSlot + In.scroll + 3) % 3);
            if (In.melee)
            {
                if (CurrentSlot != 2) { Select(2); equipEnd = Time.time + 0.08f; nextFire = equipEnd; }
                meleeQueued = true;
            }
            d = Current;
            var st = CurrentState;

            // aim
            bool canAim = (In.aimHeld || (DebugForceAim && c.IsLocalHuman)) && !Reloading && d.fireMode != FireMode.Melee && !c.Motor.Sliding && Time.time >= equipEnd - 0.1f;
            AimT = Mathf.MoveTowards(AimT, canAim ? 1f : 0f, dt / Mathf.Max(0.05f, d.adsTime));
            c.Motor.SpeedMultiplier = d.moveSpeedMultiplier * Mathf.Lerp(1f, d.adsMoveMultiplier, AimT);
            if (c.IsLocalHuman && GameCamera.I != null)
            {
                GameCamera.I.ZoomT = AimT;
                GameCamera.I.ZoomFov = d.adsFov;
                GameCamera.I.HideViewModel = d.scoped && AimT > 0.92f;
            }

            Bloom = Mathf.MoveTowards(Bloom, 0f, d.bloomRecovery * dt);

            // pump/bolt action cycle (presentation + timing)
            if (actionAt > 0f && Time.time >= actionAt) { actionAt = -1f; ActionCycled?.Invoke(d); }

            // reload
            if (In.reload) TryReload();
            if (Reloading)
            {
                if (d.reloadPerShell)
                {
                    if (Time.time >= nextShell)
                    {
                        if (st.mag < d.magSize && st.reserve > 0) { st.mag++; st.reserve--; ReloadShell?.Invoke(d); }
                        nextShell = Time.time + d.reloadTime;
                        if (st.mag >= d.magSize || st.reserve <= 0 || cancelReload) FinishReload(d);
                    }
                }
                else if (Time.time >= reloadEnd)
                {
                    int need = d.magSize - st.mag;
                    int take = Mathf.Min(need, st.reserve);
                    st.mag += take; st.reserve -= take;
                    FinishReload(d);
                }
            }

            // fire
            bool wants = d.fireMode == FireMode.Auto ? In.fireHeld : In.firePressed;
            if (meleeQueued && d.fireMode == FireMode.Melee) wants = true;
            if (wants && Time.time >= nextFire && Time.time >= equipEnd)
            {
                if (d.fireMode == FireMode.Melee) { meleeQueued = false; DoMelee(d); }
                else if (Reloading && d.reloadPerShell && st.mag > 0) { cancelReload = true; FinishReload(d); Fire(d, st); }
                else if (!Reloading)
                {
                    if (st.mag > 0) Fire(d, st);
                    else if (In.firePressed)
                    {
                        nextFire = Time.time + 0.25f;
                        DryFired?.Invoke(d);
                        TryReload();
                    }
                }
            }
            else if (!Reloading && st.mag == 0 && st.reserve > 0 && d.fireMode != FireMode.Melee && Time.time >= nextFire + 0.15f && Time.time >= equipEnd)
            {
                TryReload(); // auto reload when empty
            }

            In.firePressed = false; In.reload = false; In.melee = false; In.lastWeapon = false; In.selectSlot = -1; In.scroll = 0;
        }

        public void TryReload()
        {
            var d = Current; var st = CurrentState;
            if (d == null || Reloading || d.fireMode == FireMode.Melee) return;
            if (st.mag >= d.magSize || st.reserve <= 0 || Time.time < equipEnd) return;
            Reloading = true;
            cancelReload = false;
            AimT = Mathf.Min(AimT, 0.5f);
            if (d.reloadPerShell)
            {
                nextShell = Time.time + 0.35f;
                reloadDuration = 0.35f + d.reloadTime * (d.magSize - st.mag);
                reloadEnd = Time.time + reloadDuration;
            }
            else
            {
                reloadDuration = d.reloadTime;
                reloadEnd = Time.time + d.reloadTime;
            }
            ReloadStarted?.Invoke(d, reloadDuration);
        }

        void FinishReload(WeaponDef d)
        {
            if (!Reloading) return;
            Reloading = false;
            if (d.reloadPerShell) { actionAt = Time.time + 0.1f; nextFire = Mathf.Max(nextFire, Time.time + 0.3f); }
            ReloadFinished?.Invoke(d);
        }

        public float CurrentSpread()
        {
            var d = Current;
            float s = Mathf.Lerp(d.hipSpread, d.adsSpread, AimT);
            float speed01 = Mathf.Clamp01(c.Motor.HorizontalSpeed / GameRules.RunSpeed);
            s += d.moveSpreadAdd * speed01 * (1f - AimT * 0.6f);
            if (!c.Motor.Grounded) s += d.airSpreadAdd;
            if (c.Motor.Crouching && c.Motor.Grounded && !c.Motor.Sliding) s *= 0.8f;
            return s + Bloom;
        }

        void GetAim(out Vector3 origin, out Vector3 dir)
        {
            if (c.IsLocalHuman && GameCamera.I != null && GameCamera.I.Target == c)
            {
                var t = GameCamera.I.transform;
                origin = t.position; dir = t.forward;
            }
            else if (c.Brain != null && c.IsBot)
            {
                origin = c.EyePosition; dir = c.Brain.AimDirection;
            }
            else
            {
                origin = c.EyePosition;
                dir = Quaternion.Euler(c.Motor.Input.pitch, c.Motor.Input.yaw, 0) * Vector3.forward;
            }
        }

        void Fire(WeaponDef d, WeaponState st)
        {
            nextFire = Time.time + d.FireInterval;
            st.mag--;
            GetAim(out var origin, out var fwd);
            float spread = CurrentSpread();
            Physics.SyncTransforms();

            int n = Mathf.Max(1, d.pellets);
            int shown = Mathf.Min(n, 4);
            var shot = new ShotData { weapon = d.id, origin = origin, ends = new Vector3[shown], endKinds = new byte[shown], normals = new Vector3[shown] };
            claimScratch.Clear();
            for (int p = 0; p < n; p++)
            {
                Vector3 dir = SalvoMath.Spread(fwd, spread, rng);
                Vector3 end = origin + dir * d.range;
                byte kind = 2;
                Vector3 normal = -dir;
                if (Cast(origin, dir, d.range, out var hit, out var hb))
                {
                    end = hit.point; normal = hit.normal;
                    if (hb != null)
                    {
                        kind = 1;
                        AddClaim(hb.Owner.NetworkObjectId, hb.Zone, hit.point);
                    }
                    else kind = 0;
                }
                if (p < shown) { shot.ends[p] = end; shot.endKinds[p] = kind; shot.normals[p] = normal; }
            }
            shot.claims = claimScratch.ToArray();
            ShotsFired++;
            if (shot.claims.Length > 0) ShotsHit++;

            Bloom = Mathf.Min(d.maxBloom, Bloom + d.spreadPerShot);
            if (c.IsLocalHuman && GameCamera.I != null)
            {
                float adsDamp = Mathf.Lerp(1f, 0.6f, AimT);
                GameCamera.I.AddRecoil(d.recoilPitch * adsDamp * UnityEngine.Random.Range(0.85f, 1.15f), d.recoilYaw * UnityEngine.Random.Range(-1f, 1f) * adsDamp, d.recoilRecovery);
                GameCamera.I.Shake(d.shakeOnFire);
            }
            else if (c.IsBot && c.Brain != null) c.Brain.OnRecoil(d.recoilPitch);
            if (d.fireMode == FireMode.Pump || d.fireMode == FireMode.Bolt) actionAt = Time.time + Mathf.Min(0.3f, d.FireInterval * 0.35f);

            Fired?.Invoke(d);
            FxManager.I?.PlayShotLocal(c, shot);

            if (IsServer) ServerProcessShot(shot, true);
            else ShootRpc(shot);
        }

        void AddClaim(ulong target, HitZone zone, Vector3 point)
        {
            for (int i = 0; i < claimScratch.Count; i++)
            {
                if (claimScratch[i].target != target) continue;
                var cl = claimScratch[i];
                cl.pellets++;
                if (zone == HitZone.Head) cl.zone = (byte)HitZone.Head;
                claimScratch[i] = cl;
                return;
            }
            claimScratch.Add(new HitClaim { target = target, zone = (byte)zone, point = point, pellets = 1 });
        }

        /// <summary>Raycast against world + hitboxes, skipping our own hitboxes.</summary>
        bool Cast(Vector3 origin, Vector3 dir, float range, out RaycastHit best, out Hitbox hb)
        {
            best = default; hb = null;
            int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, Layers.ShotMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = hitBuffer[i];
                if (h.distance >= bestDist) continue;
                var box = h.collider.gameObject.layer == Layers.Hitbox ? h.collider.GetComponent<Hitbox>() : null;
                if (box != null && (box.Owner == c || !box.Owner.Alive)) continue;
                if (box == null && h.collider.transform.IsChildOf(transform)) continue;
                bestDist = h.distance; best = h; hb = box; found = true;
            }
            return found;
        }

        void DoMelee(WeaponDef d)
        {
            nextFire = Time.time + d.FireInterval;
            GetAim(out var origin, out var fwd);
            Physics.SyncTransforms();
            Meleed?.Invoke(d);
            var shot = new ShotData { weapon = d.id, origin = origin, ends = new Vector3[0], endKinds = new byte[0], normals = new Vector3[0], claims = new HitClaim[0] };
            // generous sphere sweep for melee
            int count = Physics.SphereCastNonAlloc(origin, 0.35f, fwd, hitBuffer, d.range, Layers.ShotMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue; Hitbox target = null; Vector3 point = default;
            for (int i = 0; i < count; i++)
            {
                var h = hitBuffer[i];
                var box = h.collider.GetComponent<Hitbox>();
                if (box == null || box.Owner == c || !box.Owner.Alive) continue;
                if (h.distance < bestDist) { bestDist = h.distance; target = box; point = h.point == Vector3.zero ? box.transform.position : h.point; }
            }
            if (target != null)
            {
                shot.claims = new[] { new HitClaim { target = target.Owner.NetworkObjectId, zone = (byte)HitZone.Body, point = point, pellets = 1 } };
                ShotsHit++;
            }
            ShotsFired++;
            if (IsServer) ServerProcessShot(shot, true);
            else ShootRpc(shot);
        }

        // ------------------------------------------------------------------ server
        [Rpc(SendTo.Server)]
        void ShootRpc(ShotData shot) => ServerProcessShot(shot, false);

        void ServerProcessShot(ShotData shot, bool trusted)
        {
            if (!c.Alive || c.Frozen) return;
            var d = GameConfig.I.Weapon(shot.weapon);
            if (d == null) return;
            if (!trusted)
            {
                // rate limit (generous to absorb jitter)
                if (Time.time - serverLastShot < d.FireInterval * 0.55f) return;
                if ((shot.origin - c.EyePosition).sqrMagnitude > 3.0f * 3.0f) return;
                if (shot.claims != null && shot.claims.Length > Mathf.Max(1, d.pellets)) return;
            }
            serverLastShot = Time.time;

            if (shot.claims != null)
            {
                foreach (var cl in shot.claims)
                {
                    var target = Combatant.Find(cl.target);
                    if (target == null || !target.Alive || !c.IsEnemyOf(target)) continue;
                    float dist = Vector3.Distance(shot.origin, cl.point);
                    if (dist > d.range + 2f) continue;
                    if (!trusted)
                    {
                        // claimed point must be near where the server believes the target is
                        if ((cl.point - target.CenterMass).sqrMagnitude > 4.5f * 4.5f) continue;
                        // and must not be behind solid world geometry
                        if (Physics.Linecast(shot.origin, cl.point, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                    }
                    var zone = (HitZone)cl.zone;
                    float dmg;
                    if (d.fireMode == FireMode.Melee)
                    {
                        // backstab: attacker behind the victim -> lethal
                        Vector3 toVictim = (target.transform.position - transform.position); toVictim.y = 0;
                        bool back = Vector3.Dot(target.transform.forward, toVictim.normalized) > 0.55f;
                        dmg = back ? 150f : d.damage;
                        if (back) zone = HitZone.Head;
                    }
                    else dmg = d.DamageAt(dist, zone) * Mathf.Clamp(cl.pellets, 1, Mathf.Max(1, d.pellets));
                    target.ServerApplyDamage(c, dmg, zone, d.id, shot.origin);
                }
            }
            // replicate visuals
            if (c.IsBot) ShotFxRpc(shot, RpcTarget.Everyone);
            else ShotFxRpc(shot, RpcTarget.NotOwner);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ShotFxRpc(ShotData shot, RpcParams p = default)
        {
            if (c.HasControl) return; // the shooter already played it locally
            RemoteShot?.Invoke(shot);
            FxManager.I?.PlayShotRemote(c, shot);
        }

        public void ServerConfirmHit(Combatant target, int dmg, HitZone zone, bool kill)
        {
            if (c.IsBot) return;
            HitConfirmRpc((short)dmg, (byte)zone, kill, target.CenterMass + Vector3.up * 0.6f);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(short dmg, byte zone, bool kill, Vector3 at)
        {
            HitConfirmed?.Invoke(dmg, (HitZone)zone, kill);
            Hud.I?.OnHitConfirmed(dmg, (HitZone)zone, kill, at);
        }

        [Rpc(SendTo.Owner)]
        public void RefillAmmoRpc() => RefillAmmo();
    }
}
