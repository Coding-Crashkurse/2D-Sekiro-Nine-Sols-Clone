using System.Collections;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>Husk grunt: patrols, approaches, telegraphed horizontal slash (sometimes two).</summary>
    public class Grunt : EnemyBase
    {
        protected override float CenterHeight { get { return 0.8f; } }
        protected override float StaggerOnParry { get { return EnemyTuning.GruntStaggerOnParry; } }
        public override string DisplayName { get { return "Husk Grunt"; } }

        int patrolDir = 1;

        protected override void BuildBody()
        {
            Type = EnemyType.Grunt;
            MaxHp = EnemyTuning.GruntHp;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.mass = 2f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sharedMaterial = new PhysicsMaterial2D("EnemyMat") { friction = 0f, bounciness = 0f };
            Body = rb;
            var col = gameObject.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.7f, 1.6f);
            col.offset = new Vector2(0f, 0.8f);
            col.direction = CapsuleDirection2D.Vertical;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            var cfg = new EnemyRig.Config
            {
                Prefix = "grunt", Torso = "grunt_torso", Head = "grunt_head", Arm = "grunt_arm", Weapon = "grunt_blade", Leg = "grunt_leg",
                HipY = 0.46f, TorsoH = 0.62f, HeadY = 0.60f, Shoulder = new Vector2(0.09f, 0.52f), ArmLen = 0.40f, ArmSpriteH = 0.42f,
                LegSpriteH = 0.46f, HeadSpriteH = 0.38f, WeaponOffset = new Vector2(0f, 0.26f), WeaponIdleLocal = 200f, ArmIdle = 18f,
                SortBase = SortOrder.Enemy, LightColor = Palette.RedDeep, LightIntensity = 0.35f, LightRadius = 1.4f, WeaponTipDistance = 0.62f
            };
            return EnemyRig.BuildHumanoid(transform, cfg);
        }

        protected override IEnumerator Behaviour()
        {
            yield return null;
            while (IsAlive)
            {
                if (!PlayerAlive) { Move(0f); yield return null; continue; }
                Vector2 d = Player.Center - Center;
                if (!aggro && Mathf.Abs(d.x) <= EnemyTuning.GruntAggroX && Mathf.Abs(d.y) <= EnemyTuning.GruntAggroY) { aggro = true; Rig.Flash(Palette.Red, 0.15f); }
                if (aggro && d.magnitude > EnemyTuning.GruntLoseAggro) aggro = false;

                if (!aggro)
                {
                    // patrol around spawn
                    float target = SpawnPos.x + patrolDir * 3f;
                    if (Mathf.Abs(target - transform.position.x) < 0.2f || !GroundAhead(patrolDir) || WallAhead(patrolDir))
                    {
                        Move(0f);
                        yield return Wait(Random.Range(0.8f, 1.6f));
                        patrolDir = -patrolDir;
                    }
                    Facing = patrolDir;
                    Move(patrolDir * EnemyTuning.GruntSpeed * 0.45f);
                    yield return null;
                    continue;
                }

                FacePlayer();
                if (Mathf.Abs(d.x) > EnemyTuning.GruntReach || Mathf.Abs(d.y) > 1.6f)
                {
                    Move(Facing * EnemyTuning.GruntSpeed);
                    yield return null;
                    continue;
                }

                Move(0f);
                yield return Slash(EnemyTuning.GruntTelegraph);
                yield return Wait(EnemyTuning.GruntRecovery * 0.6f);
                if (Random.value < EnemyTuning.GruntSecondSlashChance && PlayerAlive && Mathf.Abs(Player.Center.x - Center.x) < EnemyTuning.GruntReach + 0.6f)
                {
                    FacePlayer();
                    yield return Slash(EnemyTuning.GruntTelegraph2);
                }
                yield return Wait(EnemyTuning.GruntRecovery);
            }
        }

        IEnumerator Slash(float telegraph)
        {
            BeginTelegraph(AttackKind.Parryable, telegraph);
            yield return Wait(telegraph);
            EndTelegraph();
            Rig.Strike(0.12f);
            Services.Audio.PlaySfxAt("grunt_swing", Center, 0.9f);
            Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.1f, 0.1f), -30f, Facing < 0, Palette.EnemySlash, 0.9f);
            Body.linearVelocity = new Vector2(Facing * 2.5f, Body.linearVelocity.y);
            bool resolved = false;
            float t = 0f;
            while (t < EnemyTuning.GruntActive)
            {
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = EnemyTuning.GruntDamage, Knockback = EnemyTuning.GruntKnockback, Kind = AttackKind.Parryable, Tag = "grunt_slash", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.0f, 0.1f), new Vector2(1.8f, 1.4f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
