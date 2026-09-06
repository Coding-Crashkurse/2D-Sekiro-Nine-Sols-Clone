using System.Collections;
using UnityEngine;
using AshenSol.Core;
using AshenSol.Boss;

namespace AshenSol.Enemies
{
    /// <summary>Foundry brute: a slow armoured hulk with a forge hammer. Every swing is heavy and slow to
    /// wind up, the overhead smash pierces a late block, and every third attack is a RED ground slam that
    /// sends a wave along the floor each way. Its guard takes TWO perfect parries to break.</summary>
    public class HammerBrute : EnemyBase
    {
        protected override float CenterHeight { get { return 1.1f; } }
        protected override float PostureOnParry { get { return EnemyTuning.BrutePostureOnParry; } }
        protected override float PostureRegen { get { return EnemyTuning.BrutePostureRegen; } }
        protected override float PostureFromHitsMul { get { return EnemyTuning.BrutePostureFromHitsMul; } }
        public override int ExecuteDamage { get { return EnemyTuning.BruteExecuteDamage; } }
        protected override float KnockbackResist { get { return 0.8f; } }
        public override string DisplayName { get { return "Foundry Brute"; } }
        public override int AshValue { get { return 55; } }

        int attackCount;
        int patrolDir = 1;
        float stepTimer;

        protected override void BuildBody()
        {
            Type = EnemyType.HammerBrute;
            MaxHp = EnemyTuning.BruteHp;
            MaxPosture = EnemyTuning.BruteMaxPosture;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.mass = 6f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sharedMaterial = new PhysicsMaterial2D("EnemyMat") { friction = 0f, bounciness = 0f };
            Body = rb;
            var col = gameObject.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(1.05f, 2.2f);
            col.offset = new Vector2(0f, 1.1f);
            col.direction = CapsuleDirection2D.Vertical;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            // The hammer rests upright at its side (arm forward-down, head up); the telegraph hauls it
            // up behind the shoulder and the strike brings it over the top into the ground.
            var cfg = new EnemyRig.Config
            {
                Prefix = "brute", Scale = 1.12f, Torso = "brute_torso", Head = "brute_head", Arm = "brute_arm", Weapon = "brute_hammer", Leg = "brute_leg",
                HipY = 0.58f, LegOffset = 0.10f, TorsoH = 0.84f, HeadY = 0.80f, Shoulder = new Vector2(0.14f, 0.70f), ArmLen = 0.50f, ArmSpriteH = 0.54f,
                LegSpriteH = 0.58f, HeadSpriteH = 0.48f, WeaponOffset = new Vector2(0f, 0.45f), WeaponIdleLocal = -30f, ArmIdle = 30f,
                SortBase = SortOrder.Enemy, LightColor = Palette.Amber, LightIntensity = 0.5f, LightRadius = 1.8f, WeaponTipDistance = 1.15f
            };
            return EnemyRig.BuildHumanoid(transform, cfg);
        }

        protected override void OnReset() { attackCount = 0; stepTimer = 0f; }

        protected override void Update()
        {
            base.Update();
            // heavy footfalls while it walks
            if (IsAlive && Body != null && Mathf.Abs(Body.linearVelocity.x) > 0.4f)
            {
                stepTimer -= Time.deltaTime;
                if (stepTimer <= 0f)
                {
                    stepTimer = 0.55f;
                    Services.Vfx.DustPuff(transform.position, 0.6f);
                    Services.Audio.PlaySfxAt("land", Center, 0.35f, 0.1f);
                }
            }
        }

        protected override void OnAttackResolved(HitOutcome outcome, in AttackInfo info)
        {
            bool wasBroken = PostureBroken;
            base.OnAttackResolved(outcome, info);
            if (outcome == HitOutcome.Parried && !wasBroken && !PostureBroken)
            {
                // the hammer rings off the blade but the brute holds: one more parry breaks it
                Rig.Punch(1.14f, 0.86f);
                Services.Cam.Kick(new Vector2(Facing, 0.2f), 0.12f);
                Services.Vfx.Embers(Rig.WeaponTip.position, 8, Palette.Amber);
                if (Posture01 >= 0.45f)
                    Services.Vfx.FloatingText(Center + new Vector2(0f, CenterHeight + 0.7f), "ONE MORE", Palette.Posture, 1.05f);
            }
        }

