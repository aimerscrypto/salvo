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
            w.Equipped += _ => { equipT = 0f; };
            w.ReloadShell += _ => { shellBump = 1f; };
            w.ActionCycled += _ => { actionT = 0f; PlayAction(); };
            w.Meleed += _ => { meleeT = 0f; meleeSide = -meleeSide; AudioManager.Play2D(AudioManager.Bank?.meleeSwing, 0.8f, Random.Range(0.95f, 1.1f)); };
            w.DryFired += d => { dryT = 0f; AudioManager.Play2D(AudioManager.Bank?.emptyClick, 0.7f); };
            w.ReloadStarted += (d, dur) => AudioManager.Play2D(d.reloadOut, 0.75f);
            w.ReloadFinished += d => { if (!d.reloadPerShell) AudioManager.Play2D(d.reloadIn, 0.8f); };
            c.Motor.Landed += s => landT = Mathf.Clamp01(s / 12f);
            Build();
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

                // arms: take Arm_R / Arm_L from the FP_Arms model and pin them to the grip markers
                var arms = Instantiate(cfg.fpArmsModel).transform;
                var gripR = rig.model.FindDeep("GripR");
                rig.gripL = rig.model.FindDeep("GripL");
                rig.armR = arms.FindDeep("Arm_R");
                rig.armL = arms.FindDeep("Arm_L");
                if (rig.armR != null && gripR != null) { rig.armR.SetParent(gripR, false); rig.armR.localPosition = Vector3.zero; rig.armR.localRotation = MarkerFix; }
                if (rig.armL != null)
                {
                    bool twoHanded = st.def.slot == WeaponSlot.Primary;
                    if (twoHanded && rig.gripL != null)
                    {
                        rig.armL.SetParent(rig.gripL, false);
                        rig.armL.localPosition = Vector3.zero;
                        rig.armL.localRotation = MarkerFix;
                        // re-home into pose space so the reload can move it independently
                        rig.armL.SetParent(rig.pose, true);
                        rig.armLBase = rig.armL.localPosition;
                        rig.armLRot = rig.armL.localRotation;
                    }
                    else if (st.def.fireMode == FireMode.Melee)
                    {
                        Destroy(rig.armL.gameObject); rig.armL = null;
                    }
                    else if (gripR != null)
                    {
                        // pistol: support hand cups the shooting hand
                        rig.armL.SetParent(gripR, false);
                        rig.armL.localPosition = MarkerFix * new Vector3(-0.035f, -0.03f, 0.02f);
                        rig.armL.localRotation = MarkerFix;
                        rig.armL.SetParent(rig.pose, true);
                        rig.armLBase = rig.armL.localPosition;
                        rig.armLRot = rig.armL.localRotation;
                    }
                }
                Destroy(arms.gameObject);

                // team colored sleeves
                var teamMat = cfg.TeamMaterial(c.Team, c.ColorNV.Value);
                foreach (var r in rig.root.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == cfg.teamRed) mats[i] = teamMat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
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
            float ads = c.Weapons.AimT;
            float k = Mathf.Lerp(1f, 0.45f, ads);
            kickPos.Kick(new Vector3(Random.Range(-0.15f, 0.15f) * d.kickBack, d.kickBack * 0.4f, -d.kickBack * 10f) * k);
            kickRot.Kick(new Vector3(-d.kickRotation * 12f, Random.Range(-1f, 1f) * d.kickRotation * 3f, Random.Range(-1f, 1f) * d.kickRotation * 4f) * k);
            AudioManager.Play2D(d.fireSound, 0.9f, Random.Range(0.96f, 1.04f));
            if (cur != null && cur.flash != null)
            {
                cur.flash.enabled = true;
                cur.flash.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                cur.flash.transform.localScale = Vector3.one * (d.pellets > 1 ? 0.24f : 0.16f) * Random.Range(0.8f, 1.2f);
                flashTime = 0.045f;
            }
            if (cur != null && cur.eject != null && d.fireMode != FireMode.Pump && d.fireMode != FireMode.Bolt)
                FxManager.I?.EjectShell(cur.eject.position, GameCamera.I.transform.right, d.pellets > 1);
        }

        void PlayAction()
        {
            var d = c.Weapons.Current;
            if (d == null) return;
            AudioManager.Play2D(d.actionSound, 0.8f);
            if (cur != null && cur.eject != null) FxManager.I?.EjectShell(cur.eject.position, GameCamera.I.transform.right, d.pellets > 1);
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
            kickPos.Step(Vector3.zero, 6f, 0.6f, dt);
            kickRot.Step(Vector3.zero, 5f, 0.5f, dt);
            Vector2 look = SalvoInput.GameplayEnabled ? SalvoInput.Look.ReadValue<Vector2>() : Vector2.zero;
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
                    float tilt = Mathf.SmoothStep(0, 1, Mathf.Clamp01(p / 0.15f)) * (1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((p - 0.82f) / 0.18f)));
                    rot *= Quaternion.Euler(10f * tilt, 12f * tilt, 30f * tilt);
                    pos += new Vector3(-0.01f, -0.03f, 0.01f) * tilt;
                    // mag: out (0.15-0.4) away, back in (0.5-0.75)
                    float magOut = Mathf.SmoothStep(0, 1, Mathf.Clamp01((p - 0.15f) / 0.2f)) * (1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((p - 0.5f) / 0.22f)));
                    if (cur.mag != null)
                    {
                        cur.mag.localPosition = cur.magBase + Node(0, -0.22f, -0.02f) * magOut;
                        cur.mag.gameObject.SetActive(!(p > 0.33f && p < 0.5f));
                    }
                    armLOffset = new Vector3(0.0f, -0.12f, -0.12f) * magOut + new Vector3(0, -0.02f, 0) * tilt;
                    if (p > 0.72f && p < 0.76f && !reloadClick) { reloadClick = true; AudioManager.Play2D(d.reloadIn, 0.7f, 1.05f); }
                }
            }
            else
            {
                reloadClick = false;
                if (cur.mag != null) { cur.mag.localPosition = cur.magBase; if (!cur.mag.gameObject.activeSelf) cur.mag.gameObject.SetActive(true); }
            }
            shellBump = Mathf.MoveTowards(shellBump, 0f, dt * 5f);
            pos += new Vector3(0, 0.01f, 0.01f) * shellBump;
            if (shellBump > 0.95f) AudioManager.Play2D(d.reloadIn, 0.6f, Random.Range(0.95f, 1.05f));

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
            cur.pose.localPosition = pos + bob + kickPos.Value * 0.01f + swayPos.Value;
            cur.pose.localRotation = rot * Quaternion.Euler(bobRot + kickRot.Value * 0.1f + swayRot.Value);
            if (cur.armL != null) cur.armL.localPosition = cur.armLBase + armLOffset;

            if (flashTime > 0f) { flashTime -= dt; if (flashTime <= 0f && cur.flash != null) cur.flash.enabled = false; }
            if (cur.flash != null && cur.muzzle != null) cur.flash.transform.position = cur.muzzle.position;
        }

        bool reloadClick;

        public Vector3 MuzzleWorld => cur != null && cur.muzzle != null ? cur.muzzle.position : (GameCamera.I != null ? GameCamera.I.transform.position : Vector3.zero);
    }
}
