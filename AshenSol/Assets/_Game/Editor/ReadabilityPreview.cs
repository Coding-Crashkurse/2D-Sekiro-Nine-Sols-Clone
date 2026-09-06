using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Level;

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
            if (error == null) Debug.Log("[ReadabilityPreview] PASS: five intro panels and two non-overlapping hints");
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
            yield return new WaitForSecondsRealtime(4f);
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
