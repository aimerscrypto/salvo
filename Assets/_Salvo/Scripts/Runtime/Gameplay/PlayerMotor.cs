using System;
using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// Snappy arena movement on a CharacterController: quake-style ground accel/friction, air
    /// strafing with a soft speed cap, coyote time + jump buffering, crouch and slide, jump pads.
    /// Simulated only by the controlling peer (owner for humans, server for bots).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        public struct MotorInput
        {
            public Vector2 move;
            public bool jump;      // pressed (buffered)
            public bool crouch;    // held
            public bool slide;     // pressed (buffered)
            public float yaw, pitch;
        }

        public MotorInput Input;
        public Vector3 Velocity;
        public bool Grounded { get; private set; }
        public bool Crouching { get; private set; }
        public bool Sliding => slideTimer > 0f;
        public float EyeHeight { get; private set; } = GameRules.StandEye;
        public float SpeedMultiplier = 1f;
        public float HorizontalSpeed => new Vector3(Velocity.x, 0, Velocity.z).magnitude;

        public event Action Jumped;
        public event Action<float> Landed;     // impact speed
        public event Action Footstep;
        public event Action SlideStarted;
        public event Action JumpPadUsed;

        CharacterController cc;
        Combatant owner;
        float coyote, jumpBuffer, slideBuffer, slideTimer, slideCooldown, stepDistance, height = GameRules.StandHeight, airTime;
        bool jumpConsumed;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
        }

        public void OnSpawned(Combatant c)
        {
            owner = c;
            Input.yaw = transform.eulerAngles.y;
            Velocity = Vector3.zero;
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            cc.enabled = true;
            Velocity = Vector3.zero;
            Input.yaw = yaw;
            Input.pitch = 0f;
            slideTimer = 0;
            if (owner != null && owner.NetTransform != null && owner.NetTransform.CanCommitToTransform)
                owner.NetTransform.Teleport(pos, Quaternion.Euler(0, yaw, 0), Vector3.one);
            Physics.SyncTransforms();
        }

        void Update()
        {
            if (owner == null || !owner.HasControl) { UpdateRemote(); return; }
            if (!owner.Alive || owner.Frozen)
            {
                Velocity = Vector3.zero;
                return;
            }
            Simulate(Mathf.Min(Time.deltaTime, 0.05f));
        }

        void UpdateRemote()
        {
            if (owner == null) return;
            // keep remote capsules roughly in sync with crouch state for collisions/hit volumes
            Crouching = owner.CrouchNV.Value;
            float target = Crouching ? GameRules.CrouchHeight : GameRules.StandHeight;
            height = Mathf.MoveTowards(height, target, Time.deltaTime * 6f);
            ApplyHeight();
            EyeHeight = Mathf.MoveTowards(EyeHeight, Crouching ? GameRules.CrouchEye : GameRules.StandEye, Time.deltaTime * 5f);
        }

        void ApplyHeight()
        {
            cc.height = height;
            cc.center = new Vector3(0, height * 0.5f, 0);
        }

        void Simulate(float dt)
        {
            transform.rotation = Quaternion.Euler(0f, Input.yaw, 0f);
            bool wasGrounded = Grounded;
            float prevVy = Velocity.y;

            Grounded = cc.isGrounded || (Velocity.y <= 0.01f && Probe());
            if (Grounded) { coyote = 0.11f; airTime = 0f; } else { coyote -= dt; airTime += dt; }
            if (Input.jump) { jumpBuffer = 0.13f; Input.jump = false; } else jumpBuffer -= dt;
            if (Input.slide) { slideBuffer = 0.15f; Input.slide = false; } else slideBuffer -= dt;
            slideCooldown -= dt;

            // ---- crouch / slide
            Vector3 hv = new Vector3(Velocity.x, 0, Velocity.z);
            bool wantCrouch = Input.crouch;
            // slide has its own key: a tap runs the whole slide (crouch alone never slides)
            if (slideBuffer > 0f && slideTimer <= 0f && Grounded && slideCooldown <= 0f && hv.magnitude > GameRules.RunSpeed * 0.75f)
            {
                slideBuffer = 0f;
                slideTimer = GameRules.SlideDuration;
                slideCooldown = GameRules.SlideCooldown;
                Vector3 dir = hv.normalized;
                hv = dir * Mathf.Max(hv.magnitude, GameRules.SlideBoost);
                Velocity.x = hv.x; Velocity.z = hv.z;
                SlideStarted?.Invoke();
            }
            if (slideTimer > 0f) { slideTimer -= dt; if (!Grounded && airTime > 0.2f || hv.magnitude < GameRules.CrouchSpeed) slideTimer = Mathf.Min(slideTimer, 0f); }

            bool crouchNow = wantCrouch || slideTimer > 0f;
            if (!crouchNow && Crouching && CeilingBlocked()) crouchNow = true;
            Crouching = crouchNow;
            float targetH = Crouching ? GameRules.CrouchHeight : GameRules.StandHeight;
            height = Mathf.MoveTowards(height, targetH, dt * 7f);
            ApplyHeight();
            EyeHeight = Mathf.MoveTowards(EyeHeight, Crouching ? GameRules.CrouchEye : GameRules.StandEye, dt * 5.5f);

            // ---- wish direction
            Vector3 wish = transform.right * Input.move.x + transform.forward * Input.move.y;
            float wishMag = Mathf.Min(1f, wish.magnitude);
            if (wishMag > 0.001f) wish /= wish.magnitude;
            float maxSpeed = (Crouching && slideTimer <= 0f ? GameRules.CrouchSpeed : GameRules.RunSpeed) * SpeedMultiplier;

            if (Grounded)
            {
                if (slideTimer > 0f)
                {
                    ApplyFriction(GameRules.SlideFriction, dt);
                    Accelerate(wish, maxSpeed * wishMag, GameRules.GroundAccel * 0.15f, dt);
                }
                else
                {
                    ApplyFriction(GameRules.Friction, dt);
                    Accelerate(wish, maxSpeed * wishMag, GameRules.GroundAccel, dt);
                }
                Velocity.y = -2f;

                if (jumpBuffer > 0f && coyote > 0f)
                    DoJump();
            }
            else
            {
                if (jumpBuffer > 0f && coyote > 0f) DoJump();
                // quake air strafe: capped wish speed, strong accel -> controllable, rewarding
                Accelerate(wish, Mathf.Min(maxSpeed * wishMag, GameRules.AirSpeedCap), GameRules.AirAccel * maxSpeed, dt);
                Velocity.y -= GameRules.Gravity * dt;
                // soft cap so bunny hopping can't break the map
                hv = new Vector3(Velocity.x, 0, Velocity.z);
                float cap = 13.5f;
                if (hv.magnitude > cap) { hv = hv.normalized * Mathf.MoveTowards(hv.magnitude, cap, dt * 6f); Velocity.x = hv.x; Velocity.z = hv.z; }
            }

            // ---- jump pads
            var pads = JumpPad.All;
            for (int i = 0; i < pads.Count; i++)
            {
                if (pads[i].Inside(transform.position) && Velocity.y < pads[i].LaunchWorld.y * 0.5f)
                {
                    Velocity = pads[i].LaunchWorld;
                    Grounded = false;
                    coyote = 0f;
                    slideTimer = 0f;
                    JumpPadUsed?.Invoke();
                    pads[i].PlayEffect();
                    break;
                }
            }

            var flags = cc.Move(Velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && Velocity.y > 0f) Velocity.y = 0f;
            if ((flags & CollisionFlags.Sides) != 0)
            {
                // lose speed into walls (prevents sticky wall-sliding at full speed)
                Vector3 real = cc.velocity;
                Velocity.x = real.x; Velocity.z = real.z;
            }

            if (!wasGrounded && Grounded && airTime >= 0f && prevVy < -3f) Landed?.Invoke(-prevVy);

            // ---- footsteps
            float hs = HorizontalSpeed;
            if (Grounded && hs > 2.2f && slideTimer <= 0f)
            {
                stepDistance += hs * dt;
                float stride = Crouching ? 1.6f : 2.3f;
                if (stepDistance > stride) { stepDistance = 0f; Footstep?.Invoke(); }
            }
        }

        void DoJump()
        {
            Velocity.y = GameRules.JumpVelocity;
            if (slideTimer > 0f) slideTimer = 0f; // slide-jump keeps momentum
            Grounded = false;
            coyote = 0f;
            jumpBuffer = 0f;
            Jumped?.Invoke();
        }

        void ApplyFriction(float friction, float dt)
        {
            Vector3 hv = new Vector3(Velocity.x, 0, Velocity.z);
            float speed = hv.magnitude;
            if (speed < 0.01f) { Velocity.x = Velocity.z = 0f; return; }
            float control = Mathf.Max(speed, 2.5f);
            float drop = control * friction * dt;
            float ns = Mathf.Max(0f, speed - drop) / speed;
            Velocity.x *= ns; Velocity.z *= ns;
        }

        void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            if (wishSpeed <= 0f) return;
            float current = Vector3.Dot(new Vector3(Velocity.x, 0, Velocity.z), wishDir);
            float add = wishSpeed - current;
            if (add <= 0f) return;
            float a = Mathf.Min(accel * dt * Mathf.Max(wishSpeed, 1f), add);
            Velocity.x += wishDir.x * a;
            Velocity.z += wishDir.z * a;
        }

        bool Probe()
        {
            Vector3 origin = transform.position + Vector3.up * (cc.radius + 0.05f);
            return Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.down, out _, 0.15f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
        }

        bool CeilingBlocked()
        {
            Vector3 origin = transform.position + Vector3.up * (height - cc.radius);
            return Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.up, out _, GameRules.StandHeight - height + 0.05f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
        }
    }
}