        protected override IEnumerator Behaviour()
        {
            yield return null;
            while (IsAlive)
            {
                if (!PlayerAlive) { Move(0f); yield return null; continue; }
                Vector2 d = Player.Center - Center;
                if (!aggro && Mathf.Abs(d.x) <= EnemyTuning.BruteAggroX && Mathf.Abs(d.y) <= EnemyTuning.BruteAggroY)
                {
                    aggro = true;
                    Rig.Flash(Palette.Amber, 0.2f);
                    Rig.Punch(1.08f, 0.94f);
                    Services.Audio.PlaySfxAt("boss_hurt", Center, 0.35f, 0.15f);
                }
                if (aggro && d.magnitude > EnemyTuning.BruteLoseAggro) aggro = false;

                if (!aggro)
                {
                    float target = SpawnPos.x + patrolDir * 2.5f;
                    if (Mathf.Abs(target - transform.position.x) < 0.2f || !GroundAhead(patrolDir) || WallAhead(patrolDir))
                    {
                        Move(0f);
                        yield return Wait(Random.Range(1.4f, 2.6f));
                        patrolDir = -patrolDir;
                    }
                    Facing = patrolDir;
                    Move(patrolDir * EnemyTuning.BruteSpeed * 0.35f);
                    yield return null;
                    continue;
                }

                FacePlayer();
                if (Mathf.Abs(d.x) > EnemyTuning.BruteReach || Mathf.Abs(d.y) > 2.2f)
                {
                    Move(Facing * EnemyTuning.BruteSpeed);
                    yield return null;
                    continue;
                }

                Move(0f);
                attackCount++;
                if (attackCount % 3 == 0) yield return Quake();
                else if (attackCount % 3 == 1) yield return Smash();
                else yield return Sweep();
            }
        }

        // ---------------- attacks ----------------

        /// <summary>Overhead smash: long windup, hammer comes over the top into the ground. Parryable,
        /// but a late block only takes the edge off it.</summary>
        IEnumerator Smash()
        {
            GameEvents.RaiseLog("brute smash");
            BeginTelegraph(AttackKind.Parryable, EnemyTuning.SmashTelegraph);
            yield return Wait(EnemyTuning.SmashTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            FacePlayer();
            Rig.Strike(0.2f, -125f, 75f);
            Services.Audio.PlaySfxAt("boss_swing", Center, 0.7f, 0.1f);
            Body.linearVelocity = new Vector2(Facing * 2.2f, Body.linearVelocity.y);
            Vector2 impact = (Vector2)transform.position + new Vector2(Facing * 1.4f, 0f);
            Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.2f, 0.4f), -70f, Facing < 0, Palette.EnemySlash, 1.3f);
            yield return Wait(0.06f);
            Impact(impact, 0.55f, false);
            bool resolved = false;
            float t = 0f;
            while (t < EnemyTuning.SmashActive)
            {
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = EnemyTuning.SmashDamage, Knockback = EnemyTuning.SmashKnockback, Kind = AttackKind.Parryable, PierceGuard = true, Tag = "hammer_smash", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.3f, -0.2f), new Vector2(2.4f, 1.9f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            // the hammer stays buried for a beat: the punish window
            Rig.SetPose(true, 75f, 180f, -12f);
            yield return Wait(EnemyTuning.SmashRecovery);
            Rig.SetPose(false);
        }

