using System.IO;
using UnityEngine;
using AshenSol.Player;
using AshenSol.Enemies;
using AshenSol.Boss;

namespace AshenSol.Core
{
    /// <summary>Scripted play-through bot (-autopilot). Drives the real player through ScriptedInput, takes screenshots
    /// (-screenshotDir), logs gameplay events and quits after -quitAfter seconds or shortly after victory.</summary>
    public class AutoPilot : MonoBehaviour
    {
        ScriptedInput input;
        string dir;
        float quitAfter;
        StreamWriter log;
        int shots;
        float shotTimer = 3f;
        float elapsed;
        bool finished;

        bool reachedGate, bossStarted, bossDefeated, gateOpened;
        float victoryAt = -1f, diedAt = -1f;
        int deaths, parries, perfect;
        bool firstParryShot;

        float attackCd, parryCd, dashCd, qiCd, healCd, confirmCd;
        float stuckTime; float lastX;
        float pendingShotAt = -1f; string pendingLabel;

        void Awake()
        {
            dir = CmdArgs.Get("-screenshotDir", Path.Combine(Application.persistentDataPath, "Screenshots"));
            try
            {
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "autopilot_log.txt"), false) { AutoFlush = true };
            }
            catch (System.Exception e) { Debug.LogWarning("[AutoPilot] cannot open log: " + e.Message); }
            quitAfter = CmdArgs.GetFloat("-quitAfter", 240f);
            input = new ScriptedInput();
            if (InputRouter.Instance != null) InputRouter.Instance.SetProvider(input);

