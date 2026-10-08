using UnityEngine;

namespace Salvo
{
    /// <summary>Physics layer indices (configured by the project setup tool) and common masks.</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int World = 6;        // static level geometry
        public const int Hitbox = 7;       // character hit volumes (head/body/legs)
        public const int Player = 8;       // CharacterController capsules
        public const int Pickup = 9;       // trigger volumes
        public const int ViewModel = 10;   // first-person arms & weapon (never collides)
        public const int Debris = 11;      // ragdoll parts, shells
        public const int PlayerClip = 12;  // invisible walls only players collide with (map bounds)

        public static readonly int WorldMask = 1 << World | 1 << Default;
        public static readonly int ShotMask = 1 << World | 1 << Default | 1 << Hitbox;
        public static readonly int MoveMask = 1 << World | 1 << Default | 1 << Player;
    }
}
