using UnityEngine;
using UnityEngine.Rendering;

namespace Salvo
{
    /// <summary>
    /// First-person arms + weapon for the local player, fully procedural: sway, bob, ADS sight
    /// alignment, spring recoil, equip, magazine and per-shell reloads (support hand follows the
    /// mag), pump/bolt cycling, knife swings and a matte muzzle flash.
    /// Lives under GameCamera.ViewModelRoot (uniformly scaled so it never clips into walls).
    /// </summary>
    public class ViewModel : MonoBehaviour
    {
        class Rig
        {
            public WeaponDef def;
            public Transform root, pose, model, muzzle, sight, mag, action, armL, armR, gripL, eject;
            public Vector3 magBase, actionBase, armLBase;
            public Quaternion armLRot, actionRot;
            public Vector3 adsPos;
            public Renderer flash;
            public bool hasReticle;
        }

        readonly Rig[] rigs = new Rig[3];
        // Nodes inside an imported FBX share the root's frame: (x, y, z)node = (right, forward, down).
        static readonly Quaternion MarkerFix = Quaternion.identity;
        static Vector3 Node(float right, float up, float forward) => new Vector3(right, forward, -up);
        Combatant c;
        Rig cur;
        Spring3 kickPos, kickRot, swayRot, swayPos;
        float equipT = 1f, actionT = 1f, meleeT = 1f, dryT = 1f, shellBump, flashTime, landT;
        float bobPhase;
        int meleeSide = 1;
        bool built;

        public void Bind(Combatant combatant)
        {
            c = combatant;
            var w = c.Weapons;
            w.Fired += OnFired;
            w.Equipped += d => { equipT = 0f; StopReloadSound(); if (built) AudioManager.Play2D(d.equipSound, 0.55f, Random.Range(0.95f, 1.05f)); };
            w.ReloadShell += d => { shellBump = 1f; AudioManager.Play2D(d.reloadIn, 0.6f, Random.Range(0.95f, 1.05f)); };
            w.ActionCycled += _ => { actionT = 0f; PlayAction(); };
            w.Meleed += _ => { meleeT = 0f; meleeSide = -meleeSide; AudioManager.Play2D(AudioManager.Bank?.meleeSwing, 0.8f, Random.Range(0.95f, 1.1f)); };
            w.DryFired += d => { dryT = 0f; AudioManager.Play2D(AudioManager.Bank?.emptyClick, 0.7f); };
            // the whole reload is one recording laid out to the animation; stop it if the reload is cut short
            w.ReloadStarted += (d, dur) => { StopReloadSound(); reloadVoice = AudioManager.Play2DTracked(d.reloadSequence, 0.85f); reloadClip = d.reloadSequence; };
            w.ReloadFinished += d => reloadClip = null;
            c.Motor.Landed += s => landT = Mathf.Clamp01(s / 12f);
            Build();
        }

        AudioSource reloadVoice;
        AudioClip reloadClip;

        void StopReloadSound()
        {
            if (reloadVoice != null && reloadVoice.isPlaying && reloadVoice.clip == reloadClip && reloadClip != null) reloadVoice.Stop();
            reloadVoice = null; reloadClip = null;
        }

        void OnDestroy()
        {
            foreach (var r in rigs) if (r != null && r.root != null) Destroy(r.root.gameObject);
        }

