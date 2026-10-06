using UnityEngine;

namespace Salvo
{
    /// <summary>Reads device input for the local human and drives motor + weapons + owner net state.</summary>
    public class LocalPlayerInput : MonoBehaviour
    {
        Combatant c;
        bool crouchToggled, aimToggled;
        float pitchSendTimer;
        short lastSentPitch;

        public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        public void Bind(Combatant combatant)
        {
            c = combatant;
            enabled = true;
            GameCamera.I?.EnterFirstPerson(c);
        }

        void Update()
        {
            if (c == null || !c.IsSpawned) return;
            var motor = c.Motor;
            var weapons = c.Weapons;
            bool active = SalvoInput.GameplayEnabled && c.Alive && !c.Frozen && Application.isFocused;

            // ---- look
            if (active)
            {
                Vector2 look = SalvoInput.Look.ReadValue<Vector2>();
                float sens = Settings.Data.sensitivity * 0.055f;
                var def = weapons.Current;
                if (GameCamera.I != null && def != null) sens *= GameCamera.I.ZoomSensitivity(def.adsSensitivity);
                motor.Input.yaw = Mathf.Repeat(motor.Input.yaw + look.x * sens, 360f);
                float dy = look.y * sens * (Settings.Data.invertY ? 1f : -1f);
                motor.Input.pitch = Mathf.Clamp(motor.Input.pitch + dy, -88f, 88f);
            }

            // ---- move
            motor.Input.move = active ? Vector2.ClampMagnitude(SalvoInput.Move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            if (active && SalvoInput.Jump.WasPressedThisFrame()) motor.Input.jump = true;
            if (Settings.Data.toggleCrouch)
            {
                if (active && SalvoInput.Crouch.WasPressedThisFrame()) crouchToggled = !crouchToggled;
                if (motor.Input.jump) crouchToggled = false;
                motor.Input.crouch = active && crouchToggled;
            }
            else motor.Input.crouch = active && SalvoInput.Crouch.IsPressed();

            // ---- weapons
            var w = default(WeaponController.WeaponInput);
            if (active)
            {
                w.fireHeld = SalvoInput.Fire.IsPressed();
                w.firePressed = SalvoInput.Fire.WasPressedThisFrame();
                if (Settings.Data.toggleAim)
                {
                    if (SalvoInput.Aim.WasPressedThisFrame()) aimToggled = !aimToggled;
                    w.aimHeld = aimToggled;
                }
                else w.aimHeld = SalvoInput.Aim.IsPressed();
                w.reload = SalvoInput.Reload.WasPressedThisFrame();
                w.melee = SalvoInput.Melee.WasPressedThisFrame();
                w.lastWeapon = SalvoInput.LastWeapon.WasPressedThisFrame();
                w.selectSlot = SalvoInput.Slot1.WasPressedThisFrame() ? 0 : SalvoInput.Slot2.WasPressedThisFrame() ? 1 : SalvoInput.Slot3.WasPressedThisFrame() ? 2 : -1;
                float scroll = SalvoInput.NextWeapon.ReadValue<float>();
                w.scroll = scroll > 0.1f ? -1 : scroll < -0.1f ? 1 : 0;
            }
            else aimToggled = false;
            weapons.In = w;

            // ---- owner-replicated presentation state (throttled)
            pitchSendTimer -= Time.deltaTime;
            short p = (short)Mathf.RoundToInt(motor.Input.pitch * 10f);
            if (pitchSendTimer <= 0f && Mathf.Abs(p - lastSentPitch) > 15)
            {
                c.AimPitch.Value = p;
                lastSentPitch = p;
                pitchSendTimer = 0.05f;
            }
            if (c.CrouchNV.Value != motor.Crouching) c.CrouchNV.Value = motor.Crouching;
        }
    }
}
