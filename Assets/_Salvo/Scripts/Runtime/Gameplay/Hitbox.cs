using UnityEngine;

namespace Salvo
{
    /// <summary>Query-only collider on the Hitbox layer that maps a ray hit to a combatant + zone.</summary>
    public class Hitbox : MonoBehaviour
    {
        public Combatant Owner;
        public HitZone Zone;
    }
}
