using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AshenSol.EditorTools
{
    public static class BuildScript
    {
        public const string OutputDir = "C:/Users/User/Desktop/nine_sols/Builds/Windows";

        [MenuItem("Ashen Sol/Build Windows")]
        public static void BuildWindows()
        {
            try
            {
                // NOTE: do NOT run ProjectBootstrap here — creating/saving assets in the same call
                // leaves the AssetDatabase locked and BuildPipeline fails writing PlayerDataCache.
                // Tools/build.sh bootstraps in a separate editor invocation.
                string output = AshenSol.Core.CmdArgs.Get("-buildOutput", OutputDir);
                Directory.CreateDirectory(output);
                var options = BuildOptions.None;
                if (AshenSol.Core.CmdArgs.Has("-dev")) options |= BuildOptions.Development;
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectBootstrap.ScenePath },
                    locationPathName = Path.Combine(output, "AshenSol.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = options,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                Debug.Log(string.Format("[Build] result={0} size={1:F1}MB errors={2} warnings={3} time={4:F0}s",
                    s.result, s.totalSize / 1e6, s.totalErrors, s.totalWarnings, s.totalTime.TotalSeconds));
                if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
            }
            catch (Exception e)
            {
                Debug.LogError("[Build] FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
