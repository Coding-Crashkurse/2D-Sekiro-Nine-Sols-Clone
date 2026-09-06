namespace AshenSol.Enemies
{
    public static class EnemyTuning
    {
        // ---------------- posture (the yellow bar) ----------------
        // Posture fills from parries (a lot), late blocks (some) and plain hits (a little). When it
        // fills up the enemy's guard BREAKS: it is helpless for PostureBreakSeconds, takes double
        // damage, and can be finished with the Qi execution.
        public const float PostureRegenDelay = 1.6f;      // quiet seconds before posture starts draining
        public const float PostureBreakSeconds = 3.0f;    // the execution window
        public const float BrokenDamageMul = 2.0f;        // damage taken while broken
        public const float PostureFromBlock = 0.25f;      // fraction of max posture for a late block
        public const float PostureFromHit = 0.07f;        // fraction of max posture for a sword hit
        public const float PostureFromQiBlast = 0.45f;
        public const float PostureFromHeavy = 0.22f;      // the charged strike leans on the guard    // fraction of max posture for a Qi Blast

        // Grunt
        public const int GruntHp = 45;
        public const float GruntMaxPosture = 100f;
        public const float GruntPostureOnParry = 100f;    // one perfect parry breaks a grunt
        public const float GruntPostureRegen = 30f;
        public const int GruntExecuteDamage = 60;
        public const float GruntSpeed = 3.2f;
        public const float GruntAggroX = 7f, GruntAggroY = 3f, GruntLoseAggro = 12f;
        public const float GruntReach = 1.5f;
        public const float GruntTelegraph = 0.45f, GruntTelegraph2 = 0.30f, GruntActive = 0.10f, GruntRecovery = 0.6f;
        public const int GruntDamage = 14; public const float GruntKnockback = 5f;
        public const float GruntSecondSlashChance = 0.3f;

        // Spear sentinel
        public const int SpearHp = 70;
        public const float SpearMaxPosture = 100f;
        public const float SpearPostureOnParry = 100f;    // one perfect parry breaks a sentinel too
        public const float SpearPostureRegen = 28f;
        public const int SpearExecuteDamage = 85;
        public const float SpearSpeed = 2.6f;
        public const float SpearKeepDistance = 3.0f, SpearTooClose = 2.0f, SpearAttackRange = 3.4f;
        public const float SpearTelegraph = 0.5f, SpearActive = 0.12f, SpearRecovery = 0.7f;
        public const int SpearDamage = 18; public const float SpearKnockback = 6f;
        public const float LungeTelegraph = 0.65f, LungeSpeed = 14f, LungeDistance = 5f, LungeActive = 0.3f, LungeRecovery = 1.0f;
        public const int LungeDamage = 26; public const float LungeKnockback = 8f;

        // Foundry brute (the hammer). Slow, heavy, and its guard takes TWO perfect parries.
        public const int BruteHp = 150;
        public const float BruteMaxPosture = 100f;
        public const float BrutePostureOnParry = 50f;     // two perfect parries break a brute
        public const float BrutePostureRegen = 22f;
        public const float BrutePostureFromHitsMul = 0.4f;  // hits, heavies and blasts barely dent the guard: parry it open
        public const int BruteExecuteDamage = 95;
        public const float BruteSpeed = 1.9f;
        public const float BruteAggroX = 8f, BruteAggroY = 3f, BruteLoseAggro = 13f;
        public const float BruteReach = 2.4f;
        public const float SmashTelegraph = 0.95f, SmashActive = 0.16f, SmashRecovery = 1.1f;   // overhead, pierces a late block
        public const int SmashDamage = 30; public const float SmashKnockback = 9f;
        public const float SweepTelegraph = 0.8f, SweepActive = 0.2f, SweepRecovery = 0.9f;     // wide horizontal swing
        public const int SweepDamage = 24; public const float SweepKnockback = 8f;
        public const float QuakeTelegraph = 1.05f, QuakeActive = 0.14f, QuakeRecovery = 1.4f;   // RED ground slam, waves both ways
        public const int QuakeDamage = 32; public const float QuakeKnockback = 10f;
        public const float QuakeWaveSpeed = 9f, QuakeWaveLife = 1.0f; public const int QuakeWaveDamage = 16;

        // Drone
        public const int DroneHp = 30;
        public const float DroneMaxPosture = 100f;
        public const float DronePostureOnParry = 100f;    // a reflected bolt breaks it
        public const float DronePostureRegen = 40f;
        public const int DroneExecuteDamage = 45;
        public const float DroneLeash = 6f, DroneAggro = 9f, DroneFollowSpeed = 3f;
        public const float DroneBobAmp = 0.3f, DroneBobHz = 0.7f;
        public const float DroneFireInterval = 2.2f, DroneTelegraph = 0.4f;
        public const float DroneBoltSpeed = 9f; public const int DroneBoltDamage = 10;
        public const int ReflectedBoltDamage = 30;
    }
}
