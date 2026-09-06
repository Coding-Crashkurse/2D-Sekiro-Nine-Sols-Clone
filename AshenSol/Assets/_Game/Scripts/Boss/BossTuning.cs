namespace AshenSol.Boss
{
    public static class BossTuning
    {
        public const int MaxHp = 650;
        public const float Phase2Threshold = 0.55f;
        public const float Phase2TelegraphMul = 0.8f;
        public const float WalkSpeed = 3.5f;
        // Posture: the boss needs three perfect parries to break (its triple slash is exactly three).
        public const float MaxPosture = 300f;
        public const float PostureOnParry = 105f;
        public const float PostureRegen = 26f;
        public const int ExecuteDamage = 130;

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
        // SolarCollapse (phase two: he pulls a sun out of the horns and drops it)
        public const float SolarTelegraph = 2.1f, SolarRecovery = 2.2f, SolarCooldown = 17f;
        /// <summary>The blast front: slow enough to read and meet, wide enough to cross the arena.</summary>
        public const float SolarWaveSpeed = 17f, SolarWaveRadius = 30f;
        /// <summary>Fraction of the player's MAX health it takes. Parry it or lose three quarters.</summary>
        public const float SolarDamageFraction = 0.75f;

        // GoreCharge (phase two, the horns)
        public const float GoreTelegraph = 0.7f, GoreSpeed = 21f, GoreDuration = 1.1f, GoreRecovery = 1.2f;
        public const int GoreDamage = 32;

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
