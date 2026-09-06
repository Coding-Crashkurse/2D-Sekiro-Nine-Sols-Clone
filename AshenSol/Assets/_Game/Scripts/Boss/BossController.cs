using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.Player;

namespace AshenSol.Boss
{
    /// <summary>THE FORSAKEN WARDEN — two-phase glaive boss. Attack coroutines are picked by weighted, distance-gated selection.</summary>
    public class BossController : EnemyBase
    {
        public const string BossName = "THE FORSAKEN WARDEN";
        public const string BossSubtitle = "Keeper of the Sealed Gate";

        public static BossController Create(Vector2 pos, Transform parent)
        {
            var go = new GameObject("Boss");
            go.layer = Layers.Enemy;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<BossController>();
            b.Init(pos, -1, "boss");
            return b;
        }

        public int Phase { get; private set; } = 1;
        public bool IsFightActive { get; private set; }
        public string CurrentAttack { get; private set; } = "";
        public event Action Defeated;

        protected override float CenterHeight { get { return 1.6f; } }
        protected override float StaggerOnParry { get { return 0f; } }
        protected override float KnockbackResist { get { return 1f; } }
        protected override float DamageTakenMultiplier { get { return IsStaggered ? BossTuning.StaggerDamageMul : 1f; } }
        protected override string DeathSfx { get { return "boss_death"; } }
        public override string DisplayName { get { return BossName; } }

        float teleMul = 1f;
        bool invulnerable, phasePending, dying;
        int comboParryCount;
        float lastInternalStagger = -99f;
        string lastAttack = "";
        SashChain cape;
        SpriteRenderer core; Light2D coreLight;
        float lastDustX;

        // ---------------- build ----------------
        protected override void BuildBody()
        {
            Type = EnemyType.Boss;
            MaxHp = BossTuning.MaxHp;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.mass = 20f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.sharedMaterial = new PhysicsMaterial2D("BossMat") { friction = 0f, bounciness = 0f };
            Body = rb;
            var col = gameObject.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(1.4f, 3.0f);
            col.offset = new Vector2(0f, 1.5f);
            col.direction = CapsuleDirection2D.Vertical;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            var cfg = new EnemyRig.Config
            {
                Prefix = "boss", Torso = "boss_torso", Head = "boss_head", Arm = "boss_arm", Weapon = "boss_glaive", Leg = "boss_leg",
                HipY = 1.1f, LegOffset = 0.18f, TorsoH = 1.5f, HeadY = 1.42f, Shoulder = new Vector2(0.22f, 1.22f), ArmLen = 0.82f, ArmSpriteH = 0.92f,
                LegSpriteH = 1.1f, HeadSpriteH = 0.82f, WeaponOffset = new Vector2(0f, 0.55f), WeaponVerticalIdle = true, ArmIdle = 10f,
                SortBase = SortOrder.Boss, LightColor = Palette.Red, LightIntensity = 0.9f, LightRadius = 3.2f, WeaponTipDistance = 1.8f
            };
            var rig = EnemyRig.BuildHumanoid(transform, cfg);

            // cape + glowing core
            var capeAnchor = new GameObject("capeAnchor").transform;
            capeAnchor.SetParent(rig.Torso, false);
            capeAnchor.localPosition = new Vector3(-0.32f, 1.25f, 0f);
            cape = SashChain.Build(transform, capeAnchor, 7, Palette.RedDeep, "boss_cape_seg", 0.3f, SortOrder.Boss - 5, 1.7f, 0.9f);
            cape.Gravity = 16f; cape.Wind = 0.12f;

            var coreGo = new GameObject("core");
            coreGo.transform.SetParent(rig.Torso, false);
            coreGo.transform.localPosition = new Vector3(0.06f, 0.95f, 0f);
            core = coreGo.AddComponent<SpriteRenderer>();
            core.sprite = Res.Sprite("boss_core");
            core.material = MaterialLibrary.Additive;
            core.color = Palette.Red;
            core.sortingOrder = SortOrder.Boss + 3;
            coreLight = coreGo.AddComponent<Light2D>();
            coreLight.lightType = Light2D.LightType.Point;
            coreLight.color = Palette.Red;
            coreLight.intensity = 1.2f;
            coreLight.pointLightOuterRadius = 3f;
            return rig;
        }