        /// <summary>Wide horizontal sweep with a step forward. Parryable.</summary>
        IEnumerator Sweep()
        {
            GameEvents.RaiseLog("brute sweep");
            BeginTelegraph(AttackKind.Parryable, EnemyTuning.SweepTelegraph);
            yield return Wait(EnemyTuning.SweepTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            FacePlayer();
            Rig.Strike(0.24f, -125f, 55f);
            Services.Audio.PlaySfxAt("boss_swing", Center, 0.75f, 0.12f);
            Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.6f, 0.2f), -12f, Facing < 0, Palette.EnemySlash, 1.5f);
            Body.linearVelocity = new Vector2(Facing * 3.5f, Body.linearVelocity.y);
            bool resolved = false;
            float t = 0f;
            while (t < EnemyTuning.SweepActive)
            {
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = EnemyTuning.SweepDamage, Knockback = EnemyTuning.SweepKnockback, Kind = AttackKind.Parryable, Tag = "hammer_sweep", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.5f, 0.2f), new Vector2(3.2f, 1.6f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            yield return Wait(EnemyTuning.SweepRecovery);
        }

        /// <summary>RED ground slam: unblockable, a shockwave runs along the floor to both sides.
        /// Dash away, or jump the wave.</summary>
        IEnumerator Quake()
        {
            GameEvents.RaiseLog("brute quake");
            BeginTelegraph(AttackKind.Unblockable, EnemyTuning.QuakeTelegraph);
            yield return Wait(EnemyTuning.QuakeTelegraph * Settings.TelegraphMul);
            EndTelegraph();
            FacePlayer();
            Rig.Strike(0.16f, -125f, 82f);
            Services.Audio.PlaySfxAt("boss_swing", Center, 0.8f, 0.05f);
            yield return Wait(0.05f);
            Vector2 feet = (Vector2)transform.position + new Vector2(Facing * 0.9f, 0f);
            Impact(feet, 0.85f, true);
            Services.Audio.PlaySfxAt("boss_shockwave", Center, 0.6f);
            HitStop.Request(0.05f);
            var info = new AttackInfo { Damage = EnemyTuning.QuakeDamage, Knockback = EnemyTuning.QuakeKnockback, Kind = AttackKind.Unblockable, Tag = "hammer_quake", Origin = Center };
            StrikePlayer(info, Center + new Vector2(Facing * 0.8f, -0.2f), new Vector2(3.0f, 2.2f));
            GroundShockwave.Spawn(feet + new Vector2(Facing * 0.6f, 0f), Facing, EnemyTuning.QuakeWaveSpeed, EnemyTuning.QuakeWaveDamage, EnemyTuning.QuakeWaveLife, transform.parent);
            GroundShockwave.Spawn((Vector2)transform.position + new Vector2(-Facing * 0.6f, 0f), -Facing, EnemyTuning.QuakeWaveSpeed, EnemyTuning.QuakeWaveDamage, EnemyTuning.QuakeWaveLife, transform.parent);
            float t = 0f;
            while (t < EnemyTuning.QuakeActive) { t += Time.deltaTime; yield return null; }
            Move(0f);
            Rig.SetPose(true, 82f, 180f, -16f);   // kneels on the hammer: the punish window
            yield return Wait(EnemyTuning.QuakeRecovery);
            Rig.SetPose(false);
        }

        void Impact(Vector2 at, float strength, bool red)
        {
            Color c = red ? Palette.Red : Palette.Amber;
            Services.Vfx.DustPuff(at, 1.2f + strength);
            Services.Vfx.Shockwave(at, 1.6f + 2.4f * strength, c);
            Services.Vfx.HitSpark(at + new Vector2(0f, 0.2f), Vector2.up, c, 0.8f + strength);
            Services.Vfx.Embers(at, red ? 22 : 12, c);
            Services.Vfx.FlashLight(at + new Vector2(0f, 0.6f), c, 2.5f + 2f * strength, 4f + 3f * strength, 0.25f);
            Services.Cam.Shake(0.35f + 0.5f * strength);
            Services.Cam.Kick(Vector2.down, 0.15f + 0.2f * strength);
            Services.Audio.PlaySfxAt("boss_slam", at, 0.45f + 0.4f * strength, 0.1f);
        }
    }
}
