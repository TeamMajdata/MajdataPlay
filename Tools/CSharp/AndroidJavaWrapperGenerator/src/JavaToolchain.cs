using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Finds explicit, environmental, or pinned Unity Android SDK installations.</summary>
    internal static class JavaToolchain
    {
        /// <summary>Identifies the Unity version used for default toolchain discovery.</summary>
        private const string UnityVersion = "6000.3.17f1";

        /// <summary>Locates an SDK root and selects its highest installed stable platform by default.</summary>
        /// <param name="options">The explicit SDK and API selection.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        /// <returns>The selected android.jar, or null when SDK scanning is disabled.</returns>
        /// <exception cref="ArgumentException">The requested API level is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">No configured or discovered SDK root exists.</exception>
        /// <exception cref="FileNotFoundException">The selected SDK has no matching stable platform jar.</exception>
        /// <exception cref="IOException">An installed platform's metadata cannot be read.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        internal static string? FindPlatformJar(GeneratorOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.NoSdk)
            {
                return null;
            }
            if (options.ApiLevel is int requested && requested <= 0)
            {
                throw new ArgumentException("The Android API level must be a positive integer.", nameof(options));
            }
            if (options.SdkDirectory is not null)
            {
                var configured = FullPath(options.SdkDirectory, "Android SDK");
                var sdk = SdkRoot(configured);
                if (sdk is null)
                {
                    throw new DirectoryNotFoundException($"Android SDK '{configured}' has no platforms directory. Supply an SDK root or its Unity AndroidPlayer parent.");
                }
                return SelectPlatform(sdk, options.ApiLevel, cancellationToken);
            }
            var candidates = new List<string>();
            AddEnvironment(candidates, "UNITY_ANDROID_SDK");
            AddEnvironment(candidates, "ANDROID_SDK_ROOT");
            AddEnvironment(candidates, "ANDROID_HOME");
            foreach (var player in UnityAndroidPlayers())
            {
                candidates.Add(Path.Combine(player, "SDK"));
            }
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sdk = SdkRoot(candidate);
                if (sdk is not null)
                {
                    return SelectPlatform(sdk, options.ApiLevel, cancellationToken);
                }
            }
            throw new DirectoryNotFoundException($"Cannot find an Android SDK for Unity {UnityVersion}. Use --sdk, UNITY_ANDROID_SDK, ANDROID_SDK_ROOT, ANDROID_HOME, or UNITY_EDITOR_PATH; use --no-sdk for standalone inputs.");
        }

        /// <summary>Expands environment variables and validates a configured filesystem path.</summary>
        /// <param name="value">The caller's path, relative to the current working directory when necessary.</param>
        /// <param name="description">The kind of input used in diagnostics.</param>
        /// <returns>The normalized absolute path.</returns>
        /// <exception cref="ArgumentException">The path is empty, invalid, or contains a line break.</exception>
        internal static string FullPath(string value, string description)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
            {
                throw new ArgumentException($"The {description} path is empty or contains NUL or a line break.");
            }
            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                throw new ArgumentException($"Invalid {description} path '{value}': {exception.Message}", exception);
            }
        }

        /// <summary>Recognizes an SDK directory or a Unity AndroidPlayer/editor directory containing it.</summary>
        /// <param name="candidate">The SDK or Unity directory to inspect.</param>
        /// <returns>The SDK root, or null if no platforms directory exists.</returns>
        private static string? SdkRoot(string candidate)
        {
            foreach (var directory in ComponentCandidates(candidate, "SDK"))
            {
                if (Directory.Exists(Path.Combine(directory, "platforms")))
                {
                    return Path.GetFullPath(directory);
                }
            }
            return null;
        }

        /// <summary>Produces conventional SDK layouts without scanning other installed editor versions.</summary>
        /// <param name="directory">The configured directory or editor executable.</param>
        /// <param name="component">The SDK component name, or empty for AndroidPlayer directories.</param>
        /// <returns>The possible component roots in deterministic order.</returns>
        private static IEnumerable<string> ComponentCandidates(string directory, string component)
        {
            if (File.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory) ?? directory;
            }
            yield return directory;
            yield return Path.Combine(directory, component);
            yield return Path.Combine(directory, "PlaybackEngines", "AndroidPlayer", component);
            yield return Path.Combine(directory, "Data", "PlaybackEngines", "AndroidPlayer", component);
            yield return Path.Combine(directory, "Editor", "Data", "PlaybackEngines", "AndroidPlayer", component);
            yield return Path.Combine(directory, "Contents", "PlaybackEngines", "AndroidPlayer", component);
            yield return Path.Combine(directory, "Unity.app", "Contents", "PlaybackEngines", "AndroidPlayer", component);
        }

        /// <summary>Chooses an installed integer platform and excludes named or metadata-marked previews.</summary>
        /// <param name="sdk">The normalized SDK root.</param>
        /// <param name="apiLevel">The requested API level, or null for the highest stable level.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        /// <returns>The selected android.jar path.</returns>
        /// <exception cref="FileNotFoundException">No requested stable platform jar is installed.</exception>
        /// <exception cref="IOException">Platform metadata cannot be read.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private static string SelectPlatform(string sdk, int? apiLevel, CancellationToken cancellationToken)
        {
            var platforms = Path.Combine(sdk, "platforms");
            string? selected = null;
            var highest = 0;
            foreach (var directory in Directory.EnumerateDirectories(platforms))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!name.StartsWith("android-", StringComparison.Ordinal)
                    || !int.TryParse(name.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                    || level <= 0 || (apiLevel is int requested && level != requested))
                {
                    continue;
                }
                var jar = Path.Combine(directory, "android.jar");
                if (File.Exists(jar) && level > highest && IsStablePlatform(directory, cancellationToken))
                {
                    highest = level;
                    selected = jar;
                }
            }
            if (selected is null)
            {
                var requested = apiLevel is int level ? $"API {level}" : "a stable integer API level";
                throw new FileNotFoundException($"Android SDK '{sdk}' has no installed {requested} android.jar under '{platforms}'. Install the platform or choose --api explicitly.");
            }
            return Path.GetFullPath(selected);
        }

        /// <summary>Checks platform metadata for a preview codename when source.properties is present.</summary>
        /// <param name="directory">The installed Android platform directory.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        /// <returns>Whether the directory is a release platform.</returns>
        /// <exception cref="InvalidDataException">The metadata file exceeds its inspection limit.</exception>
        /// <exception cref="IOException">The metadata cannot be read.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private static bool IsStablePlatform(string directory, CancellationToken cancellationToken)
        {
            var properties = Path.Combine(directory, "source.properties");
            if (!File.Exists(properties))
            {
                return true;
            }
            if (new FileInfo(properties).Length > 1024 * 1024)
            {
                throw new InvalidDataException($"Android platform metadata '{properties}' exceeds the 1 MiB inspection limit.");
            }
            foreach (var line in File.ReadLines(properties))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var separator = line.IndexOf('=');
                if (separator >= 0 && line.Substring(0, separator).Trim() == "AndroidVersion.Codename")
                {
                    var codename = line.Substring(separator + 1).Trim();
                    return codename.Length == 0 || codename == "REL";
                }
            }
            return true;
        }

        /// <summary>Adds a nonempty optional environment path to a candidate list.</summary>
        /// <param name="candidates">The ordered destination list.</param>
        /// <param name="name">The environment variable name.</param>
        /// <exception cref="ArgumentException">The environment path is malformed.</exception>
        private static void AddEnvironment(List<string> candidates, string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(FullPath(value, name));
            }
        }

        /// <summary>Enumerates an explicit editor hint and conventional installations of the pinned Unity version.</summary>
        /// <returns>Possible Unity AndroidPlayer directories in deterministic order.</returns>
        /// <exception cref="ArgumentException">UNITY_EDITOR_PATH is malformed.</exception>
        private static IEnumerable<string> UnityAndroidPlayers()
        {
            var editor = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
            if (!string.IsNullOrWhiteSpace(editor))
            {
                foreach (var player in ComponentCandidates(FullPath(editor, "UNITY_EDITOR_PATH"), ""))
                {
                    if (Directory.Exists(player) && Directory.Exists(Path.Combine(player, "SDK")))
                    {
                        yield return player;
                    }
                }
            }
            if (OperatingSystem.IsWindows())
            {
                foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                {
                    if (programFiles.Length == 0)
                    {
                        continue;
                    }
                    yield return Path.Combine(programFiles, "Unity Editors", UnityVersion, "Editor", "Data", "PlaybackEngines", "AndroidPlayer");
                    yield return Path.Combine(programFiles, "Unity", "Hub", "Editor", UnityVersion, "Editor", "Data", "PlaybackEngines", "AndroidPlayer");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine("/Applications/Unity/Hub/Editor", UnityVersion, "Unity.app", "Contents", "PlaybackEngines", "AndroidPlayer");
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                yield return Path.Combine(home, "Unity", "Hub", "Editor", UnityVersion, "Editor", "Data", "PlaybackEngines", "AndroidPlayer");
                yield return Path.Combine("/opt/Unity/Hub/Editor", UnityVersion, "Editor", "Data", "PlaybackEngines", "AndroidPlayer");
            }
        }

    }
}
