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

        static GameplayChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Watchdog;
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
            Finish(GameManager.ExceptionCount == 0 ? null : "Runtime errors: " + GameManager.ExceptionCount);
        }

        static void Finish(string error)
        {
            SessionState.SetBool(Pending, false);
            if (error == null) Debug.Log("GAMEPLAY CHECKS PASS: " + passed);
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
            Time.captureDeltaTime = 1f / 60f;
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
        }
    }
}
