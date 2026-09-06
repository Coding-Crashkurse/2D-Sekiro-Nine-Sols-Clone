using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AshenSol.Core;
using AshenSol.Player;

namespace AshenSol.Enemies
{
    /// <summary>Shared enemy logic: health, internal damage, telegraphs, stagger, damage resolution, reset.</summary>
    public abstract class EnemyBase : MonoBehaviour, IDamageable
    {
        public static readonly List<EnemyBase> All = new List<EnemyBase>();

        public static EnemyBase Create(EnemyType type, Vector2 pos, Transform parent, string zoneId = null, int facing = -1)
        {
            if (type == EnemyType.Boss) throw new NotSupportedException("Use BossController.Create for the boss");
            var go = new GameObject(type.ToString());
            go.layer = Layers.Enemy;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            EnemyBase e;
            switch (type)
            {
                case EnemyType.SpearSentinel: e = go.AddComponent<SpearSentinel>(); break;
                case EnemyType.WatcherDrone: e = go.AddComponent<WatcherDrone>(); break;
                default: e = go.AddComponent<Grunt>(); break;
            }
            e.Init(pos, facing, zoneId);
            return e;
        }

        // ---- state ----
        public EnemyType Type { get; protected set; }
        public string ZoneId { get; set; }
        public int Hp { get; protected set; }
        public int MaxHp { get; protected set; }
        // ---- posture (yellow bar) ----
        public float MaxPosture { get; protected set; }
        public float Posture { get; protected set; }
        public bool PostureBroken { get; private set; }
        public float PostureBreakRemaining { get; private set; }
        public float Posture01 { get { return MaxPosture > 0f ? Mathf.Clamp01(Posture / MaxPosture) : 0f; } }
        /// <summary>True while the guard is broken — the window for the Qi execution.</summary>
        public bool CanBeExecuted { get { return IsAlive && PostureBroken; } }
        public bool IsAlive { get; protected set; }
        public bool IsStaggered { get { return staggerTimer > 0f; } }
        public bool IsTelegraphing { get; private set; }
        public AttackKind TelegraphKind { get; private set; }
        public float TimeUntilStrike { get; private set; } = -1f;
        public Vector2 SpawnPos { get; private set; }
        public int SpawnFacing { get; private set; }
        public int Facing { get; protected set; } = -1;
        public virtual Vector2 Center { get { return (Vector2)transform.position + new Vector2(0f, CenterHeight); } }
        public Team Team { get { return Team.Enemy; } }
        public Transform Transform { get { return transform; } }
        public SpriteRenderer[] Renderers { get { return Rig != null ? Rig.Renderers : new SpriteRenderer[0]; } }
        public EnemyRig Rig { get; protected set; }
        public Rigidbody2D Body { get; protected set; }
        public Collider2D Collider { get; protected set; }
        public float DistanceToPlayer { get { var p = Player; return p != null ? Vector2.Distance(p.Center, Center) : 999f; } }
        public virtual string DisplayName { get { return Type.ToString(); } }
        /// <summary>Ash granted for the kill.</summary>
        public virtual int AshValue { get { return 22; } }
        public event Action<EnemyBase> Died;

        protected virtual float CenterHeight { get { return 0.8f; } }
        /// <summary>Posture added by one perfect parry. Defaults to a full bar: one parry breaks a mook.</summary>
        protected virtual float PostureOnParry { get { return MaxPosture; } }
        protected virtual float PostureRegen { get { return 30f; } }
        /// <summary>Damage of the Qi execution performed on a broken guard.</summary>
        public virtual int ExecuteDamage { get { return 60; } }
        protected virtual float KnockbackResist { get { return 0f; } }
        protected virtual float DamageTakenMultiplier { get { return 1f; } }
        protected virtual string DeathSfx { get { return "enemy_death"; } }
        protected PlayerController Player { get { return PlayerController.Instance; } }
        protected bool PlayerAlive { get { var p = Player; return p != null && p.IsAlive && p.gameObject.activeInHierarchy; } }

        protected float staggerTimer, telegraphTimer;
        float postureRegenDelay;
        protected EnemyHealthBar healthBar;
        protected Coroutine behaviour;
        protected bool aggro;
        float lastHitSfx;

