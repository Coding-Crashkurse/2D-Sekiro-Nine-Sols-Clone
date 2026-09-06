using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.VFX;

namespace AshenSol.EditorTools
{
    /// <summary>Renders actual old/new VFX through URP to a texture, independent of window visibility.</summary>
    public static class SlashPreview
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Slash comparison").transform;
            var cam = new GameObject("Preview camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;
            cam.orthographicSize = 6.3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.018f, 0.025f, 0.045f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var volume = new GameObject("Bloom").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = volume.sharedProfile.Add<Bloom>(true);
            bloom.intensity.Override(1.15f); bloom.threshold.Override(0.82f); bloom.scatter.Override(0.72f);

            Label(root, "VORHER", new Vector2(-3.7f, 5.5f), 0.1f);
            Label(root, "NACHHER", new Vector2(3.7f, 5.5f), 0.1f);
            float[] phases = { 0.10f, 0.35f, 0.60f };
            var old = Res.Sprite("fx_slash");
            for (int i = 0; i < phases.Length; i++)
            {
                float y = 3f - i * 3.5f;
                var before = SpriteFx.Make(root);
                before.Play(old, VfxManager.AdditiveFor(old), new Vector2(-3.7f, y), -20f, false,
                    Palette.PlayerSlash, Palette.PlayerSlash.WithAlpha(0f), 1.7f, 2.3f, 0.17f, SortOrder.Fx + 8);
                Sample(before, Mathf.Clamp01(phases[i] * 0.24f / 0.17f));
                var after = SlashFx.Make(root);
                after.Play(new Vector2(3.7f, y), -20f, false, Palette.PlayerSlash, 1f);
                Sample(after, phases[i]);
                Label(root, Mathf.RoundToInt(phases[i] * 240f) + " ms", new Vector2(0f, y), 0.065f);
            }

            string output = CmdArgs.Get("-previewOutput", "C:/Users/User/Desktop/nine_sols/Builds/SwordFx/slash-comparison.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.aspect = 1600f / 900f;
            RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(output, texture.EncodeToPNG());
            RenderTexture.active = previous;
            rt.Release();
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(rt);
            Debug.Log("[SlashPreview] rendered " + output);
        }

        static void Sample(object fx, float phase)
        {
            fx.GetType().GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fx, new object[] { phase });
        }

        static void Label(Transform root, string text, Vector2 position, float size)
        {
            var go = new GameObject(text);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var label = go.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 64; label.characterSize = size;
            label.text = text; label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(0.66f, 0.75f, 0.82f);
            go.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        }
    }
}
