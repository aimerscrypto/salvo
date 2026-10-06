using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    /// <summary>Owner pushes loadout edits made mid-match; they take effect on the next respawn.</summary>
    public class LoadoutSync : NetworkBehaviour
    {
        Combatant c;

        void Awake() { c = GetComponent<Combatant>(); }

        public void Push()
        {
            if (!IsOwner) return;
            var d = Profile.Data;
            SetLoadoutRpc((byte)d.primary, (byte)d.secondary, (byte)Profile.EquippedSkin(d.primary), (byte)Profile.EquippedSkin(d.secondary));
            Hud.I?.Toast("LOADOUT APPLIES ON RESPAWN");
        }

        [Rpc(SendTo.Server)]
        void SetLoadoutRpc(byte primary, byte secondary, byte pSkin, byte sSkin)
        {
            var cfg = GameConfig.I;
            var p = cfg.Weapon(primary); var s = cfg.Weapon(secondary);
            if (p == null || p.slot != WeaponSlot.Primary || s == null || s.slot != WeaponSlot.Secondary) return;
            c.Loadout.Value = new LoadoutData { primary = primary, secondary = secondary, primarySkin = pSkin, secondarySkin = sSkin };
        }
    }
}
