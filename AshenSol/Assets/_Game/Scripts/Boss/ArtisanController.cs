using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.Player;

namespace AshenSol.Boss
{
    /// <summary>
    /// THE SEVENTH ARTISAN — the forge machine that still tends a dead sun. An aerial boss: it holds the
    /// high ground with bolts, beams and drones, and only comes down to swing its blades. Those melee
    /// swings are the player's parry window, so the fight teaches "wait for it to land".
    /// </summary>
    public class ArtisanController : EnemyBase, IBossFight
    {
        public const string ArtisanName = "THE SEVENTH ARTISAN";
        public const string ArtisanSubtitle = "Smith of the Dead Sun";

        public static ArtisanController Create(Vector2 pos, Transform parent)
        {
            var go = new GameObject("Artisan");
            go.layer = Layers.Enemy;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var a = go.AddComponent<ArtisanController>();
            a.Init(pos, -1, "artisan");
            return a;
        }

        // ---- IBossFight ----
        public string BossName { get { return ArtisanName; } }
        public string BossSubtitle { get { return ArtisanSubtitle; } }
        public string FightMusic { get { return "music_artisan"; } }
        public event Action Defeated;

        public int Phase { get; private set; } = 1;
        public bool IsFightActive { get; private set; }
        public string CurrentAttack { get; private set; } = "";

        protected override float CenterHeight { get { return 0f; } }
        protected override float KnockbackResist { get { return 1f; } }
        protected override float PostureOnParry { get { return ArtisanTuning.PostureOnParry; } }
        protected override float PostureRegen { get { return ArtisanTuning.PostureRegen; } }
        public override int ExecuteDamage { get { return ArtisanTuning.ExecuteDamage; } }
        protected override string DeathSfx { get { return "boss_death"; } }
        public override string DisplayName { get { return ArtisanName; } }
        public override int AshValue { get { return 190; } }

        Vector2 hoverTarget;
        float bobT, teleMul = 1f;
        bool phasePending, dying, invulnerable;
        string lastAttack = "";
        Transform rigRoot;
        SpriteRenderer core, hammerL, hammerR;
        Transform bladePivotL, bladePivotR;
        TrailRenderer bladeTrailL, bladeTrailR;
        Vector2 bladeAngles = new Vector2(-18f, 18f);
        bool bladesPosed;
        static readonly Color BladeRest = new Color(0.75f, 0.7f, 0.72f);
        Light2D coreLight;
        Rect arena;
        readonly List<EnemyBase> spawned = new List<EnemyBase>();

        public void SetArena(Rect r) { arena = r; }

        // ---------------- build ----------------
        protected override void BuildBody()
        {
            Type = EnemyType.Boss;
            MaxHp = ArtisanTuning.MaxHp;
            MaxPosture = ArtisanTuning.MaxPosture;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body = rb;
            var col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = 1.35f;
            Collider = col;
        }

        protected override EnemyRig BuildRig()
        {
            var holder = new GameObject("ArtisanRig").transform;
            holder.SetParent(transform, false);
            holder.localScale = Vector3.one * 2.6f;
            rigRoot = holder;
            var rig = EnemyRig.BuildDrone(holder, SortOrder.Boss);

            // Each blade rotates around its grip, rather than around the sprite's centre.
            hammerL = Limb(holder, new Vector2(-0.62f, -0.22f), -18f, SortOrder.Boss - 2);
            hammerR = Limb(holder, new Vector2(0.62f, -0.22f), 18f, SortOrder.Boss + 6);
            bladePivotL = hammerL.transform.parent;
            bladePivotR = hammerR.transform.parent;
            bladeTrailL = BladeTrail(hammerL);
            bladeTrailR = BladeTrail(hammerR);

            var coreGo = new GameObject("core");
            coreGo.transform.SetParent(holder, false);
            coreGo.transform.localPosition = new Vector3(0.02f, 0.02f, 0f);
            core = coreGo.AddComponent<SpriteRenderer>();
            core.sprite = Res.Sprite("boss_core");
            core.material = MaterialLibrary.Additive;
            core.color = Palette.Amber;
            core.sortingOrder = SortOrder.Boss + 4;
            coreLight = coreGo.AddComponent<Light2D>();
            coreLight.lightType = Light2D.LightType.Point;
            coreLight.color = Palette.Amber;
            coreLight.intensity = 1.6f;
            coreLight.pointLightOuterRadius = 4.5f;
            return rig;
        }

