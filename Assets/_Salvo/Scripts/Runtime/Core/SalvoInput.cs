using UnityEngine;
using UnityEngine.InputSystem;

namespace Salvo
{
    /// <summary>
    /// All gameplay input, defined in code (keyboard/mouse + gamepad). Binding overrides persist
    /// in PlayerPrefs so the Controls settings tab can rebind keys.
    /// </summary>
    public static class SalvoInput
    {
        public static InputActionMap Map { get; private set; }
        public static InputAction Move, Look, Fire, Aim, Jump, Crouch, Reload, Slot1, Slot2, Slot3, NextWeapon,
            LastWeapon, Melee, Scoreboard, Pause;

        const string OverridesKey = "salvo.bindings.v1";
        static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            Map = new InputActionMap("Gameplay");

            Move = Map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Look = Map.AddAction("Look", InputActionType.Value);
            Look.AddBinding("<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=12,y=12)");

            Fire = Map.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            Fire.AddBinding("<Gamepad>/rightTrigger");
            Aim = Map.AddAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            Aim.AddBinding("<Gamepad>/leftTrigger");
            Jump = Map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");
            Crouch = Map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            Crouch.AddBinding("<Keyboard>/c");
            Crouch.AddBinding("<Gamepad>/buttonEast");
            Reload = Map.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            Reload.AddBinding("<Gamepad>/buttonWest");
            Slot1 = Map.AddAction("Primary", InputActionType.Button, "<Keyboard>/1");
            Slot2 = Map.AddAction("Secondary", InputActionType.Button, "<Keyboard>/2");
            Slot3 = Map.AddAction("Knife", InputActionType.Button, "<Keyboard>/3");
            NextWeapon = Map.AddAction("NextWeapon", InputActionType.Value, "<Mouse>/scroll/y");
            LastWeapon = Map.AddAction("LastWeapon", InputActionType.Button, "<Keyboard>/q");
            LastWeapon.AddBinding("<Gamepad>/buttonNorth");
            Melee = Map.AddAction("Melee", InputActionType.Button, "<Keyboard>/v");
            Melee.AddBinding("<Gamepad>/rightStickPress");
            Scoreboard = Map.AddAction("Scoreboard", InputActionType.Button, "<Keyboard>/tab");
            Scoreboard.AddBinding("<Gamepad>/select");
            Pause = Map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");

            var saved = PlayerPrefs.GetString(OverridesKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                try { Map.LoadBindingOverridesFromJson(saved); } catch { /* ignore stale overrides */ }
            }
            Map.Enable();
        }

        public static void SaveOverrides()
        {
            PlayerPrefs.SetString(OverridesKey, Map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetOverrides()
        {
            Map.RemoveAllBindingOverrides();
            SaveOverrides();
        }

        /// <summary>Human readable name of the first keyboard/mouse binding.</summary>
        public static string BindingName(InputAction a)
        {
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (b.effectivePath.Contains("Keyboard") || b.effectivePath.Contains("Mouse"))
                    return a.GetBindingDisplayString(i);
            }
            return "-";
        }

        public static int KeyboardBindingIndex(InputAction a)
        {
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (b.effectivePath.Contains("Keyboard") || b.effectivePath.Contains("Mouse")) return i;
            }
            return -1;
        }

        /// <summary>True while gameplay input should be read (no menu has focus, cursor captured).</summary>
        public static bool GameplayEnabled = true;
    }
}
