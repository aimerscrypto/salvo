using UnityEngine;
using UnityEngine.AI;

namespace Salvo
{
    /// <summary>
    /// Server-side bot. Drives the same PlayerMotor/WeaponController inputs a human would, so bots
    /// obey identical movement and weapon rules. Perception is throttled (LOS every ~0.15s), aim
    /// has reaction time, tracking lag and distance-scaled error so bots feel human, not robotic.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        public BotSkill Skill = BotSkill.Normal;
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        Combatant self, target;
        float thinkTimer, seenTime, lostTime, strafeTimer, strafeDir = 1f, jumpTimer, stuckTimer, repathTimer, fireHoldTimer, crouchTimer;
        Vector3 lastKnown, goal, lastPos;
        bool hasGoal, targetVisible;
        readonly NavMeshPath path = null;
        NavMeshPath navPath;
        Vector3[] corners = new Vector3[32];
        int cornerCount, cornerIndex;
        Vector2 aimError, aimErrorTarget;
        float recoilDebt;
        float yaw, pitch;

        float Reaction => Skill == BotSkill.Easy ? 0.55f : Skill == BotSkill.Normal ? 0.33f : 0.2f;
        float TurnSpeed => Skill == BotSkill.Easy ? 220f : Skill == BotSkill.Normal ? 380f : 560f;
        float ErrorDeg => Skill == BotSkill.Easy ? 5.5f : Skill == BotSkill.Normal ? 3.2f : 1.6f;
        float ViewDistance => Skill == BotSkill.Easy ? 40f : 60f;

        void Awake()
        {
            self = GetComponent<Combatant>();
            navPath = new NavMeshPath();
        }

        void OnEnable()
        {
            yaw = transform.eulerAngles.y;
        }

        public void OnRecoil(float pitchKick) => recoilDebt += pitchKick * (Skill == BotSkill.Hard ? 0.4f : 0.8f);

