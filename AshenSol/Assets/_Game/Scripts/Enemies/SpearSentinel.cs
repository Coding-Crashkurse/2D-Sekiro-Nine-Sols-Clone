using System.Collections;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>Keeps its distance, thrusts (parryable); every third attack is a red unblockable lunge.</summary>
    public class SpearSentinel : EnemyBase
    {
        protected override float CenterHeight { get { return 0.85f; } }
        protected override float PostureOnParry { get { return EnemyTuning.SpearPostureOnParry; } }
        protected override float PostureRegen { get { return EnemyTuning.SpearPostureRegen; } }
        public override int ExecuteDamage { get { return EnemyTuning.SpearExecuteDamage; } }
        protected override float KnockbackResist { get { return 0.3f; } }
        public override string DisplayName { get { return "Spear Sentinel"; } }
        public override int AshValue { get { return 34; } }

        int attackCount;
        int patrolDir = 1;

        protected override void BuildBody()
        {
            Type = EnemyType.SpearSentinel;
            MaxHp = EnemyTuning.SpearHp;
            MaxPosture = EnemyTuning.SpearMaxPosture;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.mass = 2.5f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sharedMaterial = new PhysicsMaterial2D("EnemyMat") { friction = 0f, bounciness = 0f };
            Body = rb;
            var col = gameObject.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.7f, 1.7f);
            col.offset = new Vector2(0f, 0.85f);
            col.direction = CapsuleDirection2D.Vertical;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            var cfg = new EnemyRig.Config
            {
                Prefix = "spear", Torso = "spear_torso", Head = "spear_head", Arm = "spear_arm", Weapon = "spear_spear", Leg = "spear_leg",
                TwoSegment = true, UpperArm = "spear_arm_upper", LowerArm = "spear_arm_lower", UpperLeg = "spear_leg_upper", LowerLeg = "spear_leg_lower",
                UpperArmH = 0.24f, LowerArmH = 0.22f, UpperLegH = 0.27f, LowerLegH = 0.25f, UpperArmLen = 0.20f, LowerArmLen = 0.20f, UpperLegLen = 0.23f, LowerLegLen = 0.25f,
                HipY = 0.48f, TorsoH = 0.66f, HeadY = 0.64f, Shoulder = new Vector2(0.09f, 0.55f), ArmLen = 0.40f, ArmSpriteH = 0.42f,
                LegSpriteH = 0.48f, HeadSpriteH = 0.42f, WeaponOffset = new Vector2(0f, 0.25f), WeaponVerticalIdle = true, ArmIdle = 12f,
                SortBase = SortOrder.Enemy, LightColor = Palette.TealDeep, LightIntensity = 0.35f, LightRadius = 1.5f, WeaponTipDistance = 1.0f
            };
            return EnemyRig.BuildHumanoid(transform, cfg);
        }

        protected override void OnReset() { attackCount = 0; }

        protected override IEnumerator Behaviour()
        {
            yield return null;
            while (IsAlive)
            {
                if (!PlayerAlive) { Move(0f); yield return null; continue; }
                Vector2 d = Player.Center - Center;
                if (!aggro && Mathf.Abs(d.x) <= 8f && Mathf.Abs(d.y) <= 3.5f) { aggro = true; Rig.Flash(Palette.Teal, 0.15f); }
                if (aggro && d.magnitude > 13f) aggro = false;

                if (!aggro)
                {
                    float target = SpawnPos.x + patrolDir * 2f;
                    if (Mathf.Abs(target - transform.position.x) < 0.2f || !GroundAhead(patrolDir) || WallAhead(patrolDir))
                    {
                        Move(0f);
                        yield return Wait(Random.Range(1f, 2f));
                        patrolDir = -patrolDir;
                    }
                    Facing = patrolDir;
                    Move(patrolDir * EnemyTuning.SpearSpeed * 0.4f);
                    yield return null;
                    continue;
                }

                FacePlayer();
                float dist = Mathf.Abs(d.x);
                if (dist > EnemyTuning.SpearAttackRange || Mathf.Abs(d.y) > 2f)
                {
                    Move(Facing * EnemyTuning.SpearSpeed);
                    yield return null;
                    continue;
                }
                if (dist < EnemyTuning.SpearTooClose && attackCount % 3 != 2)
                {
                    // back off a little before attacking (never off a ledge)
                    float t = 0f;
                    while (t < 0.45f && PlayerAlive && Mathf.Abs(Player.Center.x - Center.x) < EnemyTuning.SpearKeepDistance)
                    {
                        FacePlayer();
                        Move(-Facing * EnemyTuning.SpearSpeed * 0.8f);
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
                Move(0f);
                attackCount++;
                if (attackCount % 3 == 0) yield return RedLunge();
                else yield return Thrust();
            }
        }

        IEnumerator Thrust()
        {
            BeginTelegraph(AttackKind.Parryable, EnemyTuning.SpearTelegraph);
            yield return Wait(EnemyTuning.SpearTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            Rig.Thrust(0.12f);
            Services.Audio.PlaySfxAt("spear_thrust", Center, 0.9f);
            Body.linearVelocity = new Vector2(Facing * 4f, Body.linearVelocity.y);
            Services.Vfx.SlashArc(Center + new Vector2(Facing * 2.0f, 0f), 90f, Facing < 0, Palette.EnemySlash, 0.9f, 0.3f, 0.5f);   // a thrust is a streak, not a crescent
            bool resolved = false;
            float t = 0f;
            while (t < EnemyTuning.SpearActive)
            {
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = EnemyTuning.SpearDamage, Knockback = EnemyTuning.SpearKnockback, Kind = AttackKind.Parryable, Tag = "spear_thrust", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.8f, 0f), new Vector2(3.0f, 1.0f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            yield return Wait(EnemyTuning.SpearRecovery);
        }

        IEnumerator RedLunge()
        {
            BeginTelegraph(AttackKind.Unblockable, EnemyTuning.LungeTelegraph);
            yield return Wait(EnemyTuning.LungeTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            FacePlayer();
            Rig.Thrust(0.1f);
            Services.Audio.PlaySfxAt("spear_lunge", Center, 1f);
            Services.Vfx.DustPuff(transform.position, 1.2f);
            float dashTime = EnemyTuning.LungeDistance / EnemyTuning.LungeSpeed;
            float t = 0f; bool resolved = false;
            while (t < Mathf.Max(dashTime, EnemyTuning.LungeActive))
            {
                if (t < dashTime && GroundAhead(Facing, 0.8f, 1.5f) && !WallAhead(Facing, 0.9f))
                    Body.linearVelocity = new Vector2(Facing * EnemyTuning.LungeSpeed, Body.linearVelocity.y);
                else Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
                if (!resolved && t < EnemyTuning.LungeActive)
                {
                    var info = new AttackInfo { Damage = EnemyTuning.LungeDamage, Knockback = EnemyTuning.LungeKnockback, Kind = AttackKind.Unblockable, Tag = "spear_lunge", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 0.9f, 0f), new Vector2(1.6f, 1.4f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                if (t > 0.06f && Random.value < 0.5f) Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.5f), 0.2f);
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            yield return Wait(EnemyTuning.LungeRecovery);
        }
    }
}
