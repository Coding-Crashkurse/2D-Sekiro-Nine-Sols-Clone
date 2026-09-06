namespace AshenSol.Player
{
    /// <summary>All player feel numbers in one place (see SPEC §3).</summary>
    public static class PlayerTuning
    {
        public const int MaxHp = 100;
        public const int MaxQi = 3;

        public const float MaxSpeed = 8.5f;
        public const float GroundAccel = 60f;
        public const float AirAccel = 35f;
        public const float Decel = 70f;

        public const float JumpVelocity = 13.5f;
        public const float JumpCutMultiplier = 0.45f;
        public const float CoyoteTime = 0.10f;
        public const float JumpBuffer = 0.12f;
        public const float ApexGravityScale = 0.75f;
        public const float ApexThreshold = 2.5f;
        public const float MaxFallSpeed = 22f;
        public const float DropThroughTime = 0.3f;

        public const float DashSpeed = 26f;
        public const float DashDuration = 0.16f;
        public const float DashInvulnExtra = 0.05f;
        public const float DashCooldown = 0.45f;
        public const float AfterimageInterval = 0.03f;

        public const float HurtInvuln = 0.8f;
        public const float HurtStun = 0.2f;
        public const float MinKnockback = 5f;
        public const float SpawnInvuln = 0.6f;

        public const float ParryPerfectWindow = 0.18f;
        public const float ParryBlockWindow = 0.22f;
        public const float ParryRecovery = 0.12f;
        public const float BlockChipFraction = 0.3f;
        public const float BlockKnockback = 4f;

        public const float ComboLinkWindow = 0.35f;
        public const float AttackLunge = 1.6f;

        public const float QiBlastDuration = 0.35f;
        public const float QiBlastRadius = 3.2f;
        public const int QiBlastDamage = 12;
        public const float QiBlastKnockback = 6f;

        public const float HealChannel = 0.55f;
        public const int HealAmount = 35;

        public const float FootstepInterval = 0.28f;
        public const float LandSoundFallSpeed = 6f;
        public const float SafeRecordInterval = 0.2f;
    }
}
