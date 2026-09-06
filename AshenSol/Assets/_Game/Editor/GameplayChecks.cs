using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AshenSol.Core;
using AshenSol.Player;
using AshenSol.Enemies;
using AshenSol.Level;

namespace AshenSol.EditorTools
{
    /// <summary>Real play-mode regressions, runnable without a test assembly or added packages.
    /// Unity -batchmode -projectPath ... -executeMethod AshenSol.EditorTools.GameplayChecks.Run
    /// Do not pass -quit: the runner exits after the coroutine finishes.</summary>
    [InitializeOnLoad]
    public static class GameplayChecks
    {
        const string Pending = "AshenSol.GameplayChecks";
        static double deadline;
        static int passed;
        static int errors, editorSearchErrors;

        static GameplayChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Watchdog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (!SessionState.GetBool(Pending, false) || (type != LogType.Error && type != LogType.Exception)) return;
            // This Unity version throws in its editor search index even in an empty scene.
            // Report that infrastructure fault separately; never ignore a gameplay stack.
            if (stack.Contains("UnityEditor.Search.SearchDatabase") && !stack.Contains("AshenSol.")) editorSearchErrors++;
            else errors++;
        }

        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            EditorSceneManager.OpenScene(ProjectBootstrap.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                GameManager.Run(Guard(Checks()));
        }

        static void Watchdog()
        {
            if (SessionState.GetBool(Pending, false) && EditorApplication.timeSinceStartup > deadline)
                Finish("Timed out waiting for gameplay checks");
        }

        static IEnumerator Guard(IEnumerator tests)
        {
            while (true)
            {
                object next;
                try
                {
                    if (!tests.MoveNext()) break;
                    next = tests.Current;
                }
                catch (Exception e) { Finish(e.ToString()); yield break; }
                yield return next;
            }
            Finish(errors == 0 ? null : "Errors during checks: " + errors);
        }