        protected override void OnReset()
        {
            Phase = 1; teleMul = 1f;
            IsFightActive = false; CurrentAttack = ""; invulnerable = false; phasePending = false; dying = false;
            comboParryCount = 0; lastInternalStagger = -99f; lastAttack = "";
            if (cape != null) { cape.Reset(); cape.SetVisible(true); }
            if (core != null) { core.enabled = true; coreLight.enabled = true; }
            if (Rig != null) Rig.SetPose(true, 25f, 180f, -14f); // kneeling guardian before the fight
            GroundShockwave.ClearAll();
            GameEvents.RaiseBossHealthChanged(1f, 0f);
        }

        public void ResetFight() { ResetToSpawn(); }

        public void BeginFight()
        {
            if (!IsAlive || IsFightActive) return;
            IsFightActive = true;
            Rig.SetPose(false);
            GameEvents.RaiseLog("boss fight begins");
        }

        /// <summary>Intro: stand up from the kneel with a flash.</summary>
        public void Rise()
        {
            Rig.SetPose(false);
            Rig.Punch(0.9f, 1.15f);
            Rig.Flash(Palette.Red, 0.4f);
            Services.Vfx.FlashLight(Center, Palette.Red, 3f, 7f, 0.5f);
            Services.Vfx.DustPuff(transform.position, 2f);
            Services.Vfx.Embers(Center, 30, Palette.Red);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            if (cape != null) cape.Tick(dt, Facing, Body.linearVelocity);
            if (core != null)
            {
                float pulse = 0.8f + 0.25f * Mathf.Sin(Time.time * (Phase == 2 ? 9f : 4f));
                core.color = Palette.Red.WithAlpha(pulse);
                coreLight.intensity = (Phase == 2 ? 1.8f : 1.2f) * pulse;
            }
            if (Mathf.Abs(Body.linearVelocity.x) > 2f && Mathf.Abs(transform.position.x - lastDustX) > 0.9f && IsGroundedApprox())
            {
                lastDustX = transform.position.x;
                Services.Vfx.DustPuff(transform.position, 1.1f);
            }
        }

        bool IsGroundedApprox()
        {
            return Physics2D.Raycast((Vector2)transform.position + new Vector2(0f, 0.2f), Vector2.down, 0.4f, Layers.GroundMask).collider != null;
        }

        // ---------------- damage hooks ----------------
        public override HitOutcome ReceiveAttack(in AttackInfo info)
        {
            if (!IsAlive || dying) return HitOutcome.Ignored;
            if (invulnerable)
            {
                Rig.Flash(Palette.Red, 0.1f);
                Services.Vfx.HitSpark(info.HitPoint == Vector2.zero ? Center : info.HitPoint, Vector2.up, Palette.Red, 0.6f);
                return HitOutcome.Ignored;
            }
            return base.ReceiveAttack(info);
        }

        protected override void OnHealthChanged()
        {
            GameEvents.RaiseBossHealthChanged(Mathf.Clamp01((float)Hp / MaxHp), Mathf.Clamp01((float)InternalDamage / MaxHp));
            if (Phase == 1 && Hp > 0 && Hp <= MaxHp * BossTuning.Phase2Threshold) phasePending = true;
            if (InternalDamage >= BossTuning.InternalStaggerThreshold && Time.time - lastInternalStagger > BossTuning.InternalStaggerCooldown && !IsStaggered && IsFightActive)
            {
                lastInternalStagger = Time.time;
                DoStagger("OVERWHELMED");
            }
        }

        protected override void OnHurt(in AttackInfo info, int dmg)
        {
            Services.Audio.PlaySfxAt("boss_hurt", Center, 0.5f, 0.1f);
            Rig.Punch(0.97f, 1.03f);
        }

        protected override void OnAttackResolved(HitOutcome outcome, in AttackInfo info)
        {
            if (outcome == HitOutcome.Parried)
            {
                AddInternalDamage(info.Damage);
                comboParryCount++;
                Rig.Flash(Color.white, 0.12f);
                Rig.Punch(0.92f, 1.08f);
                healthBar.Show();
                if (CurrentAttack == "TripleSlash" && comboParryCount >= 3) DoStagger("STAGGERED");
            }
            else if (outcome == HitOutcome.Blocked) Rig.Flash(Palette.Amber, 0.1f);
        }

