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

        protected override float CenterHeight { get { return 1.6f * transform.localScale.y; } }   // the second form is scaled up: hitboxes and bars follow
        protected override float PostureOnParry { get { return BossTuning.PostureOnParry; } }
        protected override float PostureRegen { get { return BossTuning.PostureRegen; } }
        public override int ExecuteDamage { get { return BossTuning.ExecuteDamage; } }
        protected override float KnockbackResist { get { return 1f; } }
        protected override string DeathSfx { get { return "boss_death"; } }
        public override string DisplayName { get { return BossName; } }
        public override int AshValue { get { return 260; } }

        float teleMul = 1f;
        /// <summary>Phase-2 speed-up combined with the difficulty setting.</summary>
        // Waits use the combined duration; BeginTelegraph applies difficulty internally.
        float Tele { get { return teleMul * Settings.TelegraphMul; } }
        bool invulnerable, phasePending, dying;
        string lastAttack = "";
        SashChain cape;
        SpriteRenderer core; Light2D coreLight;
        Transform halo; SpriteRenderer haloRing, haloCorona; Light2D haloLight;
        Transform sun; SpriteRenderer sunCore, sunCorona, sunBody, sunRing, sunRing2; Light2D sunLight;
        bool solarCam;
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
                TwoSegment = true, UpperArm = "boss_arm_upper", LowerArm = "boss_arm_lower", UpperLeg = "boss_leg_upper", LowerLeg = "boss_leg_lower",
                UpperArmH = 0.48f, LowerArmH = 0.44f, UpperLegH = 0.62f, LowerLegH = 0.56f, UpperArmLen = 0.42f, LowerArmLen = 0.40f, UpperLegLen = 0.54f, LowerLegLen = 0.56f,
                HipY = 1.1f, LegOffset = 0.18f, TorsoH = 1.5f, HeadY = 1.42f, Shoulder = new Vector2(0.22f, 1.22f), ArmLen = 0.82f, ArmSpriteH = 0.92f,
                LegSpriteH = 1.1f, HeadSpriteH = 0.82f, WeaponOffset = new Vector2(0f, 0.55f), WeaponVerticalIdle = true, ArmIdle = 10f,
                SortBase = SortOrder.Boss, LightColor = Palette.Red, LightIntensity = 0.9f, LightRadius = 3.2f, WeaponTipDistance = 1.8f, TrailColor = Palette.BossSlash
            };
            var rig = EnemyRig.BuildHumanoid(transform, cfg);

            // cape + glowing core
            var capeAnchor = new GameObject("capeAnchor").transform;
            capeAnchor.SetParent(rig.Torso, false);
            capeAnchor.localPosition = new Vector3(-0.32f, 1.25f, 0f);
            cape = SashChain.Build(transform, capeAnchor, 7, Palette.RedDeep, "boss_cape_seg", 0.3f, SortOrder.Boss - 5, 1.7f, 0.9f);
            cape.Gravity = 30f; cape.Wind = 1.2f; cape.Drag = 1.6f;

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

            // what the ninth sun left behind: a burning crown that ignites BEHIND the mask in the second form,
            // so the silhouette of the head reads against it. Built at zero scale; the phase change grows it.
            halo = new GameObject("halo").transform;
            halo.SetParent(rig.Head, false);
            halo.localPosition = new Vector3(-0.04f, 0.42f, 0f);
            haloCorona = AdditiveSprite(halo, CoronaSprite(), Palette.Gold, SortOrder.Boss - 6, 0.9f);
            haloRing = AdditiveSprite(halo, Res.Sprite("fx_ring"), Palette.Gold, SortOrder.Boss - 5, 1.55f);
            var hl = new GameObject("haloLight");
            hl.transform.SetParent(halo, false);
            haloLight = hl.AddComponent<Light2D>();
            haloLight.lightType = Light2D.LightType.Point;
            haloLight.color = Palette.Gold;
            haloLight.intensity = 0f;
            haloLight.pointLightOuterRadius = 6f;
            SetHalo(0f);

            // the sun he drags out of the crown in phase two; dormant until then. A hard disc for the body,
            // a soft corona, two rings and a flare, all HDR so the bloom carries them across the arena.
            var sgo = new GameObject("sun");
            sgo.transform.SetParent(transform, false);
            sgo.transform.localPosition = new Vector3(0f, 5.0f, 0f);
            sun = sgo.transform;
            sunCorona = AdditiveSprite(sun, Res.Sprite("fx_glow"), Palette.Amber.Glow(1.6f), SortOrder.Boss + 2, 6.5f);
            sunRing2 = Additive(sun, "fx_ring", Palette.Red.Glow(1.4f), SortOrder.Boss + 4, 2.4f);
            sunRing = Additive(sun, "fx_ring", Palette.Gold.Glow(1.8f), SortOrder.Boss + 5, 1.85f);
            sunCore = AdditiveSprite(sun, DiscSprite(), Color.white, SortOrder.Boss + 6, 0.56f);
            sunBody = Additive(sun, "fx_flare", Color.white.Glow(1.5f), SortOrder.Boss + 7, 1.1f);
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
            auraGlow = Additive(aura, "fx_glow", Palette.Red, SortOrder.Boss - 4, 6.5f);
            auraRing2 = Additive(aura, "fx_ring", Palette.Red, SortOrder.Boss - 3, 5.2f);
            auraRing = Additive(aura, "fx_ring", Palette.Amber, SortOrder.Boss - 2, 3.8f);
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

        /// <summary>0 = gone, 1 = a small sun burning over his head. heat whitens the core for the last
        /// stretch of the charge; beat scales the body for the heartbeat.</summary>
        void SetSun(float grow, float heat = 0f, float beat = 1f)
        {
            float g = Mathf.Clamp01(grow);
            if (sun != null) sun.localScale = Vector3.one * ((0.2f + 1.5f * g) * beat);
            if (sunRing != null) sunRing.transform.localScale = Vector3.one * 1.85f;
            if (sunRing2 != null) sunRing2.transform.localScale = Vector3.one * 2.4f;
            Color core = Color.Lerp(Color.Lerp(Palette.Gold, Color.white, 0.45f), Color.white, heat).Glow(2.2f + 1.5f * heat);
            if (sunCore != null) sunCore.color = core.WithAlpha(g);
            if (sunCorona != null) sunCorona.color = Palette.Amber.Glow(1.6f).WithAlpha(0.55f * g);
            if (sunRing2 != null) sunRing2.color = Palette.Red.Glow(1.4f).WithAlpha(0.6f * g);
            if (sunRing != null) sunRing.color = Palette.Gold.Glow(1.8f).WithAlpha(0.75f * g);
            if (sunBody != null) sunBody.color = Color.white.Glow(1.5f).WithAlpha(0.9f * g);
            if (sunLight != null) sunLight.intensity = 12f * g * (1f + 0.5f * heat);
        }

        /// <summary>The beat before the burst: everything collapses into a white-hot point. Nothing dims.</summary>
        void SetSunFold(float p)
        {
            p = Mathf.Clamp01(p);
            if (sun != null) sun.localScale = Vector3.one * Mathf.Lerp(1.7f, 0.45f, Ease.InCubic(p));
            float ringIn = 1f - 0.75f * p;
            if (sunRing != null) sunRing.transform.localScale = Vector3.one * (1.85f * ringIn);
            if (sunRing2 != null) sunRing2.transform.localScale = Vector3.one * (2.4f * ringIn);
            if (sunCore != null) sunCore.color = Color.white.Glow(2.5f + 3f * p);
            if (sunCorona != null) sunCorona.color = Color.Lerp(Palette.Amber, Color.white, p).Glow(1.6f).WithAlpha(0.55f + 0.35f * p);
            if (sunBody != null) sunBody.color = Color.white.Glow(2f);
            if (sunLight != null) sunLight.intensity = 12f + 10f * p;
        }

        /// <summary>The camera pulls back and keeps the sun and the player in one frame; the arena dims so
        /// the sun is the light. Call every frame of the charge.</summary>
        void SolarCameraFrame(Vector2 sunAt)
        {
            var pc = PlayerController.Instance;
            Vector2 focus = pc != null ? Vector2.Lerp(pc.Center, sunAt, 0.5f) : sunAt;
            if (!solarCam)
            {
                solarCam = true;
                Services.Cam.SetZoom(7.8f, 1.2f);
                var info = GameFlow.Instance != null ? GameFlow.Instance.CurrentLevelInfo : null;
                if (info != null) CameraController.SetAmbient(info.AmbientColor, info.AmbientIntensity * 0.45f);
            }
            Services.Cam.Focus(focus, 0.7f);
        }

        /// <summary>Hands the camera and the light back. Safe to call when nothing was taken.</summary>
        void ReleaseSolarCamera()
        {
            if (!solarCam) return;
            solarCam = false;
            Services.Cam.ReleaseFocus(0.8f);
            Services.Cam.SetZoom(6.8f, 1f);
            var info = GameFlow.Instance != null ? GameFlow.Instance.CurrentLevelInfo : null;
            if (info != null) CameraController.SetAmbient(info.AmbientColor, info.AmbientIntensity);
        }

        /// <summary>Hermite step from e0 to e1, the shader kind. Mathf.SmoothStep interpolates BETWEEN its first two
        /// arguments and is not this.</summary>
        static float Step01(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static Sprite discSprite;

        /// <summary>A hard-edged disc with a feathered rim, made once at runtime. The soft radial glow sprite
        /// has no edge, and an exploding sun needs one.</summary>
        static Sprite DiscSprite()
        {
            if (discSprite != null) return discSprite;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * N + x] = new Color(1f, 1f, 1f, 1f - Step01(0.84f, 1f, r));
                }
            tex.SetPixels(px); tex.Apply();
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            discSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            discSprite.name = "disc";
            return discSprite;
        }

        static SpriteRenderer AdditiveSprite(Transform parent, Sprite sprite, Color color, int sort, float scale)
        {
            var go = new GameObject(sprite != null ? sprite.name : "sprite");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.material = MaterialLibrary.Additive;
            sr.color = color.WithAlpha(0f);
            sr.sortingOrder = sort;
            return sr;
        }

        /// <summary>0 = a man in armour, 1 = a man with embers rising off him. Kept quiet on purpose: the crown
        /// is the second form's signature, the aura only warms the air around it.</summary>
        void SetAura(float amount)
        {
            auraAmount = Mathf.Clamp01(amount);
            if (auraGlow != null) auraGlow.color = Palette.Red.WithAlpha(0.3f * auraAmount);
            if (auraRing != null) auraRing.color = Palette.Amber.WithAlpha(0.1f * auraAmount);
            if (auraRing2 != null) auraRing2.color = Palette.Red.WithAlpha(0.07f * auraAmount);
            if (auraLight != null) auraLight.intensity = 1.2f * auraAmount;
        }

        static Sprite coronaSprite;

        /// <summary>A spiked ring, made once at runtime: the crown of a small sun. Twelve soft flame tips on
        /// the outside, a clean inner edge so the mask reads against it.</summary>
        static Sprite CoronaSprite()
        {
            if (coronaSprite != null) return coronaSprite;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float tips = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 6f)), 3f);
                    float tips2 = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 6f + 1.9f)), 6f) * 0.55f;
                    float outer = 0.68f + 0.30f * Mathf.Max(tips, tips2);
                    const float inner = 0.55f;
                    float a = Step01(inner - 0.04f, inner + 0.03f, r) * (1f - Step01(outer - 0.06f, outer, r));
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            coronaSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            coronaSprite.name = "corona";
            return coronaSprite;
        }

        /// <summary>Rig renderers plus the crown, which is not part of the rig's own array.</summary>
        SpriteRenderer[] AllRenderers()
        {
            var baseR = Renderers;
            var list = new System.Collections.Generic.List<SpriteRenderer>(baseR.Length + 2);
            for (int i = 0; i < baseR.Length; i++) if (baseR[i] != null) list.Add(baseR[i]);
            bool crown = halo != null && halo.localScale.x > 0.01f;
            if (crown && haloCorona != null && haloCorona.enabled) list.Add(haloCorona);
            if (crown && haloRing != null && haloRing.enabled) list.Add(haloRing);
            return list.ToArray();
        }

        /// <summary>0 = nothing, 1 = the crown burning behind the mask.</summary>
        void SetHalo(float grow)
        {
            float g = Mathf.Clamp01(grow);
            if (halo != null) halo.localScale = Vector3.one * (g < 0.001f ? 0f : Mathf.Lerp(0.3f, 1f, Ease.OutBack(g)));
            if (haloCorona != null) haloCorona.color = Palette.Gold.Glow(1.9f).WithAlpha(0.85f * g);
            if (haloRing != null) haloRing.color = Color.Lerp(Palette.Gold, Color.white, 0.35f).Glow(2.2f).WithAlpha(0.9f * g);
            if (haloLight != null) { haloLight.intensity = 3.2f * g; haloLight.pointLightOuterRadius = 4f + 3f * g; }
        }

        protected override void OnReset()
        {
            Phase = 1; teleMul = 1f;
            IsFightActive = false; CurrentAttack = ""; invulnerable = false; phasePending = false; dying = false;
            lastAttack = "";
            if (cape != null) { cape.Reset(); cape.SetVisible(true); }
            if (core != null) { core.enabled = true; coreLight.enabled = true; }
            transform.localScale = Vector3.one;
            SetHalo(0f);
            SetSun(0f);
            SetAura(0f);
            solarPending = false; solarCd = 0f;
            ReleaseSolarCamera();
            if (haloCorona != null) haloCorona.enabled = true;
            if (haloRing != null) haloRing.enabled = true;
            if (haloLight != null) haloLight.enabled = true;
            if (Rig != null)
            {
                foreach (var r in Renderers) if (r != null) r.color = Color.white;
                Rig.Cfg.LightColor = Palette.Red;
                Rig.Cfg.LightIntensity = 0.9f;
                if (cape != null) cape.Wind = 1.2f;
                Rig.SetPose(true, 25f, 180f, -14f, 60f, -30f, -130f, -60f, 40f); // kneeling guardian before the fight
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
            // -bossPhase 2 on the command line: the fight opens with the transformation, for looking at the
            // second form without playing the first
            if (Phase == 1 && CmdArgs.Get("-bossPhase") == "2")
            {
                Hp = Mathf.Max(1, Mathf.FloorToInt(MaxHp * BossTuning.Phase2Threshold) - 1);
                OnHealthChanged();
                phasePending = true;
            }
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
            if (haloCorona != null) haloCorona.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 14f);
            if (haloRing != null) haloRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * -9f);
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
            if (auraGlow != null) auraGlow.color = Palette.Red.WithAlpha(0.3f * auraAmount * pulse);
            if (auraRing != null)
            {
                auraRing.color = Palette.Amber.WithAlpha(0.1f * auraAmount * pulse);
                auraRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 27f);
            }
            if (auraRing2 != null)
            {
                auraRing2.color = Palette.Red.WithAlpha(0.07f * auraAmount * (1.8f - pulse));
                auraRing2.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * -18f);
            }
            if (aura != null) aura.localScale = Vector3.one * (auraAmount * (1f + 0.07f * Mathf.Sin(Time.time * 3.3f)));
            if (auraLight != null) auraLight.intensity = 1.2f * auraAmount * pulse;

            auraEmber -= dt;
            if (auraEmber <= 0f)
            {
                auraEmber = 0.32f;
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
            GameEvents.RaiseBossHealthChanged(Mathf.Clamp01((float)Hp / MaxHp), PostureDisplay01, PostureBroken);
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
                Rig.Recoil(0.2f);   // the glaive bounces off the parry instead of finishing its arc
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
            Rig.SetPose(true, 40f, 180f, -22f, 60f, -30f, -130f, -60f, 45f);   // down on one knee
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
            EndTelegraph(false);
            ReleaseSolarCamera();
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
            EndTelegraph(false);
            ReleaseSolarCamera();
            staggerTimer = 0f;
            Collider.enabled = false;
            Body.linearVelocity = Vector2.zero;
            Body.simulated = false;
            GroundShockwave.ClearAll();
            healthBar.Hide();
            GameEvents.RaiseBossHealthChanged(0f, 0f, false);
            Progression.AddAsh(AshValue);
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
            // the crown lives outside the rig, so hide it explicitly or it hangs in the air
            SetHalo(0f);
            SetSun(0f);
            SetAura(0f);
            if (haloCorona != null) haloCorona.enabled = false;
            if (haloRing != null) haloRing.enabled = false;
            if (haloLight != null) haloLight.enabled = false;
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
            Rig.SetPose(true, 35f, 180f, -24f, 60f, -30f, -130f, -60f, 45f);          // down on one knee
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

            // --- 5. the crown of the ninth sun ignites behind the mask
            Services.Audio.PlaySfxAt("solar_charge", Center, 0.7f);
            float g = 0f;
            while (g < 1f && !cineSkipped)
            {
                g += Time.unscaledDeltaTime / 2.2f;
                SetHalo(Ease.OutCubic(Mathf.Clamp01(g)));
                if (Mathf.Repeat(g * 2.2f, 0.28f) < Time.unscaledDeltaTime)
                {
                    Services.Vfx.HitSpark(Center + new Vector2(0f, 2.4f), Vector2.up, Palette.Gold, 1.1f);
                    Services.Cam.Shake(0.14f + 0.3f * g);
                    Services.Vfx.Embers(Center + new Vector2(UnityEngine.Random.Range(-3f, 3f), 0f), 8, Palette.Red);
                    Services.Vfx.ChromaticPulse(0.35f * g, 0.25f);
                    Services.Audio.PlaySfxAt("enemy_telegraph_red", Center, 0.5f, 0.2f);
                }
                yield return null;
            }
            SetHalo(1f);
            Services.Vfx.FlashLight(Center + new Vector2(0f, 2.2f), Palette.Gold, 4f, 7f, 0.6f);
            Services.Vfx.Embers(Center + new Vector2(0f, 2f), 40, Palette.Gold);
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
            SetHalo(1f);
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
            if (coreLight != null) { coreLight.intensity = 2.4f; coreLight.pointLightOuterRadius = 5f; }
            if (Rig.Light != null) { Rig.Light.color = Palette.Red; Rig.Light.intensity = 1.8f; Rig.Light.pointLightOuterRadius = 5f; }
            Rig.Cfg.LightColor = Palette.Red;
            Rig.Cfg.LightIntensity = 2.4f;
            transform.localScale = Vector3.one * 1.24f;
            if (cape != null) cape.Wind = 4f;               // burning gusts
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
                BeginTelegraph(AttackKind.Parryable, BossTuning.SlashTelegraph * teleMul);
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
            BeginTelegraph(AttackKind.Parryable, BossTuning.DashTelegraph * teleMul);
            Rig.SetPose(true, -45f, 180f, 18f, 50f, 50f, -50f, -50f, 60f);   // crouched, coiled to spring
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
            BeginTelegraph(AttackKind.Unblockable, BossTuning.SlamTelegraph * teleMul);
            Rig.SetPose(true, -150f, 180f, -8f, -34f, 28f, -50f, -70f, 30f);   // legs tucked for the leap
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
            Rig.SetPose(true, 60f, 180f, -20f, 26f, -30f, -20f, -20f, 20f);   // legs reaching for the floor
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
            Rig.Punch(1.3f, 0.7f);                            // the mass arrives
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
            Rig.SetPose(true, 30f, 180f, -18f, 60f, -30f, -130f, -60f, 40f); // kneel: punish window
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
            BeginTelegraph(AttackKind.Parryable, BossTuning.WhirlTelegraph * teleMul);
            yield return Wait(BossTuning.WhirlTelegraph * Tele);
            EndTelegraph();
            Services.Audio.PlaySfxAt("boss_whirl", Center, 1f);
            Rig.Spin(BossTuning.WhirlHits * BossTuning.WhirlInterval, 1300f);   // one continuous turn, not four restarted swings
            for (int h = 0; h < BossTuning.WhirlHits; h++)
            {
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
            Rig.SetPose(true, -35f, 180f, 14f, 40f, -30f, -30f, -10f, 50f);   // set to lunge
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
        /// SOLAR COLLAPSE. He plants the glaive, drags a small sun out of the crown and drops it on the
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

            // --- 1. he plants and reaches up; the white telegraph says this one can be met. The camera pulls
            // back so the sun and the player share the frame, and the arena dims: the sun is the light now.
            float tel = BossTuning.SolarTelegraph * teleMul;
            BeginTelegraph(AttackKind.Parryable, tel);
            float charge = tel * Settings.TelegraphMul;
            Rig.SetPose(true, -105f, 180f, -105f, 12f, -12f, -10f, -10f, 10f);   // both arms up, blade overhead, feet planted
            Services.Audio.PlaySfxAt("solar_charge", Center, 1f, 0.02f);
            Services.Ui.ShowPrompt("SOLAR COLLAPSE", 1.6f);
            GameEvents.RaiseLog("solar charge");

            Vector2 sunAt = sun != null ? (Vector2)sun.position : Center + new Vector2(0f, 5.2f);
            float t = 0f, ember = 0f, shake = 0f;
            while (t < charge)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, charge));
                float heat = Mathf.Clamp01((k - 0.7f) / 0.3f);                  // the last stretch goes white
                float beat = 1f + 0.06f * Mathf.Sin(t * (5f + 22f * k * k));     // a heartbeat that races
                SetSun(Ease.InCubic(k) * 0.9f + 0.1f * k, heat, beat);
                sunAt = sun != null ? (Vector2)sun.position : Center + new Vector2(0f, 5.2f);
                SolarCameraFrame(sunAt);
                Services.Vfx.FlashLight(sunAt, Palette.Amber, 2f * k, 9f + 16f * k, 0.12f);

                ember -= Time.deltaTime;
                if (ember <= 0f)
                {
                    ember = 0.06f;
                    float a = UnityEngine.Random.value * Mathf.PI * 2f, r = 9f - 6f * k;
                    Services.Vfx.HitSpark(sunAt + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.6f),
                                          -new Vector2(Mathf.Cos(a), Mathf.Sin(a)), Palette.Gold, 0.7f + 0.7f * k);
                }
                shake -= Time.deltaTime;
                if (shake <= 0f) { shake = 0.12f; Services.Cam.Shake(0.05f + 0.18f * k); }
                if (k > 0.5f) Services.Vfx.ChromaticPulse((k - 0.5f) * 1.6f, 0.15f);

                t += Time.deltaTime;
                yield return null;
            }
            EndTelegraph();

            // --- 2. the sun folds into a white point. Nothing fades: it concentrates, and the world darkens
            // for a breath around it. That is the beat.
            Services.Vfx.ScreenFlash(Palette.Ink.WithAlpha(0.5f), 0.16f);
            float c = 0f;
            while (c < 0.16f)
            {
                SetSunFold(c / 0.16f);
                c += Time.deltaTime;
                yield return null;
            }
            SetSun(0f);

            // --- 3. it goes off: a sphere of light grows out of the point and fills the arena
            var pc = PlayerController.Instance;
            int dmg = pc != null
                ? Mathf.Max(1, Mathf.RoundToInt(pc.MaxHp * BossTuning.SolarDamageFraction))
                : 75;
            var info = new AttackInfo
            {
                Damage = dmg, Knockback = 15f, Kind = AttackKind.Parryable, PierceGuard = true,
                Tag = "boss_solar", Origin = Center, HitPoint = pc != null ? pc.Center : Center
            };

            Services.Audio.PlaySfxAt("solar_burst", Center, 1f, 0.02f);
            Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.45f), 0.18f);
            Services.Vfx.FlashLight(sunAt, Color.white, 14f, 36f, 0.9f);
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

            // The front is a filled sphere with a hot rim, drawn behind the fighters so they stay readable as
            // silhouettes inside it, and it takes real time to arrive. The hit lands when the rim touches you,
            // not on the flash.
            var waveGo = new GameObject("solar_wave");
            waveGo.transform.SetParent(transform.parent, false);
            waveGo.transform.position = sunAt;
            var waveHalo = AdditiveSprite(waveGo.transform, Res.Sprite("fx_glow"), Palette.Amber, SortOrder.EnemyBack - 5, 1f);
            var waveDisc = AdditiveSprite(waveGo.transform, DiscSprite(), Color.white, SortOrder.EnemyBack - 4, 1f);
            var waveRimIn = Additive(waveGo.transform, "fx_ring", Color.white, SortOrder.Fx + 8, 1f);
            var waveRim = Additive(waveGo.transform, "fx_ring", Palette.Gold, SortOrder.Fx + 9, 1f);
            var groundGo = new GameObject("solar_ground");
            groundGo.transform.SetParent(transform.parent, false);
            groundGo.transform.position = new Vector3(sunAt.x, transform.position.y + 0.12f, 0f);
            var groundRing = Additive(groundGo.transform, "fx_shockwave", Palette.Amber, SortOrder.GroundDecor + 3, 1f);

            bool struck = false;
            float radius = 1.2f, edge = 0f;
            while (radius < BossTuning.SolarWaveRadius)
            {
                radius += BossTuning.SolarWaveSpeed * Time.deltaTime;
                float k = Mathf.Clamp01(radius / BossTuning.SolarWaveRadius);
                float bright = 1f - k * 0.6f;
                // sprite sizes at scale 1: disc 2.56 u, ring 1.28 u, glow 0.64 u, ground ring 2.56 u wide
                waveDisc.transform.localScale = Vector3.one * (radius / 1.28f);
                waveHalo.transform.localScale = Vector3.one * (radius / 0.64f * 1.25f);
                waveRim.transform.localScale = Vector3.one * (radius / 0.64f);
                waveRimIn.transform.localScale = Vector3.one * (radius / 0.64f * 0.93f);
                groundRing.transform.localScale = new Vector3(radius / 1.28f, radius / 1.28f * 0.55f, 1f);
                waveDisc.color = Color.Lerp(Color.white.Glow(1.8f), Palette.Amber.Glow(1.2f), k).WithAlpha(Mathf.Lerp(0.7f, 0.12f, k));
                waveHalo.color = Palette.Amber.Glow(1.4f).WithAlpha(0.35f * bright);
                waveRim.color = Color.Lerp(Palette.Gold, Color.white, 0.3f).Glow(2.4f).WithAlpha(0.95f * bright);
                waveRimIn.color = Color.white.Glow(1.6f).WithAlpha(0.7f * bright);
                groundRing.color = Palette.Amber.Glow(1.5f).WithAlpha(0.8f * bright);

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
            Destroy(groundGo);

            // --- 4. it costs him: a long opening while the crown cools
            Rig.SetPose(true, 30f, 180f, -18f, 60f, -30f, -130f, -60f, 40f);   // on one knee while the crown cools
            yield return Wait(BossTuning.SolarRecovery * 0.5f);
            ReleaseSolarCamera();
            Rig.SetPose(false);
            yield return Wait(BossTuning.SolarRecovery * 0.3f);
        }

        /// <summary>Phase two only: he puts his head down and runs the arena, the burning crown first.</summary>
        IEnumerator GoreCharge()
        {
            CurrentAttack = "Gore";
            FacePlayer();
            Move(0f);
            BeginTelegraph(AttackKind.Unblockable, BossTuning.GoreTelegraph * teleMul);
            Rig.SetPose(true, -20f, 180f, 28f, 44f, -40f, -35f, -10f, 40f);        // head down, crown forward
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