        static SpriteRenderer Limb(Transform parent, Vector2 pos, float angle, int sort)
        {
            var grip = new GameObject("BladeGrip").transform;
            grip.SetParent(parent, false);
            grip.localPosition = new Vector3(pos.x, pos.y, 0f);
            grip.localRotation = Quaternion.Euler(0f, 0f, angle);
            var go = new GameObject("blade");
            go.transform.SetParent(grip, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("grunt_blade");
            sr.sortingOrder = sort;
            sr.color = BladeRest;
            go.transform.localScale = new Vector3(1.3f, 1.5f, 1f);
            return sr;
        }

        static TrailRenderer BladeTrail(SpriteRenderer blade)
        {
            var go = new GameObject("BladeTipTrail");
            go.transform.SetParent(blade.transform, false);
            go.transform.localPosition = new Vector3(0f, blade.sprite.bounds.max.y * 0.95f, 0f);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = AshenSol.VFX.VfxManager.AdditiveFor(Res.Sprite("fx_glow"));
            trail.time = 0.12f;
            trail.minVertexDistance = 0.025f;
            trail.widthMultiplier = 0.13f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.numCornerVertices = 6;
            trail.numCapVertices = 6;
            trail.sortingOrder = SortOrder.Fx + 7;
            trail.startColor = Palette.Gold.WithAlpha(0.75f);
            trail.endColor = Palette.Amber.WithAlpha(0f);
            trail.emitting = false;
            return trail;
        }

        void PoseBlades(Vector2 angles)
        {
            // Sample the curved tip path between frames so fast cuts don't leave polygonal trails.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(angles.x - bladeAngles.x), Mathf.Abs(angles.y - bladeAngles.y)) / 4f));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 pose = Vector2.Lerp(bladeAngles, angles, (float)i / steps);
                bladePivotL.localRotation = Quaternion.Euler(0f, 0f, pose.x);
                bladePivotR.localRotation = Quaternion.Euler(0f, 0f, pose.y);
                if (bladeTrailL.emitting && Time.deltaTime > 0f) bladeTrailL.AddPosition(bladeTrailL.transform.position);
                if (bladeTrailR.emitting && Time.deltaTime > 0f) bladeTrailR.AddPosition(bladeTrailR.transform.position);
            }
            bladeAngles = angles;
        }

        Vector2 FacingPose(Vector2 rightFacing)
        {
            return Facing > 0 ? rightFacing : new Vector2(-rightFacing.y, -rightFacing.x);
        }

        IEnumerator WindUpBlades(Vector2 target, float seconds, Color cue)
        {
            bladesPosed = true;
            Vector2 start = bladeAngles;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                PoseBlades(Vector2.Lerp(start, target, p));
                hammerL.color = hammerR.color = Color.Lerp(BladeRest, cue, p * 0.8f);
                yield return null;
            }
            PoseBlades(target);
        }

        void ReleaseBlades(bool clearTrails = false)
        {
            bladesPosed = false;
            hammerL.color = hammerR.color = BladeRest;
            bladeTrailL.emitting = bladeTrailR.emitting = false;
            if (clearTrails)
            {
                bladeTrailL.Clear(); bladeTrailR.Clear();
                bladeTrailL.enabled = bladeTrailR.enabled = false;
            }
        }

        void StartBladeTrails(bool left, bool right)
        {
            if (left) { bladeTrailL.Clear(); bladeTrailL.enabled = true; }
            if (right) { bladeTrailR.Clear(); bladeTrailR.enabled = true; }
            bladeTrailL.emitting = left;
            bladeTrailR.emitting = right;
        }