        void DoStagger(string text)
        {
            if (!IsAlive || !IsFightActive) return;
            Stagger(BossTuning.StaggerSeconds);
            CurrentAttack = "";
            Body.linearVelocity = Vector2.zero;
            Body.gravityScale = 1f;
            Services.Audio.PlaySfxAt("boss_stagger", Center, 1f);
            Services.Vfx.FloatingText(Center + new Vector2(0f, 2.2f), text, Palette.Gold, 1.6f);
            Services.Vfx.FlashLight(Center, Palette.Gold, 2.5f, 6f, 0.4f);
            Services.Cam.Shake(0.4f);
            GameEvents.RaiseLog("boss " + text.ToLower());
        }

        protected override void Stagger(float seconds)
        {
            if (!IsAlive) return;
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            EndTelegraph();
            staggerTimer = Mathf.Max(staggerTimer, seconds);
            Rig.Stagger(seconds);
            Rig.SetPose(false);
        }

        // ---------------- death ----------------
        protected override void Die()
        {
            if (!IsAlive || dying) return;
            dying = true;
            IsAlive = false;
            IsFightActive = false;
            CurrentAttack = "";
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            StopAllCoroutines();
            EndTelegraph();
            staggerTimer = 0f;
            Collider.enabled = false;
            Body.linearVelocity = Vector2.zero;
            Body.simulated = false;
            GroundShockwave.ClearAll();
            healthBar.Hide();
            GameEvents.RaiseBossHealthChanged(0f, 0f);
            GameEvents.RaiseEnemyKilled(this);
            GameEvents.RaiseLog("boss killed");
            StartCoroutine(DeathSequence());
        }

