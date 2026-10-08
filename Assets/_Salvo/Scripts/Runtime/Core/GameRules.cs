namespace Salvo
{
    /// <summary>Tunable gameplay constants shared by server and clients.</summary>
    public static class GameRules
    {
        public const string Version = "1.0.0";
        public const string NetVersion = "salvo-2";   // 2: Relay over secure WebSockets (web players)
        public const ushort DefaultPort = 7777;

        // health
        public const int MaxHealth = 100;
        public const float RegenDelay = 5f;
        public const float RegenPerSecond = 14f;
        public const float SpawnProtection = 2f;
        public const float RespawnDelay = 3.5f;
        public const float AssistWindow = 10f;
        public const float KillHeight = -25f;

        // movement (m/s, m/s^2)
        public const float RunSpeed = 7.2f;
        public const float CrouchSpeed = 3.4f;
        public const float GroundAccel = 65f;
        public const float AirAccel = 18f;
        public const float AirSpeedCap = 1.6f;
        public const float Friction = 9f;
        public const float Gravity = 21f;
        public const float JumpVelocity = 7.1f;
        public const float SlideBoost = 11.5f;
        public const float SlideDuration = 0.65f;
        public const float SlideCooldown = 0.9f;
        public const float StandHeight = 1.8f;
        public const float CrouchHeight = 1.2f;
        public const float StandEye = 1.62f;
        public const float CrouchEye = 1.05f;

        // match
        public const int FfaScoreLimit = 25;
        public const int TdmScoreLimit = 50;
        public const float MatchLength = 8 * 60f;
        public const float EndScreenTime = 14f;
        public const int MaxPlayers = 10;

        // scoring
        public const int ScoreKill = 100;
        public const int ScoreHeadshotBonus = 25;
        public const int ScoreAssist = 40;
    }
}
