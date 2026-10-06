using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Salvo
{
    public enum Surface : byte { Sand = 0, Stone = 1, Wood = 2, Metal = 3 }

    /// <summary>
    /// What a piece of world geometry is made of, for footsteps and bullet impacts. Map props are
    /// separate colliders named after what they are (Cont…, Crate…, Pier…), so the name decides;
    /// the open ground follows the map (desert sand on Outpost, paving on Harbor).
    /// </summary>
    public static class Surfaces
    {
        // first match wins: specific props before the area prefixes they share (ContCrate, ContRamp...)
        static readonly (string key, Surface s)[] Rules =
        {
            ("Bags", Surface.Sand),
            ("Crate", Surface.Wood), ("Ramp", Surface.Wood), ("Pier", Surface.Wood), ("Boat", Surface.Wood),
            ("Stall", Surface.Wood), ("Cart", Surface.Wood), ("WaterTower", Surface.Wood),
            ("Barrel", Surface.Metal), ("Cont", Surface.Metal), ("Catwalk", Surface.Metal), ("Lamp", Surface.Metal),
            ("Traps", Surface.Metal), ("HallRoof", Surface.Metal),
        };

        static readonly Dictionary<Collider, Surface> cache = new Dictionary<Collider, Surface>();
        static string cachedScene;

        public static Surface Ground => SceneManager.GetActiveScene().name == "Harbor" ? Surface.Stone : Surface.Sand;

        public static Surface Of(Collider c)
        {
            if (c == null) return Ground;
            string scene = SceneManager.GetActiveScene().name;
            if (scene != cachedScene) { cache.Clear(); cachedScene = scene; }
            if (cache.TryGetValue(c, out var s)) return s;
            s = Classify(c.name);
            cache[c] = s;
            return s;
        }

        static Surface Classify(string name)
        {
            foreach (var (key, s) in Rules)
                if (name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) return s;
            if (name.StartsWith("Ground") || name.StartsWith("NC_Ground")) return Ground;
            return Surface.Stone; // houses, walls, roofs, plazas, stairs, quays
        }

        /// <summary>Surface under a standing point (feet).</summary>
        public static Surface Under(Vector3 feet)
        {
            return Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out var hit, 1.2f, Layers.WorldMask, QueryTriggerInteraction.Ignore)
                ? Of(hit.collider) : Ground;
        }

        /// <summary>Surface at a bullet hit point.</summary>
        public static Surface At(Vector3 point, Vector3 normal)
        {
            return Physics.Raycast(point + normal * 0.05f, -normal, out var hit, 0.2f, Layers.WorldMask, QueryTriggerInteraction.Ignore)
                ? Of(hit.collider) : Surface.Stone;
        }
    }
}