        IEnumerator DeathSequence()
        {
            TimeController.Instance.SlowMo(0.2f, 1.5f);
            Services.Audio.PlaySfx("boss_death");
            Services.Cam.Shake(1f);
            for (int i = 0; i < 3; i++)
            {
                Rig.Flash(Color.white, 0.25f);
                Services.Vfx.FlashLight(Center, Color.white, 4f, 8f, 0.25f);
                Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.35f), 0.2f);
                Services.Vfx.Embers(Center, 20, Palette.Red);
                yield return new WaitForSecondsRealtime(0.35f);
            }
            Services.Vfx.Dissolve(Renderers, Center, Palette.Red);
            Services.Vfx.Embers(Center, 80, Palette.Gold);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Ink, 30);
            Services.Vfx.Shockwave(transform.position, 7f, Palette.Red);
            Services.Cam.Shake(0.8f);
            Rig.SetVisible(false);
            if (cape != null) cape.SetVisible(false);
            core.enabled = false; coreLight.enabled = false;
            yield return new WaitForSecondsRealtime(2.5f);
            var d = Defeated; if (d != null) d();
            GameEvents.RaiseBossDefeated();
        }

        // ---------------- behaviour ----------------
        protected override IEnumerator Behaviour()
        {
            yield return null;
            while (IsAlive)
            {
                if (!IsFightActive || !PlayerAlive)
                {
                    Move(0f);
                    yield return null;
                    continue;
                }
                if (phasePending) { yield return PhaseTransition(); continue; }

                yield return Approach(UnityEngine.Random.Range(0.35f, 0.9f));
                if (!PlayerAlive) continue;
                string attack = ChooseAttack();
                lastAttack = attack;
                switch (attack)
                {
                    case "TripleSlash": yield return TripleSlash(); break;
                    case "DashSlash": yield return DashSlash(); break;
                    case "Slam": yield return Slam(); break;
                    case "Bolts": yield return Bolts(); break;
                    case "Whirl": yield return Whirl(); break;
                    case "RedThrust": yield return RedThrust(); break;
                }
                CurrentAttack = "";
            }
        }

        string ChooseAttack()
        {
            float d = Mathf.Abs(Player.Center.x - Center.x);
            var names = new System.Collections.Generic.List<string>();
            var weights = new System.Collections.Generic.List<float>();
            void Add(string n, float w) { if (w > 0f && n != lastAttack) { names.Add(n); weights.Add(w); } }
            Add("TripleSlash", d <= 3.6f ? 3f : 0f);
            Add("DashSlash", d > 3f && d <= 12f ? 3f : 0f);
            Add("Slam", 2f);
            Add("Bolts", d > 5f ? 3f : 1f);
            if (Phase >= 2)
            {
                Add("Whirl", d <= 4.5f ? 3f : 0f);
                Add("RedThrust", d > 3f ? 2f : 0f);
            }
            if (names.Count == 0) return d <= 3.6f ? "TripleSlash" : "DashSlash";
            float total = 0f; foreach (var w in weights) total += w;
            float r = UnityEngine.Random.value * total;
            for (int i = 0; i < names.Count; i++) { r -= weights[i]; if (r <= 0f) return names[i]; }
            return names[names.Count - 1];
        }

        IEnumerator Approach(float seconds)
        {
            CurrentAttack = "";
            float t = 0f;
            while (t < seconds && PlayerAlive)
            {
                FacePlayer();
                float d = Mathf.Abs(Player.Center.x - Center.x);
                Move(d > 2.4f ? Facing * BossTuning.WalkSpeed : 0f);
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
        }

        IEnumerator PhaseTransition()
        {
            phasePending = false;
            CurrentAttack = "Phase";
            invulnerable = true;
            Move(0f);
            Rig.SetPose(true, -95f, 180f, -12f);
            Services.Audio.PlaySfxAt("boss_phase2", Center, 1f);
            Services.Audio.PlaySfxAt("boss_roar", Center, 0.8f);
            Rig.Flash(Palette.Red, 0.6f);
            Services.Vfx.FlashLight(Center, Palette.Red, 4f, 9f, 0.8f);
            Services.Vfx.Shockwave(transform.position, 6f, Palette.Red);
            Services.Vfx.Embers(Center, 50, Palette.Red);
            Services.Cam.Shake(0.8f);
            Services.Vfx.ChromaticPulse(0.8f, 0.6f);
            float t = 0f;
            while (t < 2f) { t += Time.deltaTime; if (Mathf.Repeat(t, 0.4f) < Time.deltaTime) Rig.Flash(Palette.Red, 0.3f); yield return null; }
            Phase = 2;
            teleMul = BossTuning.Phase2TelegraphMul;
            invulnerable = false;
            Rig.SetPose(false);
            GameEvents.RaiseBossPhaseChanged(2);
            GameEvents.RaiseLog("boss phase 2");
            CurrentAttack = "";
        }

        IEnumerator TripleSlash()
        {
            CurrentAttack = "TripleSlash";
            comboParryCount = 0;
            for (int i = 0; i < 3; i++)
            {
                FacePlayer();
                Move(0f);
                BeginTelegraph(AttackKind.Parryable, BossTuning.SlashTelegraph * teleMul);
                yield return Wait(BossTuning.SlashTelegraph * teleMul);
                EndTelegraph();
                bool up = i == 1;
                Rig.Strike(0.14f, up ? 85f : -125f, up ? -70f : 85f);
                Services.Audio.PlaySfxAt("boss_swing", Center, 1f, 0.08f);
                Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.7f, 0.3f), up ? 35f : -25f, Facing < 0, Palette.BossSlash, 2.4f);
                Body.linearVelocity = new Vector2(Facing * 3f, Body.linearVelocity.y);
                yield return ActiveWindow(BossTuning.SlashActive, Center + new Vector2(Facing * 1.7f, 0.1f), new Vector2(3.0f, 2.4f), BossTuning.SlashDamage, BossTuning.SlashKnockback, AttackKind.Parryable, "boss_slash" + (i + 1));
                Move(0f);
                if (i < 2) yield return Wait(BossTuning.SlashGap);
            }
            yield return Wait(BossTuning.SlashRecovery);
        }

        IEnumerator DashSlash()
        {
            CurrentAttack = "DashSlash";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Parryable, BossTuning.DashTelegraph * teleMul);
            Rig.SetPose(true, -45f, 180f, 18f);
            yield return Wait(BossTuning.DashTelegraph * teleMul);
            EndTelegraph();
            Rig.SetPose(false);
            Services.Audio.PlaySfxAt("boss_dash", Center, 1f);
            Services.Vfx.DustPuff(transform.position, 1.6f);
            float t = 0f, ai = 0f;
            while (t < BossTuning.DashMaxTime && PlayerAlive && Mathf.Abs(Player.Center.x - Center.x) > BossTuning.DashStopDistance)
            {
                if (!WallAhead(Facing, 1.2f)) Body.linearVelocity = new Vector2(Facing * BossTuning.DashSpeed, Body.linearVelocity.y);
                else break;
                ai -= Time.deltaTime;
                if (ai <= 0f) { ai = 0.04f; Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.45f), 0.25f); }
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            Rig.Strike(0.14f, -130f, 90f);
            Services.Audio.PlaySfxAt("boss_swing", Center, 1f, 0.05f);
            Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.8f, 0.2f), -30f, Facing < 0, Palette.BossSlash, 2.6f);
            yield return ActiveWindow(BossTuning.DashSlashActive, Center + new Vector2(Facing * 1.8f, 0.1f), new Vector2(3.2f, 2.4f), BossTuning.DashDamage, BossTuning.DashKnockback, AttackKind.Parryable, "boss_dashslash");
            yield return Wait(BossTuning.DashRecovery);
        }

        IEnumerator Slam()
        {
            CurrentAttack = "Slam";
            FacePlayer();
            Move(0f);
            float tele = BossTuning.SlamTelegraph * teleMul;
            BeginTelegraph(AttackKind.Unblockable, tele);
            Rig.SetPose(true, -150f, 180f, -8f);
            Body.gravityScale = 0f;
            float t = 0f;
            float startY = transform.position.y;
            while (t < tele)
            {
                float vy = t < BossTuning.SlamLeapTime ? BossTuning.SlamLeapHeight / BossTuning.SlamLeapTime : 0f;
                float vx = 0f;
                if (PlayerAlive && t < tele - 0.2f) vx = Mathf.Clamp((Player.Center.x - Center.x) * 4f, -7f, 7f);
                Body.linearVelocity = new Vector2(vx, vy);
                if (PlayerAlive) Facing = Player.Center.x >= Center.x ? 1 : -1;
                t += Time.deltaTime;
                yield return null;
            }
            EndTelegraph();
            Rig.SetPose(true, 60f, 180f, -20f);
            // drop
            t = 0f;
            while (t < 0.7f)
            {
                Body.linearVelocity = new Vector2(0f, -BossTuning.SlamDropSpeed);
                if (IsGroundedApprox() && t > 0.03f) break;
                t += Time.deltaTime;
                yield return null;
            }
            Body.linearVelocity = Vector2.zero;
            Body.gravityScale = 1f;
            Vector2 feet = transform.position;
            Services.Vfx.Shockwave(feet, 4f, Palette.Red);
            Services.Vfx.DustPuff(feet + new Vector2(-1f, 0f), 1.8f);
            Services.Vfx.DustPuff(feet + new Vector2(1f, 0f), 1.8f);
            Services.Vfx.FlashLight(feet + new Vector2(0f, 1f), Palette.Red, 4f, 8f, 0.3f);
            Services.Cam.Shake(0.9f);
            Services.Cam.Kick(Vector2.down, 0.3f);
            HitStop.Request(0.05f);
            Services.Audio.PlaySfxAt("boss_slam", Center, 1f);
            Services.Audio.PlaySfxAt("boss_shockwave", Center, 0.9f);
            var info = new AttackInfo { Damage = BossTuning.SlamDamage, Knockback = BossTuning.SlamKnockback, Kind = AttackKind.Unblockable, Tag = "boss_slam", Origin = Center };
            StrikePlayer(info, Center, new Vector2(3.6f, 3.2f));
            GroundShockwave.Spawn(feet + new Vector2(1.2f, 0f), 1, BossTuning.ShockwaveSpeed, BossTuning.ShockwaveDamage, BossTuning.ShockwaveLife, transform.parent);
            GroundShockwave.Spawn(feet + new Vector2(-1.2f, 0f), -1, BossTuning.ShockwaveSpeed, BossTuning.ShockwaveDamage, BossTuning.ShockwaveLife, transform.parent);
            Rig.SetPose(true, 30f, 180f, -18f); // kneel: punish window
            yield return Wait(BossTuning.SlamRecovery);
            Rig.SetPose(false);
        }

        IEnumerator Bolts()
        {
            CurrentAttack = "Bolts";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Parryable, BossTuning.BoltsTelegraph * teleMul);
            Rig.SetPose(true, -100f, 180f, 5f);
            yield return Wait(BossTuning.BoltsTelegraph * teleMul);
            EndTelegraph();
            Rig.SetPose(false);
            Rig.Strike(0.12f, -100f, 60f);
            Services.Audio.PlaySfxAt("boss_bolts", Center, 1f);
            Vector2 origin = Center + new Vector2(Facing * 0.9f, 0.6f);
            Vector2 baseDir = PlayerAlive ? (Player.Center - origin).normalized : new Vector2(Facing, 0f);
            Services.Vfx.FlashLight(origin, Palette.Red, 3f, 4f, 0.2f);
            for (int i = -1; i <= 1; i++)
            {
                Vector2 dir = Quaternion.Euler(0f, 0f, i * 15f) * baseDir;
                Projectile.Fire(origin, dir * BossTuning.BoltSpeed, Team.Enemy, BossTuning.BoltDamage, AttackKind.Parryable, Palette.Red, "boss_bolt", 5f, 0.22f);
            }
            yield return Wait(BossTuning.BoltsRecovery);
        }

        IEnumerator Whirl()
        {
            CurrentAttack = "Whirl";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Parryable, BossTuning.WhirlTelegraph * teleMul);
            yield return Wait(BossTuning.WhirlTelegraph * teleMul);
            EndTelegraph();
            Services.Audio.PlaySfxAt("boss_whirl", Center, 1f);
            for (int h = 0; h < BossTuning.WhirlHits; h++)
            {
                Rig.Strike(0.22f, -160f, 200f);
                Services.Vfx.SlashArc(Center + new Vector2(Facing * 0.6f, 0.4f), h * 90f, Facing < 0, Palette.BossSlash, 2.2f);
                if (h > 0) Services.Audio.PlaySfxAt("boss_swing", Center, 0.8f, 0.12f);
                float t = 0f; bool resolved = false;
                while (t < BossTuning.WhirlInterval)
                {
                    if (PlayerAlive) { FacePlayer(); Move(Facing * BossTuning.WhirlDrift); }
                    if (!resolved && t < 0.1f)
                    {
                        var col = Physics2D.OverlapCircle(Center, BossTuning.WhirlRadius, Layers.PlayerMask);
                        if (col != null)
                        {
                            var info = new AttackInfo { Damage = BossTuning.WhirlDamage, Knockback = BossTuning.WhirlKnockback, Kind = AttackKind.Parryable, Tag = "boss_whirl" + (h + 1), Origin = Center };
                            var o = StrikePlayer(info, Center, new Vector2(BossTuning.WhirlRadius * 2f, BossTuning.WhirlRadius * 1.6f));
                            if (o != HitOutcome.Ignored) resolved = true;
                        }
                    }
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            Move(0f);
            yield return Wait(BossTuning.WhirlRecovery);
        }

        IEnumerator RedThrust()
        {
            CurrentAttack = "RedThrust";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Unblockable, BossTuning.ThrustTelegraph * teleMul);
            Rig.SetPose(true, -35f, 180f, 14f);
            yield return Wait(BossTuning.ThrustTelegraph * teleMul);
            EndTelegraph();
            FacePlayer();
            Rig.SetPose(false);
            Rig.Thrust(0.12f);
            Services.Audio.PlaySfxAt("boss_dash", Center, 1f, 0.05f);
            Services.Vfx.DustPuff(transform.position, 1.6f);
            float dur = BossTuning.ThrustDistance / BossTuning.ThrustSpeed;
            float t = 0f, ai = 0f; bool resolved = false;
            while (t < dur)
            {
                if (!WallAhead(Facing, 1.3f)) Body.linearVelocity = new Vector2(Facing * BossTuning.ThrustSpeed, Body.linearVelocity.y);
                else Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = BossTuning.ThrustDamage, Knockback = BossTuning.ThrustKnockback, Kind = AttackKind.Unblockable, Tag = "boss_thrust", Origin = Center };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.5f, 0f), new Vector2(2.2f, 1.8f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                ai -= Time.deltaTime;
                if (ai <= 0f) { ai = 0.04f; Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.5f), 0.25f); }
                t += Time.deltaTime;
                yield return null;
            }
            Move(0f);
            yield return Wait(BossTuning.ThrustRecovery);
        }

        IEnumerator ActiveWindow(float seconds, Vector2 boxCenter, Vector2 boxSize, int damage, float knockback, AttackKind kind, string tag)
        {
            float t = 0f; bool resolved = false;
            while (t < seconds)
            {
                if (!resolved)
                {
                    var info = new AttackInfo { Damage = damage, Knockback = knockback, Kind = kind, Tag = tag, Origin = Center };
                    var o = StrikePlayer(info, boxCenter, boxSize);
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
