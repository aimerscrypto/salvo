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
        Vector3 strideDir = Vector3.forward;
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
            phase += dt * hs * 1.75f;
            // legs stride along the actual travel direction (forward, back, sideways, diagonal)
            Vector3 travel = hs > 0.2f ? local / hs : Vector3.forward;
            strideDir = Vector3.Lerp(strideDir, travel, 1f - Mathf.Exp(-10f * dt));
            int step = Mathf.FloorToInt(phase / Mathf.PI);
            if (step != lastStep)
            {
                lastStep = step;
                if (!c.IsLocalHuman && !airborne && hs > 2.5f && !c.Motor.Crouching)
                {
                    var bank = AudioManager.Bank;
                    if (bank != null) AudioManager.PlayWorld(bank.Step(Surfaces.Under(transform.position)), transform.position, hs > GameRules.RunSpeed * 0.8f ? 0.7f : 0.5f, Random.Range(0.92f, 1.08f), 32f);
                }
            }
            float amp = Mathf.Lerp(0f, 38f, run) * (1f - airT);
            float sL = Mathf.Sin(phase), sR = Mathf.Sin(phase + Mathf.PI);
            float crouchThigh = 70f * crouchT, crouchShin = -95f * crouchT;
            Leg(thL, shL, sL * amp, crouchThigh + airT * 30f, Mathf.Min(0f, Mathf.Cos(phase)) * amp * 1.2f + crouchShin - airT * 45f);
            Leg(thR, shR, sR * amp, crouchThigh - airT * 5f, Mathf.Min(0f, Mathf.Cos(phase + Mathf.PI)) * amp * 1.2f + crouchShin - airT * 20f);
            // lean a little into sideways movement (local Y is the model's forward axis -> roll)
            float lean = -strideDir.x * run * 5f * (1f - airT);
            hips.localRotation = baseRot[hips];
            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.045f * run * (1f - airT);
            hips.localPosition = hipsBase + new Vector3(0, -0.36f * crouchT - bob, 0);

            // spine and head follow aim (in wrapper space, +X rotation pitches "forward" for the model)
            torso.localRotation = baseRot[torso] * Quaternion.Euler((-pitch * 0.45f - crouchT * 10f) * AimSign, lean, 0);
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

        /// <summary>stride swings the thigh along strideDir; bend is a forward lift (crouch/air).
        /// Node axes: local X pitches the leg (negative = knee forward), local Y swings it sideways
        /// (positive = knee to the right), local Z runs along the leg.</summary>
        void Leg(Transform thigh, Transform shin, float stride, float bend, float shinAngle)
        {
            // sideways steps are shorter than forward ones
            Vector3 w = new Vector3(-strideDir.z, strideDir.x * 0.7f, 0f);
            float m = w.magnitude;
            Quaternion swing = m > 1e-4f ? Quaternion.AngleAxis(stride * m, w / m) : Quaternion.identity;
            thigh.localRotation = baseRot[thigh] * Quaternion.Euler(bend * LegSign, 0, 0) * swing;
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
        /// <summary>The body keeps its last pose and topples away from the shot, then sinks out of sight.</summary>
        public void PlayDeath(Vector3 dir, bool headshot)
        {
            if (broken || model == null) return;
            broken = true;
#if DEVELOPMENT_BUILD
            Bootstrap.FrameBreaks++;
#endif
            foreach (var h in hitboxes) if (h != null) h.enabled = false;
            if (weaponPivot != null) weaponPivot.gameObject.SetActive(false);
            if (blobShadow != null) blobShadow.SetActive(false);

            var corpse = new GameObject("Corpse_" + c.Name).transform;
            corpse.SetPositionAndRotation(transform.position, transform.rotation);
            wrapper.SetParent(corpse, true);
            Vector3 fall = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (fall.sqrMagnitude < 1e-4f) fall = -transform.forward;
            fall = ClearFallDirection(fall.normalized);
            corpse.gameObject.AddComponent<Corpse>().Begin(fall.normalized, headshot, crouchT, shL, shR, torso, head, vel);
            wrapper = null; model = null;
        }

        /// <summary>Direction closest to the shot that has room for a body to lie down (no wall within 1.7 m).</summary>
        Vector3 ClearFallDirection(Vector3 preferred)
        {
            Vector3 chest = transform.position + Vector3.up * 0.9f;
            Vector3 best = preferred;
            float bestRoom = -1f;
            foreach (float yaw in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f })
            {
                Vector3 d = Quaternion.Euler(0, yaw, 0) * preferred;
                float room = Physics.SphereCast(chest, 0.15f, d, out var hit, 1.7f, Layers.WorldMask, QueryTriggerInteraction.Ignore) ? hit.distance : 1.7f;
                if (room >= 1.69f) return d;
                if (room > bestRoom) { bestRoom = room; best = d; }
            }
            return best;
        }

        void OnDestroy()
        {
            if (blobShadow != null) Destroy(blobShadow);
        }
    }

    /// <summary>
    /// Procedural death: the body pivots over its feet away from the shot (gravity ease-in, a small
    /// bounce on landing), knees buckle and the head lolls, then it rests and sinks into the ground.
    /// </summary>
    public class Corpse : MonoBehaviour
    {
        const float FallTime = 0.5f, Rest = 2.2f, SinkTime = 0.8f;
        Vector3 axis, slide;
        Quaternion startRot;
        Vector3 startPos;
        Transform shL, shR, torso, head;
        Quaternion shLBase, shRBase, torsoBase, headBase;
        float t, crouch, headKick;

        public void Begin(Vector3 fallDir, bool headshot, float crouchT, Transform shinL, Transform shinR, Transform torsoT, Transform headT, Vector3 velocity)
        {
            axis = Vector3.Cross(Vector3.up, fallDir).normalized;
            startRot = transform.rotation;
            startPos = transform.position;
            slide = Vector3.ProjectOnPlane(velocity, Vector3.up) * 0.25f + fallDir * (headshot ? 0.35f : 0.2f);
            crouch = crouchT;
            headKick = headshot ? 1f : 0.4f;
            shL = shinL; shR = shinR; torso = torsoT; head = headT;
            if (shL != null) shLBase = shL.localRotation;
            if (shR != null) shRBase = shR.localRotation;
            if (torso != null) torsoBase = torso.localRotation;
            if (head != null) headBase = head.localRotation;
        }

        void Update()
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / FallTime);
            float angle = 88f * f * f; // accelerate like a fall
            if (t > FallTime)
            {
                float k = t - FallTime;
                angle = 88f - Mathf.Sin(k * 16f) * Mathf.Exp(-k * 9f) * 7f; // thud and settle
            }
            // pivot over the feet; lift slightly as it lies down so the body rests on, not in, the floor
            Vector3 pos = startPos + slide * Mathf.SmoothStep(0, 1, f) + Vector3.up * (0.14f * Mathf.SmoothStep(0, 1, f));
            if (t > Rest) pos += Vector3.down * Mathf.SmoothStep(0f, 0.75f, (t - Rest) / SinkTime);
            transform.SetPositionAndRotation(pos, Quaternion.AngleAxis(angle, axis) * startRot);

            // knees buckle early, the upper body curls, the head lolls back
            float buckle = Mathf.Sin(Mathf.Clamp01(t / (FallTime * 0.8f)) * Mathf.PI) * (1f - crouch) * 45f;
            if (shL != null) shL.localRotation = shLBase * Quaternion.Euler(buckle, 0, 0);
            if (shR != null) shR.localRotation = shRBase * Quaternion.Euler(buckle * 0.7f, 0, 0);
            float curl = Mathf.SmoothStep(0, 1, f);
            if (torso != null) torso.localRotation = torsoBase * Quaternion.Euler(8f * curl, 0, 0);
            if (head != null) head.localRotation = headBase * Quaternion.Euler(-25f * headKick * curl, 0, 0);

            if (t > Rest + SinkTime) Destroy(gameObject);
        }
    }
}