        protected virtual void Awake() { All.Add(this); }
        protected virtual void OnDestroy() { All.Remove(this); }

        public virtual void Init(Vector2 pos, int facing, string zoneId)
        {
            SpawnPos = pos; SpawnFacing = facing == 0 ? -1 : facing; ZoneId = zoneId;
            BuildBody();
            Rig = BuildRig();
            healthBar = EnemyHealthBar.Create(this, CenterHeight * 2f + 0.25f);
            ResetToSpawn();
        }

        protected abstract void BuildBody();
        protected abstract EnemyRig BuildRig();
        protected abstract IEnumerator Behaviour();

        public virtual void ResetToSpawn()
        {
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            StopAllCoroutines();
            IsAlive = true;
            Hp = MaxHp;
            Posture = 0f; PostureBroken = false; PostureBreakRemaining = 0f; postureRegenDelay = 0f;
            staggerTimer = 0f; aggro = false;
            IsTelegraphing = false; TimeUntilStrike = -1f;
            transform.position = SpawnPos;
            Facing = SpawnFacing;
            if (Collider != null) Collider.enabled = true;
            if (Body != null) { Body.simulated = true; Body.linearVelocity = Vector2.zero; }
            gameObject.SetActive(true);
            if (Rig != null) { Rig.Reset(); Rig.SetFacing(Facing); Rig.SetVisible(true); }
            if (healthBar != null) healthBar.Hide();
            OnReset();
            behaviour = StartCoroutine(Behaviour());
        }

        protected virtual void OnReset() { }

        protected virtual void Update()
        {
            if (!IsAlive) return;
            float dt = Time.deltaTime;
            if (IsTelegraphing)
            {
                telegraphTimer -= dt;
                TimeUntilStrike = Mathf.Max(0f, telegraphTimer);
            }
            if (staggerTimer > 0f)
            {
                staggerTimer -= dt;
                if (staggerTimer <= 0f && behaviour == null) behaviour = StartCoroutine(Behaviour());
            }
            if (PostureBroken)
            {
                PostureBreakRemaining -= dt;
                if (PostureBreakRemaining <= 0f) RecoverPosture();
            }
            else if (Posture > 0f)
            {
                postureRegenDelay -= dt;
                if (postureRegenDelay <= 0f)
                {
                    Posture = Mathf.Max(0f, Posture - PostureRegen * dt);
                    OnHealthChanged();
                }
            }
            if (Rig != null) { Rig.SetFacing(Facing); Rig.Animate(this, dt); }
            if (healthBar != null) healthBar.Tick(dt);
        }

        // ---------------- telegraph / attacks ----------------
        protected void BeginTelegraph(AttackKind kind, float seconds)
        {
            seconds *= Settings.TelegraphMul;   // difficulty: longer telegraphs are easier to read
            IsTelegraphing = true; TelegraphKind = kind;
            telegraphTimer = seconds; TimeUntilStrike = seconds;
            if (Rig != null) Rig.Telegraph(kind, seconds);
            Services.Audio.PlaySfxAt(kind == AttackKind.Parryable ? "enemy_telegraph" : "enemy_telegraph_red", Center, 0.9f);
        }

        protected void EndTelegraph()
        {
            IsTelegraphing = false; TimeUntilStrike = -1f;
            if (Rig != null) Rig.EndTelegraph();
        }

        protected IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        protected HitOutcome StrikePlayer(AttackInfo info, Vector2 boxCenter, Vector2 boxSize)
        {
            var p = Player;
            if (p == null || !p.IsAlive) return HitOutcome.Ignored;
            var hit = Physics2D.OverlapBox(boxCenter, boxSize, 0f, Layers.PlayerMask);
            if (hit == null) return HitOutcome.Ignored;
            if (info.HitPoint == Vector2.zero) info.HitPoint = Vector2.Lerp(Center, p.Center, 0.65f);
            if (info.Origin == Vector2.zero) info.Origin = Center;
            info.Source = gameObject; info.Team = Team.Enemy;
            var outcome = p.ReceiveAttack(info);
            OnAttackResolved(outcome, info);
            return outcome;
        }

