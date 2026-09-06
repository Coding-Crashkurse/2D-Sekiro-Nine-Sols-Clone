using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Attack combo, parry stance, Qi blast and heal. Plain class driven by PlayerController.Tick.</summary>
    public class PlayerCombat
    {
        public struct HitDef { public float Startup, Active, Recovery; public int Damage; public float Knockback; public float ArcAngle; public HitDef(float s, float a, float r, int d, float k, float arc) { Startup = s; Active = a; Recovery = r; Damage = d; Knockback = k; ArcAngle = arc; } }

        public static readonly HitDef[] Hits =
        {
            new HitDef(0.07f, 0.08f, 0.16f, 10, 3f, -20f),
            new HitDef(0.07f, 0.08f, 0.16f, 10, 3f, 25f),
            new HitDef(0.10f, 0.10f, 0.26f, 18, 6f, 260f),
        };

        readonly PlayerController c;
        readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();

        public bool IsAttacking { get; private set; }
        public bool LungeActive { get; private set; }
        public bool IsParrying { get; private set; }
        public bool PerfectWindow { get; private set; }
        public bool BlockWindow { get; private set; }
        public bool IsHealing { get; private set; }
        public bool IsQiBlasting { get; private set; }
        public int ComboIndex { get; private set; }

        public bool LocksMovement { get { return IsParrying || IsHealing || IsQiBlasting || IsAttacking; } }
        public bool LocksFacing { get { return IsAttacking || IsParrying || IsHealing || IsQiBlasting; } }
        public bool BlocksJump { get { return IsParrying || IsHealing || IsQiBlasting || (IsAttacking && !inRecovery); } }

        Coroutine attackCo, parryCo, healCo, qiCo;
        bool comboQueued, inRecovery, parrySuccess, parryPerfect, healInterrupted;
        float comboResetTimer, parryRecoveryTimer, attackInputLock;
        float attackBuffer, parryBuffer;

        public PlayerCombat(PlayerController controller) { c = controller; }

        public void Reset()
        {
            CancelAll();
            ComboIndex = 0; comboQueued = false; comboResetTimer = 0f; parryRecoveryTimer = 0f;
            attackInputLock = 0f;
        }

        public void CancelAll()
        {
            ClearInputBuffers();
            CancelAttack();
            if (parryCo != null) { c.StopCoroutine(parryCo); parryCo = null; }
            if (healCo != null) { c.StopCoroutine(healCo); healCo = null; }
            if (qiCo != null) { c.StopCoroutine(qiCo); qiCo = null; }
            IsParrying = PerfectWindow = BlockWindow = IsHealing = IsQiBlasting = false;
            c.Rig.HideParryGlyph();
            c.Rig.EndParry();
            c.Rig.EndHeal();
            c.Rig.EndQiBlast();
        }

        public void CancelAttack()
        {
            if (attackCo != null) { c.StopCoroutine(attackCo); attackCo = null; }
            IsAttacking = false; LungeActive = false; inRecovery = false; comboQueued = false;
            c.Rig.EndAttack();
        }

        public void Tick(float dt)
        {
            var inp = Services.Input;
            if (comboResetTimer > 0f) { comboResetTimer -= dt; if (comboResetTimer <= 0f && !IsAttacking) ComboIndex = 0; }
            if (parryRecoveryTimer > 0f) parryRecoveryTimer -= dt;
            if (attackInputLock > 0f) attackInputLock -= dt;

            attackBuffer = Mathf.Max(0f, attackBuffer - dt);
            parryBuffer = Mathf.Max(0f, parryBuffer - dt);
            if (inp.AttackPressed) attackBuffer = PlayerTuning.ActionBuffer;
            if (inp.ParryPressed) parryBuffer = PlayerTuning.ActionBuffer;
            if (c.IsStunned) return;

            if (parryBuffer > 0f && TryParry()) ClearInputBuffers();
            if (attackBuffer > 0f && TryAttack()) attackBuffer = 0f;
            if (inp.QiBlastPressed) TryQiBlast();
            if (inp.HealPressed) TryHeal();
        }

        public void ClearInputBuffers() { attackBuffer = parryBuffer = 0f; }

        // ---------------- attack ----------------
        bool TryAttack()
        {
            if (IsParrying || IsHealing || IsQiBlasting || c.IsDashing) return false;
            if (IsAttacking) { comboQueued = true; return true; }
            if (attackInputLock > 0f) return false;
            int idx = c.IsGrounded ? ComboIndex : 0;
            if (idx >= Hits.Length) idx = 0;
            attackCo = c.StartCoroutine(AttackRoutine(idx));
            return true;
        }

        IEnumerator AttackRoutine(int idx)
        {
            IsAttacking = true; inRecovery = false; comboQueued = false;
            hitThisSwing.Clear();
            var h = Hits[idx];
            c.Rig.PlayAttack(idx, h.Startup + h.Active);
            Services.Audio.PlaySfx("sword_swing_" + (idx + 1), 0.9f, 0.08f);
            LungeActive = true;

            float t = 0f;
            while (t < h.Startup) { t += Time.deltaTime; yield return null; }

            Vector2 arcPos = c.Center + new Vector2(c.Facing * 1.0f, 0.15f);
            float arcAngle = idx == 2 ? 0f : h.ArcAngle;
            Services.Vfx.SlashArc(arcPos, arcAngle, c.Facing < 0, Palette.PlayerSlash, idx == 2 ? 1.35f : 1f);

            t = 0f;
            while (t < h.Active)
            {
                DoHitScan(idx, h);
                t += Time.deltaTime;
                yield return null;
            }
            LungeActive = false;
            inRecovery = true;

            t = 0f;
            while (t < h.Recovery)
            {
                if (comboQueued && idx < Hits.Length - 1 && t > 0.05f) break;
                t += Time.deltaTime;
                yield return null;
            }

            c.Rig.EndAttack();
            IsAttacking = false; inRecovery = false;
            if (comboQueued && idx < Hits.Length - 1 && c.IsGrounded)
            {
                ComboIndex = idx + 1;
                comboQueued = false;
                attackCo = c.StartCoroutine(AttackRoutine(ComboIndex));
                yield break;
            }
            comboQueued = false;
            ComboIndex = idx < Hits.Length - 1 ? idx + 1 : 0;
            comboResetTimer = PlayerTuning.ComboLinkWindow;
            attackInputLock = idx == Hits.Length - 1 ? 0.12f : 0f;
            attackCo = null;
        }

        void DoHitScan(int idx, HitDef h)
        {
            Vector2 center = c.Center + new Vector2(c.Facing * 1.0f, 0.1f);
            var cols = Physics2D.OverlapBoxAll(center, new Vector2(1.9f, 1.5f), 0f, Layers.EnemyMask);
            for (int i = 0; i < cols.Length; i++)
            {
                var d = cols[i].GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || d.Team != Team.Enemy || hitThisSwing.Contains(d)) continue;
                hitThisSwing.Add(d);
                var info = new AttackInfo
                {
                    Source = c.gameObject, Team = Team.Player, Damage = Mathf.RoundToInt(h.Damage * Progression.DamageMul), Origin = c.Center,
                    HitPoint = Vector2.Lerp(c.Center, d.Center, 0.6f), Kind = AttackKind.Parryable,
                    Knockback = h.Knockback, IsProjectile = false, Payload = null, Tag = "player_combo" + (idx + 1)
                };
                var outcome = d.ReceiveAttack(info);
                if (outcome == HitOutcome.Hit)
                {
                    HitStop.Request(idx == 2 ? 0.07f : 0.045f);
                    Services.Cam.Kick(new Vector2(c.Facing, 0f), idx == 2 ? 0.22f : 0.15f);
                    Services.Audio.PlaySfx("sword_hit", 1f, 0.1f);
                }
            }
        }

        // ---------------- parry ----------------
        bool TryParry()
        {
            if (IsParrying || IsHealing || IsQiBlasting || c.IsDashing || parryRecoveryTimer > 0f) return false;
            CancelAttack();
            parryCo = c.StartCoroutine(ParryRoutine());
            return true;
        }

        IEnumerator ParryRoutine()
        {
            IsParrying = true; parrySuccess = false; parryPerfect = false;
            PerfectWindow = true; BlockWindow = false;
            c.Rig.PlayParry();
            c.Rig.ShowParryGlyph(false);
            Services.Audio.PlaySfx("parry_ready", 0.45f, 0.1f);

            float t = 0f;
            float perfectWindow = Settings.ParryPerfectWindow;
            while (t < perfectWindow && !parrySuccess) { t += Time.deltaTime; yield return null; }
            PerfectWindow = false;
            if (!parrySuccess)
            {
                BlockWindow = true;
                c.Rig.ShowParryGlyph(true);
                t = 0f;
                while (t < PlayerTuning.ParryBlockWindow && !parrySuccess) { t += Time.deltaTime; yield return null; }
                BlockWindow = false;
            }

            if (parrySuccess)
            {
                c.Rig.ParrySuccessPose(parryPerfect);
                t = 0f;
                while (t < 0.1f) { t += Time.deltaTime; yield return null; }
                parryRecoveryTimer = 0f;
            }
            else
            {
                c.Rig.HideParryGlyph();
                parryRecoveryTimer = PlayerTuning.ParryRecovery;
                t = 0f;
                while (t < PlayerTuning.ParryRecovery) { t += Time.deltaTime; yield return null; }
            }
            c.Rig.HideParryGlyph();
            c.Rig.EndParry();
            IsParrying = false; PerfectWindow = false; BlockWindow = false;
            parryCo = null;
        }

        /// <summary>Called by PlayerController when an attack was parried/blocked during the stance.</summary>
        public void OnParrySuccess(bool perfect)
        {
            parrySuccess = true; parryPerfect = perfect;
            PerfectWindow = false; BlockWindow = false;
        }

        // ---------------- qi blast ----------------
        /// <summary>Nearest enemy whose guard is broken and that is close enough to finish.</summary>
        AshenSol.Enemies.EnemyBase FindExecuteTarget()
        {
            AshenSol.Enemies.EnemyBase best = null;
            float bestDist = float.MaxValue;
            var all = AshenSol.Enemies.EnemyBase.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || !e.CanBeExecuted || !e.gameObject.activeInHierarchy) continue;
                Vector2 d = e.Center - c.Center;
                if (Mathf.Abs(d.x) > PlayerTuning.ExecuteRangeX || Mathf.Abs(d.y) > PlayerTuning.ExecuteRangeY) continue;
                if (Mathf.Abs(d.x) < bestDist) { bestDist = Mathf.Abs(d.x); best = e; }
            }
            return best;
        }

        void TryQiBlast()
        {
            if (IsParrying || IsHealing || IsQiBlasting || c.IsDashing) return;

            // I is contextual: next to a broken guard it becomes the execution
            var victim = FindExecuteTarget();
            if (victim != null && c.Qi >= 1)
            {
                CancelAttack();
                c.SpendQi(1);
                qiCo = c.StartCoroutine(ExecuteRoutine(victim));
                return;
            }
            if (c.Qi < 1)
            {
                Services.Audio.PlaySfx("ui_move", 0.6f);
                c.Rig.Flash(Palette.Teal.WithAlpha(0.5f), 0.1f);
                return;
            }
            CancelAttack();
            c.SpendQi(1);
            qiCo = c.StartCoroutine(QiBlastRoutine());
        }

        /// <summary>The payoff for breaking a guard: a single devastating Qi-charged strike.</summary>
        IEnumerator ExecuteRoutine(AshenSol.Enemies.EnemyBase victim)
        {
            IsQiBlasting = true;
            c.FaceTowards(victim.Center.x);
            c.Rig.PlayAttack(2, 0.22f);
            c.Rig.PulseSwordLight(5f, 0.5f);
            Services.Audio.PlaySfx("sword_swing_3", 1f, 0.03f);
            Services.Vfx.ChromaticPulse(0.5f, 0.4f);
            TimeController.Instance.SlowMo(0.35f, 0.35f);

            float t = 0f;
            while (t < 0.22f) { t += ExecutionDelta; yield return null; }
            while (TimeController.Instance != null && TimeController.Instance.IsPaused) yield return null;

            if (victim != null && victim.IsAlive)
            {
                Vector2 at = victim.Center;
                Services.Vfx.SlashArc(at, 0f, c.Facing < 0, Palette.Gold, 2.6f);
                Services.Vfx.FlashLight(at, Palette.Gold, 5f, 7f, 0.35f);
                Services.Vfx.ScreenFlash(Color.white.WithAlpha(0.3f), 0.14f);
                Services.Vfx.Shockwave(at, 3f, Palette.Gold);
                Services.Vfx.Embers(at, 34, Palette.Gold);
                Services.Vfx.FloatingText(at + new Vector2(0f, 1.4f), "EXECUTE", Palette.Gold, 1.7f);
                Services.Cam.Shake(0.8f);
                Services.Cam.Kick(new Vector2(c.Facing, 0f), 0.35f);
                HitStop.Request(0.14f);
                Services.Audio.PlaySfx("execute");
                var info = new AttackInfo
                {
                    Source = c.gameObject, Team = Team.Player, Damage = Mathf.RoundToInt(victim.ExecuteDamage * Progression.DamageMul), Origin = c.Center,
                    HitPoint = at, Kind = AttackKind.Unblockable, Knockback = 4f, Tag = "execute"
                };
                victim.ReceiveAttack(info);
                GameEvents.RaiseLog("execution on " + victim.DisplayName + " for " + victim.ExecuteDamage);
            }

            while (t < PlayerTuning.ExecuteDuration) { t += ExecutionDelta; yield return null; }
            while (TimeController.Instance != null && TimeController.Instance.IsPaused) yield return null;
            c.Rig.EndAttack();
            c.Rig.EndQiBlast();
            IsQiBlasting = false;
            qiCo = null;
        }

        // The cinematic ignores slow motion, but must still freeze in menus.
        static float ExecutionDelta { get { return TimeController.Instance != null && TimeController.Instance.IsPaused ? 0f : Time.unscaledDeltaTime; } }

        IEnumerator QiBlastRoutine()
        {
            IsQiBlasting = true;
            c.Rig.PlayQiBlast();
            float t = 0f;
            while (t < 0.12f) { t += Time.deltaTime; yield return null; }

            Vector2 center = c.Center + new Vector2(c.Facing * 1.2f, 0f);
            Services.Vfx.Shockwave(c.Position, PlayerTuning.QiBlastRadius, Palette.Teal);
            Services.Vfx.FlashLight(center, Palette.Teal, 3f, 5f, 0.25f);
            Services.Vfx.Embers(center, 24, Palette.Teal);
            Services.Cam.Shake(0.5f);
            HitStop.Request(0.05f);
            Services.Audio.PlaySfx("qi_blast");
            c.Rig.PulseSwordLight(4f, 0.3f);

            var cols = Physics2D.OverlapCircleAll(center, PlayerTuning.QiBlastRadius, Layers.EnemyMask);
            var seen = new HashSet<IDamageable>();
            for (int i = 0; i < cols.Length; i++)
            {
                var d = cols[i].GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || d.Team != Team.Enemy || seen.Contains(d)) continue;
                seen.Add(d);
                var info = new AttackInfo
                {
                    Source = c.gameObject, Team = Team.Player, Damage = Mathf.RoundToInt(PlayerTuning.QiBlastDamage * Progression.DamageMul), Origin = c.Center,
                    HitPoint = d.Center, Kind = AttackKind.Unblockable, Knockback = PlayerTuning.QiBlastKnockback, Tag = "qi_blast"
                };
                d.ReceiveAttack(info);
            }
            GameEvents.RaiseLog("qi blast hits=" + seen.Count);

            while (t < PlayerTuning.QiBlastDuration) { t += Time.deltaTime; yield return null; }
            c.Rig.EndQiBlast();
            IsQiBlasting = false;
            qiCo = null;
        }

        // ---------------- heal ----------------
        void TryHeal()
        {
            if (IsParrying || IsHealing || IsQiBlasting || IsAttacking || c.IsDashing || !c.IsGrounded) return;
            if (c.Qi < 1 || c.Hp >= c.MaxHp)
            {
                Services.Audio.PlaySfx("ui_move", 0.5f);
                return;
            }
            healCo = c.StartCoroutine(HealRoutine());
        }

        IEnumerator HealRoutine()
        {
            IsHealing = true; healInterrupted = false;
            c.Rig.PlayHeal();
            Services.Audio.PlaySfx("heal", 0.9f);
            Services.Vfx.FlashLight(c.Center, Palette.Teal, 1.5f, 3f, PlayerTuning.HealChannel);
            float t = 0f;
            while (t < PlayerTuning.HealChannel && !healInterrupted) { t += Time.deltaTime; yield return null; }
            if (!healInterrupted)
            {
                c.SpendQi(1);
                c.Heal(Settings.HealAmount);
                Services.Vfx.Embers(c.Center, 30, Palette.Teal);
                Services.Vfx.FlashLight(c.Center, Palette.Teal, 3f, 4f, 0.3f);
                c.Rig.Flash(Palette.Teal.WithAlpha(0.6f), 0.2f);
                GameEvents.RaiseLog("healed hp=" + c.Hp);
            }
            c.Rig.EndHeal();
            IsHealing = false;
            healCo = null;
        }

        /// <summary>Damage interrupts channels and attacks.</summary>
        public void OnHurt()
        {
            ClearInputBuffers();
            healInterrupted = true;
            if (IsHealing) { if (healCo != null) c.StopCoroutine(healCo); healCo = null; IsHealing = false; c.Rig.EndHeal(); }
            CancelAttack();
            if (IsQiBlasting) { if (qiCo != null) c.StopCoroutine(qiCo); qiCo = null; IsQiBlasting = false; c.Rig.EndQiBlast(); }
            if (IsParrying) { if (parryCo != null) c.StopCoroutine(parryCo); parryCo = null; IsParrying = PerfectWindow = BlockWindow = false; c.Rig.HideParryGlyph(); c.Rig.EndParry(); }
        }
    }
}
