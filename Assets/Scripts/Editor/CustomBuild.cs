using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
#if UNITY_STANDALONE_OSX
using UnityEditor.Build;
using UnityEditor.OSXStandalone;
#endif

namespace MajdataPlay.Editor
{
    public class CustomBuild
    {
        private static readonly string Eol = Environment.NewLine;

        private static readonly string[] Secrets =
            {"androidKeystorePass", "androidKeyaliasName", "androidKeyaliasPass"};

        public static void BuildRelease()
        {
            var (a, b, c) = Handle();
            Build(a, b, c, null);
        }
        public static void BuildDebug()
        {
            var (a, b, c) = Handle();
            Build(a, b, c, BuildOptions.Development);
        }
        static (BuildTarget, int, string) Handle()
        {
            // Gather values from args
            var options = GetValidatedOptions();

            // Set version for this build
            if (options.TryGetValue("buildVersion", out var buildVersion) && buildVersion != "none")
            {
                PlayerSettings.bundleVersion = buildVersion;
                PlayerSettings.macOS.buildNumber = buildVersion;
            }
            if (options.TryGetValue("androidVersionCode", out var versionCode) && versionCode != "0")
            {
                PlayerSettings.Android.bundleVersionCode = int.Parse(options["androidVersionCode"]);
            }

            // Apply build target
            var buildTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), options["buildTarget"]);
            switch (buildTarget)
            {
                case BuildTarget.Android:
                    {
                        EditorUserBuildSettings.buildAppBundle = options["customBuildPath"].EndsWith(".aab");
                        if (options.TryGetValue("androidKeystoreName", out var keystoreName) &&
                            !string.IsNullOrEmpty(keystoreName))
                        {
                            PlayerSettings.Android.useCustomKeystore = true;
                            PlayerSettings.Android.keystoreName = keystoreName;
                        }
                        if (options.TryGetValue("androidKeystorePass", out var keystorePass) &&
                            !string.IsNullOrEmpty(keystorePass))
                        {
                            PlayerSettings.Android.keystorePass = keystorePass;
                        }
                        if (options.TryGetValue("androidKeyaliasName", out var keyaliasName) &&
                            !string.IsNullOrEmpty(keyaliasName))
                        {
                            PlayerSettings.Android.keyaliasName = keyaliasName;
                        }
                        if (options.TryGetValue("androidKeyaliasPass", out var keyaliasPass) &&
                            !string.IsNullOrEmpty(keyaliasPass))
                        {
                            PlayerSettings.Android.keyaliasPass = keyaliasPass;
                        }
                        if (options.TryGetValue("androidTargetSdkVersion", out var androidTargetSdkVersion) &&
                            !string.IsNullOrEmpty(androidTargetSdkVersion))
                        {
                            var targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                            try
                            {
                                targetSdkVersion =
                                    (AndroidSdkVersions)Enum.Parse(typeof(AndroidSdkVersions), androidTargetSdkVersion);
                            }
                            catch
                            {
                                UnityEngine.Debug.Log("Failed to parse androidTargetSdkVersion! Fallback to AndroidApiLevelAuto");
                            }

                            PlayerSettings.Android.targetSdkVersion = targetSdkVersion;
                        }

                        break;
                    }
                case BuildTarget.StandaloneOSX:
                    PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                    if (options.TryGetValue("macOSArchitecture", out var macOSArchitecture) &&
                        !string.IsNullOrEmpty(macOSArchitecture))
                    {
                        ApplyMacOSArchitecture(macOSArchitecture);
                    }
                    break;
            }

