namespace Salvo
{
    public enum WeaponSlot : byte { Primary = 0, Secondary = 1, Melee = 2 }

    public enum FireMode : byte { Auto, Semi, Pump, Bolt, Melee }

    /// <summary>What the support (left) hand holds: a handguard/pump, a vertical foregrip, or the shooting hand.</summary>
    public enum SupportGrip : byte { Handguard = 0, Foregrip = 1, Pistol = 2, None = 3 }

    public enum HitZone : byte { Body = 0, Head = 1, Limb = 2 }

    public enum GameMode : byte { FreeForAll = 0, TeamDeathmatch = 1 }

    public enum Team : byte { None = 0, Red = 1, Blue = 2 }

    public enum MatchPhase : byte { Waiting = 0, Live = 1, Ended = 2 }

    public enum Rarity : byte { Common, Rare, Epic, Legendary }

    public enum BotSkill : byte { Easy = 0, Normal = 1, Hard = 2 }
}
