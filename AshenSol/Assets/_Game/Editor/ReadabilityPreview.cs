using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Level;
using AshenSol.Boss;

namespace AshenSol.EditorTools
{
    /// <summary>Captures each live intro panel and both opening hints through the game camera.</summary>
    [InitializeOnLoad]
    public static class ReadabilityPreview
    {
        const string Pending = "AshenSol.ReadabilityPreview";
        const string Output = "C:/Users/User/Desktop/nine_sols/Builds/Readability";
        static double deadline;

        static ReadabilityPreview()
        {
            deadline = EditorApplication.timeSinceStartup + 150;
            EditorApplication.update += () =>
            {
                if (SessionState.GetBool(Pending, false) && EditorApplication.timeSinceStartup > deadline)
                    Finish("Preview timed out");
            };
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                    GameManager.Run(Guard(CaptureSequence()));
            };
        }

        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            EditorSceneManager.OpenScene(ProjectBootstrap.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        static IEnumerator Guard(IEnumerator routine)
        {
            while (true)
            {
                object next;
                try { if (!routine.MoveNext()) break; next = routine.Current; }
                catch (Exception e) { Finish(e.ToString()); yield break; }
                yield return next;
            }
            Finish(null);
        }

        static void Finish(string error)
        {
            SessionState.SetBool(Pending, false);
            if (error == null) Debug.Log("[ReadabilityPreview] PASS: five intro panels, two non-overlapping hints, Artisan swings in both directions, drop and reset");
            else Debug.LogError("[ReadabilityPreview] FAILED: " + error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }

        static IEnumerator CaptureSequence()
        {
            Directory.CreateDirectory(Output);
            InputRouter.Instance.SetProvider(new ScriptedInput());
            var flow = GameFlow.Instance;
            while (flow.State != GameState.Title || flow.Busy) yield return null;
            flow.StartGame();
            for (int i = 0; i < 5; i++)
            {
                while (GameObject.Find("IntroStage/Panel" + i) == null) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                Capture("intro-" + (i + 1));
            }
            while (flow.State != GameState.Level1 || flow.Busy) yield return null;
            while (AshenSol.UI.UiManager.Instance.NameCardVisible) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            TimeController.Instance.SetPaused(true);
            float[] positions = { 6.5f, 9.5f };
            for (int i = 0; i < positions.Length; i++)
            {
                flow.Player.Respawn(new Vector2(positions[i], 0.05f));
                Services.Cam.SnapToTarget();
                yield return null;
                yield return null;
                int shown = 0;
                foreach (var sign in flow.LevelRoot.GetComponentsInChildren<TutorialSign>())
                    if (sign.GetComponentInChildren<TextMesh>().GetComponent<Renderer>().enabled) shown++;
                if (shown != 1) throw new InvalidOperationException("Expected one visible tutorial, got " + shown);
                Capture("tutorial-" + (i + 1));
            }
            TimeController.Instance.SetPaused(false);
            flow.LoadLevel(LevelId.Works);
            while (flow.Busy || flow.State != GameState.Works) yield return null;
            while (AshenSol.UI.UiManager.Instance.NameCardVisible) yield return null;
            var boss = (ArtisanController)flow.CurrentLevelInfo.Arena.BossEnemy;
            flow.CurrentLevelInfo.Arena.gameObject.SetActive(false);
            foreach (var enemy in flow.LevelRoot.GetComponentsInChildren<AshenSol.Enemies.EnemyBase>())
                if (enemy != boss) enemy.gameObject.SetActive(false);
            CameraController.Instance.SetManual(true);
            Services.Cam.SetZoom(5f, 0f);
            CameraController.Instance.SetPosition(new Vector2(128f, 31f));
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var angles = typeof(ArtisanController).GetField("bladeAngles", fields);
            var posed = typeof(ArtisanController).GetField("bladesPosed", fields);
            for (int side = 0; side < 2; side++)
            {
                boss.ResetFight();
                boss.StopAllCoroutines();
                flow.Player.Respawn(new Vector2(side == 0 ? 130f : 126f, 28.05f));
                flow.Player.SetControlEnabled(false);
                typeof(AshenSol.Player.PlayerController).GetField("invulnTimer", fields).SetValue(flow.Player, 100f);
                boss.transform.position = new Vector3(128f, 29.5f, 0f);
                bool complete = false;
                var attack = (IEnumerator)typeof(ArtisanController).GetMethod("DescendAndStrike", fields).Invoke(boss, null);
                boss.StartCoroutine(Track(attack, () => complete = true));
                var trails = boss.GetComponentsInChildren<TrailRenderer>();
                while (!boss.IsTelegraphing) yield return null;
                yield return new WaitForSeconds(ArtisanTuning.StrikeTelegraph * Settings.TelegraphMul * 0.8f);
                Vector2 windup = (Vector2)angles.GetValue(boss);
                Capture("artisan-" + side + "-windup");
                while (!Array.Exists(trails, trail => trail.emitting)) yield return null;
                yield return new WaitForSeconds(0.06f);
                if (Vector2.Distance(windup, (Vector2)angles.GetValue(boss)) < 40f)
                    throw new InvalidOperationException("Artisan blade did not swing towards side " + side);
                Capture("artisan-" + side + "-cut");
                while (!complete) yield return null;
                if ((bool)posed.GetValue(boss) || Array.Exists(trails, trail => trail.emitting))
                    throw new InvalidOperationException("Artisan retained attack pose/trail after combo");
            }
            boss.ResetFight();
            boss.StopAllCoroutines();
            bool dropComplete = false;
            boss.StartCoroutine(Track((IEnumerator)typeof(ArtisanController).GetMethod("HammerDrop", fields).Invoke(boss, null), () => dropComplete = true));
            while (!boss.IsTelegraphing) yield return null;
            while (boss.IsTelegraphing) yield return null;
            yield return new WaitForSeconds(0.12f);
            Capture("artisan-drop");
            while (!dropComplete) yield return null;
            boss.ResetFight();
            boss.StopAllCoroutines();
            // Unity can retain one head point after Clear; a single point draws no trail.
            if ((bool)posed.GetValue(boss) || Array.Exists(boss.GetComponentsInChildren<TrailRenderer>(), trail => trail.emitting || trail.positionCount > 1))
                throw new InvalidOperationException("Artisan reset retained blade effects");
        }

        static IEnumerator Track(IEnumerator attack, Action done)
        {
            yield return attack;
            done();
        }

        static void Capture(string name)
        {
            var cam = Services.Cam.Camera;
            var canvas = AshenSol.UI.UiManager.Instance.GetComponentInChildren<Canvas>();
            var mode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance;
            float aspect = cam.aspect;
            var previous = RenderTexture.active;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                rt.Create();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                cam.aspect = 1600f / 900f;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                cam.aspect = aspect;
                canvas.renderMode = mode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                UnityEngine.Object.Destroy(texture);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            Debug.Log("[ReadabilityPreview] captured " + name);
        }
    }
}