        static void Finish(string error)
        {
            SessionState.SetBool(Pending, false);
            if (error == null) Debug.Log("GAMEPLAY CHECKS PASS: " + passed + "; editor search errors: " + editorSearchErrors);
            else Debug.LogError("GAMEPLAY CHECKS FAILED: " + error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            passed++;
            Debug.Log("[GameplayCheck] PASS " + message);
        }

        static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        static void Call(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        }

        static IEnumerator Checks()
        {
            var flow = GameFlow.Instance;
            while (flow.Busy || flow.State == GameState.Boot) yield return null;
            if (!flow.InLevel) flow.LoadLevel(LevelId.Level1);
            while (flow.Busy || flow.Player == null) yield return null;
            Time.captureDeltaTime = 1f / Mathf.Max(1f, CmdArgs.GetFloat("-checkFps", 60f));
            yield return null;
            var p = flow.Player;
            p.enabled = false;
            p.Respawn(new Vector2(0f, 100f));
            p.Body.simulated = false;
            var input = new ScriptedInput();
            InputRouter.Instance.enabled = false;
            InputRouter.Instance.SetProvider(input);

            // A press during the last part of a lock is retained and consumed once.
            Set(p.Combat, "attackInputLock", 0.08f);
            input.Attack(); input.Tick(); p.Combat.Tick(0.01f);
            Check(!p.IsAttacking, "attack respects recovery lock");
            input.Tick(); p.Combat.Tick(0.08f);
            Check(p.IsAttacking, "attack buffered across recovery");
            p.Combat.Reset();
            Set(p.Combat, "attackInputLock", 0.3f);
            input.Attack(); input.Tick(); p.Combat.Tick(0.01f);
            input.Tick(); p.Combat.Tick(0.2f); p.Combat.Tick(0.2f);
            Check(!p.IsAttacking, "expired attack does not fire later");

            Set(p.Combat, "parryRecoveryTimer", 0.06f);
            input.Parry(); input.Tick(); p.Combat.Tick(0.01f);
            Check(!p.Combat.IsParrying, "parry respects recovery lock");
            input.Tick(); p.Combat.Tick(0.06f);
            Check(p.IsParryWindowOpen, "parry buffered across recovery");
            p.Combat.Reset();

            Set(p, "dashCooldown", 0.05f);
            input.Dash(); input.Tick(); Call(p, "Update");
            Check(!p.IsDashing, "dash respects cooldown");
            Set(p, "dashCooldown", 0f);
            input.Tick(); Call(p, "Update");
            Check(p.IsDashing, "dash buffered across cooldown");
            p.Respawn(new Vector2(0f, 100f)); p.Body.simulated = false;

            input.Attack(); input.Tick(); p.Combat.Tick(0.01f);
            input.Horizontal = -1f;
            input.Dash(); input.Tick(); Call(p, "Update");
            Check(p.IsDashing && p.Facing == -1, "dash cancel follows requested direction");
            input.Horizontal = 0f; input.Tick();
            p.Respawn(new Vector2(0f, 100f)); p.Body.simulated = false;

            TimeController.Instance.SetPaused(true);
            input.Attack(); input.Tick(); Call(p, "Update");
            TimeController.Instance.SetPaused(false);
            Call(p, "Update");
            Check(!p.IsAttacking, "menu input cannot leak into resumed gameplay");
            input.Tick();
            yield return null;

            var ladder = new GameObject("CheckLadder").AddComponent<ClimbSurface>();
            ladder.Top = 110f;
            ladder.transform.position = new Vector2(0f, 100f);
            p.OfferClimb(ladder);
            input.Vertical = 1f; input.Jump(); input.JumpHeld = true; input.Tick(); Call(p, "Update");
            Check(p.IsClimbing, "up/jump on ladder grabs without immediately jumping off");
            input.Tick(); Call(p, "Update");
            input.Jump(); input.Tick(); Call(p, "Update");
            Check(!p.IsClimbing && p.Velocity.y > 0f, "fresh jump leaves ladder");
            UnityEngine.Object.Destroy(ladder.gameObject);
            input.Vertical = 0f; input.JumpHeld = false; input.Tick();

            var platform = new GameObject("CheckOneWay");
            platform.layer = Layers.OneWay;
            platform.transform.position = new Vector2(0f, 99.9f);
            platform.AddComponent<BoxCollider2D>().size = new Vector2(3f, 0.2f);
            p.Respawn(new Vector2(0f, 100f));
            Physics2D.SyncTransforms();
            Call(p, "FixedUpdate");
            Check(p.IsGrounded, "one-way platform supports standing");
            input.Vertical = -1f; input.Jump(); input.Tick(); Call(p, "Update");
            Call(p, "FixedUpdate");
            Check(!p.IsGrounded && p.Velocity.y < 0f, "drop-through clears grounded state immediately");
            UnityEngine.Object.Destroy(platform);
            input.Vertical = 0f; input.Tick();

            p.Respawn(new Vector2(0f, 100f)); p.Body.simulated = false;
            var enemy = EnemyBase.Create(EnemyType.SpearSentinel, new Vector2(1f, 100f), flow.LevelRoot);
            enemy.StopAllCoroutines(); enemy.Body.simulated = false;
            int before = enemy.Hp;
            enemy.ReceiveAttack(new AttackInfo { Team = Team.Player, Damage = 7, Tag = "execute", Origin = p.Center });
            Check(before - enemy.Hp == 7, "execution honors supplied upgraded damage");
            enemy.AddPosture(enemy.MaxPosture);
            p.AddQi(1);
            input.QiBlast(); input.Tick(); p.Combat.Tick(0.01f);
            Check(p.Combat.IsQiBlasting, "contextual execution starts");
            before = enemy.Hp;
            TimeController.Instance.SetPaused(true);
            float stop = HitStop.Remaining;
            yield return new WaitForSecondsRealtime(0.35f);
            Check(enemy.Hp == before && p.Combat.IsQiBlasting, "pause freezes execution before damage");
            Check(Mathf.Approximately(HitStop.Remaining, stop), "pause preserves hit-stop remaining");
            TimeController.Instance.SetPaused(false);
            yield return new WaitForSecondsRealtime(0.7f);
            Check(enemy.Hp < before && !p.Combat.IsQiBlasting, "execution resumes and finishes after pause");

            Progression.AddAsh(30);
            Progression.DropOnDeath(new Vector2(2f, 100f), flow.CurrentLevel);
            Call(flow, "SpawnAshPile");
            Progression.AddAsh(20);
            Progression.DropOnDeath(new Vector2(4f, 100f), flow.CurrentLevel);
            Call(flow, "SpawnAshPile");
            var piles = flow.LevelRoot.GetComponentsInChildren<AshPile>();
            Check(piles.Length == 1 && Mathf.Approximately(piles[0].transform.position.x, 4f), "second death replaces the old ash pile");
            Progression.DropOnDeath(Vector2.zero, flow.CurrentLevel);
            Call(flow, "SpawnAshPile");
            Check(flow.LevelRoot.GetComponentsInChildren<AshPile>().Length == 0, "death with no ash removes old pile");

            var cam = CameraController.Instance;
            Settings.ScreenShake = false;
            Set(cam, "kick", Vector2.zero);
            cam.Kick(Vector2.right, 1f);
            var kick = (Vector2)typeof(CameraController).GetField("kick", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cam);
            Check(kick == Vector2.zero, "screen-shake off suppresses camera kicks");

            GameEvents.RaisePlayerQiChanged(6, 6);
            var ui = AshenSol.UI.UiManager.Instance;
            var pips = (UnityEngine.UI.Image[])ui.GetType().GetField("pips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            Check(pips.Length == Progression.PlayerBaseQi + Progression.MaxFocus && Array.TrueForAll(pips, pip => pip.gameObject.activeSelf), "HUD displays every upgraded Qi slot");
            GameEvents.RaisePlayerQiChanged(0, 3);
            Check(!pips[3].gameObject.activeSelf && pips[2].gameObject.activeSelf, "HUD resets Qi capacity for a new run");

            p.Respawn(new Vector2(3f, 100f));
            var floor = new GameObject("CheckShrineFloor");
            floor.layer = Layers.Ground;
            floor.transform.position = new Vector2(3f, 99.9f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(3f, 0.2f);
            var shrine = Checkpoint.Create(new Vector2(3f, 100f), "check", flow.LevelRoot);
            Physics2D.SyncTransforms();
            Call(p, "FixedUpdate");
            input.Interact(); input.Tick();
            Call(shrine, "Update");
            Check(!p.ControlEnabled, "shrine interaction works without a physics trigger callback");
            shrine.StopAllCoroutines();
            UnityEngine.Object.Destroy(shrine.gameObject);
            UnityEngine.Object.Destroy(floor);
            input.Tick();

            p.Respawn(new Vector2(0f, 100f));
            p.ApplyHazard(15);
            Check(p.HazardRecovering, "hazard recovery starts");
            p.Respawn(new Vector2(10f, 100f)); p.Body.simulated = false;
            yield return new WaitForSecondsRealtime(0.5f);
            Check(p.Position == new Vector2(10f, 100f) && !p.HazardRecovering, "respawn cancels the previous hazard teleport");

            p.Respawn(new Vector2(0f, 100f));
            var moving = MovingPlatform.Create(new Vector2(20f, 100f), new Vector2(24f, 100f), 3f, 2f, flow.LevelRoot);
            p.transform.position = moving.TopCenter;
            Physics2D.SyncTransforms();
            Call(p, "FixedUpdate");
            Check(p.IsGrounded && p.LastSafeGroundPosition == new Vector2(0f, 100f), "moving platforms cannot replace a permanent safe return point");
        }
    }
}
