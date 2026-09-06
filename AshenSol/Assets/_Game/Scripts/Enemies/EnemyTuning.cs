namespace AshenSol.Enemies
{
    public static class EnemyTuning
    {
        // Grunt
        public const int GruntHp = 45;
        public const float GruntSpeed = 3.2f;
        public const float GruntAggroX = 7f, GruntAggroY = 3f, GruntLoseAggro = 12f;
        public const float GruntReach = 1.5f;
        public const float GruntTelegraph = 0.45f, GruntTelegraph2 = 0.30f, GruntActive = 0.10f, GruntRecovery = 0.6f;
        public const int GruntDamage = 14; public const float GruntKnockback = 5f;
        public const float GruntStaggerOnParry = 0.55f;
        public const float GruntSecondSlashChance = 0.3f;

        // Spear sentinel
        public const int SpearHp = 70;
        public const float SpearSpeed = 2.6f;
        public const float SpearKeepDistance = 3.0f, SpearTooClose = 2.0f, SpearAttackRange = 3.4f;
        public const float SpearTelegraph = 0.5f, SpearActive = 0.12f, SpearRecovery = 0.7f;
        public const int SpearDamage = 18; public const float SpearKnockback = 6f;
        public const float LungeTelegraph = 0.65f, LungeSpeed = 14f, LungeDistance = 5f, LungeActive = 0.3f, LungeRecovery = 1.0f;
        public const int LungeDamage = 26; public const float LungeKnockback = 8f;
        public const float SpearStaggerOnParry = 0.5f;

        // Drone
        public const int DroneHp = 30;
        public const float DroneLeash = 6f, DroneAggro = 9f, DroneFollowSpeed = 3f;
        public const float DroneBobAmp = 0.3f, DroneBobHz = 0.7f;
        public const float DroneFireInterval = 2.2f, DroneTelegraph = 0.4f;
        public const float DroneBoltSpeed = 9f; public const int DroneBoltDamage = 10;
        public const int ReflectedBoltDamage = 30;
    }
}
