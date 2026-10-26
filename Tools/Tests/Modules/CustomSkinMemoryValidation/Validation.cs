using System;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

#nullable enable
namespace CustomSkinMemoryValidation
{
    /// <summary>
    /// Editor entry points of the isolated CustomSkin memory validation.
    /// </summary>
    public static class Validation
    {
        /// <summary>
        /// Holds the scene built into the probe player.
        /// </summary>
        private const string ScenePath = "Assets/Probe.unity";

        /// <summary>
        /// Measures one load in this fresh Editor process and exits with the result.
        /// </summary>
        public static async void Run()
        {
            var exitCode = await SkinMemoryProbe.RunFromCommandLineAsync();
            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// Builds a Windows x64 development player whose start-up runs the same probe, then exits.
        /// The output path follows the -playerPath switch.
        /// </summary>
        public static void BuildPlayer()
        {
            try
            {
                var arguments = Environment.GetCommandLineArgs();
                var playerPath = Array.IndexOf(arguments, "-playerPath") is var index && index >= 0 && index < arguments.Length - 1
                    ? arguments[index + 1]
                    : throw new ArgumentException("Missing command-line argument -playerPath.");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                // Development builds expose the graphics-driver allocation counter used by the probe.
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                Debug.Log($"CUSTOM_SKIN_MEMORY_BUILD result={report.summary.result} path={playerPath}");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
