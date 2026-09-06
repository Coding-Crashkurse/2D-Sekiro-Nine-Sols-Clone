using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenSol.EditorTools
{
    /// <summary>
    /// One-shot project configuration. Run from the menu or in batch mode:
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod AshenSol.EditorTools.ProjectBootstrap.Run
    /// </summary>
    public static class ProjectBootstrap
    {
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        static readonly Dictionary<int, string> LayerNames = new Dictionary<int, string>
        {
            { 6, "Ground" }, { 7, "Player" }, { 8, "Enemy" }, { 9, "Hazard" }, { 10, "Projectile" }, { 11, "Trigger" }, { 12, "OneWay" }
        };

        static readonly string[] AlwaysIncludedShaders =
        {
            "Universal Render Pipeline/2D/Sprite-Lit-Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default",
            "UI/Default",
            "AshenSol/Silhouette",
            "AshenSol/SpriteAdditive",
            // NOTE: never add editor-internal shaders such as "GUI/Text Shader" here — they live in
            // "Library/unity default resources" with HideFlags.DontSave and break the player build.
        };

        [MenuItem("Ashen Sol/Bootstrap Project")]
        public static void Run()
        {
            try
            {
                SetupLayers();
                SetupShaders();
                SetupPlayerSettings();
                SetupPipeline();
                SetupScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Bootstrap] OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[Bootstrap] FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void SetupLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[Bootstrap] TagManager not found"); return; }
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            foreach (var kv in LayerNames)
            {
                if (kv.Key < layers.arraySize)
                {
                    var el = layers.GetArrayElementAtIndex(kv.Key);
                    if (el.stringValue != kv.Value) el.stringValue = kv.Value;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[Bootstrap] layers named");
        }

        static void SetupShaders()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[Bootstrap] GraphicsSettings not found"); return; }
            var so = new SerializedObject(assets[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");

            // Drop editor-internal shaders (HideFlags.DontSave, e.g. "GUI/Text Shader"): including them
            // makes the player build fail with "An asset is marked with HideFlags.DontSave".
            int removed = 0;
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (s != null && (s.hideFlags & HideFlags.DontSave) != 0)
                {
                    arr.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }
            if (removed > 0) Debug.Log("[Bootstrap] removed " + removed + " editor-internal shader(s) from Always Included");

            var present = new HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (s != null) present.Add(s);
            }
            int added = 0;
            foreach (var name in AlwaysIncludedShaders)
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning("[Bootstrap] shader not found: " + name); continue; }
                if (present.Contains(sh)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                present.Add(sh);
                added++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[Bootstrap] always-included shaders added: " + added);
        }

        static void SetupPlayerSettings()
        {
            PlayerSettings.productName = "Ashen Sol";
            PlayerSettings.companyName = "AshenSol";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SplashScreen.show = false;
            Debug.Log("[Bootstrap] player settings set");
        }

        static void SetupPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            if (urp != null)
            {
                urp.supportsHDR = true;
                urp.msaaSampleCount = 1;
                EditorUtility.SetDirty(urp);
                if (GraphicsSettings.defaultRenderPipeline == null) GraphicsSettings.defaultRenderPipeline = urp;
            }
            else Debug.LogWarning("[Bootstrap] UniversalRP.asset not found");

            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = 1;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("[Bootstrap] pipeline configured");
        }

        static void SetupScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("GameBootstrap");
                go.AddComponent<AshenSol.Core.GameBootstrap>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[Bootstrap] scene created: " + ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
