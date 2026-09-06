using System;
using System.Collections;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Movement, health, damage resolution (parry rules live in ReceiveAttack). Combat actions in PlayerCombat.</summary>
    public class PlayerController : MonoBehaviour, IDamageable
    {
        public static PlayerController Instance { get; private set; }

        public static PlayerController Create(Vector2 spawnPos, Transform parent)
        {
            var go = new GameObject("Player");
            go.layer = Layers.Player;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = spawnPos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.mass = 1f;
            rb.linearDamping = 0f;
            var mat = new PhysicsMaterial2D("PlayerMat") { friction = 0f, bounciness = 0f };
            rb.sharedMaterial = mat;

            var col = go.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.6f, 1.5f);
            col.offset = new Vector2(0f, 0.75f);
            col.direction = CapsuleDirection2D.Vertical;
            col.sharedMaterial = mat;

            var pc = go.AddComponent<PlayerController>();
            pc.Combat = new PlayerCombat(pc);
            pc.Rig = PlayerRig.Build(pc);
            pc.Respawn(spawnPos);
            return pc;
        }

        // ---- public state ----
        public int Hp { get; private set; }
        public int MaxHp { get { return PlayerTuning.MaxHp; } }
        public int Qi { get; private set; }
        public int MaxQi { get { return PlayerTuning.MaxQi; } }
        public int Facing { get; private set; } = 1;
        public bool IsGrounded { get; private set; }
        public bool IsDashing { get { return dashTimer > 0f; } }
        public bool IsInvulnerable { get { return IsDead || IsDashing || invulnTimer > 0f; } }
        public bool IsParryWindowOpen { get { return Combat != null && Combat.PerfectWindow; } }
        public bool IsBlocking { get { return Combat != null && Combat.BlockWindow; } }
        public bool IsAttacking { get { return Combat != null && Combat.IsAttacking; } }
        public bool IsHealing { get { return Combat != null && Combat.IsHealing; } }
        public bool IsDead { get; private set; }
        public bool IsStunned { get { return stunTimer > 0f; } }
        public bool ControlEnabled { get; private set; } = true;
        public bool HazardRecovering { get { return hazardRecovering; } }
        public Vector2 Position { get { return transform.position; } }
        public Vector2 Center { get { return (Vector2)transform.position + new Vector2(0f, 0.85f); } }
        public Vector2 Velocity { get { return Body != null ? Body.linearVelocity : Vector2.zero; } }
        public Rigidbody2D Body { get; private set; }
        public CapsuleCollider2D Collider { get; private set; }
        public SpriteRenderer[] Renderers { get { return Rig != null ? Rig.Renderers : new SpriteRenderer[0]; } }
        public Vector2 LastSafeGroundPosition { get; private set; }
        public PlayerRig Rig { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public Team Team { get { return Team.Player; } }
        public bool IsAlive { get { return !IsDead; } }
        public Transform Transform { get { return transform; } }
        public float HorizontalInput { get; private set; }
        public float LastJumpTime { get; private set; } = -10f;

        public event Action Died;

        // ---- internals ----
        float coyote, jumpBuffer, invulnTimer, stunTimer, dashTimer, dashCooldown, dropThroughTimer, safeTimer, footstepTimer, afterimageTimer;
        int dashDir = 1;
        bool airDashUsed, jumpCut, wasGrounded, groundIsSolid, hazardRecovering;
        float lastVy;
        int flickerFrame;

        void Awake()
        {
            Instance = this;
            Body = GetComponent<Rigidbody2D>();
            Collider = GetComponent<CapsuleCollider2D>();
            GameEvents.EnemyKilled += OnEnemyKilled;
        }

        void OnDestroy()
        {
            GameEvents.EnemyKilled -= OnEnemyKilled;
            if (Instance == this) Instance = null;
        }

        void OnEnemyKilled(IDamageable e)
        {
            if (!IsDead) AddQi(1);
        }

        // ---------------- public API ----------------
        public void SetControlEnabled(bool enabled)
        {
            ControlEnabled = enabled;
            if (!enabled) HorizontalInput = 0f;
        }

        public void Respawn(Vector2 pos)
        {
            IsDead = false;
            Hp = MaxHp; Qi = 0;
            Facing = 1;
            coyote = jumpBuffer = stunTimer = dashTimer = dashCooldown = dropThroughTimer = 0f;
            invulnTimer = PlayerTuning.SpawnInvuln;
            airDashUsed = false; jumpCut = false; hazardRecovering = false;
            transform.position = pos;
            LastSafeGroundPosition = pos;
            if (Body != null) { Body.simulated = true; Body.linearVelocity = Vector2.zero; Body.gravityScale = 1f; }
            if (Collider != null) Collider.enabled = true;
            Physics2D.IgnoreLayerCollision(Layers.Player, Layers.OneWay, false);
            if (Combat != null) Combat.Reset();
            if (Rig != null) { Rig.ResetPose(); Rig.SetVisible(true); Rig.SetFacing(Facing); }
            ControlEnabled = true;
            GameEvents.RaisePlayerHealthChanged(Hp, MaxHp);
            GameEvents.RaisePlayerQiChanged(Qi, MaxQi);
        }

        public void TeleportSafe()
        {
            transform.position = LastSafeGroundPosition;
            if (Body != null) Body.linearVelocity = Vector2.zero;
            dashTimer = 0f;
            Services.Vfx.DustPuff(LastSafeGroundPosition, 1f);
        }

        public void ApplyHazard(int damage)
        {
            if (IsDead || hazardRecovering) return;
            StartCoroutine(HazardRoutine(damage));
        }

        IEnumerator HazardRoutine(int damage)
        {
            hazardRecovering = true;
            Services.Audio.PlaySfx("spikes");
            Services.Vfx.ScreenFlash(Palette.Red.WithAlpha(0.35f), 0.25f);
            Services.Cam.Shake(0.5f);
            if (invulnTimer <= 0f)
            {
                ApplyDamage(damage, Vector2.zero, 0f, "hazard");
                if (IsDead) { hazardRecovering = false; yield break; }
            }
            bool hadControl = ControlEnabled;
            ControlEnabled = false;
            Combat.CancelAll();
            dashTimer = 0f;
            Body.linearVelocity = Vector2.zero;
            Body.simulated = false;
            Rig.SetVisible(false);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Red, 10);
            yield return new WaitForSeconds(0.35f);
            transform.position = LastSafeGroundPosition;
            Body.simulated = true;
            Body.linearVelocity = Vector2.zero;
            Rig.SetVisible(true);
            Services.Vfx.DustPuff(LastSafeGroundPosition, 1f);
            invulnTimer = Mathf.Max(invulnTimer, 1f);
            ControlEnabled = hadControl;
            hazardRecovering = false;
        }

        public void Heal(int amount)
        {
            if (IsDead) return;
            Hp = Mathf.Min(MaxHp, Hp + amount);
            GameEvents.RaisePlayerHealthChanged(Hp, MaxHp);
        }

        public void AddQi(int n)
        {
            int before = Qi;
            Qi = Mathf.Clamp(Qi + n, 0, MaxQi);
            if (Qi != before)
            {
                GameEvents.RaisePlayerQiChanged(Qi, MaxQi);
                if (Qi > before) Services.Audio.PlaySfx("qi_gain", 0.7f);
            }
        }

        public bool SpendQi(int n)
        {
            if (Qi < n) return false;
            Qi -= n;
            GameEvents.RaisePlayerQiChanged(Qi, MaxQi);
            return true;
        }

        // ---------------- damage resolution ----------------
        public HitOutcome ReceiveAttack(in AttackInfo info)
        {
            HitOutcome outcome = Resolve(info);
            GameEvents.RaisePlayerAttacked(info, outcome);
            return outcome;
        }

        HitOutcome Resolve(in AttackInfo info)
        {
            if (IsDead) return HitOutcome.Ignored;
            if (IsDashing || invulnTimer > 0f)
            {
                if (IsDashing) Services.Vfx.Afterimage(Renderers, Palette.Teal.WithAlpha(0.5f), 0.25f);
                return HitOutcome.Dodged;
            }

            Vector2 hitPoint = info.HitPoint == Vector2.zero ? Center : info.HitPoint;
            Vector2 away = ((Vector2)Center - info.Origin);
            if (away.sqrMagnitude < 0.001f) away = new Vector2(-Facing, 0f);
            away.Normalize();

            if (info.Kind == AttackKind.Parryable)
            {
                if (Combat.PerfectWindow)
                {
                    // PERFECT PARRY
                    Combat.OnParrySuccess(true);
                    AddQi(1);
                    HitStop.Request(0.09f);
                    Services.Cam.Shake(0.35f);
                    Services.Cam.Kick(-away, 0.1f);
                    Services.Vfx.ParrySpark(hitPoint, true);
                    Services.Vfx.FlashLight(hitPoint, Color.white, 3.5f, 5f, 0.15f);
                    Services.Audio.PlaySfx("parry_perfect");
                    Rig.PulseSwordLight(3f, 0.15f);
                    if (info.IsProjectile && info.Payload is IReflectable refl)
                    {
                        Vector2 dir = info.Source != null ? ((Vector2)info.Source.transform.position - Center) : -away;
                        if (dir.sqrMagnitude < 0.01f) dir = new Vector2(Facing, 0f);
                        refl.Reflect(dir.normalized, Team.Player, 30);
                    }
                    GameEvents.RaisePlayerParried(hitPoint, true);
                    GameEvents.RaiseLog("perfect parry vs " + info.Tag);
                    return HitOutcome.Parried;
                }
                if (Combat.BlockWindow)
                {
                    int chip = Mathf.CeilToInt(info.Damage * PlayerTuning.BlockChipFraction);
                    Combat.OnParrySuccess(false);
                    HitStop.Request(0.04f);
                    Services.Vfx.ParrySpark(hitPoint, false);
                    Services.Audio.PlaySfx("parry_block");
                    Body.linearVelocity = new Vector2(away.x * PlayerTuning.BlockKnockback, Mathf.Max(Body.linearVelocity.y, 1.5f));
                    Hp -= chip;
                    GameEvents.RaisePlayerHealthChanged(Hp, MaxHp);
                    GameEvents.RaisePlayerParried(hitPoint, false);
                    GameEvents.RaiseLog("block vs " + info.Tag + " chip=" + chip);
                    if (Hp <= 0) Die();
                    return HitOutcome.Blocked;
                }
            }
            else if (Combat.IsParrying)
            {
                // unblockable attack through a parry stance: teach the rule
                Services.Audio.PlaySfx("parry_fail");
                Services.Vfx.ScreenFlash(Palette.Red.WithAlpha(0.3f), 0.12f);
            }

            ApplyDamage(info.Damage, away, Mathf.Max(PlayerTuning.MinKnockback, info.Knockback), info.Tag);
            return HitOutcome.Hit;
        }

        void ApplyDamage(int damage, Vector2 away, float knockback, string tag)
        {
            damage = Mathf.Max(1, Mathf.RoundToInt(damage * Settings.DamageTakenMul));
            Hp -= damage;
            GameEvents.RaisePlayerHealthChanged(Hp, MaxHp);
            Combat.OnHurt();
            invulnTimer = PlayerTuning.HurtInvuln;
            stunTimer = PlayerTuning.HurtStun;
            dashTimer = 0f;
            if (knockback > 0f && away != Vector2.zero)
                Body.linearVelocity = new Vector2(away.x * knockback, Mathf.Max(3.5f, away.y * knockback * 0.5f));
            HitStop.Request(0.06f);
            Services.Cam.Shake(0.5f);
            Services.Vfx.ChromaticPulse(0.6f, 0.25f);
            Services.Vfx.InkSplatter(Center, away, Palette.Red, 10);
            Services.Vfx.HitSpark(Center, away, Palette.Red, 0.8f);
            Services.Audio.PlaySfx("player_hurt");
            Rig.Flash(Palette.Red, 0.12f);
            Rig.Hurt();
            GameEvents.RaiseLog("player hit by " + tag + " dmg=" + damage + " hp=" + Hp);
            if (Hp <= 0) Die();
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Hp = 0;
            ControlEnabled = false;
            Combat.CancelAll();
            Collider.enabled = false;
            Body.linearVelocity = Vector2.zero;
            Body.simulated = false;
            Services.Vfx.Dissolve(Renderers, Center, Palette.Red);
            Services.Vfx.InkSplatter(Center, Vector2.up, Palette.Red, 20);
            Services.Cam.Shake(0.7f);
            Rig.SetVisible(false);
            Services.Audio.PlaySfx("player_death");
            var d = Died; if (d != null) d();
            GameEvents.RaisePlayerDied();
        }

        // ---------------- update loop ----------------
        void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            if (invulnTimer > 0f) invulnTimer -= dt;
            if (stunTimer > 0f) stunTimer -= dt;
            if (dashCooldown > 0f) dashCooldown -= dt;
            if (coyote > 0f) coyote -= dt;
            if (jumpBuffer > 0f) jumpBuffer -= dt;
            if (dropThroughTimer > 0f)
            {
                dropThroughTimer -= dt;
                if (dropThroughTimer <= 0f) Physics2D.IgnoreLayerCollision(Layers.Player, Layers.OneWay, false);
            }

            var inp = Services.Input;
            bool paused = TimeController.Instance != null && TimeController.Instance.IsPaused;
            bool canAct = ControlEnabled && !IsStunned && !hazardRecovering && !paused && inp != null;
            HorizontalInput = canAct ? inp.Horizontal : 0f;
            if (Mathf.Abs(HorizontalInput) < 0.2f) HorizontalInput = 0f;
            if (Combat.LocksMovement && IsGrounded) HorizontalInput = 0f;

            if (canAct && HorizontalInput != 0f && !Combat.LocksFacing && !IsDashing)
                Facing = HorizontalInput > 0f ? 1 : -1;

            if (canAct)
            {
                if (inp.JumpPressed) jumpBuffer = PlayerTuning.JumpBuffer;
                if (inp.DashPressed) TryDash();
                Combat.Tick(dt);
            }

            // jump (buffered + coyote)
            if (canAct && jumpBuffer > 0f && (IsGrounded || coyote > 0f) && !IsDashing && !Combat.BlocksJump)
            {
                if (inp.Vertical < -0.5f && IsGrounded && !groundIsSolid)
                {
                    dropThroughTimer = PlayerTuning.DropThroughTime;
                    Physics2D.IgnoreLayerCollision(Layers.Player, Layers.OneWay, true);
                    jumpBuffer = 0f;
                }
                else DoJump();
            }
            if (!IsDashing && !jumpCut && Body.linearVelocity.y > 0f && canAct && !inp.JumpHeld && Time.time - LastJumpTime < 0.45f && Time.time - LastJumpTime > 0.04f)
            {
                jumpCut = true;
                Body.linearVelocity = new Vector2(Body.linearVelocity.x, Body.linearVelocity.y * PlayerTuning.JumpCutMultiplier);
            }

            // dash afterimages
            if (IsDashing)
            {
                afterimageTimer -= dt;
                if (afterimageTimer <= 0f)
                {
                    afterimageTimer = PlayerTuning.AfterimageInterval;
                    Services.Vfx.Afterimage(Renderers, Palette.Teal.WithAlpha(0.6f), 0.28f);
                }
            }

            // footsteps
            if (IsGrounded && Mathf.Abs(Body.linearVelocity.x) > 3f && !Combat.LocksMovement)
            {
                footstepTimer -= dt;
                if (footstepTimer <= 0f)
                {
                    footstepTimer = PlayerTuning.FootstepInterval;
                    Services.Audio.PlaySfx("footstep", 0.45f, 0.15f);
                    if (UnityEngine.Random.value < 0.35f) Services.Vfx.DustPuff(Position, 0.5f);
                }
            }
            else footstepTimer = 0.05f;

            // i-frame flicker
            if (invulnTimer > 0f && !IsDashing)
            {
                flickerFrame++;
                Rig.SetVisible((Mathf.FloorToInt(Time.unscaledTime * 24f) & 1) == 0);
            }
            else if (!hazardRecovering) Rig.SetVisible(true);

            Rig.SetFacing(Facing);
            Rig.Animate(this, dt);
        }

        void DoJump()
        {
            Body.linearVelocity = new Vector2(Body.linearVelocity.x, PlayerTuning.JumpVelocity);
            jumpBuffer = 0f; coyote = 0f; jumpCut = false;
            LastJumpTime = Time.time;
            IsGrounded = false;
            Services.Audio.PlaySfx("jump", 0.8f);
            Rig.Stretch();
            Services.Vfx.DustPuff(Position, 0.7f);
        }

        void TryDash()
        {
            if (IsDashing || dashCooldown > 0f || IsHealing || Combat.IsQiBlasting || hazardRecovering) return;
            if (!IsGrounded && airDashUsed) return;
            if (Combat.IsParrying) return;
            dashDir = HorizontalInput != 0f ? (HorizontalInput > 0f ? 1 : -1) : Facing;
            Facing = dashDir;
            dashTimer = PlayerTuning.DashDuration;
            dashCooldown = PlayerTuning.DashCooldown;
            airDashUsed = !IsGrounded;
            afterimageTimer = 0f;
            invulnTimer = Mathf.Max(invulnTimer, PlayerTuning.DashDuration + PlayerTuning.DashInvulnExtra);
            Combat.CancelAttack();
            Services.Audio.PlaySfx("dash");
            Services.Cam.Kick(new Vector2(dashDir, 0f), 0.12f);
            Services.Vfx.DustPuff(Position, 1f);
            Rig.Dash();
        }

        void FixedUpdate()
        {
            if (IsDead || !Body.simulated) return;
            float dt = Time.fixedDeltaTime;
            Vector2 v = Body.linearVelocity;

            // ground check
            Vector2 feet = (Vector2)transform.position + new Vector2(0f, 0.04f);
            var hit = Physics2D.OverlapBox(feet, new Vector2(0.48f, 0.12f), 0f, Layers.GroundMask);
            bool grounded = hit != null && v.y <= 0.5f;
            groundIsSolid = hit != null && hit.gameObject.layer == Layers.Ground;
            if (grounded && !IsGrounded)
            {
                // landed
                airDashUsed = false;
                if (lastVy < -PlayerTuning.LandSoundFallSpeed)
                {
                    Services.Audio.PlaySfx("land", 0.8f);
                    Services.Vfx.DustPuff(Position, 1f);
                    Rig.Squash();
                    if (lastVy < -16f) Services.Cam.Shake(0.15f);
                }
            }
            if (IsGrounded && !grounded) coyote = PlayerTuning.CoyoteTime;
            IsGrounded = grounded;

            if (IsDashing)
            {
                dashTimer -= dt;
                v = new Vector2(dashDir * PlayerTuning.DashSpeed, 0f);
                Body.gravityScale = 0f;
                if (dashTimer <= 0f) { v.x = dashDir * PlayerTuning.MaxSpeed * 0.9f; Body.gravityScale = 1f; }
            }
            else
            {
                Body.gravityScale = (!grounded && Mathf.Abs(v.y) < PlayerTuning.ApexThreshold && v.y > -0.5f) ? PlayerTuning.ApexGravityScale : 1f;
                if (!IsStunned)
                {
                    if (Combat.IsAttacking && grounded && Combat.LungeActive)
                        v.x = Facing * PlayerTuning.AttackLunge;
                    else
                    {
                        float target = HorizontalInput * PlayerTuning.MaxSpeed;
                        float accel = HorizontalInput != 0f ? (grounded ? PlayerTuning.GroundAccel : PlayerTuning.AirAccel) : PlayerTuning.Decel;
                        if (Combat.LocksMovement && grounded) target = 0f;
                        v.x = Mathf.MoveTowards(v.x, target, accel * dt);
                    }
                }
                if (v.y < -PlayerTuning.MaxFallSpeed) v.y = -PlayerTuning.MaxFallSpeed;
            }
            Body.linearVelocity = v;
            lastVy = v.y;

            // safe ground bookkeeping
            if (grounded && groundIsSolid && Mathf.Abs(v.y) < 0.5f)
            {
                safeTimer -= dt;
                if (safeTimer <= 0f)
                {
                    safeTimer = PlayerTuning.SafeRecordInterval;
                    Vector2 p = transform.position;
                    bool l = Physics2D.Raycast(p + new Vector2(-0.4f, 0.1f), Vector2.down, 0.35f, 1 << Layers.Ground).collider != null;
                    bool r = Physics2D.Raycast(p + new Vector2(0.4f, 0.1f), Vector2.down, 0.35f, 1 << Layers.Ground).collider != null;
                    bool hazardNear = Physics2D.OverlapBox(p + new Vector2(0f, 0.3f), new Vector2(2.2f, 1f), 0f, 1 << Layers.Hazard) != null;
                    if (l && r && !hazardNear) LastSafeGroundPosition = p;
                }
            }
            wasGrounded = grounded;
        }
    }
}