        protected virtual void OnAttackResolved(HitOutcome outcome, in AttackInfo info)
        {
            if (outcome == HitOutcome.Parried)
            {
                healthBar.Show();
                AddPosture(PostureOnParry);
                if (!PostureBroken && Rig != null) { Rig.Flash(Color.white, 0.12f); Rig.Punch(0.93f, 1.07f); }
            }
            else if (outcome == HitOutcome.Blocked)
            {
                AddPosture(MaxPosture * EnemyTuning.PostureFromBlock);
                if (Rig != null) Rig.Flash(Palette.Amber, 0.08f);
            }
        }

        protected virtual void Stagger(float seconds)
        {
            if (!IsAlive) return;
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            EndTelegraph();
            staggerTimer = Mathf.Max(staggerTimer, seconds);
            if (Body != null && Body.bodyType == RigidbodyType2D.Dynamic)
                Body.linearVelocity = new Vector2(-Facing * 2.5f, 1.5f);
            if (Rig != null) { Rig.Stagger(seconds); Rig.Flash(Color.white, 0.15f); }
            Services.Vfx.HitSpark(Center, new Vector2(-Facing, 0.3f), Palette.Bone, 0.7f);
        }

        // ---------------- damage ----------------
        public void AddPosture(float amount)
        {
            if (!IsAlive || PostureBroken || amount <= 0f) return;
            Posture = Mathf.Min(MaxPosture, Posture + amount);
            postureRegenDelay = EnemyTuning.PostureRegenDelay;
            OnHealthChanged();
            if (Posture >= MaxPosture) BreakPosture();
        }

        /// <summary>Guard broken: helpless, double damage, open to the Qi execution.</summary>
        protected virtual void BreakPosture()
        {
            if (!IsAlive || PostureBroken) return;
            PostureBroken = true;
            PostureBreakRemaining = EnemyTuning.PostureBreakSeconds;
            Posture = MaxPosture;
            Stagger(EnemyTuning.PostureBreakSeconds);
            healthBar.Show();
            HitStop.Request(0.09f);
            Services.Cam.Shake(0.4f);
            Services.Vfx.FloatingText(Center + new Vector2(0f, CenterHeight + 0.9f), "GUARD BROKEN", Palette.Gold, 1.3f);
            Services.Vfx.FlashLight(Center, Palette.Gold, 3f, 5f, 0.4f);
            Services.Vfx.HitSpark(Center, Vector2.up, Palette.Gold, 1.6f);
            Services.Vfx.Embers(Center, 20, Palette.Gold);
            Services.Audio.PlaySfxAt("posture_break", Center, 1f);
            OnHealthChanged();
            GameEvents.RaiseLog("guard broken: " + DisplayName);
        }

        /// <summary>Break window is over (or was spent by an execution).</summary>
        protected virtual void RecoverPosture()
        {
            PostureBroken = false;
            PostureBreakRemaining = 0f;
            Posture = 0f;
            postureRegenDelay = 0f;
            OnHealthChanged();
        }

        public virtual HitOutcome ReceiveAttack(in AttackInfo info)
        {
            if (!IsAlive) return HitOutcome.Ignored;
            bool execute = info.Tag == "execute";
            int dmg = info.Damage;
            if (!execute && PostureBroken) dmg = Mathf.RoundToInt(dmg * EnemyTuning.BrokenDamageMul);
            dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * DamageTakenMultiplier));
            Hp -= dmg;
            GameEvents.RaiseEnemyDamaged(this, dmg);

            Vector2 dir = Center - info.Origin;
            if (dir.sqrMagnitude < 0.001f) dir = new Vector2(-Facing, 0f);
            dir.Normalize();
            Vector2 hp = info.HitPoint == Vector2.zero ? Center : info.HitPoint;