        public void Build()
        {
            foreach (var r in rigs) if (r != null && r.root != null) Destroy(r.root.gameObject);
            var cam = GameCamera.I;
            if (cam == null) return;
            var cfg = GameConfig.I;
            for (int s = 0; s < 3; s++)
            {
                var st = c.Weapons.States[s];
                if (st == null || st.def == null || st.def.model == null) { rigs[s] = null; continue; }
                var rig = new Rig { def = st.def };
                rig.root = new GameObject("VM_" + st.def.displayName).transform;
                rig.root.SetParent(cam.ViewModelRoot, false);
                rig.pose = new GameObject("Pose").transform;
                rig.pose.SetParent(rig.root, false);
                var wrap = new GameObject("Wrap").transform;
                wrap.SetParent(rig.pose, false);
                rig.model = Instantiate(st.def.model, wrap).transform;
                CharacterVisual.ApplySkin(rig.model, st.def, st.skin);
                rig.muzzle = rig.model.FindDeep("Muzzle");
                rig.sight = rig.model.FindDeep("Sight");
                rig.eject = rig.model.FindDeep("Eject");
                rig.mag = rig.model.FindDeep("Mag");
                rig.action = rig.model.FindDeep("Pump") ?? rig.model.FindDeep("Bolt");
                if (rig.mag != null) rig.magBase = rig.mag.localPosition;
                if (rig.action != null) { rig.actionBase = rig.action.localPosition; rig.actionRot = rig.action.localRotation; }

                // arms: the FP_Arms model holds hands posed for each weapon's grips (Arm_R / Arm_R_<Weapon>,
                // Arm_L_<Weapon>), authored in their marker's frame -> parent with an identity transform
                var arms = Instantiate(cfg.fpArmsModel).transform;
                string key = st.def.model.name.StartsWith("W_") ? st.def.model.name.Substring(2) : st.def.model.name;
                var gripR = rig.model.FindDeep("GripR");
                rig.gripL = rig.model.FindDeep("GripL");
                rig.armR = arms.FindDeep("Arm_R_" + key) ?? arms.FindDeep("Arm_R");
                rig.armL = st.def.supportGrip == SupportGrip.None ? null : arms.FindDeep("Arm_L_" + key);
                if (rig.armR != null && gripR != null) { rig.armR.SetParent(gripR, false); rig.armR.localPosition = Vector3.zero; rig.armR.localRotation = MarkerFix; }
                // pistol support hands cup the shooting hand, so they share the GripR frame
                var supportMarker = st.def.supportGrip == SupportGrip.Pistol ? gripR : rig.gripL;
                if (rig.armL != null && supportMarker != null)
                {
                    rig.armL.SetParent(supportMarker, false);
                    rig.armL.localPosition = Vector3.zero;
                    rig.armL.localRotation = MarkerFix;
                    // re-home into pose space so reloads / pumps can move it independently
                    rig.armL.SetParent(rig.pose, true);
                    rig.armLBase = rig.armL.localPosition;
                    rig.armLRot = rig.armL.localRotation;
                }
                else rig.armL = null;
                Destroy(arms.gameObject);

                // team colored sleeves
                var teamMat = cfg.TeamMaterial(c.Team, c.ColorNV.Value);
                foreach (var r in rig.root.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == cfg.teamRed) mats[i] = teamMat;
                        else if (mats[i] == cfg.glassMaterial && cfg.sightGlass != null) mats[i] = cfg.sightGlass;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                // red-dot / holo reticle: a small unlit dot on the optic's glass, on the sight line
                var reticle = rig.model.FindDeep("Reticle");
                if (reticle != null)
                {
                    var rq = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(rq.GetComponent<Collider>());
                    rq.name = "ReticleDot";
                    rq.transform.SetParent(rig.pose, false);
                    rq.transform.position = reticle.position;
                    rq.transform.localRotation = Quaternion.identity;
                    rq.transform.localScale = Vector3.one * 0.0085f;
                    var rr = rq.GetComponent<MeshRenderer>();
                    rr.sharedMaterial = ReticleMaterial(cfg);
                    rr.shadowCastingMode = ShadowCastingMode.Off;
                    rr.receiveShadows = false;
                    rig.hasReticle = true;
                }

                // muzzle flash quad
                if (rig.muzzle != null && st.def.fireMode != FireMode.Melee)
                {
                    var fq = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(fq.GetComponent<Collider>());
                    fq.name = "Flash";
                    fq.transform.SetParent(rig.pose, false);
                    fq.transform.position = rig.muzzle.position;
                    fq.transform.localRotation = Quaternion.identity;
                    fq.transform.localScale = Vector3.one * (st.def.pellets > 1 ? 0.2f : 0.15f);
                    var mr = fq.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = cfg.fxFlash;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.enabled = false;
                    rig.flash = mr;
                }

                // ADS: put the sight on the camera axis at sightDistance
                rig.pose.localPosition = Vector3.zero;
                rig.pose.localRotation = Quaternion.identity;
                Vector3 sightLocal = rig.sight != null ? rig.root.InverseTransformPoint(rig.sight.position) : Vector3.zero;
                rig.adsPos = new Vector3(-sightLocal.x, -sightLocal.y, st.def.sightDistance - sightLocal.z) + st.def.adsOffset;

                rig.root.gameObject.SetLayerRecursive(Layers.ViewModel);
                rig.root.gameObject.SetActive(false);
                rigs[s] = rig;
            }
            cur = null;
            built = true;
        }

