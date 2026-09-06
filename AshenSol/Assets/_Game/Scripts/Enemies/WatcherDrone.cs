using System.Collections;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>Hovering sentry that fires parryable (reflectable) bolts.</summary>
    public class WatcherDrone : EnemyBase
    {
        protected override float CenterHeight { get { return 0f; } }
        protected override float PostureOnParry { get { return EnemyTuning.DronePostureOnParry; } }
        protected override float PostureRegen { get { return EnemyTuning.DronePostureRegen; } }
        public override int ExecuteDamage { get { return EnemyTuning.DroneExecuteDamage; } }
        protected override float KnockbackResist { get { return 1f; } }
        protected override string DeathSfx { get { return "drone_death"; } }
        public override string DisplayName { get { return "Watcher Drone"; } }
        public override int AshValue { get { return 16; } }

        Vector2 hoverTarget;
        float bobT;

        protected override void BuildBody()
        {
            Type = EnemyType.WatcherDrone;
            MaxHp = EnemyTuning.DroneHp;
            MaxPosture = EnemyTuning.DroneMaxPosture;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body = rb;
            var col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.32f;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            return EnemyRig.BuildDrone(transform, SortOrder.Enemy);
        }

        protected override void OnReset() { hoverTarget = SpawnPos; bobT = Random.value * 10f; }

        void FixedUpdate()
        {
            if (!IsAlive || Body == null) return;
            bobT += Time.fixedDeltaTime;
            Vector2 goal = hoverTarget + new Vector2(0f, Mathf.Sin(bobT * EnemyTuning.DroneBobHz * Mathf.PI * 2f) * EnemyTuning.DroneBobAmp);
            Vector2 p = Vector2.MoveTowards(Body.position, goal, EnemyTuning.DroneFollowSpeed * Time.fixedDeltaTime);
            Body.MovePosition(p);
        }

        protected override IEnumerator Behaviour()
        {
            yield return null;
            float fireTimer = Random.Range(0.6f, 1.4f);
            while (IsAlive)
            {
                if (!PlayerAlive) { hoverTarget = SpawnPos; yield return null; continue; }
                Vector2 d = Player.Center - Center;
                bool inRange = d.magnitude <= EnemyTuning.DroneAggro;
                aggro = inRange;
                Facing = d.x >= 0f ? 1 : -1;
                float tx = inRange ? Mathf.Clamp(Player.Center.x, SpawnPos.x - EnemyTuning.DroneLeash, SpawnPos.x + EnemyTuning.DroneLeash) : SpawnPos.x;
                hoverTarget = new Vector2(tx, SpawnPos.y);

                if (inRange)
                {
                    fireTimer -= Time.deltaTime;
                    if (fireTimer <= 0f)
                    {
                        yield return Shoot();
                        fireTimer = EnemyTuning.DroneFireInterval;
                    }
                }
                yield return null;
            }
        }

        IEnumerator Shoot()
        {
            BeginTelegraph(AttackKind.Parryable, EnemyTuning.DroneTelegraph);
            yield return Wait(EnemyTuning.DroneTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            if (!PlayerAlive) yield break;
            Vector2 dir = (Player.Center - Center).normalized;
            Facing = dir.x >= 0f ? 1 : -1;
            Rig.Punch(1.25f, 0.8f);
            Rig.Strike(0.1f);
            Services.Audio.PlaySfxAt("drone_shot", Center, 0.9f);
            Services.Vfx.FlashLight(Center, Palette.Amber, 2f, 2.5f, 0.12f);
            Projectile.Fire(Center + dir * 0.45f, dir * EnemyTuning.DroneBoltSpeed, Team.Enemy, EnemyTuning.DroneBoltDamage, AttackKind.Parryable, Palette.Amber, "drone_bolt");
        }
    }
}
