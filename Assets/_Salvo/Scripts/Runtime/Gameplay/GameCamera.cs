using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// The single scene camera. First-person view for the local player (recoil, bob, landing dip,
    /// shake, ADS zoom), a death cam that frames your killer, and a menu mode driven externally.
    /// Hosts the view-model root so the weapon renders with the same camera (no camera stacking).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class GameCamera : MonoBehaviour
    {
        public static GameCamera I { get; private set; }
        public enum Mode { Menu, FirstPerson, DeathCam, Overview }

        public Camera Cam { get; private set; }
        public Transform ViewModelRoot { get; private set; }
        public Mode CurrentMode { get; private set; } = Mode.Menu;
        public Combatant Target { get; private set; }

        // aim punch that the shooter sees and fights against; recovers automatically
        public float RecoilPitch, RecoilYaw;
        float recoilRecovery = 9f;
        Spring3 kick;
        Spring1 land;
        float trauma;
        float bobPhase, bobAmount;
        float eyeHeight;
        float roll;
        float fovKick;
        public float ZoomT;          // 0..1 ADS blend (set by weapons)
        public float ZoomFov = 60f;  // horizontal ADS fov
        public bool HideViewModel;

        // menu / death
        Vector3 menuPos, deathPos;
        Quaternion menuRot;
        float menuFov = 50f;
        Combatant killer;
        float deathTime;

        public const float ViewModelScale = 0.32f;

        void Awake()
        {
            I = this;
            Cam = GetComponent<Camera>();
            Cam.nearClipPlane = 0.02f;
            Cam.farClipPlane = 600f;
            var vm = new GameObject("ViewModelRoot");
            ViewModelRoot = vm.transform;
            ViewModelRoot.SetParent(transform, false);
            ViewModelRoot.localScale = Vector3.one * ViewModelScale;
            menuPos = transform.position;
            menuRot = transform.rotation;
        }

        void OnDestroy() { if (I == this) I = null; }

        public static float HorizontalToVertical(float hfov, float aspect) =>
            2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;

        public void SetMenuPose(Vector3 pos, Quaternion rot, float vfov)
        {
            menuPos = pos; menuRot = rot; menuFov = vfov;
        }

        public void EnterMenu()
        {
            CurrentMode = Mode.Menu;
            Target = null;
        }

        public void EnterFirstPerson(Combatant c)
        {
            Target = c;
            CurrentMode = Mode.FirstPerson;
            RecoilPitch = RecoilYaw = 0f;
            kick = default;
            eyeHeight = c.Motor.EyeHeight;
        }

        public void EnterDeathCam(Combatant self, Combatant k)
        {
            CurrentMode = Mode.DeathCam;
            killer = k != self ? k : null;
            deathPos = self.transform.position + Vector3.up * 1.2f;
            deathTime = Time.time;
            trauma = 0f;
        }

        public void EnterOverview()
        {
            CurrentMode = Mode.Overview;
        }

        public void AddRecoil(float pitch, float yaw, float recovery)
        {
            RecoilPitch += pitch;
            RecoilYaw += yaw;
            recoilRecovery = recovery;
            kick.Kick(new Vector3(-pitch * 9f, yaw * 6f, Random.Range(-1f, 1f) * pitch * 4f));
        }

        public void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        public void LandImpact(float speed)
        {
            land.Velocity -= Mathf.Clamp(speed * 0.06f, 0.05f, 0.6f);
        }

        public Ray AimRay => new Ray(transform.position, transform.forward);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            switch (CurrentMode)
            {
                case Mode.FirstPerson: UpdateFirstPerson(dt); break;
                case Mode.DeathCam: UpdateDeathCam(dt); break;
                case Mode.Overview: UpdateOverview(dt); break;
                default: UpdateMenu(dt); break;
            }
        }

        void UpdateMenu(float dt)
        {
            transform.SetPositionAndRotation(SalvoMath.Damp(transform.position, menuPos, 3f, dt), SalvoMath.Damp(transform.rotation, menuRot, 3f, dt));
            Cam.fieldOfView = SalvoMath.Damp(Cam.fieldOfView, menuFov, 3f, dt);
            ViewModelRoot.gameObject.SetActive(false);
        }

        void UpdateOverview(float dt)
        {
            float t = Time.time * 0.04f;
            Vector3 p = new Vector3(Mathf.Sin(t) * 38f, 26f, Mathf.Cos(t) * 38f);
            transform.SetPositionAndRotation(SalvoMath.Damp(transform.position, p, 2f, dt),
                SalvoMath.Damp(transform.rotation, Quaternion.LookRotation(-p + Vector3.up * 8f), 2f, dt));
            Cam.fieldOfView = SalvoMath.Damp(Cam.fieldOfView, 55f, 2f, dt);
            ViewModelRoot.gameObject.SetActive(false);
        }

        void UpdateDeathCam(float dt)
        {
            ViewModelRoot.gameObject.SetActive(false);
            float t = Time.time - deathTime;
            Vector3 focus = killer != null && killer.Alive ? killer.CenterMass : deathPos;
            Vector3 toFocus = focus - deathPos;
            Vector3 back = toFocus.sqrMagnitude > 0.01f ? -toFocus.normalized : -transform.forward;
            Vector3 camPos = deathPos + Vector3.up * Mathf.Lerp(0.4f, 2.2f, Mathf.Clamp01(t)) + back * Mathf.Lerp(0.5f, 2.5f, Mathf.Clamp01(t));
            // don't go through walls
            if (Physics.Linecast(deathPos, camPos, out var hit, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                camPos = hit.point + hit.normal * 0.2f;
            transform.position = SalvoMath.Damp(transform.position, camPos, 4f, dt);
            transform.rotation = SalvoMath.Damp(transform.rotation, Quaternion.LookRotation(focus - transform.position), 5f, dt);
            float vf = HorizontalToVertical(Settings.Data.fov, Cam.aspect);
            Cam.fieldOfView = SalvoMath.Damp(Cam.fieldOfView, killer != null ? vf * 0.7f : vf, 3f, dt);
        }

        void UpdateFirstPerson(float dt)
        {
            if (Target == null || !Target.IsSpawned) { EnterOverview(); return; }
            var motor = Target.Motor;
            ViewModelRoot.gameObject.SetActive(!HideViewModel);

            // recoil recovery (exponential, rate set by weapon)
            RecoilPitch = SalvoMath.Damp(RecoilPitch, 0f, recoilRecovery, dt);
            RecoilYaw = SalvoMath.Damp(RecoilYaw, 0f, recoilRecovery, dt);
            kick.Step(Vector3.zero, 3.2f, 0.55f, dt);
            land.Step(0f, 2.6f, 0.45f, dt);

            // view bob
            float hs = motor.HorizontalSpeed;
            float bobTarget = motor.Grounded && !motor.Sliding ? Mathf.Clamp01(hs / GameRules.RunSpeed) : 0f;
            bobAmount = SalvoMath.Damp(bobAmount, bobTarget * (Settings.Data.viewBob ? 1f : 0.25f) * (1f - ZoomT * 0.85f), 10f, dt);
            bobPhase += dt * Mathf.Lerp(6f, 11f, Mathf.Clamp01(hs / GameRules.RunSpeed));
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * 0.022f, Mathf.Abs(Mathf.Cos(bobPhase)) * 0.032f, 0f) * bobAmount;

            // shake (perlin, trauma^2)
            trauma = Mathf.MoveTowards(trauma, 0f, dt * 1.8f);
            float sh = trauma * trauma;
            Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(Time.time * 25f, 1f) - 0.5f, Mathf.PerlinNoise(2f, Time.time * 25f) - 0.5f, Mathf.PerlinNoise(Time.time * 25f, 5f) - 0.5f) * (sh * 6f);

            eyeHeight = SalvoMath.Damp(eyeHeight, motor.EyeHeight, 18f, dt);
            Vector3 eye = Target.transform.position + Vector3.up * (eyeHeight + land.Value);
            float yaw = motor.Input.yaw, pitch = motor.Input.pitch;

            // strafe roll
            float strafe = Vector3.Dot(motor.Velocity, Target.transform.right);
            roll = SalvoMath.Damp(roll, -strafe * 0.35f + (motor.Sliding ? 4f : 0f), 8f, dt);

            Quaternion look = Quaternion.Euler(pitch - RecoilPitch, yaw + RecoilYaw, 0f);
            Quaternion fx = Quaternion.Euler(kick.Value.x + shakeRot.x, kick.Value.y + shakeRot.y, kick.Value.z + roll + shakeRot.z);
            transform.SetPositionAndRotation(eye + look * bob, look * fx);

            float baseV = HorizontalToVertical(Settings.Data.fov, Cam.aspect);
            float zoomV = HorizontalToVertical(ZoomFov, Cam.aspect);
            float kickTarget = motor.Sliding ? 6f : hs > GameRules.RunSpeed * 1.15f ? 3f : 0f;
            fovKick = SalvoMath.Damp(fovKick, kickTarget, 6f, dt);
            Cam.fieldOfView = Mathf.Lerp(baseV + fovKick, zoomV, Mathf.SmoothStep(0f, 1f, ZoomT));
        }

        /// <summary>Sensitivity multiplier while zoomed (keeps aim feel consistent across FOVs).</summary>
        public float ZoomSensitivity(float weaponAdsSens)
        {
            if (ZoomT <= 0.001f) return 1f;
            float ratio = Mathf.Tan(HorizontalToVertical(ZoomFov, Cam.aspect) * 0.5f * Mathf.Deg2Rad) /
                          Mathf.Tan(HorizontalToVertical(Settings.Data.fov, Cam.aspect) * 0.5f * Mathf.Deg2Rad);
            return Mathf.Lerp(1f, ratio * Settings.Data.adsSensitivity * weaponAdsSens / 0.8f, ZoomT);
        }
    }
}