        void Update()
        {
            if (self == null || !self.IsSpawned || !self.IsServer) return;
            var motor = self.Motor;
            var weapons = self.Weapons;
            if (!self.Alive || self.Frozen)
            {
                motor.Input.move = Vector2.zero;
                target = null;
                hasGoal = false;
                return;
            }
            float dt = Time.deltaTime;

            thinkTimer -= dt;
            if (thinkTimer <= 0f) { thinkTimer = Random.Range(0.12f, 0.2f); Think(); }

            // ---- aim
            Vector3 aimPoint;
            bool engaging = target != null && targetVisible && Time.time - seenTime < 0.05f + thinkTimer + 0.2f;
            if (engaging)
            {
                // aim at chest, sometimes head; error shrinks the longer we track
                float track = Mathf.Clamp01((Time.time - (seenTime - trackStart)) / 1.2f);
                aimErrorTarget = Random.insideUnitCircle * ErrorDeg * Mathf.Lerp(1.4f, 0.55f, track);
                aimPoint = target.CenterMass + Vector3.up * (headAim ? 0.55f : 0.1f) + target.Motor.Velocity * 0.05f;
            }
            else if (hasGoal && cornerCount > 0)
                aimPoint = corners[Mathf.Min(cornerIndex, cornerCount - 1)] + Vector3.up * 1.5f;
            else
                aimPoint = transform.position + transform.forward * 5f + Vector3.up * 1.5f;

            aimError = Vector2.Lerp(aimError, aimErrorTarget, 1f - Mathf.Exp(-4f * dt));
            recoilDebt = Mathf.MoveTowards(recoilDebt, 0f, dt * 6f);
            Vector3 to = aimPoint - self.EyePosition;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + aimError.x;
            float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + aimError.y - recoilDebt;
            float turn = TurnSpeed * dt * (engaging ? 1f : 0.6f);
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, turn);
            pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(wantPitch, -80f, 80f), turn);
            motor.Input.yaw = yaw;
            motor.Input.pitch = pitch;
            AimDirection = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            short p10 = (short)(pitch * 10f);
            if (Mathf.Abs(self.AimPitch.Value - p10) > 20) self.AimPitch.Value = p10;

            // ---- weapons
            var w = default(WeaponController.WeaponInput);
            w.selectSlot = -1;
            var def = weapons.Current;
            var st = weapons.CurrentState;
            if (engaging && def != null)
            {
                float dist = Vector3.Distance(transform.position, target.transform.position);
                float angleOff = Vector3.Angle(AimDirection, (target.CenterMass - self.EyePosition));
                bool reacted = Time.time - firstSeen > Reaction;
                bool inRange = dist < def.falloffEnd * 1.4f || def.scoped;
                // switch to pistol when primary is dry and the fight is on
                if (st.mag == 0 && weapons.CurrentSlot == 0 && weapons.States[1] != null && weapons.States[1].mag > 0 && dist < 25f) w.selectSlot = 1;
                // knife at point blank
                if (dist < 1.8f && weapons.CurrentSlot != 2 && Random.value < 0.02f) w.melee = true;
                if (reacted && inRange && angleOff < 6f + 40f / Mathf.Max(dist, 1f))
                {
                    fireHoldTimer -= dt;
                    bool burst = def.fireMode != FireMode.Auto || fireHoldTimer > -0.25f;
                    w.fireHeld = burst;
                    w.firePressed = burst && (def.fireMode != FireMode.Auto) && Random.value < 0.35f;
                    if (fireHoldTimer < -0.35f) fireHoldTimer = Random.Range(0.3f, 0.9f);
                }
                w.aimHeld = def.scoped ? dist > 10f : dist > 18f && Skill != BotSkill.Easy;
            }
            else
            {
                if (st != null && st.mag < def.magSize * 0.5f && st.reserve > 0) w.reload = true;
                if (weapons.CurrentSlot != 0 && weapons.States[0] != null && weapons.States[0].mag + weapons.States[0].reserve > 0) w.selectSlot = 0;
            }
            weapons.In = w;

            // ---- movement
            Vector3 moveWorld = Vector3.zero;
            if (hasGoal && cornerCount > 0)
            {
                Vector3 c = corners[Mathf.Min(cornerIndex, cornerCount - 1)];
                Vector3 flat = c - transform.position; flat.y = 0;
                if (flat.magnitude < 0.8f && cornerIndex < cornerCount - 1) cornerIndex++;
                else if (flat.magnitude < 1f && cornerIndex >= cornerCount - 1) hasGoal = false;
                moveWorld = flat.normalized;
            }
            if (engaging)
            {
                float dist = Vector3.Distance(transform.position, target.transform.position);
                float ideal = def != null && def.pellets > 1 ? 5f : def != null && def.scoped ? 30f : def != null && def.fireMode == FireMode.Melee ? 1f : 15f;
                Vector3 toT = target.transform.position - transform.position; toT.y = 0; toT.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, toT) * strafeDir;
                strafeTimer -= dt;
                if (strafeTimer <= 0f) { strafeTimer = Random.Range(0.35f, 1.1f); strafeDir = Random.value < 0.5f ? -1f : 1f; }
                float approach = dist > ideal * 1.3f ? 1f : dist < ideal * 0.6f ? -0.7f : 0f;
                moveWorld = (side * (Skill == BotSkill.Easy ? 0.5f : 1f) + toT * approach + moveWorld * 0.3f);
                crouchTimer -= dt;
                if (crouchTimer <= 0f) crouchTimer = Random.Range(0.6f, 2.5f);
                motor.Input.crouch = Skill != BotSkill.Easy && crouchTimer < 0.3f;
                jumpTimer -= dt;
                if (jumpTimer <= 0f) { jumpTimer = Random.Range(1.5f, 4f); if (Skill == BotSkill.Hard || Random.value < 0.4f) motor.Input.jump = true; }
            }
            else motor.Input.crouch = false;

            Vector3 local = transform.InverseTransformDirection(moveWorld);
            motor.Input.move = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f);

            // ---- stuck detection
            stuckTimer += dt;
            if (stuckTimer > 1.2f)
            {
                if ((transform.position - lastPos).sqrMagnitude < 0.3f && moveWorld.sqrMagnitude > 0.1f)
                {
                    motor.Input.jump = true;
                    hasGoal = false;
                    strafeDir = -strafeDir;
                }
                lastPos = transform.position;
                stuckTimer = 0f;
            }
        }

        float firstSeen, trackStart;
        bool headAim;

        void Think()
        {
            // ---- perception
            Combatant best = null;
            float bestScore = float.MaxValue;
            Vector3 eye = self.EyePosition;
            foreach (var c in Combatant.All)
            {
                if (!c.Alive || !self.IsEnemyOf(c)) continue;
                Vector3 to = c.CenterMass - eye;
                float d = to.magnitude;
                if (d > ViewDistance) continue;
                float ang = Vector3.Angle(transform.forward, to);
                bool heard = c == target && Time.time - lostTime < 3f;
                if (ang > 75f && !heard && d > 6f) continue;
                if (Physics.Linecast(eye, c.CenterMass, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                float score = d + ang * 0.15f + (c == target ? -6f : 0f);
                if (score < bestScore) { bestScore = score; best = c; }
            }

            if (best != null)
            {
                if (best != target || !targetVisible) { firstSeen = Time.time; trackStart = 0f; headAim = Random.value < (Skill == BotSkill.Hard ? 0.45f : 0.2f); }
                target = best;
                targetVisible = true;
                seenTime = Time.time;
                trackStart += 0.15f;
                lastKnown = best.transform.position;
            }
            else
            {
                if (targetVisible) lostTime = Time.time;
                targetVisible = false;
                if (target != null && (!target.Alive || Time.time - lostTime > 6f)) target = null;
            }

            // ---- goals
            repathTimer -= 0.15f;
            if (!targetVisible && (repathTimer <= 0f || !hasGoal))
            {
                repathTimer = Random.Range(2f, 4f);
                Vector3 dest;
                if (self.Health.Value < 45 && Pickup.NearestAvailable(transform.position, Pickup.Kind.Health, out var hp)) dest = hp;
                else if (target != null) dest = lastKnown;
                else dest = PickRoamPoint();
                SetGoal(dest);
            }
        }

        Vector3 PickRoamPoint()
        {
            var map = MapInfo.I;
            if (map != null && map.roamPoints != null && map.roamPoints.Length > 0)
            {
                // prefer points near enemies so bots find fights
                Vector3 bias = Vector3.zero; int n = 0;
                foreach (var c in Combatant.All) if (c.Alive && self.IsEnemyOf(c)) { bias += c.transform.position; n++; }
                Transform best = null; float bestScore = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    var p = map.roamPoints[Random.Range(0, map.roamPoints.Length)];
                    if (p == null) continue;
                    float s = n > 0 ? Vector3.Distance(p.position, bias / n) : Random.value;
                    if (s < bestScore) { bestScore = s; best = p; }
                }
                if (best != null) return best.position;
            }
            return transform.position + Random.insideUnitSphere * 15f;
        }

        void SetGoal(Vector3 dest)
        {
            if (!NavMesh.SamplePosition(dest, out var hit, 4f, NavMesh.AllAreas)) { hasGoal = false; return; }
            if (!NavMesh.SamplePosition(transform.position, out var from, 3f, NavMesh.AllAreas)) { hasGoal = false; return; }
            if (NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, navPath) && navPath.status != NavMeshPathStatus.PathInvalid)
            {
                cornerCount = navPath.GetCornersNonAlloc(corners);
                cornerIndex = cornerCount > 1 ? 1 : 0;
                goal = hit.position;
                hasGoal = cornerCount > 0;
            }
            else hasGoal = false;
        }
    }
}