            if (Rig != null) Rig.Flash(Color.white, 0.08f);
            Services.Vfx.InkSplatter(hp, dir, Palette.Ink, execute ? 22 : 10);
            Services.Vfx.HitSpark(hp, dir, execute ? Palette.Gold : Palette.Bone, execute ? 2f : 1f);
            Services.Vfx.FloatingText(Center + new Vector2(UnityEngine.Random.Range(-0.2f, 0.2f), CenterHeight + 0.3f), dmg.ToString(),
                execute ? Palette.Gold : (PostureBroken ? Color.white : Palette.Bone), execute ? 1.8f : (PostureBroken ? 1.3f : 1f));
            HitStop.Request(0.03f);
            if (Time.unscaledTime - lastHitSfx > 0.05f) { Services.Audio.PlaySfxAt("enemy_hit", Center, 0.9f, 0.1f); lastHitSfx = Time.unscaledTime; }
            healthBar.Show();

            if (Body != null && Body.bodyType == RigidbodyType2D.Dynamic && info.Knockback > 0f && KnockbackResist < 1f)
            {
                float kb = info.Knockback * (1f - KnockbackResist);
                Body.linearVelocity = new Vector2(dir.x * kb, Mathf.Max(Body.linearVelocity.y, 1.5f));
            }
            OnHurt(info, dmg);
            OnHealthChanged();
            if (Hp <= 0) { Die(); return HitOutcome.Hit; }

            if (execute) RecoverPosture();                       // the break is consumed by the finisher
            else if (info.Tag == "qi_blast") AddPosture(MaxPosture * EnemyTuning.PostureFromQiBlast);
            else if (info.Tag == "player_heavy") AddPosture(MaxPosture * EnemyTuning.PostureFromHeavy);
            else if (info.Team == Team.Player) AddPosture(MaxPosture * EnemyTuning.PostureFromHit);
            return HitOutcome.Hit;
        }

        protected virtual void OnHurt(in AttackInfo info, int dmg) { }
        protected virtual void OnHealthChanged() { }

        protected virtual void Die()
        {
            if (!IsAlive) return;
            IsAlive = false;
            if (behaviour != null) { StopCoroutine(behaviour); behaviour = null; }
            StopAllCoroutines();
            EndTelegraph();
            staggerTimer = 0f;
            if (Collider != null) Collider.enabled = false;
            if (Body != null) { Body.linearVelocity = Vector2.zero; Body.simulated = false; }
            Services.Vfx.Dissolve(Renderers, Center, Palette.Red);
            Services.Vfx.Embers(Center, 18, Palette.Amber);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Ink, 14);
            Services.Audio.PlaySfxAt(DeathSfx, Center, 1f);
            if (Rig != null) Rig.SetVisible(false);
            if (healthBar != null) healthBar.Hide();
            OnDied();
            Progression.AddAsh(AshValue);
            var d = Died; if (d != null) d(this);
            GameEvents.RaiseEnemyKilled(this);
            GameEvents.RaiseLog("enemy killed: " + DisplayName + (ZoneId != null ? " zone=" + ZoneId : ""));
        }

        protected virtual void OnDied() { }

        // ---------------- helpers for ground enemies ----------------
        protected bool GroundAhead(int dir, float ahead = 0.6f, float down = 1.2f)
        {
            Vector2 o = (Vector2)transform.position + new Vector2(dir * ahead, 0.15f);
            return Physics2D.Raycast(o, Vector2.down, down, Layers.GroundMask).collider != null;
        }

        protected bool WallAhead(int dir, float dist = 0.7f)
        {
            Vector2 o = (Vector2)transform.position + new Vector2(0f, CenterHeight);
            return Physics2D.Raycast(o, new Vector2(dir, 0f), dist, 1 << Layers.Ground).collider != null;
        }

        /// <summary>Set horizontal velocity, refusing to walk off ledges or into walls.</summary>
        protected void Move(float vx)
        {
            if (Body == null) return;
            if (vx != 0f)
            {
                int d = vx > 0f ? 1 : -1;
                if (!GroundAhead(d) || WallAhead(d)) vx = 0f;
            }
            Body.linearVelocity = new Vector2(vx, Body.linearVelocity.y);
        }

        protected void FacePlayer()
        {
            if (Player != null) Facing = Player.Center.x >= Center.x ? 1 : -1;
        }
    }
}
