using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Salvo
{
    /// <summary>
    /// Third-person body: a segmented trooper animated procedurally (no skinning, no Animator).
    /// Walk/run cycle from velocity, crouch and airborne poses, aim pitch through spine/head,
    /// two-bone IK that keeps both hands on the held weapon, and a toy-like break-apart death.
    /// Local player: same body, rendered shadows-only.
    /// </summary>
    public class CharacterVisual : MonoBehaviour
    {
        Combatant c;
        Transform wrapper, model;
        Transform hips, torso, head, upL, upR, loL, loR, thL, thR, shL, shR, wristL, wristR;
        TrooperHands hands;
        Vector3 hipsBase;
        readonly Dictionary<Transform, Quaternion> baseRot = new Dictionary<Transform, Quaternion>();
        float upperLen = 0.28f, lowerLen = 0.28f;

        Transform weaponPivot;
        readonly GameObject[] weaponObjs = new GameObject[3];
        readonly Transform[] muzzles = new Transform[3];
        readonly Transform[] gripLs = new Transform[3];
        readonly Transform[] gripRs = new Transform[3];
        int shownSlot = -1;

        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Collider> hitboxes = new List<Collider>();
        Vector3 lastPos, vel;
        float phase, crouchT, airT, pitch, fireKick, hurtFlash;
        bool broken;
        int lastStep;
        GameObject blobShadow;

        public Vector3 MuzzlePosition
        {
            get
            {
                int s = c != null ? c.Slot.Value : 0;
                var m = s >= 0 && s < 3 ? muzzles[s] : null;
                return m != null ? m.position : (c != null ? c.EyePosition : transform.position);
            }
        }

        public void Build(Combatant combatant)
        {
            c = combatant;
            c.Weapons.RemoteShot += _ => fireKick = 1f;
            c.Weapons.Fired += _ => fireKick = 1f;
            c.Damaged += (dmg, from, att, zone) => hurtFlash = 1f;
            Rebuild();
        }

        public void Rebuild()
        {
#if DEVELOPMENT_BUILD
            Bootstrap.FrameRebuilds++;
#endif
            if (wrapper != null) Destroy(wrapper.gameObject);
            if (weaponPivot != null) Destroy(weaponPivot.gameObject);
            broken = false;
            renderers.Clear();
            hitboxes.Clear();
            baseRot.Clear();
            shownSlot = -1;

            var cfg = GameConfig.I;
            wrapper = new GameObject("ModelWrapper").transform;
            wrapper.SetParent(transform, false);
            model = Instantiate(cfg.trooperModel, wrapper).transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;

            hips = model.FindDeep("Hips"); torso = model.FindDeep("Torso"); head = model.FindDeep("Head");
            upL = model.FindDeep("UpperArm_L"); upR = model.FindDeep("UpperArm_R");
            loL = model.FindDeep("LowerArm_L"); loR = model.FindDeep("LowerArm_R");
            thL = model.FindDeep("Thigh_L"); thR = model.FindDeep("Thigh_R");
            shL = model.FindDeep("Shin_L"); shR = model.FindDeep("Shin_R");
            hipsBase = hips.localPosition;
            foreach (var t in new[] { hips, torso, head, upL, upR, loL, loR, thL, thR, shL, shR }) baseRot[t] = t.localRotation;
            upperLen = Vector3.Distance(upR.position, loR.position);
            // hands are posed per weapon and placed directly on the grips
            hands = new TrooperHands(model, loR, loL);
            wristR = hands.WristR; wristL = hands.WristL;
            lowerLen = Vector3.Distance(loR.position, wristR.position);

            // team colors
            var teamMat = cfg.TeamMaterial(c.Team, c.ColorNV.Value);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) // include the inactive hand variants
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == cfg.teamRed) mats[i] = teamMat;
                r.sharedMaterials = mats;
                renderers.Add(r);
            }

            BuildHitboxes();
            BuildWeapons();
            ApplyVisibility();

            if (blobShadow == null && !c.IsLocalHuman) blobShadow = FxManager.I != null ? FxManager.I.CreateBlobShadow(transform) : null;
            lastPos = transform.position;
        }

        void BuildHitboxes()
        {
            var body = wrapper.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            AddBox(head, HitZone.Head, new Vector3(0, 0.12f, 0.01f), Vector3.zero, 0.17f);
            AddBox(torso, HitZone.Body, new Vector3(0, 0.2f, 0), new Vector3(0.44f, 0.46f, 0.32f), 0);
            AddBox(hips, HitZone.Body, new Vector3(0, -0.02f, 0), new Vector3(0.36f, 0.22f, 0.26f), 0);
            AddBox(thL, HitZone.Limb, new Vector3(0, -0.21f, 0), new Vector3(0.17f, 0.44f, 0.18f), 0);
            AddBox(thR, HitZone.Limb, new Vector3(0, -0.21f, 0), new Vector3(0.17f, 0.44f, 0.18f), 0);
            AddBox(shL, HitZone.Limb, new Vector3(0, -0.24f, 0), new Vector3(0.15f, 0.5f, 0.2f), 0);
            AddBox(shR, HitZone.Limb, new Vector3(0, -0.24f, 0), new Vector3(0.15f, 0.5f, 0.2f), 0);
            AddBox(upL, HitZone.Limb, new Vector3(0, -0.14f, 0), new Vector3(0.14f, 0.3f, 0.14f), 0);
            AddBox(upR, HitZone.Limb, new Vector3(0, -0.14f, 0), new Vector3(0.14f, 0.3f, 0.14f), 0);
            AddBox(loL, HitZone.Limb, new Vector3(0, -0.15f, 0), new Vector3(0.13f, 0.32f, 0.13f), 0);
            AddBox(loR, HitZone.Limb, new Vector3(0, -0.15f, 0), new Vector3(0.13f, 0.32f, 0.13f), 0);
        }

        void AddBox(Transform part, HitZone zone, Vector3 center, Vector3 size, float sphereRadius)
        {
            var go = new GameObject("HB_" + part.name);
            go.layer = Layers.Hitbox;
            go.transform.SetParent(part, false);
            // offsets are authored as (right, up, forward); FBX nodes use (right, forward, down)
            go.transform.localPosition = new Vector3(center.x, center.z, -center.y);
            size = new Vector3(size.x, size.z, size.y);
            Collider col;
            if (sphereRadius > 0) { var s = go.AddComponent<SphereCollider>(); s.radius = sphereRadius; col = s; }
            else { var b = go.AddComponent<BoxCollider>(); b.size = size; col = b; }
            var hb = go.AddComponent<Hitbox>();
            hb.Owner = c; hb.Zone = zone;
            hitboxes.Add(col);
        }

        void BuildWeapons()
        {
            var cfg = GameConfig.I;
            weaponPivot = new GameObject("WeaponPivot").transform;
            weaponPivot.SetParent(transform, false);
            weaponPivot.localPosition = new Vector3(0f, 1.36f, 0f);
            var states = c.Weapons.States;
            for (int s = 0; s < 3; s++)
            {
                var st = states[s];
                if (st == null || st.def == null || st.def.model == null) continue;
                var holder = new GameObject("W" + s).transform;
                holder.SetParent(weaponPivot, false);
                var wrap = new GameObject("Wrap").transform;
                wrap.SetParent(holder, false);
                var inst = Instantiate(st.def.model, wrap).transform;
                ApplySkin(inst, st.def, st.skin);
                var gR = inst.FindDeep("GripR");
                gripRs[s] = gR; gripLs[s] = inst.FindDeep("GripL"); muzzles[s] = inst.FindDeep("Muzzle");
                // place grip at the right-hand hold point (right of chest, forward)
                Vector3 hold = st.def.fireMode == FireMode.Melee ? new Vector3(0.2f, -0.2f, 0.32f) : new Vector3(0.12f, -0.12f, 0.3f);
                if (st.def.fireMode == FireMode.Melee) wrap.localRotation = Quaternion.Euler(-55f, 0f, 0f);
                Vector3 gripLocal = gR != null ? holder.InverseTransformPoint(gR.position) : Vector3.zero;
                wrap.localPosition = hold - gripLocal;
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) renderers.Add(r);
                weaponObjs[s] = holder.gameObject;
                holder.gameObject.SetActive(false);
            }
        }

        public static void ApplySkin(Transform weapon, WeaponDef def, int skinId)
        {
            var cfg = GameConfig.I;
            var skin = cfg.Skin(skinId);
            if (skin == null) return;
            var mat = skin.GetMaterial(cfg.skinTemplate);
            foreach (var r in weapon.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == cfg.skinTemplate || (mats[i] != null && mats[i].name.StartsWith("Skin_"))) { mats[i] = mat; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void ApplyVisibility()
        {
            bool local = c.IsLocalHuman;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.shadowCastingMode = local ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                r.receiveShadows = !local;
            }
        }

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            if (c == null || broken || model == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // velocity estimate works for every peer (local/remote/bot)
            Vector3 p = transform.position;
            Vector3 v = c.HasControl ? c.Motor.Velocity : (p - lastPos) / dt;
            lastPos = p;
            vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-12f * dt));
            Vector3 local = transform.InverseTransformDirection(new Vector3(vel.x, 0, vel.z));
            float hs = local.magnitude;
            bool airborne = c.HasControl ? !c.Motor.Grounded : Mathf.Abs(vel.y) > 2.2f;
            airT = Mathf.MoveTowards(airT, airborne ? 1f : 0f, dt * 6f);
            crouchT = Mathf.MoveTowards(crouchT, c.Motor.Crouching ? 1f : 0f, dt * 7f);
            float targetPitch = c.HasControl ? c.Motor.Input.pitch : c.AimPitch.Value * 0.1f;
            pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-15f * dt));
            fireKick = Mathf.MoveTowards(fireKick, 0f, dt * 8f);

            foreach (var kv in baseRot) kv.Key.localRotation = kv.Value;

            // legs
            float run = Mathf.Clamp01(hs / GameRules.RunSpeed);
            float dirSign = local.z < -0.5f ? -1f : 1f;
            phase += dt * hs * 1.75f * dirSign;
            int step = Mathf.FloorToInt(phase / Mathf.PI);
            if (step != lastStep)
            {
                lastStep = step;
                if (!c.IsLocalHuman && !airborne && hs > 2.5f && !c.Motor.Crouching)
                {
                    var fs = AudioManager.Bank != null ? AudioManager.Bank.footsteps : null;
                    if (fs != null && fs.Length > 0) AudioManager.Play3D(fs[Random.Range(0, fs.Length)], transform.position, 0.55f, Random.Range(0.9f, 1.1f), 28f);
                }
            }
            float amp = Mathf.Lerp(0f, 38f, run) * (1f - airT);
            float sL = Mathf.Sin(phase), sR = Mathf.Sin(phase + Mathf.PI);
            float crouchThigh = 70f * crouchT, crouchShin = -95f * crouchT;
            Leg(thL, shL, sL * amp + crouchThigh + airT * 30f, Mathf.Min(0f, Mathf.Cos(phase)) * amp * 1.2f + crouchShin - airT * 45f);
            Leg(thR, shR, sR * amp + crouchThigh - airT * 5f, Mathf.Min(0f, Mathf.Cos(phase + Mathf.PI)) * amp * 1.2f + crouchShin - airT * 20f);
            // strafing: turn the hips into the direction of travel a bit
            float strafeYaw = hs > 0.5f ? Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Abs(local.z) + 0.01f) * Mathf.Rad2Deg, -45f, 45f) * run : 0f;
            hips.localRotation = baseRot[hips] * Quaternion.Euler(0, -strafeYaw * dirSign * 0.6f, 0);
            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.045f * run * (1f - airT);
            hips.localPosition = hipsBase + new Vector3(0, -0.36f * crouchT - bob, 0);

            // spine and head follow aim (in wrapper space, +X rotation pitches "forward" for the model)
            torso.localRotation = baseRot[torso] * Quaternion.Euler((-pitch * 0.45f - crouchT * 10f) * AimSign, strafeYaw * dirSign * 0.6f, 0);
            head.localRotation = baseRot[head] * Quaternion.Euler(-pitch * 0.4f * AimSign, 0, 0);

            // weapon follows aim and crouch
            weaponPivot.localPosition = new Vector3(0f, 1.36f - 0.36f * crouchT - bob, -fireKick * 0.05f);
            weaponPivot.localRotation = Quaternion.Euler(pitch * 0.95f - fireKick * 4f, 0, 0);
            int slot = c.Slot.Value;
            if (slot != shownSlot)
            {
                for (int i = 0; i < 3; i++) if (weaponObjs[i] != null) weaponObjs[i].SetActive(i == slot);
                shownSlot = slot;
            }

            // hands: snap the posed hand variants onto the weapon grips, then IK each arm to its wrist
            var def = c.Weapons.Current;
            var gR = slot >= 0 && slot < 3 ? gripRs[slot] : null;
            var gL = slot >= 0 && slot < 3 ? gripLs[slot] : null;
            hands.Use(def);
            Vector3 poleR = transform.position + transform.right * 0.5f + Vector3.down * 0.4f - transform.forward * 0.2f;
            Vector3 poleL = transform.position - transform.right * 0.6f + Vector3.down * 0.4f;
            if (hands.SnapRight(gR, out var wR)) SolveArm(upR, loR, wristR, wR, poleR);
            else hands.RestRight();
            if (hands.SnapLeft(TrooperHands.SupportMarker(def, gR, gL), out var wL)) SolveArm(upL, loL, wristL, wL, poleL);
            else
            {
                // free hand (knife): guard position in front of the chest
                Vector3 tgt = transform.position + Vector3.up * (1.05f - 0.3f * crouchT) + transform.forward * 0.15f - transform.right * 0.25f;
                SolveArm(upL, loL, wristL, tgt, poleL);
                hands.RestLeft();
            }

            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, dt * 6f);
        }

        // Model-space sign conventions (verified in editor; axis-converted FBX nodes)
        const float LegSign = -1f;
        const float AimSign = -1f;

        void Leg(Transform thigh, Transform shin, float thighAngle, float shinAngle)
        {
            thigh.localRotation = baseRot[thigh] * Quaternion.Euler(thighAngle * LegSign, 0, 0);
            shin.localRotation = baseRot[shin] * Quaternion.Euler(shinAngle * LegSign, 0, 0);
        }

        void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 s = upper.position;
            Vector3 toT = target - s;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (upperLen + lowerLen) * 0.999f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((upperLen * upperLen + d * d - lowerLen * lowerLen) / (2f * upperLen * d), -1f, 1f);
            Vector3 bendAxis = Vector3.Cross(dir, pole - s);
            if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = transform.right;
            Vector3 perp = Vector3.Cross(bendAxis.normalized, dir).normalized;
            Vector3 elbow = s + dir * (upperLen * cosA) + perp * (upperLen * Mathf.Sqrt(1f - cosA * cosA));

            Vector3 curUpper = lower.position - s;
            upper.rotation = Quaternion.FromToRotation(curUpper, elbow - s) * upper.rotation;
            Vector3 curLower = hand.position - lower.position;
            lower.rotation = Quaternion.FromToRotation(curLower, target - lower.position) * lower.rotation;
        }

        // ------------------------------------------------------------------ death
        public void BreakApart(Vector3 dir, bool headshot)
        {
            if (broken || model == null) return;
            broken = true;
#if DEVELOPMENT_BUILD
            Bootstrap.FrameBreaks++;
#endif
            foreach (var h in hitboxes) if (h != null) h.enabled = false;
            if (weaponPivot != null) weaponPivot.gameObject.SetActive(false);
            if (blobShadow != null) blobShadow.SetActive(false);
            FxManager.I?.DeathPoof(transform.position + Vector3.up * 1f, c.TeamColor);

            var parts = new List<Transform>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>()) parts.Add(r.transform);
            var debrisRoot = new GameObject("Debris_" + c.Name).transform;
            foreach (var part in parts)
            {
                part.SetParent(debrisRoot, true);
                part.gameObject.layer = Layers.Debris;
                var mf = part.GetComponent<MeshFilter>();
                var box = part.gameObject.AddComponent<BoxCollider>();
                if (mf != null && mf.sharedMesh != null) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size * 0.85f, Vector3.one * 0.06f); }
                var rb = part.gameObject.AddComponent<Rigidbody>();
                rb.mass = 2f;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                Vector3 push = dir * Random.Range(2f, 4.5f) + Vector3.up * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 1.5f;
                if (headshot && part.name == "Head") push += Vector3.up * 4f + dir * 3f;
                rb.linearVelocity = push + vel * 0.6f;
                rb.angularVelocity = Random.insideUnitSphere * 8f;
            }
            debrisRoot.gameObject.AddComponent<DebrisFader>().Begin(3.2f);
            Destroy(wrapper.gameObject);
            wrapper = null; model = null;
        }

        void OnDestroy()
        {
            if (blobShadow != null) Destroy(blobShadow);
        }
    }

    /// <summary>Shrinks and removes break-apart debris.</summary>
    public class DebrisFader : MonoBehaviour
    {
        float life, t;
        public void Begin(float seconds) { life = seconds; }

        void Update()
        {
            t += Time.deltaTime;
            if (t > life)
            {
                float k = 1f - (t - life) / 0.5f;
                if (k <= 0f) { Destroy(gameObject); return; }
                foreach (Transform ch in transform) ch.localScale = Vector3.one * k;
            }
        }
    }
}
