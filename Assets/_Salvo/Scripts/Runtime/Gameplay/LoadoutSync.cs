using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// Owner pushes loadout edits made mid-match. From the picker shown on joining (before firing a
    /// shot) they swap the guns at once; otherwise they take effect on the next respawn.
    /// </summary>
    public class LoadoutSync : NetworkBehaviour
    {
        Combatant c;

        void Awake() { c = GetComponent<Combatant>(); }

        /// <summary>swapNow: the player is still on the join picker and hasn't fired since spawning.</summary>
        public void Push(bool swapNow = false)
        {
            if (!IsOwner) return;
            var d = Profile.Data;
            swapNow &= c.Alive;
            SetLoadoutRpc((byte)d.primary, (byte)d.secondary, (byte)Profile.EquippedSkin(d.primary), (byte)Profile.EquippedSkin(d.secondary), swapNow);
            if (!swapNow) Hud.I?.Toast(c.Alive ? "LOADOUT APPLIES ON RESPAWN" : "LOADOUT READY FOR RESPAWN");
        }

        const double SwapWindow = 90.0; // seconds after a spawn the join picker may still swap guns

        [Rpc(SendTo.Server)]
        void SetLoadoutRpc(byte primary, byte secondary, byte pSkin, byte sSkin, bool swapNow)
        {
            var cfg = GameConfig.I;
            var p = cfg.Weapon(primary); var s = cfg.Weapon(secondary);
            if (p == null || p.slot != WeaponSlot.Primary || s == null || s.slot != WeaponSlot.Secondary) return;
            var l = new LoadoutData { primary = primary, secondary = secondary, primarySkin = pSkin, secondarySkin = sSkin };
            bool changed = !l.Equals(c.Loadout.Value);
            c.Loadout.Value = l;
            if (swapNow && changed && c.Alive && NetworkManager.ServerTime.Time - c.SpawnTime.Value < SwapWindow) c.SwapLoadoutRpc(l);
            Deploy();
        }

        /// <summary>Owner opened / closed the join picker.</summary>
        [Rpc(SendTo.Server)]
        public void SetPickingRpc(bool picking)
        {
            if (picking) { if (c.Alive) c.Picking.Value = true; }
            else Deploy();
        }

        void Deploy()
        {
            if (!c.Picking.Value) return;
            c.Picking.Value = false;
            c.SpawnTime.Value = NetworkManager.ServerTime.Time;   // fresh spawn shield once you're in
        }
    }
}
