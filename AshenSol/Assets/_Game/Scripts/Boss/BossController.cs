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
    public class BossController : EnemyBase, IBossFight
    {
        public const string WardenName = "THE FORSAKEN WARDEN";
        public const string WardenSubtitle = "Keeper of the Sealed Gate";
        public const string SecondFormName = "ASH UNBOUND";
        public const string SecondFormSubtitle = "What the Ninth Sun Left Behind";

        // kept for the UI and the intro, which refer to the first form by name
        public const string BossName = WardenName;
        public const string BossSubtitle = WardenSubtitle;

        public string FightMusic { get { return "music_boss"; } }
        string IBossFight.BossName { get { return WardenName; } }
        string IBossFight.BossSubtitle { get { return WardenSubtitle; } }

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
        protected override float PostureOnParry { get { return BossTuning.PostureOnParry; } }
        protected override float PostureRegen { get { return BossTuning.PostureRegen; } }
        public override int ExecuteDamage { get { return BossTuning.ExecuteDamage; } }
        protected override float KnockbackResist { get { return 1f; } }
        protected override string DeathSfx { get { return "boss_death"; } }
        public override string DisplayName { get { return BossName; } }
        public override int AshValue { get { return 260; } }

        float teleMul = 1f;
        /// <summary>Phase-2 speed-up combined with the difficulty setting.</summary>
        float Tele { get { return teleMul * Settings.TelegraphMul; } }
        bool invulnerable, phasePending, dying;
        string lastAttack = "";
        SashChain cape;
        SpriteRenderer core; Light2D coreLight;
        SpriteRenderer hornL, hornR; Light2D hornLight;
        Transform sun; SpriteRenderer sunGlow, sunBody, sunRing, sunRing2; Light2D sunLight;
        Transform aura; SpriteRenderer auraGlow, auraRing, auraRing2; Light2D auraLight;
        float auraAmount, auraEmber, auraTrail;
        bool solarPending; float solarCd;
        float lastDustX;

        // ---------------- build ----------------
        protected override void BuildBody()
        {
            Type = EnemyType.Boss;
            MaxHp = BossTuning.MaxHp;
            MaxPosture = BossTuning.MaxPosture;
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

            // the horns are grown during the phase change, so they start at zero scale
            hornL = Horn(rig.Head, new Vector2(-0.34f, 0.52f), 18f);
            hornR = Horn(rig.Head, new Vector2(0.30f, 0.55f), -14f);
            var hl = new GameObject("hornLight");
            hl.transform.SetParent(rig.Head, false);
            hl.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            hornLight = hl.AddComponent<Light2D>();
            hornLight.lightType = Light2D.LightType.Point;
            hornLight.color = Palette.Red;
            hornLight.intensity = 0f;
            hornLight.pointLightOuterRadius = 5f;

            // the sun he drags out of the horns in phase two; dormant until then
            var sgo = new GameObject("sun");
            sgo.transform.SetParent(transform, false);
            sgo.transform.localPosition = new Vector3(0f, 5.8f, 0f);
            sun = sgo.transform;
            sunGlow = Additive(sun, "fx_glow", Palette.Amber, SortOrder.Boss + 3, 17f);
            sunRing2 = Additive(sun, "fx_ring", Palette.Red, SortOrder.Boss + 4, 13f);
            sunRing = Additive(sun, "fx_ring", Palette.Gold, SortOrder.Boss + 5, 9f);
            sunBody = Additive(sun, "fx_flare", Color.white, SortOrder.Boss + 6, 6f);
            var sl = new GameObject("sunLight");
            sl.transform.SetParent(sun, false);
            sunLight = sl.AddComponent<Light2D>();
            sunLight.lightType = Light2D.LightType.Point;
            sunLight.color = Palette.Amber;
            sunLight.intensity = 0f;
            sunLight.pointLightOuterRadius = 30f;
            SetSun(0f);

            // the corona he burns inside once the ash takes the body back
            var ago = new GameObject("aura");
            ago.transform.SetParent(transform, false);
            ago.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            aura = ago.transform;
            auraGlow = Additive(aura, "fx_glow", Palette.Red, SortOrder.Boss - 4, 8.5f);
            auraRing2 = Additive(aura, "fx_ring", Palette.Red, SortOrder.Boss - 3, 6.6f);
            auraRing = Additive(aura, "fx_ring", Palette.Amber, SortOrder.Boss - 2, 4.8f);
            var al = new GameObject("auraLight");
            al.transform.SetParent(aura, false);
            auraLight = al.AddComponent<Light2D>();
            auraLight.lightType = Light2D.LightType.Point;
            auraLight.color = Palette.Red;
            auraLight.intensity = 0f;
            auraLight.pointLightOuterRadius = 11f;
            SetAura(0f);
            return rig;
        }

        static SpriteRenderer Additive(Transform parent, string sprite, Color color, int sort, float scale)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.material = MaterialLibrary.Additive;
            sr.color = color.WithAlpha(0f);
            sr.sortingOrder = sort;
            return sr;
        }

        /// <summary>0 = gone, 1 = a small sun burning over his head.</summary>
        void SetSun(float grow)
        {
            float g = Mathf.Clamp01(grow);
            if (sun != null) sun.localScale = Vector3.one * (0.2f + 1.5f * g);
            if (sunGlow != null) sunGlow.color = Palette.Amber.WithAlpha(0.8f * g);
            if (sunRing2 != null) sunRing2.color = Palette.Red.WithAlpha(0.5f * g);
            if (sunRing != null) sunRing.color = Palette.Gold.WithAlpha(0.65f * g);
            if (sunBody != null) sunBody.color = Color.Lerp(Palette.Gold, Color.white, g).WithAlpha(0.95f * g);
            if (sunLight != null) sunLight.intensity = 9f * g;
        }

        /// <summary>0 = a man in armour, 1 = a man standing inside a small fire.</summary>
        void SetAura(float amount)
        {
            auraAmount = Mathf.Clamp01(amount);
            if (auraGlow != null) auraGlow.color = Palette.Red.WithAlpha(0.8f * auraAmount);
            if (auraRing != null) auraRing.color = Palette.Amber.WithAlpha(0.26f * auraAmount);
            if (auraRing2 != null) auraRing2.color = Palette.Red.WithAlpha(0.2f * auraAmount);
            if (auraLight != null) auraLight.intensity = 2.6f * auraAmount;
        }

        static SpriteRenderer Horn(Transform parent, Vector2 pos, float angle)
        {
            var go = new GameObject("horn");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("boss_horn");
            sr.sortingOrder = SortOrder.Boss + 2;
            return sr;
        }

        /// <summary>Rig renderers plus the horns, which are not part of the rig's own array.</summary>
        SpriteRenderer[] AllRenderers()
        {
            var baseR = Renderers;
            var list = new System.Collections.Generic.List<SpriteRenderer>(baseR.Length + 2);
            for (int i = 0; i < baseR.Length; i++) if (baseR[i] != null) list.Add(baseR[i]);
            if (hornL != null && hornL.enabled && hornL.transform.localScale.x > 0.01f) list.Add(hornL);
            if (hornR != null && hornR.enabled && hornR.transform.localScale.x > 0.01f) list.Add(hornR);
            return list.ToArray();
        }

        void SetHorns(float grow)
        {
            float g = Mathf.Clamp01(grow);
            if (hornL != null) hornL.transform.localScale = new Vector3(g, g, 1f);
            if (hornR != null) hornR.transform.localScale = new Vector3(g, g, 1f);
            if (hornLight != null) { hornLight.intensity = 5.2f * g; hornLight.pointLightOuterRadius = 5f + 2.5f * g; }
        }

        protected override void OnReset()
        {
            Phase = 1; teleMul = 1f;
            IsFightActive = false; CurrentAttack = ""; invulnerable = false; phasePending = false; dying = false;
            lastAttack = "";
            if (cape != null) { cape.Reset(); cape.SetVisible(true); }
            if (core != null) { core.enabled = true; coreLight.enabled = true; }
            transform.localScale = Vector3.one;
            SetHorns(0f);
            SetSun(0f);
            SetAura(0f);
            solarPending = false; solarCd = 0f;
            if (hornL != null) hornL.enabled = true;
            if (hornR != null) hornR.enabled = true;
            if (hornLight != null) hornLight.enabled = true;
            if (Rig != null)
            {
                foreach (var r in Renderers) if (r != null) r.color = Color.white;
                Rig.Cfg.LightColor = Palette.Red;
                Rig.Cfg.LightIntensity = 0.9f;
                if (cape != null) cape.Wind = 0.12f;
                Rig.SetPose(true, 25f, 180f, -14f); // kneeling guardian before the fight
            }
            GroundShockwave.ClearAll();
            GameEvents.RaiseBossHealthChanged(1f, 0f, false);
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
            if (solarCd > 0f) solarCd -= dt;
            if (sun != null) sun.localRotation = Quaternion.Euler(0f, 0f, Time.time * 34f);
            if (sunRing != null) sunRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * -78f);
            if (sunRing2 != null) sunRing2.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 46f);
            if (auraAmount > 0.001f) TickAura(dt);
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

        /// <summary>The second form never stops burning: a pulsing corona, drifting embers, and a smear
        /// of afterimages whenever he moves fast enough to leave one.</summary>
        void TickAura(float dt)
        {
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.time * 5.2f);
            if (auraGlow != null) auraGlow.color = Palette.Red.WithAlpha(0.8f * auraAmount * pulse);
            if (auraRing != null)
            {
                auraRing.color = Palette.Amber.WithAlpha(0.26f * auraAmount * pulse);
                auraRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 27f);
            }
            if (auraRing2 != null)
            {
                auraRing2.color = Palette.Red.WithAlpha(0.2f * auraAmount * (1.8f - pulse));
                auraRing2.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * -18f);
            }
            if (aura != null) aura.localScale = Vector3.one * (auraAmount * (1f + 0.07f * Mathf.Sin(Time.time * 3.3f)));
            if (auraLight != null) auraLight.intensity = 2.6f * auraAmount * pulse;

            auraEmber -= dt;
            if (auraEmber <= 0f)
            {
                auraEmber = 0.2f;
                Services.Vfx.Embers(Center + new Vector2(UnityEngine.Random.Range(-0.9f, 0.9f), UnityEngine.Random.Range(-0.8f, 1.4f)), 3, Palette.Amber);
            }
            if (Mathf.Abs(Body.linearVelocity.x) > 5f)
            {
                auraTrail -= dt;
                if (auraTrail <= 0f)
                {
                    auraTrail = 0.06f;
                    Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.35f), 0.28f);
                }
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
            GameEvents.RaiseBossHealthChanged(Mathf.Clamp01((float)Hp / MaxHp), Posture01, PostureBroken);
            if (Phase == 1 && Hp > 0 && Hp <= MaxHp * BossTuning.Phase2Threshold) phasePending = true;
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
                healthBar.Show();
                AddPosture(PostureOnParry);
                if (!PostureBroken) { Rig.Flash(Color.white, 0.12f); Rig.Punch(0.92f, 1.08f); }
            }
            else if (outcome == HitOutcome.Blocked)
            {
                AddPosture(MaxPosture * AshenSol.Enemies.EnemyTuning.PostureFromBlock);
                Rig.Flash(Palette.Amber, 0.1f);
            }
        }

        protected override void BreakPosture()
        {
            if (!IsAlive || PostureBroken) return;
            CurrentAttack = "";
            Body.linearVelocity = Vector2.zero;
            Body.gravityScale = 1f;
            base.BreakPosture();
            Services.Audio.PlaySfxAt("boss_stagger", Center, 1f);
            Services.Vfx.Shockwave(transform.position, 5f, Palette.Gold);
            Services.Vfx.ScreenFlash(Palette.Gold.WithAlpha(0.18f), 0.2f);
            Services.Cam.Shake(0.5f);
            Rig.SetPose(true, 40f, 180f, -22f);   // dropped to one knee
        }

        protected override void RecoverPosture()
        {
            bool was = PostureBroken;
            base.RecoverPosture();
            if (was && IsAlive) Rig.SetPose(false);
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
            GameEvents.RaiseBossHealthChanged(0f, 0f, false);
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
            Services.Vfx.Dissolve(AllRenderers(), Center, Palette.Red);
            Services.Vfx.Embers(Center, 80, Palette.Gold);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Ink, 30);
            Services.Vfx.Shockwave(transform.position, 7f, Palette.Red);
            Services.Cam.Shake(0.8f);
            Rig.SetVisible(false);
            if (cape != null) cape.SetVisible(false);
            core.enabled = false; coreLight.enabled = false;
            // the horns live outside the rig, so hide them explicitly or they hang in the air
            SetHorns(0f);
            SetSun(0f);
            SetAura(0f);
            if (hornL != null) hornL.enabled = false;
            if (hornR != null) hornR.enabled = false;
            if (hornLight != null) hornLight.enabled = false;
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
                if (solarPending)
                {
                    solarPending = false;
                    yield return SolarCollapse();
                    continue;
                }

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
                    case "Gore": yield return GoreCharge(); break;
                    case "RedThrust": yield return RedThrust(); break;
                    case "Solar": yield return SolarCollapse(); break;
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
            Add("Gore", Phase >= 2 ? 2.6f : 0f);
            Add("Bolts", d > 5f ? 3f : 1f);
            if (Phase >= 2)
            {
                Add("Whirl", d <= 4.5f ? 3f : 0f);
                Add("RedThrust", d > 3f ? 2f : 0f);
                Add("Solar", solarCd <= 0f ? 2.2f : 0f);
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

        /// <summary>The second form, played as a kill that does not take: the bar empties and hides, the
        /// Warden goes down, and then the ash inside him takes the body back under a new name with a
        /// fresh bar. Unscaled time throughout so the slow-motion does not stretch the beats.</summary>
        bool cineSkipped; float cineArm;

        /// <summary>
        /// Cutscene wait. Skipping takes a deliberate press of E: the transition starts mid-fight with
        /// the player's hands on attack, parry and jump, and anything looser than this skips the whole
        /// scene on the first stray key before a word of it is heard.
        /// </summary>
        IEnumerator Cine(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                var inp = Services.Input;
                if (!cineSkipped && inp != null && Time.unscaledTime > cineArm && inp.InteractPressed)
                {
                    cineSkipped = true;
                    Services.Audio.StopVoice();
                    GameEvents.RaiseLog("phase cutscene skipped");
                }
                if (cineSkipped) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator PhaseTransition()
        {
            phasePending = false;
            cineSkipped = false;
            cineArm = Time.unscaledTime + 1.5f;   // the opening beat is never skippable
            // flip the phase immediately: posture regen keeps firing OnHealthChanged during the
            // transition, which would re-arm phasePending and run the whole thing a second time
            Phase = 2;
            CurrentAttack = "Phase";
            invulnerable = true;
            Move(0f);
            Body.linearVelocity = Vector2.zero;
            GroundShockwave.ClearAll();
            AshenSol.Enemies.Projectile.ClearAll();

            var player = AshenSol.Player.PlayerController.Instance;
            if (player != null) player.SetControlEnabled(false);
            Services.Ui.SetLetterbox(1f, 0.6f);
            Services.Ui.ShowSkipHint(true);
            Services.Cam.Focus(Center + new Vector2(0f, 0.8f), 0.9f);
            Services.Cam.SetZoom(4.6f, 1.2f);

            // --- 1. the killing blow that does not land
            TimeController.Instance.SlowMo(0.2f, 1.8f);
            Services.Cam.Shake(0.7f);
            HitStop.Request(0.12f);
            Services.Audio.PlaySfxAt("boss_stagger", Center, 1f);
            Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.45f), 0.4f);
            Services.Vfx.FlashLight(Center, Color.white, 4f, 9f, 0.5f);
            Rig.SetPose(true, 35f, 180f, -24f);          // down on one knee
            Rig.Flash(Color.white, 0.5f);
            Services.Audio.PlayMusic("music_transform", 0.5f);   // the cue builds with the scene
            Services.Audio.SetMusicDuck(1f, 0.4f);
            yield return Cine(1.3f);

            // --- 2. the bar drains and the fight looks over
            Services.Ui.UpdateBossBar(0f, 0f, false);
            Services.Ui.HideBossBar();
            Services.Vfx.Embers(Center, 14, Palette.Bone);
            yield return Cine(1.1f);

            // --- 3. he recognises the forms, not the student
            Services.Audio.SetMusicDuck(0.4f, 0.4f);
            float l1 = Services.Audio.PlayVoice("warden_1", 1f);
            Services.Ui.ShowSubtitle("\u201cYou still hold the blade\u2026 the way I taught you.\u201d");
            yield return Cine(Mathf.Max(l1, 3.5f) + 0.5f);
            Services.Ui.ShowSubtitle(null);
            Services.Audio.SetMusicDuck(1f, 0.5f);

            // --- 4. the ash refuses to be finished
            Services.Audio.PlaySfxAt("posture_break", Center, 1f);
            Services.Vfx.ChromaticPulse(1f, 1.2f);
            if (core != null) core.color = Color.white;
            Services.Cam.Shake(0.35f);
            yield return Cine(1.2f);

            // --- 5. the horns come through the mask
            Services.Audio.PlaySfxAt("boss_slam", Center, 0.8f);
            float g = 0f;
            while (g < 1f && !cineSkipped)
            {
                g += Time.unscaledDeltaTime / 2.2f;
                SetHorns(Ease.OutCubic(Mathf.Clamp01(g)));
                if (Mathf.Repeat(g * 2.2f, 0.28f) < Time.unscaledDeltaTime)
                {
                    Services.Vfx.HitSpark(Center + new Vector2(0f, 2.4f), Vector2.up, Palette.Red, 1.1f);
                    Services.Cam.Shake(0.14f + 0.3f * g);
                    Services.Vfx.Embers(Center + new Vector2(UnityEngine.Random.Range(-3f, 3f), 0f), 8, Palette.Red);
                    Services.Vfx.ChromaticPulse(0.35f * g, 0.25f);
                    Services.Audio.PlaySfxAt("enemy_telegraph_red", Center, 0.5f, 0.2f);
                }
                yield return null;
            }
            SetHorns(1f);
            Services.Vfx.FlashLight(Center + new Vector2(0f, 2.2f), Palette.Red, 4f, 7f, 0.6f);
            Services.Vfx.Embers(Center + new Vector2(0f, 2f), 40, Palette.Red);
            yield return Cine(0.5f);

            // --- 6. he stands, and answers
            Rig.SetPose(false);
            Rig.Punch(0.85f, 1.25f);
            ApplySecondForm();
            Services.Audio.SetMusicDuck(0.45f, 0.3f);
            float l2 = Services.Audio.PlayVoice("warden_2", 1f);
            Services.Ui.ShowSubtitle("\u201cNow hold it\u2026 against what the sun left in me.\u201d");
            Services.Cam.SetZoom(5.4f, 1.5f);
            yield return Cine(Mathf.Max(l2, 3.5f) + 0.3f);
            Services.Ui.ShowSubtitle(null);
            Services.Audio.SetMusicDuck(1f, 0.3f);

            // --- 7. the roar
            Services.Audio.PlaySfxAt("boss_roar", Center, 1f);
            Services.Audio.PlaySfxAt("boss_phase2", Center, 1f);
            Services.Vfx.Shockwave(transform.position, 10f, Palette.Red);
            Services.Vfx.Shockwave(transform.position, 20f, Palette.Amber);
            Services.Vfx.ScreenFlash(Palette.Red.WithAlpha(0.55f), 0.6f);
            Services.Vfx.FlashLight(Center, Palette.Red, 8f, 20f, 1.2f);
            Services.Vfx.Embers(Center, 140, Palette.Red);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Red, 26);
            Services.Vfx.ChromaticPulse(1f, 1.6f);
            GroundShockwave.Spawn((Vector2)transform.position + new Vector2(2.5f, 0f), 1, 12f, 0, 1.6f, transform.parent);
            GroundShockwave.Spawn((Vector2)transform.position + new Vector2(-2.5f, 0f), -1, 12f, 0, 1.6f, transform.parent);
            Services.Cam.Shake(1f);
            Services.Cam.Kick(Vector2.up, 0.4f);
            Services.Ui.ShowNameCard(SecondFormName, SecondFormSubtitle, 2.4f);
            yield return Cine(1.6f);

            // --- 8. back to the fight
            SetHorns(1f);
            ApplySecondForm();
            Rig.SetPose(false);
            Services.Ui.ShowSubtitle(null);
            Services.Ui.ShowSkipHint(false);
            Services.Ui.SetLetterbox(0f, 0.5f);
            Services.Ui.ShowBossBar(SecondFormName, SecondFormSubtitle);
            Services.Audio.PlayMusic("music_boss", 0.6f);
            Services.Audio.SetMusicDuck(1f, 0.6f);
            Services.Cam.ReleaseFocus(0.7f);
            Services.Cam.SetZoom(6.8f, 1f);
            if (player != null && player.IsAlive) player.SetControlEnabled(true);
            GameEvents.RaiseLog("warden second form: " + SecondFormName);
            OnHealthChanged();
            yield return new WaitForSecondsRealtime(0.4f);

            teleMul = BossTuning.Phase2TelegraphMul;
            invulnerable = false;
            GameEvents.RaiseBossPhaseChanged(2);
            CurrentAttack = "";
            solarPending = true;   // the new form opens with the sun
        }

        /// <summary>Ash-lit second form: hotter palette, brighter core, slightly larger silhouette.</summary>
        void ApplySecondForm()
        {
            var hot = new Color(0.98f, 0.55f, 0.46f);   // charred iron catching its own firelight
            foreach (var r in Renderers) if (r != null) r.color = hot;
            if (core != null) core.color = Palette.Red;
            if (coreLight != null) { coreLight.intensity = 3.6f; coreLight.pointLightOuterRadius = 6.5f; }
            if (Rig.Light != null) { Rig.Light.color = Palette.Red; Rig.Light.intensity = 2.8f; Rig.Light.pointLightOuterRadius = 7f; }
            Rig.Cfg.LightColor = Palette.Red;
            Rig.Cfg.LightIntensity = 2.4f;
            transform.localScale = Vector3.one * 1.24f;
            if (cape != null) cape.Wind = 0.5f;
            SetAura(1f);
            Services.Vfx.Embers(Center, 40, Palette.Red);
        }

        IEnumerator TripleSlash()
        {
            CurrentAttack = "TripleSlash";
            for (int i = 0; i < 3; i++)
            {
                FacePlayer();
                Move(0f);
                BeginTelegraph(AttackKind.Parryable, BossTuning.SlashTelegraph * Tele);
                yield return Wait(BossTuning.SlashTelegraph * Tele);
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
            BeginTelegraph(AttackKind.Parryable, BossTuning.DashTelegraph * Tele);
            Rig.SetPose(true, -45f, 180f, 18f);
            yield return Wait(BossTuning.DashTelegraph * Tele);
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
            float tele = BossTuning.SlamTelegraph * Tele;
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
            BeginTelegraph(AttackKind.Parryable, BossTuning.BoltsTelegraph * Tele);
            Rig.SetPose(true, -100f, 180f, 5f);
            yield return Wait(BossTuning.BoltsTelegraph * Tele);
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
            BeginTelegraph(AttackKind.Parryable, BossTuning.WhirlTelegraph * Tele);
            yield return Wait(BossTuning.WhirlTelegraph * Tele);
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
            BeginTelegraph(AttackKind.Unblockable, BossTuning.ThrustTelegraph * Tele);
            Rig.SetPose(true, -35f, 180f, 14f);
            yield return Wait(BossTuning.ThrustTelegraph * Tele);
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

        /// <summary>
        /// SOLAR COLLAPSE. He plants the glaive, drags a small sun out of the horns and drops it on the
        /// arena. There is no outrunning it — the blast fills the room. It is parryable, and if it is
        /// not parried it takes three quarters of the health pool, so it is the attack the fight is about.
        /// </summary>
        IEnumerator SolarCollapse()
        {
            CurrentAttack = "Solar";
            solarCd = BossTuning.SolarCooldown;
            FacePlayer();
            Move(0f);
            Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);

            // --- 1. he plants and reaches up; the white telegraph says this one can be met
            float tel = BossTuning.SolarTelegraph * Tele;
            BeginTelegraph(AttackKind.Parryable, tel);
            float charge = tel * Settings.TelegraphMul;
            Rig.SetPose(true, -105f, 180f, -105f);   // both arms up, blade overhead
            Services.Audio.PlaySfxAt("solar_charge", Center, 1f, 0.02f);
            Services.Ui.ShowPrompt("SOLAR COLLAPSE", 1.6f);

            Vector2 sunAt = sun != null ? (Vector2)sun.position : Center + new Vector2(0f, 5.2f);
            float t = 0f, ember = 0f, shake = 0f;
            while (t < charge)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, charge));
                SetSun(Ease.InCubic(k) * 0.9f + 0.1f * k);
                sunAt = sun != null ? (Vector2)sun.position : Center + new Vector2(0f, 5.2f);
                Services.Vfx.FlashLight(sunAt, Palette.Amber, 2f * k, 9f + 16f * k, 0.12f);

                ember -= Time.deltaTime;
                if (ember <= 0f)
                {
                    ember = 0.07f;
                    float a = UnityEngine.Random.value * Mathf.PI * 2f, r = 9f - 6f * k;
                    Services.Vfx.HitSpark(sunAt + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.6f),
                                          -new Vector2(Mathf.Cos(a), Mathf.Sin(a)), Palette.Gold, 0.6f + 0.6f * k);
                }
                shake -= Time.deltaTime;
                if (shake <= 0f) { shake = 0.12f; Services.Cam.Shake(0.05f + 0.18f * k); }
                if (k > 0.5f) Services.Vfx.ChromaticPulse((k - 0.5f) * 1.6f, 0.15f);

                t += Time.deltaTime;
                yield return null;
            }
            EndTelegraph();

            // --- 2. the sun folds inward: the one beat you parry on
            Services.Vfx.ScreenFlash(Palette.Gold.WithAlpha(0.35f), 0.18f);
            float c = 0f;
            while (c < 0.14f)
            {
                SetSun(1f - c / 0.14f * 0.75f);
                c += Time.deltaTime;
                yield return null;
            }
            SetSun(0f);

            // --- 3. it goes off, and the whole arena is inside it
            var pc = AshenSol.Player.PlayerController.Instance;
            int dmg = pc != null
                ? Mathf.Max(1, Mathf.RoundToInt(pc.MaxHp * BossTuning.SolarDamageFraction))
                : 75;
            var info = new AttackInfo
            {
                Damage = dmg, Knockback = 15f, Kind = AttackKind.Parryable, PierceGuard = true,
                Tag = "boss_solar", Origin = Center, HitPoint = pc != null ? pc.Center : Center
            };

            Services.Audio.PlaySfxAt("solar_burst", Center, 1f, 0.02f);
            Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.6f), 0.4f);
            Services.Vfx.FlashLight(sunAt, Color.white, 12f, 34f, 0.9f);
            Services.Vfx.ChromaticPulse(1f, 1.4f);
            Services.Vfx.Embers(sunAt, 140, Palette.Gold);
            Services.Vfx.InkSplatter(sunAt, Vector2.up, Palette.Amber, 30);
            Services.Cam.Shake(1f);
            Services.Cam.Kick(Vector2.up, 0.5f);
            HitStop.Request(0.12f);
            if (TimeController.Instance != null) TimeController.Instance.SlowMo(0.4f, 0.5f);
            GameEvents.RaiseLog("solar collapse (" + dmg + " dmg)");

            GroundShockwave.Spawn((Vector2)transform.position + new Vector2(2f, 0f), 1, 13f, BossTuning.ShockwaveDamage, 1.8f, transform.parent);
            GroundShockwave.Spawn((Vector2)transform.position + new Vector2(-2f, 0f), -1, 13f, BossTuning.ShockwaveDamage, 1.8f, transform.parent);

            // the front: it is drawn every frame and it takes real time to arrive, so it can be
            // read and met. The hit lands the moment the ring touches you, not on the flash.
            var waveGo = new GameObject("solar_wave");
            waveGo.transform.SetParent(transform.parent, false);
            waveGo.transform.position = sunAt;
            var waveRing = Additive(waveGo.transform, "fx_shockwave", Color.white, SortOrder.Boss + 9, 0.4f);
            var waveHalo = Additive(waveGo.transform, "fx_ring", Palette.Amber, SortOrder.Boss + 8, 0.4f);

            bool struck = false;
            float radius = 1.2f, edge = 0f;
            while (radius < BossTuning.SolarWaveRadius)
            {
                radius += BossTuning.SolarWaveSpeed * Time.deltaTime;
                float k = Mathf.Clamp01(radius / BossTuning.SolarWaveRadius);
                float bright = 1f - k * 0.6f;
                waveRing.transform.localScale = Vector3.one * (radius * 0.85f);
                waveHalo.transform.localScale = Vector3.one * (radius * 1.05f);
                waveRing.color = Color.Lerp(Color.white, Palette.Gold, k).WithAlpha(0.95f * bright);
                waveHalo.color = Palette.Amber.WithAlpha(0.55f * bright);

                edge -= Time.deltaTime;
                if (edge <= 0f)
                {
                    edge = 0.05f;
                    float a = UnityEngine.Random.Range(0.15f, Mathf.PI - 0.15f);
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Services.Vfx.HitSpark(sunAt + dir * radius, dir, Palette.Gold, 0.7f * bright);
                    Services.Vfx.FlashLight(sunAt, Palette.Amber, 3f * bright, radius * 1.2f, 0.1f);
                }

                if (!struck && pc != null && pc.IsAlive && Vector2.Distance(pc.Center, sunAt) <= radius)
                {
                    struck = true;
                    info.HitPoint = pc.Center;
                    StrikePlayer(info, pc.Center, new Vector2(2.4f, 2.8f));
                    Services.Cam.Shake(0.55f);
                    Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.25f), 0.15f);
                }
                yield return null;
            }
            Destroy(waveGo);

            // --- 4. it costs him: a long opening while the horns cool
            Rig.SetPose(true, 30f, 180f, -18f);
            yield return Wait(BossTuning.SolarRecovery * 0.5f);
            Rig.SetPose(false);
            yield return Wait(BossTuning.SolarRecovery * 0.3f);
        }

        /// <summary>Phase two only: he puts his head down and runs the arena with the new horns.</summary>
        IEnumerator GoreCharge()
        {
            CurrentAttack = "Gore";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Unblockable, BossTuning.GoreTelegraph * Tele);
            Rig.SetPose(true, -20f, 180f, 28f);        // head down, horns forward
            yield return Wait(BossTuning.GoreTelegraph * Tele);
            EndTelegraph();
            FacePlayer();
            Services.Audio.PlaySfxAt("boss_dash", Center, 1f, 0.04f);
            Services.Vfx.DustPuff(transform.position, 2f);

            float t = 0f, ai = 0f; bool resolved = false;
            while (t < BossTuning.GoreDuration)
            {
                if (!WallAhead(Facing, 1.4f))
                    Body.linearVelocity = new Vector2(Facing * BossTuning.GoreSpeed, Body.linearVelocity.y);
                else break;
                if (!resolved)
                {
                    var info = new AttackInfo
                    {
                        Damage = BossTuning.GoreDamage, Knockback = 11f,
                        Kind = AttackKind.Unblockable, Tag = "boss_gore", Origin = Center
                    };
                    var o = StrikePlayer(info, Center + new Vector2(Facing * 1.6f, 0.2f), new Vector2(2.6f, 2.4f));
                    if (o != HitOutcome.Ignored) resolved = true;
                }
                ai -= Time.deltaTime;
                if (ai <= 0f)
                {
                    ai = 0.045f;
                    Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.5f), 0.25f);
                    Services.Vfx.HitSpark(Center + new Vector2(0f, 1.8f), Vector2.up, Palette.Red, 0.5f);
                }
                t += Time.deltaTime;
                yield return null;
            }
            // he slams into the wall and the impact rolls back out
            Move(0f);
            Vector2 at = new Vector2(transform.position.x, transform.position.y);
            Services.Vfx.Shockwave(at, 5f, Palette.Red);
            Services.Cam.Shake(0.8f);
            HitStop.Request(0.06f);
            Services.Audio.PlaySfxAt("boss_slam", at, 1f);
            GroundShockwave.Spawn(at + new Vector2(-Facing * 1.4f, 0f), -Facing, 10f, BossTuning.ShockwaveDamage, 1.5f, transform.parent);
            Rig.SetPose(false);
            yield return Wait(BossTuning.GoreRecovery);
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