        protected override void OnReset()
        {
            Phase = 1; teleMul = 1f;
            IsFightActive = false; CurrentAttack = ""; phasePending = false; dying = false; invulnerable = false;
            lastAttack = "";
            hoverTarget = SpawnPos;
            transform.position = SpawnPos;
            bobT = 0f;
            hoverVel = Vector2.zero;
            if (rigRoot != null) rigRoot.localRotation = Quaternion.identity;
            ReleaseBlades(true);
            PoseBlades(new Vector2(-18f, 18f));
            hammerL.enabled = hammerR.enabled = true;
            if (core != null) { core.enabled = true; coreLight.enabled = true; }
            ClearSpawned();
            GroundShockwave.ClearAll();
            GameEvents.RaiseBossHealthChanged(1f, 0f, false);
        }

        void ClearSpawned()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Destroy(spawned[i].gameObject);
            spawned.Clear();
        }

        public void ResetFight() { ResetToSpawn(); }

        public void Rise()
        {
            Rig.Punch(1.25f, 0.8f);
            Rig.Flash(Palette.Amber, 0.5f);
            Services.Vfx.FlashLight(Center, Palette.Amber, 3.5f, 8f, 0.6f);
            Services.Vfx.Embers(Center, 34, Palette.Amber);
            Services.Audio.PlaySfxAt("boss_roar", Center, 0.9f);
        }

        public void BeginFight()
        {
            if (!IsAlive || IsFightActive) return;
            IsFightActive = true;
            GameEvents.RaiseLog("artisan fight begins");
        }

        // ---------------- per frame ----------------
        protected override void Update()
        {
            base.Update();
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            bobT += dt;
            if (core != null)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (Phase == 2 ? 10f : 5f));
                core.color = (Phase == 2 ? Palette.Red : Palette.Amber).WithAlpha(pulse);
                coreLight.color = Phase == 2 ? Palette.Red : Palette.Amber;
                coreLight.intensity = (Phase == 2 ? 2.4f : 1.6f) * pulse;
            }
            if (hammerL != null)
            {
                if (!bladesPosed)
                {
                    float sway = Mathf.Sin(bobT * 1.6f) * 7f;
                    Vector2 rest = PostureBroken ? new Vector2(-165f, 165f) : new Vector2(-18f + sway, 18f - sway);
                    PoseBlades(Vector2.Lerp(bladeAngles, rest, 1f - Mathf.Exp(-9f * dt)));
                }
            }
        }

        Vector2 hoverVel;

        void FixedUpdate()
        {
            if (!IsAlive || Body == null) return;
            Vector2 goal = hoverTarget + new Vector2(0f, Mathf.Sin(bobT * 1.1f) * 0.35f);
            // eased flight instead of a constant-speed slide: it leans into a move and settles out of it
            Vector2 next = Vector2.SmoothDamp(Body.position, goal, ref hoverVel, 0.32f, ArtisanTuning.MoveSpeed * 1.7f, Time.fixedDeltaTime);
            Body.MovePosition(next);
            if (rigRoot != null)
            {
                float bank = Mathf.Clamp(-hoverVel.x * 2.2f, -16f, 16f);
                rigRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(rigRoot.localEulerAngles.z, bank, 0.2f));
            }
        }

        // ---------------- damage ----------------
        public override HitOutcome ReceiveAttack(in AttackInfo info)
        {
            if (!IsAlive || dying) return HitOutcome.Ignored;
            if (invulnerable)
            {
                Rig.Flash(Palette.Amber, 0.1f);
                Services.Vfx.HitSpark(info.HitPoint == Vector2.zero ? Center : info.HitPoint, Vector2.up, Palette.Amber, 0.6f);
                return HitOutcome.Ignored;
            }
            return base.ReceiveAttack(info);
        }

        protected override void OnHealthChanged()
        {
            GameEvents.RaiseBossHealthChanged(Mathf.Clamp01((float)Hp / MaxHp), PostureDisplay01, PostureBroken);
            if (Phase == 1 && Hp > 0 && Hp <= MaxHp * ArtisanTuning.Phase2Threshold) phasePending = true;
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
                AddPosture(MaxPosture * EnemyTuning.PostureFromBlock);
                Rig.Flash(Palette.Amber, 0.1f);
            }
        }

        protected override void BreakPosture()
        {
            if (!IsAlive || PostureBroken) return;
            ReleaseBlades(true);
            CurrentAttack = "";
            // it drops out of the sky — that is what makes the break readable and reachable
            hoverTarget = new Vector2(transform.position.x, arena.yMin + 1.3f);
            base.BreakPosture();
            Services.Audio.PlaySfxAt("boss_stagger", Center, 1f);
            Services.Vfx.Shockwave(new Vector2(transform.position.x, arena.yMin), 4.5f, Palette.Gold);
            Services.Vfx.ScreenFlash(Palette.Gold.WithAlpha(0.16f), 0.2f);
            Services.Cam.Shake(0.5f);
        }

        protected override void RecoverPosture()
        {
            bool was = PostureBroken;
            base.RecoverPosture();
            if (was && IsAlive) hoverTarget = new Vector2(transform.position.x, arena.yMin + ArtisanTuning.HoverHeight);
        }

        protected override void Die()
        {
            if (!IsAlive || dying) return;
            dying = true;
            ReleaseBlades(true);
            IsAlive = false;
            IsFightActive = false;
            CurrentAttack = "";
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            StopAllCoroutines();
            EndTelegraph(false);
            staggerTimer = 0f;
            Collider.enabled = false;
            ClearSpawned();
            GroundShockwave.ClearAll();
            healthBar.Hide();
            GameEvents.RaiseBossHealthChanged(0f, 0f, false);
            Progression.AddAsh(AshValue);
            GameEvents.RaiseEnemyKilled(this);
            GameEvents.RaiseLog("artisan killed");
            StartCoroutine(DeathSequence());
        }

        IEnumerator DeathSequence()
        {
            TimeController.Instance.SlowMo(0.25f, 1.2f);
            Services.Audio.PlaySfx("boss_death");
            Services.Cam.Shake(0.9f);
            for (int i = 0; i < 3; i++)
            {
                Rig.Flash(Color.white, 0.25f);
                Services.Vfx.FlashLight(Center, Palette.Amber, 4f, 8f, 0.25f);
                Services.Vfx.Embers(Center, 22, Palette.Amber);
                Services.Vfx.HitSpark(Center + UnityEngine.Random.insideUnitCircle * 1.5f, UnityEngine.Random.insideUnitCircle.normalized, Palette.Amber, 1.4f);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            // it falls out of the air
            float t = 0f;
            while (t < 0.8f)
            {
                t += Time.unscaledDeltaTime;
                transform.position += Vector3.down * 9f * Time.unscaledDeltaTime;
                Rig.Root.localRotation = Quaternion.Euler(0f, 0f, t * 90f);
                yield return null;
            }
            Services.Vfx.Dissolve(Renderers, Center, Palette.Amber);
            Services.Vfx.Shockwave(transform.position, 6f, Palette.Amber);
            Services.Vfx.Embers(Center, 60, Palette.Gold);
            Services.Cam.Shake(0.9f);
            Rig.SetVisible(false);
            core.enabled = false; coreLight.enabled = false;
            hammerL.enabled = false; hammerR.enabled = false;
            yield return new WaitForSecondsRealtime(2.2f);
            var d = Defeated; if (d != null) d();
            GameEvents.RaiseBossDefeated();
        }

        // ---------------- behaviour ----------------
        protected override IEnumerator Behaviour()
        {
            yield return null;
            while (IsAlive)
            {
                if (!IsFightActive || !PlayerAlive) { yield return null; continue; }
                if (phasePending) { yield return PhaseTransition(); continue; }

                yield return Reposition(UnityEngine.Random.Range(0.5f, 1.1f));
                if (!PlayerAlive) continue;
                string attack = Choose();
                lastAttack = attack;
                switch (attack)
                {
                    case "Bolts": yield return BoltFan(); break;
                    case "Drop": yield return HammerDrop(); break;
                    case "Beam": yield return BeamSweep(); break;
                    case "Drones": yield return SummonDrones(); break;
                    default: yield return DescendAndStrike(); break;
                }
                CurrentAttack = "";
            }
        }

        string Choose()
        {
            var names = new List<string>();
            var weights = new List<float>();
            void Add(string n, float w) { if (w > 0f && n != lastAttack) { names.Add(n); weights.Add(w); } }
            Add("Strike", 3.2f);                       // the parry window: keep it frequent
            Add("Bolts", 2.5f);
            Add("Drop", 2f);
            Add("Beam", Phase >= 2 ? 2.2f : 1.2f);
            Add("Drones", AliveSpawned() < 2 ? 1.6f : 0f);
            if (names.Count == 0) return "Strike";
            float total = 0f; foreach (var w in weights) total += w;
            float r = UnityEngine.Random.value * total;
            for (int i = 0; i < names.Count; i++) { r -= weights[i]; if (r <= 0f) return names[i]; }
            return names[names.Count - 1];
        }

        int AliveSpawned()
        {
            int n = 0;
            for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null && spawned[i].IsAlive) n++;
            return n;
        }

        IEnumerator Reposition(float seconds)
        {
            CurrentAttack = "";
            float x = PlayerAlive ? Player.Center.x + UnityEngine.Random.Range(-3.5f, 3.5f) : arena.center.x;
            hoverTarget = new Vector2(Mathf.Clamp(x, arena.xMin + 3f, arena.xMax - 3f), arena.yMin + ArtisanTuning.HoverHeight);
            yield return Wait(seconds);
        }

        // --- comes down and swings: three parryable hits, the posture break window
        IEnumerator DescendAndStrike()
        {
            CurrentAttack = "Strike";
            if (!PlayerAlive) yield break;
            hoverTarget = new Vector2(Mathf.Clamp(Player.Center.x, arena.xMin + 2.5f, arena.xMax - 2.5f), arena.yMin + 1.5f);
            Services.Audio.PlaySfxAt("boss_dash", Center, 0.8f);
            yield return Wait(0.55f);

            for (int i = 0; i < 3; i++)
            {
                if (!PlayerAlive) break;
                FacePlayer();
                Vector2 windup = FacingPose(i == 0 ? new Vector2(-25f, 65f)
                    : i == 1 ? new Vector2(-125f, -25f) : new Vector2(-55f, 55f));
                Vector2 followThrough = FacingPose(i == 0 ? new Vector2(-5f, -115f)
                    : i == 1 ? new Vector2(45f, -15f) : new Vector2(115f, -115f));
                BeginTelegraph(AttackKind.Parryable, ArtisanTuning.StrikeTelegraph * teleMul);
                yield return WindUpBlades(windup, ArtisanTuning.StrikeTelegraph * teleMul * Settings.TelegraphMul, Color.white);
                EndTelegraph();
                bool swingRight = (i == 0) == (Facing > 0);
                StartBladeTrails(i == 2 || !swingRight, i == 2 || swingRight);
                Rig.Punch(1.2f, 0.82f);
                Services.Audio.PlaySfxAt("boss_swing", Center, 0.95f, 0.08f);
                Services.Vfx.SlashArc(Center + new Vector2(Facing * 1.4f, -0.4f), i % 2 == 0 ? -25f : 30f, Facing < 0, Palette.Amber, 2f);
                float t = 0f; bool resolved = false;
                while (t < ArtisanTuning.StrikeActive)
                {
                    float swing = Mathf.Clamp01((t + Time.deltaTime) / ArtisanTuning.StrikeActive);
                    PoseBlades(Vector2.Lerp(windup, followThrough, Ease.OutCubic(swing)));
                    // Contact starts once the blade has visibly entered its cut.
                    if (!resolved && swing >= 0.35f)
                    {
                        var info = new AttackInfo
                        {
                            Damage = ArtisanTuning.StrikeDamage, Knockback = 6f,
                            Kind = AttackKind.Parryable, Tag = "artisan_strike" + (i + 1), Origin = Center
                        };
                        var o = StrikePlayer(info, Center + new Vector2(Facing * 1.5f, -0.5f), new Vector2(3.2f, 2.6f));
                        if (o != HitOutcome.Ignored) resolved = true;
                    }
                    t += Time.deltaTime;
                    yield return null;
                }
                PoseBlades(followThrough);
                ReleaseBlades();
                yield return Wait(ArtisanTuning.StrikeGap);
            }
            hoverTarget = new Vector2(transform.position.x, arena.yMin + ArtisanTuning.HoverHeight);
            yield return Wait(ArtisanTuning.StrikeRecovery);
        }

        // --- a fan of parryable bolts: reflect one and it hurts
        IEnumerator BoltFan()
        {
            CurrentAttack = "Bolts";
            FacePlayer();
            BeginTelegraph(AttackKind.Parryable, ArtisanTuning.BoltTelegraph * teleMul);
            yield return WindUpBlades(new Vector2(55f, -55f), ArtisanTuning.BoltTelegraph * teleMul * Settings.TelegraphMul, Color.white);
            EndTelegraph();
            if (!PlayerAlive) { ReleaseBlades(); yield break; }
            Services.Audio.PlaySfxAt("boss_bolts", Center, 1f);
            Services.Vfx.FlashLight(Center, Palette.Amber, 3f, 5f, 0.2f);
            Vector2 baseDir = (Player.Center - Center).normalized;
            int count = Phase >= 2 ? 5 : 3;
            for (int i = 0; i < count; i++)
            {
                float a = (i - (count - 1) * 0.5f) * 13f;
                Vector2 dir = Quaternion.Euler(0f, 0f, a) * baseDir;
                Projectile.Fire(Center + dir * 1.4f, dir * ArtisanTuning.BoltSpeed, Team.Enemy,
                    ArtisanTuning.BoltDamage, AttackKind.Parryable, Palette.Amber, "artisan_bolt", 5f, 0.22f);
                yield return Wait(0.07f);
            }
            ReleaseBlades();
            yield return Wait(ArtisanTuning.BoltRecovery);
        }

        // --- unblockable body slam with ground waves: dash or jump
        IEnumerator HammerDrop()
        {
            CurrentAttack = "Drop";
            if (!PlayerAlive) yield break;
            BeginTelegraph(AttackKind.Unblockable, ArtisanTuning.DropTelegraph * teleMul);
            bladesPosed = true;
            Vector2 startPose = bladeAngles;
            Vector2 raised = new Vector2(-40f, 40f);
            float t = 0f;
            float tele = ArtisanTuning.DropTelegraph * teleMul * Settings.TelegraphMul;
            while (t < tele)
            {
                // tracks the player until just before it commits
                if (PlayerAlive && t < tele - 0.25f)
                    hoverTarget = new Vector2(Mathf.Clamp(Player.Center.x, arena.xMin + 2f, arena.xMax - 2f), arena.yMin + ArtisanTuning.HoverHeight + 2f);
                t += Time.deltaTime;
                PoseBlades(Vector2.Lerp(startPose, raised, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / tele))));
                hammerL.color = hammerR.color = Color.Lerp(BladeRest, Palette.Red, Mathf.Clamp01(t / tele));
                yield return null;
            }
            EndTelegraph();

            // drop: the hover target goes to the floor too, so FixedUpdate pulls the same way instead of fighting it
            float y = transform.position.y;
            hoverTarget = new Vector2(transform.position.x, arena.yMin + 1.1f);
            StartBladeTrails(true, true);
            while (transform.position.y > arena.yMin + 1.1f)
            {
                transform.position += Vector3.down * ArtisanTuning.DropSpeed * Time.deltaTime;
                float fall = Mathf.Clamp01((y - transform.position.y) / Mathf.Max(0.1f, y - arena.yMin - 1.1f));
                PoseBlades(Vector2.Lerp(raised, new Vector2(105f, -105f), Ease.OutCubic(fall)));
                Services.Vfx.Afterimage(Renderers, Palette.Red.WithAlpha(0.4f), 0.18f);
                yield return null;
            }
            Vector2 impact = new Vector2(transform.position.x, arena.yMin);
            hoverTarget = new Vector2(transform.position.x, arena.yMin + 1.1f);
            PoseBlades(new Vector2(105f, -105f));
            bladeTrailL.emitting = bladeTrailR.emitting = false;
            Services.Vfx.Shockwave(impact, 4.5f, Palette.Red);
            Services.Vfx.FlashLight(impact + new Vector2(0f, 1f), Palette.Red, 4f, 8f, 0.3f);
            Services.Cam.Shake(0.9f);
            HitStop.Request(0.05f);
            Services.Audio.PlaySfxAt("boss_slam", impact, 1f);
            var info2 = new AttackInfo { Damage = ArtisanTuning.DropDamage, Knockback = 9f, Kind = AttackKind.Unblockable, Tag = "artisan_drop", Origin = Center };
            StrikePlayer(info2, impact + new Vector2(0f, 1.2f), new Vector2(4f, 3f));
            GroundShockwave.Spawn(impact + new Vector2(1.4f, 0f), 1, 9f, ArtisanTuning.WaveDamage, 1.7f, transform.parent);
            GroundShockwave.Spawn(impact + new Vector2(-1.4f, 0f), -1, 9f, ArtisanTuning.WaveDamage, 1.7f, transform.parent);
            yield return Wait(ArtisanTuning.DropRecovery);   // punish window
            ReleaseBlades();
            hoverTarget = new Vector2(transform.position.x, arena.yMin + ArtisanTuning.HoverHeight);
        }

        // --- a red beam that sweeps the arena: get above it
        IEnumerator BeamSweep()
        {
            CurrentAttack = "Beam";
            int dir = PlayerAlive && Player.Center.x > arena.center.x ? -1 : 1;
            float beamY = arena.yMin + ArtisanTuning.BeamHeight;
            hoverTarget = new Vector2(dir > 0 ? arena.xMin + 2.5f : arena.xMax - 2.5f, beamY + 1.2f);
            yield return Wait(0.6f);
            BeginTelegraph(AttackKind.Unblockable, ArtisanTuning.BeamTelegraph * teleMul);

            // telegraph line
            var warn = MakeBeam(beamY, 0.12f, Palette.Red.WithAlpha(0.35f));
            yield return WindUpBlades(new Vector2(-55f, 55f), ArtisanTuning.BeamTelegraph * teleMul * Settings.TelegraphMul, Palette.Red);
            EndTelegraph();
            if (warn != null) Destroy(warn.gameObject);

            var beam = MakeBeam(beamY, 0.85f, Palette.Red);
            Services.Audio.PlaySfxAt("boss_whirl", Center, 1f);
            float x = dir > 0 ? arena.xMin : arena.xMax;
            float end = dir > 0 ? arena.xMax : arena.xMin;
            bool hit = false;
            while ((dir > 0 && x < end) || (dir < 0 && x > end))
            {
                float dt = Time.deltaTime;
                x += dir * ArtisanTuning.BeamSpeed * dt;
                hoverTarget = new Vector2(x, beamY + 1.2f);
                if (beam != null) beam.transform.position = new Vector3(x, beamY, 0f);
                Services.Vfx.HitSpark(new Vector2(x, beamY), Vector2.up, Palette.Red, 0.5f);
                if (!hit)
                {
                    var info = new AttackInfo { Damage = ArtisanTuning.BeamDamage, Knockback = 7f, Kind = AttackKind.Unblockable, Tag = "artisan_beam", Origin = new Vector2(x, beamY) };
                    var o = StrikePlayer(info, new Vector2(x, beamY), new Vector2(1.6f, 1.2f));
                    if (o == HitOutcome.Hit || o == HitOutcome.Blocked) hit = true;
                }
                yield return null;
            }
            if (beam != null) Destroy(beam.gameObject);
            ReleaseBlades();
            hoverTarget = new Vector2(Mathf.Clamp(x, arena.xMin + 3f, arena.xMax - 3f), arena.yMin + ArtisanTuning.HoverHeight);
            yield return Wait(ArtisanTuning.BeamRecovery);
        }

        SpriteRenderer MakeBeam(float y, float thickness, Color color)
        {
            var go = new GameObject("beam");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = new Vector3(arena.center.x, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("fx_bolt");
            sr.material = MaterialLibrary.Additive;
            sr.color = color;
            sr.sortingOrder = SortOrder.Fx + 2;
            go.transform.localScale = new Vector3(2.2f, thickness * 6f, 1f);
            return sr;
        }

        IEnumerator SummonDrones()
        {
            CurrentAttack = "Drones";
            BeginTelegraph(AttackKind.Parryable, 0.5f * teleMul);
            yield return WindUpBlades(new Vector2(75f, -75f), 0.5f * teleMul * Settings.TelegraphMul, Color.white);
            EndTelegraph();
            Services.Audio.PlaySfxAt("drone_shot", Center, 0.9f);
            for (int i = 0; i < 2; i++)
            {
                Vector2 at = Center + new Vector2(i == 0 ? -2.2f : 2.2f, -0.4f);
                var d = EnemyBase.Create(EnemyType.WatcherDrone, at, transform.parent);
                spawned.Add(d);
                Services.Vfx.FlashLight(at, Palette.Amber, 2.5f, 3f, 0.3f);
                Services.Vfx.Embers(at, 14, Palette.Amber);
            }
            ReleaseBlades();
            yield return Wait(0.7f);
        }

        IEnumerator PhaseTransition()
        {
            ReleaseBlades(true);
            phasePending = false;
            Phase = 2;
            CurrentAttack = "Phase";
            invulnerable = true;
            hoverTarget = new Vector2(arena.center.x, arena.yMin + ArtisanTuning.HoverHeight + 1.5f);
            Services.Audio.PlaySfxAt("boss_phase2", Center, 1f);
            Rig.Flash(Palette.Red, 0.6f);
            Services.Vfx.FlashLight(Center, Palette.Red, 4f, 9f, 0.8f);
            Services.Vfx.Shockwave(Center, 6f, Palette.Amber);
            Services.Vfx.Embers(Center, 46, Palette.Amber);
            Services.Cam.Shake(0.7f);
            Services.Vfx.ChromaticPulse(0.7f, 0.6f);
            float t = 0f;
            while (t < 1.8f) { t += Time.deltaTime; if (Mathf.Repeat(t, 0.35f) < Time.deltaTime) Rig.Flash(Palette.Red, 0.25f); yield return null; }
            teleMul = ArtisanTuning.Phase2TelegraphMul;
            invulnerable = false;
            GameEvents.RaiseBossPhaseChanged(2);
            CurrentAttack = "";
        }
    }

    public static class ArtisanTuning
    {
        public const int MaxHp = 460;
        public const float MaxPosture = 240f;
        public const float PostureOnParry = 85f;    // three parries break it
        public const float PostureRegen = 24f;
        public const int ExecuteDamage = 110;
        public const float Phase2Threshold = 0.5f;
        public const float Phase2TelegraphMul = 0.85f;

        public const float MoveSpeed = 7f;
        public const float HoverHeight = 5.2f;

        public const float StrikeTelegraph = 0.4f, StrikeActive = 0.12f, StrikeGap = 0.3f, StrikeRecovery = 0.9f;
        public const int StrikeDamage = 15;

        public const float BoltTelegraph = 0.45f, BoltSpeed = 10f, BoltRecovery = 0.7f;
        public const int BoltDamage = 12;

        public const float DropTelegraph = 0.75f, DropSpeed = 34f, DropRecovery = 1.1f;
        public const int DropDamage = 26, WaveDamage = 14;

        public const float BeamTelegraph = 0.8f, BeamSpeed = 11f, BeamHeight = 1.1f, BeamRecovery = 0.8f;
        public const int BeamDamage = 20;
    }
}
