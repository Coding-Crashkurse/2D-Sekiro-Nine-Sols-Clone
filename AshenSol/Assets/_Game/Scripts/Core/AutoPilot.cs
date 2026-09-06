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

        float attackCd, parryCd, dashCd, qiCd, healCd, confirmCd, interactCd;
        int swings;   // every fourth swing is the charged one, so the move stays covered by runs
        float stuckTime; float lastX;
        // forward-progress watchdog: jumping at a wall keeps the bot ungrounded, so the grounded
        // stuck check never fires. Watch the furthest x instead and fall back to the nearest vent.
        float bestX = -999f, noProgress, ventUntil; Vector2 ventAt;
        float pendingShotAt = -1f; string pendingLabel;
        float statusTimer; string brainState = "-";
        int climbShots; float climbShotTimer;   // -climbShots N captures the climb cycle frame by frame
        float introSeenUntil;   // watch a slice of the intro, then skip so runs stay short
        float killAt = -1f;   // -killPlayerAt <sec>: die once on purpose to test the ash drop
        float creditsTime, victoryHold;   // how much of the end roll to sit through before skipping
        float jumpHold;   // the bot must HOLD jump — tapping triggers the variable-height jump cut

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
            introSeenUntil = CmdArgs.GetFloat("-introSeconds", 12f);
            victoryHold = CmdArgs.GetFloat("-victoryHold", 6f);
            killAt = CmdArgs.GetFloat("-killPlayerAt", -1f);
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
            ScheduleShot("title", 3.0f);
            confirmCd = 3.6f;   // linger on the title menu so it is actually visible in the capture
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

        void OnGameLog(string s)
        {
            Log(s);
            if (s.StartsWith("guard broken")) ScheduleShot("guard_break", 0.25f);
            else if (s.StartsWith("intro panel")) ScheduleShot("intro" + s.Substring(12).Trim(), 2.2f);
        }
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
            bestX = -999f; noProgress = 0f; ventUntil = 0f;
            if (id == LevelId.BossArena) reachedGate = true;
            ScheduleShot("level_" + id, 2.5f);
        }
        void OnBossStarted(string n, string s) { bossStarted = true; Log("boss fight started: " + n); ScheduleShot("boss_intro", 1.6f); }
        void OnBossPhase(int p) { Log("boss phase " + p); ScheduleShot("boss_phase" + p, 0.8f); }
        void OnBossDefeated()
        {
            // The Artisan raises this event too. Only the final arena ends the run.
            if (GameFlow.Instance == null || GameFlow.Instance.CurrentLevel != LevelId.BossArena)
            {
                Log("area boss defeated");
                return;
            }
            bossDefeated = true; victoryAt = elapsed; Log("boss defeated"); ScheduleShot("victory", 3.5f);
        }
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
            attackCd -= dt; parryCd -= dt; dashCd -= dt; qiCd -= dt; healCd -= dt; confirmCd -= dt; interactCd -= dt;

            if (killAt >= 0f && elapsed >= killAt)
            {
                killAt = -1f;
                var victim = PlayerController.Instance;
                if (victim != null && victim.IsAlive)
                {
                    Log("scripted death at " + victim.Position.x.ToString("F1") + " carrying " + Progression.Ash + " ash");
                    victim.ReceiveAttack(new AttackInfo
                    {
                        Damage = 9999, Kind = AttackKind.Unblockable, Team = Team.Enemy,
                        Tag = "test_kill", Origin = victim.Center + new Vector2(2f, 0f)
                    });
                }
            }
            if (pendingShotAt >= 0f && elapsed >= pendingShotAt) { Shot(pendingLabel); pendingShotAt = -1f; }
            else if (shotTimer >= 6f) Shot("t" + Mathf.RoundToInt(elapsed));

            // capture the climb animation on request
            int wantClimbShots = Mathf.RoundToInt(CmdArgs.GetFloat("-climbShots", 0f));
            var pc = PlayerController.Instance;
            if (wantClimbShots > 0 && pc != null && pc.IsClimbing && climbShots < wantClimbShots)
            {
                climbShotTimer -= dt;
                if (climbShotTimer <= 0f)
                {
                    climbShotTimer = 0.3f;
                    climbShots++;
                    Shot("climb" + climbShots);
                    pendingShotAt = -1f;
                }
            }

            statusTimer -= dt;
            if (statusTimer <= 0f)
            {
                statusTimer = 2f;
                var pp = PlayerController.Instance;
                if (pp != null)
                    Log(string.Format("pos=({0:F1},{1:F1}) vel=({2:F1},{3:F1}) grounded={4} ctrl={5} stun={6} hazard={7} dead={8} hp={9} qi={10} brain={11} in=({12:F1}) enemies={13}",
                        pp.Position.x, pp.Position.y, pp.Velocity.x, pp.Velocity.y, pp.IsGrounded, pp.ControlEnabled, pp.IsStunned,
                        pp.HazardRecovering, pp.IsDead, pp.Hp, pp.Qi, brainState, input.Horizontal, AliveEnemyCount()));
            }

            if (elapsed >= quitAfter || (victoryAt >= 0f && elapsed >= victoryAt + victoryHold)) { Finish(); return; }

            input.Horizontal = 0f; input.Vertical = 0f; input.ParryHeld = false;
            jumpHold -= dt;
            input.JumpHeld = jumpHold > 0f;
            // the end roll owns the screen; watch a slice of it, then skip
            if (Services.Ui != null && Services.Ui.CreditsRolling)
            {
                brainState = "credits";
                creditsTime += dt;
                if (creditsTime > 22f && confirmCd <= 0f) { input.Confirm(); confirmCd = 1.5f; }
                return;
            }
            // shrines no longer open by themselves: ask for the rest whenever a level is affordable
            if (Progression.CanAffordLevel && interactCd <= 0f) { input.Interact(); interactCd = 0.4f; }
            // the shrine menu pauses the world and owns input until something is bought
            if (Services.Ui != null && Services.Ui.UpgradePanelOpen)
            {
                brainState = "shrine";
                if (confirmCd <= 0f) { input.Confirm(); confirmCd = 0.6f; }
                return;
            }

            var flow = GameFlow.Instance;
            // the intro runs while the flow is busy, and the bot still has to be able to skip it
            if (flow == null || (flow.Busy && flow.State != GameState.Intro)) return;

            switch (flow.State)
            {
                case GameState.Intro:
                    // let a few seconds of the cutscene actually run, then use the skip path
                    if (elapsed > introSeenUntil && confirmCd <= 0f) { input.Confirm(); confirmCd = 1f; }
                    break;
                case GameState.Title:
                    if (confirmCd <= 0f) { input.Confirm(); confirmCd = 1f; }
                    break;
                case GameState.Victory:
                    // sit on the stats card for a beat, then roll the credits
                    if (victoryAt >= 0f && elapsed > victoryAt + 3.5f && confirmCd <= 0f) { input.Confirm(); confirmCd = 1.5f; }
                    break;
                case GameState.Level1:
                case GameState.Works:
                case GameState.Stair:
                case GameState.BossArena:
                    if (flow.DeathScreenShowing)
                    {
                        if (diedAt >= 0f && elapsed > diedAt + 2.2f && confirmCd <= 0f) { input.Confirm(); confirmCd = 1f; }
                    }
                    else Brain();
                    break;
            }
        }

        int AliveEnemyCount()
        {
            int n = 0;
            for (int i = 0; i < EnemyBase.All.Count; i++)
                if (EnemyBase.All[i] != null && EnemyBase.All[i].IsAlive) n++;
            return n;
        }

        void Brain()
        {
            var p = PlayerController.Instance;
            if (p == null || p.IsDead || !p.ControlEnabled) { brainState = "no-control"; return; }

            if (ventUntil > elapsed) { RideVent(p); return; }
            if (p.Position.x > bestX + 0.4f) { bestX = p.Position.x; noProgress = 0f; }
            else noProgress += Time.unscaledDeltaTime;
            if (noProgress > 7f && FindVent(p, out ventAt))
            {
                noProgress = 0f;
                ventUntil = elapsed + 8f;
                Log("no progress at x=" + p.Position.x.ToString("F1") + " -> riding vent at x=" + ventAt.x.ToString("F1"));
                RideVent(p);
                return;
            }

            // threats: enemy projectiles heading at us
            Projectile threat = null;
            for (int i = 0; i < Projectile.Active.Count; i++)
            {
                var pr = Projectile.Active[i];
                if (pr == null || pr.Team != Team.Enemy) continue;
                Vector2 d = (Vector2)pr.transform.position - p.Center;
                if (d.magnitude < 1.7f && Vector2.Dot(pr.Velocity, -d) > 0f) { threat = pr; break; }
            }
            if (threat != null && parryCd <= 0f) { brainState = "parry-bolt"; input.Parry(); parryCd = 0.45f; return; }

            if (GroundShockwave.AnyApproaching(p.Center, 1.9f) && p.IsGrounded) { brainState = "jump-wave"; DoJump(); return; }

            // nearest enemy within engagement box
            EnemyBase target = null; float best = 999f;
            for (int i = 0; i < EnemyBase.All.Count; i++)
            {
                var e = EnemyBase.All[i];
                if (e == null || !e.IsAlive || !e.gameObject.activeInHierarchy) continue;
                Vector2 d = e.Center - p.Center;
                if (Mathf.Abs(d.x) <= 5.5f && Mathf.Abs(d.y) <= 4.6f && Mathf.Abs(d.x) < best) { best = Mathf.Abs(d.x); target = e; }
            }

            if (target != null)
            {
                int dirTo = target.Center.x > p.Center.x ? 1 : -1;
                float dist = Mathf.Abs(target.Center.x - p.Center.x);
                var boss = target as BossController;

                if (target.IsTelegraphing)
                {
                    brainState = "telegraph:" + target.TelegraphKind;
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
                    brainState = "evade-slam";
                    input.Horizontal = -dirTo;
                    if (dashCd <= 0f) { input.Dash(); dashCd = 0.6f; }
                    return;
                }
                if (target.CanBeExecuted && p.Qi > 0 && dist < PlayerTuning.ExecuteRangeX && qiCd <= 0f)
                { brainState = "execute"; input.QiBlast(); qiCd = 0.9f; return; }
                if (target.CanBeExecuted) { brainState = "close-for-execute"; input.Horizontal = dirTo; return; }
                if (p.Qi > 2 && target.Posture01 > 0.5f && dist < 2.6f && qiCd <= 0f) { brainState = "qi-blast"; input.QiBlast(); qiCd = 1.2f; return; }
                if (p.Hp < 40 && p.Qi > 0 && dist > 3.2f && healCd <= 0f) { brainState = "heal"; input.Heal(); healCd = 2.5f; return; }

                if (target.Type == EnemyType.WatcherDrone && target.Center.y > p.Center.y + 1.2f)
                {
                    // get underneath it, then jump-attack; its bolts are handled by the parry check above
                    brainState = "hunt-drone";
                    float dx = target.Center.x - p.Center.x;
                    if (Mathf.Abs(dx) > 0.5f) input.Horizontal = dirTo;
                    if (p.IsGrounded && Mathf.Abs(dx) < 1.3f) DoJump();
                    if (attackCd <= 0f && Mathf.Abs(dx) < 1.8f && target.Center.y - p.Center.y < 2.8f) { input.Attack(); attackCd = 0.22f; }
                    return;
                }
                if (target.Center.y > p.Center.y + 1.7f)
                {
                    // it is above us: get up there instead of swinging at its feet
                    brainState = "get-above";
                    input.Horizontal = dirTo;
                    if (p.IsGrounded) DoJump();
                    return;
                }
                if (dist > 1.75f)
                {
                    brainState = "approach";
                    input.Horizontal = dirTo;
                    if (target.Center.y > p.Center.y + 1.4f && p.IsGrounded) DoJump();
                }
                else
                {
                    brainState = "attack";
                    if (attackCd <= 0f)
                    {
                        // every fourth swing is the charged one so runs keep covering it
                        if ((++swings & 3) == 0) { input.Heavy(); attackCd = 1.1f; brainState = "heavy"; Log("charged attack"); ScheduleShot("charge", 0.28f); }
                        else { input.Attack(); attackCd = 0.22f; }
                    }
                }
                return;
            }

            // out of combat: top up health while it is safe
            if (p.Hp < 60 && p.Qi > 0 && p.IsGrounded && healCd <= 0f) { brainState = "heal"; input.Heal(); healCd = 3f; return; }

            // climbing beats everything else: hold up until we top out
            if (p.IsClimbing)
            {
                brainState = "climb";
                input.Vertical = 1f;
                return;
            }
            if (p.ClimbAvailable && p.Position.y < p.ClimbTop - 1.6f)
            {
                brainState = "grab-climb";
                input.Vertical = 1f;
                return;
            }
            // inside a qi vent: ride it and drift toward the next ledge
            if (p.InUpdraft)
            {
                brainState = "updraft";
                input.Horizontal = 1f;
                return;
            }

            // a gap with a shuttle over it: stand still until the ride is actually here
            bool ready;
            if (p.IsGrounded && !GroundAhead(p) && PlatformServing(p, out ready) && !ready)
            {
                brainState = "wait-lift";
                return;
            }

            // traverse: walk right, jump over gaps / walls / spikes
            brainState = "traverse";
            input.Horizontal = 1f;

            // if the exit gate is still sealed, go back and finish the stragglers instead of
            // grinding against the level's far wall
            var info = GameFlow.Instance != null ? GameFlow.Instance.CurrentLevelInfo : null;
            // A fight can carry us past an exit before its opening animation finishes.
            // Steer back through the trigger instead of jumping forever against the far wall.
            if (info != null && info.ExitGate != null && info.ExitGate.IsOpen)
            {
                Vector2 exit = info.ExitGate.transform.position;
                if (Mathf.Abs(p.Position.x - exit.x) < 6f && p.Position.y >= exit.y - 0.5f)
                {
                    brainState = "exit";
                    input.Horizontal = Mathf.Abs(p.Position.x - exit.x) < 0.2f ? 0f : Mathf.Sign(exit.x - p.Position.x);
                    return;
                }
            }
            if (info != null && info.ExitGate != null && !info.ExitGate.IsOpen
                && p.Position.x > info.ExitGate.transform.position.x - 3f)
            {
                EnemyBase straggler = null; float bestD = 9999f;
                for (int i = 0; i < EnemyBase.All.Count; i++)
                {
                    var e = EnemyBase.All[i];
                    if (e == null || !e.IsAlive || !e.gameObject.activeInHierarchy) continue;
                    float dd = Mathf.Abs(e.Center.x - p.Center.x);
                    if (dd < bestD) { bestD = dd; straggler = e; }
                }
                if (straggler != null)
                {
                    brainState = "hunt";
                    input.Horizontal = straggler.Center.x > p.Center.x ? 1f : -1f;
                    if (p.IsGrounded && bestD < 1.5f && straggler.Center.y > p.Center.y + 1.2f) DoJump();
                    return;
                }
            }
            if (p.IsGrounded && ShouldJump(p)) DoJump();

            // stuck detection
            if (p.IsGrounded && Mathf.Abs(p.Position.x - lastX) < 0.02f) stuckTime += Time.unscaledDeltaTime; else stuckTime = 0f;
            lastX = p.Position.x;
            if (stuckTime > 1.2f && p.IsGrounded) DoJump();
            if (stuckTime > 3f && dashCd <= 0f) { input.Dash(); dashCd = 0.6f; stuckTime = 0f; }
        }

        /// <summary>Walk back onto the vent column and let the updraft do the climbing.</summary>
        void RideVent(PlayerController p)
        {
            brainState = "vent";
            float rise = p.Position.y - ventAt.y;
            if (rise > 8.5f)
            {
                input.Horizontal = 1f;                       // step off the column onto the ledge
                if (p.IsGrounded) { ventUntil = 0f; bestX = p.Position.x; noProgress = 0f; }
                return;
            }
            float dx = ventAt.x - p.Position.x;
            input.Horizontal = Mathf.Abs(dx) > 0.4f ? Mathf.Sign(dx) : 0f;
            input.JumpHeld = true;
            jumpHold = 0.3f;
            if (p.IsGrounded) input.Jump();
        }

        /// <summary>Nearest lifting vent, if one is close enough to be the intended route.</summary>
        static bool FindVent(PlayerController p, out Vector2 at)
        {
            at = Vector2.zero;
            var vents = UnityEngine.Object.FindObjectsByType<AshenSol.Level.QiVent>(FindObjectsSortMode.None);
            float best = 999f;
            for (int i = 0; i < vents.Length; i++)
            {
                var v = vents[i];
                if (v == null || v.Kind != AshenSol.Level.QiVent.Mode.Column) continue;
                float d = Mathf.Abs(v.transform.position.x - p.Position.x);
                if (d < best && d < 16f) { best = d; at = v.transform.position; }
            }
            return best < 900f;
        }

        static bool GroundAhead(PlayerController p)
        {
            Vector2 feet = p.Position;
            var g = Physics2D.Raycast(new Vector2(feet.x + 1.35f, feet.y + 0.3f), Vector2.down, 3.0f, Layers.GroundMask);
            return g.collider != null;
        }

        /// <summary>Is a moving platform the intended way across the gap ahead — and is it here yet?</summary>
        static bool PlatformServing(PlayerController p, out bool ready)
        {
            ready = false;
            bool serving = false;
            Vector2 feet = p.Position;
            var all = AshenSol.Level.MovingPlatform.All;
            for (int i = 0; i < all.Count; i++)
            {
                var mp = all[i];
                if (mp == null) continue;
                Vector2 top = mp.TopCenter;
                if (top.x < feet.x - 1f || top.x > feet.x + 9f) continue;   // not the gap we are looking at
                serving = true;
                bool nearEnough = top.x - mp.HalfWidth < feet.x + 2.6f;
                bool levelWithUs = top.y > feet.y - 0.6f && top.y < feet.y + 1.6f;
                if (nearEnough && levelWithUs) ready = true;
            }
            return serving;
        }

        /// <summary>Press AND hold jump so the player reaches full jump height (a tap is cut to ~45%).</summary>
        void DoJump()
        {
            input.Jump();
            jumpHold = 0.35f;
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