        void OnFired(WeaponDef d)
        {
            // recoil: the gun slams back into the shoulder and the muzzle flips up, then springs home.
            // Impulses are sized for a peak of `back` units / `flip` degrees (see KickPos/KickRot springs).
            float ads = c.Weapons.AimT;
            float kp = Mathf.Lerp(1f, 0.6f, ads), kr = Mathf.Lerp(1f, 0.4f, ads);
            float back = d.kickBack * 1.6f, flip = d.kickRotation * 1.1f;
            kickPos.Kick(new Vector3(Random.Range(-0.12f, 0.12f) * back, back * 0.18f, -back) * (KickPosGain * kp));
            kickRot.Kick(new Vector3(-flip, Random.Range(-0.25f, 0.25f) * flip, Random.Range(-0.4f, 0.4f) * flip) * (KickRotGain * kr));
            float shotPitch = Random.Range(0.97f, 1.03f);
            AudioManager.OwnGunshot(d, c.EyePosition, shotPitch);
            if (cur != null && cur.flash != null)
            {
                cur.flash.enabled = true;
                cur.flash.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                cur.flash.transform.localScale = Vector3.one * (d.pellets > 1 ? 0.24f : 0.16f) * Random.Range(0.8f, 1.2f);
                flashTime = 0.045f;
            }
            if (cur != null && cur.eject != null && d.fireMode != FireMode.Pump && d.fireMode != FireMode.Bolt)
            {
                FxManager.I?.EjectShell(cur.eject.position, GameCamera.I.transform.right, d.pellets > 1);
                AudioManager.Casing(c.EyePosition + GameCamera.I.transform.right * 0.6f, false, true);
            }
        }

        void PlayAction()
        {
            var d = c.Weapons.Current;
            if (d == null) return;
            AudioManager.Play2D(d.actionSound, 0.8f);
            if (cur != null && cur.eject != null)
            {
                FxManager.I?.EjectShell(cur.eject.position, GameCamera.I.transform.right, d.pellets > 1);
                AudioManager.Casing(c.EyePosition + GameCamera.I.transform.right * 0.6f, d.pellets > 1, true);
            }
        }

        void LateUpdate()
        {
            if (!built || c == null) return;
            var cam = GameCamera.I;
            if (cam == null) return;
            var w = c.Weapons;
            int slot = w.CurrentSlot;
            var rig = rigs[slot];
            if (rig != cur)
            {
                if (cur != null) cur.root.gameObject.SetActive(false);
                cur = rig;
                if (cur != null) cur.root.gameObject.SetActive(true);
            }
            if (cur == null) return;
            float dt = Time.deltaTime;
            var d = cur.def;
            var motor = c.Motor;
            float ads = Mathf.SmoothStep(0f, 1f, w.AimT);

            // --- springs
            kickPos.Step(Vector3.zero, KickPosHz, 0.5f, dt);
            kickRot.Step(Vector3.zero, KickRotHz, 0.45f, dt);
            // sway from look *rate*, not this frame's raw delta: per-frame deltas swing with frame
            // time and made the gun twitch whenever the frame rate wobbled
            Vector2 rawLook = SalvoInput.GameplayEnabled ? SalvoInput.Look.ReadValue<Vector2>() : Vector2.zero;
            lookRate = Vector2.Lerp(lookRate, rawLook / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-14f * dt));
            Vector2 look = lookRate / 60f; // in "pixels per 60 Hz frame", the scale the sway was tuned for
            float swayK = Mathf.Lerp(1f, 0.2f, ads);
            swayRot.Step(new Vector3(Mathf.Clamp(look.y * 0.08f, -4f, 4f), Mathf.Clamp(-look.x * 0.08f, -5f, 5f), Mathf.Clamp(-look.x * 0.06f, -4f, 4f)) * swayK, 3.5f, 0.6f, dt);
            Vector3 localVel = c.transform.InverseTransformDirection(motor.Velocity);
            swayPos.Step(new Vector3(-localVel.x * 0.0025f, -Mathf.Clamp(localVel.y, -12f, 12f) * 0.0025f, -localVel.z * 0.0012f) * swayK, 4f, 0.7f, dt);

            // --- bob
            float hs = motor.HorizontalSpeed;
            float run = motor.Grounded && !motor.Sliding ? Mathf.Clamp01(hs / GameRules.RunSpeed) : 0f;
            bobPhase += dt * Mathf.Lerp(5f, 10.5f, run);
            float bobK = run * Mathf.Lerp(1f, 0.12f, ads) * (Settings.Data.viewBob ? 1f : 0.4f);
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * 0.012f, -Mathf.Abs(Mathf.Cos(bobPhase)) * 0.012f, 0) * bobK;
            Vector3 bobRot = new Vector3(Mathf.Abs(Mathf.Cos(bobPhase)) * 1.2f, Mathf.Sin(bobPhase) * 1.4f, Mathf.Sin(bobPhase) * 1.2f) * bobK;