            GameEvents.Log += OnGameLog;
            GameEvents.PlayerParried += OnParried;
            GameEvents.PlayerDied += OnDied;
            GameEvents.CheckpointActivated += OnCheckpoint;
            GameEvents.GateOpened += OnGateOpened;
            GameEvents.LevelBuilt += OnLevelBuilt;
            GameEvents.BossFightStarted += OnBossStarted;
            GameEvents.BossPhaseChanged += OnBossPhase;
            GameEvents.BossDefeated += OnBossDefeated;
            GameEvents.StateChanged += OnState;
            Log("AutoPilot start dir=" + dir + " quitAfter=" + quitAfter);
        }

        void OnDestroy()
        {
            GameEvents.Log -= OnGameLog;
            GameEvents.PlayerParried -= OnParried;
            GameEvents.PlayerDied -= OnDied;
            GameEvents.CheckpointActivated -= OnCheckpoint;
            GameEvents.GateOpened -= OnGateOpened;
            GameEvents.LevelBuilt -= OnLevelBuilt;
            GameEvents.BossFightStarted -= OnBossStarted;
            GameEvents.BossPhaseChanged -= OnBossPhase;
            GameEvents.BossDefeated -= OnBossDefeated;
            GameEvents.StateChanged -= OnState;
            if (log != null) { log.Close(); log = null; }
        }

        void Log(string s)
        {
            string line = string.Format("[{0,7:F2}] {1}", elapsed, s);
            try { if (log != null) log.WriteLine(line); } catch { }
        }

        void OnGameLog(string s) { Log(s); }
        void OnParried(Vector2 p, bool perf)
        {
            parries++; if (perf) perfect++;
            if (!firstParryShot) { firstParryShot = true; ScheduleShot("first_parry", 0.05f); }
        }
        void OnDied() { deaths++; diedAt = elapsed; ScheduleShot("death", 0.6f); }
        void OnCheckpoint(string id) { Log("checkpoint " + id); ScheduleShot("checkpoint_" + id, 0.5f); }
        void OnGateOpened() { gateOpened = true; Log("gate opened"); ScheduleShot("gate_open", 1.0f); }
        void OnLevelBuilt(LevelId id)
        {
            Log("level built " + id);
            if (id == LevelId.BossArena) reachedGate = true;
            ScheduleShot("level_" + id, 2.5f);
        }
        void OnBossStarted(string n, string s) { bossStarted = true; Log("boss fight started: " + n); ScheduleShot("boss_intro", 1.6f); }
        void OnBossPhase(int p) { Log("boss phase " + p); ScheduleShot("boss_phase" + p, 0.8f); }
        void OnBossDefeated() { bossDefeated = true; victoryAt = elapsed; Log("boss defeated"); ScheduleShot("victory", 3.5f); }
        void OnState(GameState s) { }

        void ScheduleShot(string label, float delay)
        {
            if (pendingShotAt >= 0f && elapsed + delay > pendingShotAt) return; // keep the earlier one
            pendingShotAt = elapsed + delay; pendingLabel = label;
        }

        void Shot(string label)
        {
            if (shots >= 40) return;
            shots++;
            string file = Path.Combine(dir, string.Format("shot_{0:D2}_{1}.png", shots, label));
            try { ScreenCapture.CaptureScreenshot(file); Log("screenshot " + Path.GetFileName(file)); } catch (System.Exception e) { Log("screenshot failed: " + e.Message); }
            shotTimer = 0f;
        }

        void Update()
        {
            if (finished) return;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt; shotTimer += dt;
            attackCd -= dt; parryCd -= dt; dashCd -= dt; qiCd -= dt; healCd -= dt; confirmCd -= dt;

            if (pendingShotAt >= 0f && elapsed >= pendingShotAt) { Shot(pendingLabel); pendingShotAt = -1f; }
            else if (shotTimer >= 6f) Shot("t" + Mathf.RoundToInt(elapsed));

            if (elapsed >= quitAfter || (victoryAt >= 0f && elapsed >= victoryAt + 6f)) { Finish(); return; }

            input.Horizontal = 0f; input.Vertical = 0f; input.JumpHeld = false; input.ParryHeld = false;
            var flow = GameFlow.Instance;
            if (flow == null || flow.Busy) return;

            switch (flow.State)
            {
                case GameState.Title:
                    if (confirmCd <= 0f) { input.Confirm(); confirmCd = 1f; }
                    break;
                case GameState.Level1:
                case GameState.BossArena:
                    if (flow.DeathScreenShowing)
                    {
                        if (diedAt >= 0f && elapsed > diedAt + 2.2f && confirmCd <= 0f) { input.Confirm(); confirmCd = 1f; }
                    }
                    else Brain();
                    break;
            }
        }

        void Brain()
        {
            var p = PlayerController.Instance;
            if (p == null || p.IsDead || !p.ControlEnabled) return;

            // threats: enemy projectiles heading at us
            Projectile threat = null;
            for (int i = 0; i < Projectile.Active.Count; i++)
            {
                var pr = Projectile.Active[i];
                if (pr == null || pr.Team != Team.Enemy) continue;
                Vector2 d = (Vector2)pr.transform.position - p.Center;
                if (d.magnitude < 1.7f && Vector2.Dot(pr.Velocity, -d) > 0f) { threat = pr; break; }
            }
            if (threat != null && parryCd <= 0f) { input.Parry(); parryCd = 0.45f; return; }

            if (GroundShockwave.AnyApproaching(p.Center, 1.9f) && p.IsGrounded) { input.Jump(); return; }

            // nearest enemy within engagement box
            EnemyBase target = null; float best = 999f;
            for (int i = 0; i < EnemyBase.All.Count; i++)
            {
                var e = EnemyBase.All[i];
                if (e == null || !e.IsAlive || !e.gameObject.activeInHierarchy) continue;
                Vector2 d = e.Center - p.Center;
                if (Mathf.Abs(d.x) <= 4.8f && Mathf.Abs(d.y) <= 3.2f && Mathf.Abs(d.x) < best) { best = Mathf.Abs(d.x); target = e; }
            }

            if (target != null)
            {
                int dirTo = target.Center.x > p.Center.x ? 1 : -1;
                float dist = Mathf.Abs(target.Center.x - p.Center.x);
                var boss = target as BossController;

                if (target.IsTelegraphing)
                {
                    if (target.TelegraphKind == AttackKind.Parryable)
                    {
                        if (target.TimeUntilStrike <= 0.14f && parryCd <= 0f) { input.Parry(); parryCd = 0.3f; }
                        else if (dist > 2.6f) input.Horizontal = dirTo;
                    }
                    else
                    {
                        input.Horizontal = -dirTo;
                        if (target.TimeUntilStrike <= 0.32f && dashCd <= 0f) { input.Dash(); dashCd = 0.6f; }
                    }
                    return;
                }
                if (boss != null && boss.CurrentAttack == "Slam")
                {
                    input.Horizontal = -dirTo;
                    if (dashCd <= 0f) { input.Dash(); dashCd = 0.6f; }
                    return;
                }
                if (p.Qi > 0 && target.InternalDamage >= 20 && dist < 2.6f && qiCd <= 0f) { input.QiBlast(); qiCd = 1.2f; return; }
                if (p.Hp < 40 && p.Qi > 0 && dist > 3.2f && healCd <= 0f) { input.Heal(); healCd = 2.5f; return; }

                if (target.Type == EnemyType.WatcherDrone && target.Center.y > p.Center.y + 2.2f)
                {
                    // can't reach a hovering drone: hold position and let the bolt-parry logic reflect its shots
                    return;
                }
                if (dist > 1.75f)
                {
                    input.Horizontal = dirTo;
                    if (target.Center.y > p.Center.y + 1.4f && p.IsGrounded) input.Jump();
                }
                else
                {
                    if (attackCd <= 0f) { input.Attack(); attackCd = 0.22f; }
                }
                return;
            }

            // traverse: walk right, jump over gaps / walls / spikes
            input.Horizontal = 1f;
            if (p.IsGrounded && ShouldJump(p)) input.Jump();

            // stuck detection
            if (p.IsGrounded && Mathf.Abs(p.Position.x - lastX) < 0.02f) stuckTime += Time.unscaledDeltaTime; else stuckTime = 0f;
            lastX = p.Position.x;
            if (stuckTime > 1.2f && p.IsGrounded) input.Jump();
            if (stuckTime > 3f && dashCd <= 0f) { input.Dash(); dashCd = 0.6f; stuckTime = 0f; }
        }

        bool ShouldJump(PlayerController p)
        {
            Vector2 feet = p.Position;
            Vector2 center = p.Center;
            var wall = Physics2D.Raycast(center, Vector2.right, 0.95f, Layers.GroundMask);
            if (wall.collider != null) return true;
            var gap = Physics2D.Raycast(new Vector2(feet.x + 1.35f, feet.y + 0.3f), Vector2.down, 3.0f, Layers.GroundMask | (1 << Layers.Hazard));
            if (gap.collider == null) return true;
            if (gap.collider.gameObject.layer == Layers.Hazard) return true;
            var haz = Physics2D.Raycast(new Vector2(feet.x + 0.3f, feet.y + 0.25f), Vector2.right, 2.4f, 1 << Layers.Hazard);
            if (haz.collider != null) return true;
            return false;
        }

        void Finish()
        {
            finished = true;
            Log(string.Format("AUTOPILOT RESULT: reachedGate={0} gateOpened={1} bossStarted={2} bossDefeated={3} deaths={4} parries={5} perfect={6} time={7:F1} exceptions={8}",
                reachedGate, gateOpened, bossStarted, bossDefeated, deaths, parries, perfect, elapsed, GameManager.ExceptionCount));
            for (int i = 0; i < GameManager.RecentErrors.Count && i < 20; i++) Log("ERR: " + GameManager.RecentErrors[i]);
            if (log != null) { log.Flush(); log.Close(); log = null; }
            Application.Quit();
        }
    }
}