            // Determine subtarget
            var buildSubtarget = 0;
#if UNITY_2021_2_OR_NEWER
            if (!options.TryGetValue("standaloneBuildSubtarget", out var subtargetValue) || !Enum.TryParse(subtargetValue, out StandaloneBuildSubtarget buildSubtargetValue))
            {
                buildSubtargetValue = default;
            }
            buildSubtarget = (int)buildSubtargetValue;
#endif
            return (buildTarget, buildSubtarget, options["customBuildPath"]);
        }

        /// <summary>
        /// Sets the CPU architecture of the macOS player.
        /// The FFmpeg dylibs ship as separate ARM64 and x86_64 files with the same names,
        /// so a Universal (x64ARM64) build fails Unity's plugin collision check.
        /// </summary>
        /// <param name="value">A <c>UnityEditor.Build.OSArchitecture</c> name, such as <c>ARM64</c>.</param>
        private static void ApplyMacOSArchitecture(string value)
        {
#if UNITY_STANDALONE_OSX
            if (!Enum.TryParse(value, true, out OSArchitecture architecture))
            {
                Console.WriteLine($"{value} is not a defined {nameof(OSArchitecture)}");
                EditorApplication.Exit(140);
                return;
            }
            UserBuildSettings.architecture = architecture;
            Console.WriteLine($"macOS architecture set to {architecture}.");
#else
            Console.WriteLine("-macOSArchitecture requires the macOS build target to be active.");
            EditorApplication.Exit(141);
#endif
        }

        private static Dictionary<string, string> GetValidatedOptions()
        {
            ParseCommandLineArguments(out var validatedOptions);

            if (!validatedOptions.TryGetValue("projectPath", out var _))
            {
                Console.WriteLine("Missing argument -projectPath");
                EditorApplication.Exit(110);
            }

            if (!validatedOptions.TryGetValue("buildTarget", out var buildTarget))
            {
                Console.WriteLine("Missing argument -buildTarget");
                EditorApplication.Exit(120);
            }

            if (!Enum.IsDefined(typeof(BuildTarget), buildTarget ?? string.Empty))
            {
                Console.WriteLine($"{buildTarget} is not a defined {nameof(BuildTarget)}");
                EditorApplication.Exit(121);
            }

            if (!validatedOptions.TryGetValue("customBuildPath", out var _))
            {
                Console.WriteLine("Missing argument -customBuildPath");
                EditorApplication.Exit(130);
            }

            const string defaultCustomBuildName = "TestBuild";
            if (!validatedOptions.TryGetValue("customBuildName", out var customBuildName))
            {
                Console.WriteLine($"Missing argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }
            else if (customBuildName == "")
            {
                Console.WriteLine($"Invalid argument -customBuildName, defaulting to {defaultCustomBuildName}.");
                validatedOptions.Add("customBuildName", defaultCustomBuildName);
            }

            return validatedOptions;
        }

        private static void ParseCommandLineArguments(out Dictionary<string, string> providedArguments)
        {
            providedArguments = new Dictionary<string, string>();
            var args = Environment.GetCommandLineArgs();

            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#    Parsing settings     #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}"
            );

            // Extract flags with optional values
            for (int current = 0, next = 1; current < args.Length; current++, next++)
            {
                // Parse flag
                var isFlag = args[current].StartsWith("-");
                if (!isFlag) continue;
                var flag = args[current].TrimStart('-');

                // Parse optional value
                var flagHasValue = next < args.Length && !args[next].StartsWith("-");
                var value = flagHasValue ? args[next].TrimStart('-') : "";
                var secret = Secrets.Contains(flag);
                var displayValue = secret ? "*HIDDEN*" : "\"" + value + "\"";

                // Assign
                Console.WriteLine($"Found flag \"{flag}\" with value {displayValue}.");
                providedArguments.Add(flag, value);
            }
        }

        private static void Build(BuildTarget buildTarget, int buildSubtarget, string filePath, BuildOptions? options)
        {
            var buildOption = BuildOptions.None;
            if (options is not null)
                buildOption = (BuildOptions)options;
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                target = buildTarget,
                //                targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget),
                locationPathName = filePath,
                options = buildOption,
#if UNITY_2021_2_OR_NEWER
                subtarget = buildSubtarget
#endif
            };

            var buildSummary = BuildPipeline.BuildPlayer(buildPlayerOptions).summary;
            ReportSummary(buildSummary);
            ExitWithResult(buildSummary.result);
        }

        private static void ReportSummary(BuildSummary summary)
        {
            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#      Build results      #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"Duration: {summary.totalTime.ToString()}{Eol}" +
                $"Warnings: {summary.totalWarnings.ToString()}{Eol}" +
                $"Errors: {summary.totalErrors.ToString()}{Eol}" +
                $"Size: {summary.totalSize.ToString()} bytes{Eol}" +
                $"{Eol}"
            );
        }

        private static void ExitWithResult(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Succeeded:
                    Console.WriteLine("Build succeeded!");
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Failed:
                    Console.WriteLine("Build failed!");
                    EditorApplication.Exit(101);
                    break;
                case BuildResult.Cancelled:
                    Console.WriteLine("Build cancelled!");
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                default:
                    Console.WriteLine("Build result is unknown!");
                    EditorApplication.Exit(103);
                    break;
            }
        }
    }
}