            // --- base pose
            Vector3 hipPos = d.hipPosition;
            Quaternion hipRot = Quaternion.Euler(d.hipEuler);
            Vector3 pos = Vector3.Lerp(hipPos, cur.adsPos, ads);
            Quaternion rot = Quaternion.Slerp(hipRot, Quaternion.identity, ads);
            if (motor.Sliding) { pos += new Vector3(-0.02f, -0.03f, -0.02f); rot *= Quaternion.Euler(4f, -6f, 14f); }
            if (motor.Crouching && !motor.Sliding) pos += new Vector3(-0.006f, 0.004f, 0) * (1f - ads);

            // --- equip
            equipT = Mathf.MoveTowards(equipT, 1f, dt / Mathf.Max(0.05f, d.equipTime));
            float e = 1f - Mathf.SmoothStep(0f, 1f, equipT);
            pos += new Vector3(0, -0.12f, -0.04f) * e;
            rot *= Quaternion.Euler(35f * e, -10f * e, 15f * e);

            // --- landing dip
            landT = Mathf.MoveTowards(landT, 0f, dt * 3f);
            pos += Vector3.down * Mathf.Sin(landT * Mathf.PI) * 0.025f;

            // --- reload
            Vector3 armLOffset = Vector3.zero;
            if (w.Reloading)
            {
                float p = w.ReloadProgress;
                if (d.reloadPerShell)
                {
                    float tilt = Mathf.SmoothStep(0, 1, Mathf.Clamp01(p * 6f)) * (1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((p - 0.92f) * 12f)));
                    rot *= Quaternion.Euler(-8f * tilt, 8f * tilt, 28f * tilt);
                    pos += new Vector3(0.0f, -0.02f, 0) * tilt;
                    armLOffset = new Vector3(0.02f, -0.06f, -0.05f) * tilt;
                }
                else
                {
                    // keyed in seconds to the reload recording: mag leaves at reloadOutAt, is seated at
                    // reloadInAt, charging handle / slide / bolt at reloadChargeAt
                    float t = p * d.reloadTime, end = d.reloadTime;
                    float tilt = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.22f)) * (1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - (end - 0.3f)) / 0.3f)));
                    rot *= Quaternion.Euler(10f * tilt, 12f * tilt, 30f * tilt);
                    pos += new Vector3(-0.01f, -0.03f, 0.01f) * tilt;
                    float travel = Mathf.Clamp((d.reloadInAt - d.reloadOutAt) * 0.4f, 0.12f, 0.3f);
                    float leave = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - d.reloadOutAt) / travel));
                    float seat = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - (d.reloadInAt - travel)) / travel));
                    float magOut = leave * (1f - seat);
                    if (cur.mag != null)
                    {
                        cur.mag.localPosition = cur.magBase + Node(0, -0.22f, -0.02f) * magOut;
                        cur.mag.gameObject.SetActive(magOut < 0.97f);
                    }
                    // seating slap and the charging handle / slide: a short jolt of the whole gun
                    float slap = Mathf.Sin(Mathf.Clamp01((t - d.reloadInAt + 0.03f) / 0.18f) * Mathf.PI);
                    float charge = d.reloadChargeAt > 0f ? Mathf.Sin(Mathf.Clamp01((t - d.reloadChargeAt + 0.08f) / 0.28f) * Mathf.PI) : 0f;
                    rot *= Quaternion.Euler(-3f * slap - 4f * charge, 0f, -7f * charge);
                    pos += new Vector3(0f, 0.006f * slap, -0.014f * charge);
                    if (d.fireMode == FireMode.Bolt && d.reloadChargeAt > 0f && t > d.reloadChargeAt - 0.05f && t < d.reloadChargeAt + 0.45f)
                        actionT = (t - d.reloadChargeAt + 0.05f) / 0.45f; // work the bolt with the sound
                    armLOffset = new Vector3(0.0f, -0.12f, -0.12f) * magOut + new Vector3(0, -0.02f, 0) * tilt
                               + new Vector3(0.02f, 0.03f, -0.1f) * charge;
                }
            }
            else
            {
                if (cur.mag != null) { cur.mag.localPosition = cur.magBase; if (!cur.mag.gameObject.activeSelf) cur.mag.gameObject.SetActive(true); }
            }
            shellBump = Mathf.MoveTowards(shellBump, 0f, dt * 5f);
            pos += new Vector3(0, 0.01f, 0.01f) * shellBump;

            // --- pump / bolt
            actionT = Mathf.MoveTowards(actionT, 1f, dt / 0.38f);
            float act = Mathf.Sin(Mathf.Clamp01(actionT) * Mathf.PI);
            if (cur.action != null)
            {
                if (d.fireMode == FireMode.Pump) cur.action.localPosition = cur.actionBase + Node(0, 0, -0.09f) * act;
                else if (d.fireMode == FireMode.Bolt)
                {
                    float lift = Mathf.Clamp01(act * 2f);
                    cur.action.localRotation = cur.actionRot * Quaternion.Euler(0, -70f * lift, 0);
                    cur.action.localPosition = cur.actionBase + Node(0, 0, -0.06f) * Mathf.Clamp01(act * 2f - 0.6f);
                }
            }
            if (d.fireMode == FireMode.Pump) { armLOffset += new Vector3(0, 0, -0.09f) * act; rot *= Quaternion.Euler(-3f * act, 0, 4f * act); }
            if (d.fireMode == FireMode.Bolt) rot *= Quaternion.Euler(-4f * act, 0, 8f * act);

            // --- melee swing
            meleeT = Mathf.MoveTowards(meleeT, 1f, dt / 0.42f);
            if (meleeT < 1f)
            {
                float m = meleeT;
                float wind = Mathf.SmoothStep(0, 1, Mathf.Clamp01(m / 0.25f));
                float slash = Mathf.SmoothStep(0, 1, Mathf.Clamp01((m - 0.2f) / 0.3f));
                float back = Mathf.SmoothStep(0, 1, Mathf.Clamp01((m - 0.6f) / 0.4f));
                float side = meleeSide;
                Vector3 mp = new Vector3(0.06f * side, 0.04f, -0.03f) * (wind - back) + new Vector3(-0.16f * side, -0.05f, 0.12f) * (slash - back);
                pos += mp;
                rot *= Quaternion.Euler(-20f * wind + 50f * slash * 0.5f, 40f * side * (wind - slash), -60f * side * (slash - back) * 0.6f);
            }

            // --- dry fire jiggle
            dryT = Mathf.MoveTowards(dryT, 1f, dt * 6f);
            rot *= Quaternion.Euler(Mathf.Sin(dryT * Mathf.PI) * 3f, 0, 0);

            // --- compose
            cur.pose.localPosition = pos + bob + kickPos.Value + swayPos.Value;
            cur.pose.localRotation = rot * Quaternion.Euler(bobRot + kickRot.Value + swayRot.Value);
            if (cur.armL != null) cur.armL.localPosition = cur.armLBase + armLOffset;

            if (flashTime > 0f) { flashTime -= dt; if (flashTime <= 0f && cur.flash != null) cur.flash.enabled = false; }
            if (cur.flash != null && cur.muzzle != null) cur.flash.transform.position = cur.muzzle.position;
        }

        // recoil springs: a velocity impulse v peaks at v * exp(-z/sqrt(1-z^2) * atan(sqrt(1-z^2)/z)) / w,
        // so the gains below turn "peak units / degrees" into impulses
        const float KickPosHz = 9f, KickRotHz = 7f;
        static readonly float KickPosGain = 2f * Mathf.PI * KickPosHz / 0.546f;
        static readonly float KickRotGain = 2f * Mathf.PI * KickRotHz / 0.573f;

        /// <summary>True when the held weapon's optic draws its own red dot (HUD dot fades while aiming).</summary>
        public bool HasReticle => cur != null && cur.hasReticle;

        static Material reticleMat;
        static Material ReticleMaterial(GameConfig cfg)
        {
            if (reticleMat != null) return reticleMat;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ReticleDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01((0.42f - d) / 0.12f);           // crisp dot
                    a = Mathf.Max(a, Mathf.Clamp01(1f - d) * 0.18f);        // faint bloom around it
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            reticleMat = new Material(cfg.sightGlass) { name = "M_ReticleDot", renderQueue = 3010 };
            reticleMat.SetTexture("_BaseMap", tex);
            reticleMat.SetColor("_BaseColor", new Color(1.6f, 0.12f, 0.08f, 1f));
            return reticleMat;
        }
        Vector2 lookRate;

        public Vector3 MuzzleWorld => cur != null && cur.muzzle != null ? cur.muzzle.position : (GameCamera.I != null ? GameCamera.I.transform.position : Vector3.zero);
    }
}
