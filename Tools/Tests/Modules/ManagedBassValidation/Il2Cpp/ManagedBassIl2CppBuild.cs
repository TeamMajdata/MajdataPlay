using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

#nullable enable
namespace MajdataPlay.Validation.Il2Cpp
{
    /// <summary>Builds the isolated ManagedBass smoke project as an Android IL2CPP Player.</summary>
    public static class ManagedBassIl2CppBuild
    {
        /// <summary>
        /// Builds the Player. Called with <c>-executeMethod</c>; reads <c>-smokeArchitecture</c> (arm64, armv7 or x86_64) and
        /// <c>-smokeOutput</c> (the output directory) from the command line.
        /// </summary>
        /// <exception cref="ArgumentException">The output directory or the architecture is missing or invalid.</exception>
        /// <exception cref="InvalidOperationException">The build failed.</exception>
        public static void Run()
        {
            var output = GetArgument("-smokeOutput");
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("Specify the output directory with -smokeOutput.");
            }
            PlayerSettings.Android.targetArchitectures = (GetArgument("-smokeArchitecture") ?? "arm64") switch
            {
                "arm64" => AndroidArchitecture.ARM64,
                "armv7" => AndroidArchitecture.ARMv7,
                "x86_64" => AndroidArchitecture.X86_64,
                var other => throw new ArgumentException($"Unknown architecture: {other}"),
            };
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            // The game builds Android with Low stripping; link.xml keeps every ManagedBass callback so that the generated C++ can be inspected.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "net.majdata.managedbass.il2cpp");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.allowUnsafeCode = true;
            PlayerSettings.runInBackground = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Smoke.unity" },
                target = BuildTarget.Android,
                locationPathName = Path.Combine(output, "ManagedBassSmoke.apk"),
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"The Android IL2CPP Player build failed: {report.summary.result}");
            }
        }

        /// <summary>Reads the value that follows a command line argument.</summary>
        /// <param name="name">The argument name.</param>
        /// <returns>The value, or <see langword="null"/> when the argument is missing.</returns>
        private static string? GetArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
