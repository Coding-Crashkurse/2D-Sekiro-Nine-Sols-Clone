namespace AshenSol.Boss
{
    public static class BossTuning
    {
        public const int MaxHp = 650;
        public const float Phase2Threshold = 0.55f;
        public const float Phase2TelegraphMul = 0.8f;
        public const float WalkSpeed = 3.5f;
        public const float StaggerSeconds = 1.4f;
        public const float StaggerDamageMul = 1.5f;
        public const int InternalStaggerThreshold = 110;
        public const float InternalStaggerCooldown = 8f;

        // TripleSlash
        public const float SlashTelegraph = 0.38f, SlashActive = 0.10f, SlashGap = 0.25f, SlashRecovery = 0.5f;
        public const int SlashDamage = 16; public const float SlashKnockback = 6f;
        // DashSlash
        public const float DashTelegraph = 0.5f, DashSpeed = 12f, DashMaxTime = 0.6f, DashStopDistance = 1.6f, DashSlashActive = 0.12f, DashRecovery = 0.7f;
        public const int DashDamage = 20; public const float DashKnockback = 8f;
        // Slam
        public const float SlamTelegraph = 0.7f, SlamLeapHeight = 4.5f, SlamLeapTime = 0.35f, SlamDropSpeed = 40f, SlamRecovery = 1.0f;
        public const int SlamDamage = 30; public const float SlamKnockback = 10f;
        public const float ShockwaveSpeed = 9f, ShockwaveLife = 1.6f; public const int ShockwaveDamage = 15;
        // Bolts
        public const float BoltsTelegraph = 0.45f, BoltSpeed = 9f, BoltsRecovery = 0.6f;
        public const int BoltDamage = 12; public const int ReflectedBoltDamage = 35;
        // Whirl
        public const float WhirlTelegraph = 0.5f, WhirlInterval = 0.28f, WhirlRadius = 3.4f, WhirlDrift = 2f, WhirlRecovery = 0.8f;
        public const int WhirlHits = 4, WhirlDamage = 12; public const float WhirlKnockback = 4f;
        // RedThrust
        public const float ThrustTelegraph = 0.6f, ThrustSpeed = 16f, ThrustDistance = 7f, ThrustRecovery = 1.0f;
        public const int ThrustDamage = 28; public const float ThrustKnockback = 9f;
    }
}
