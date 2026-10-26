#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.ResourceUpdate;

namespace MajdataPlay
{
    /// <summary>Supplies only the environment paths consumed by the linked resource workflows.</summary>
    internal static class MajEnv
    {
        /// <summary>Gets or sets the fixture's application root.</summary>
        public static StorageDirectory RootDirectory { get; set; } = FileSystem.Directory.Open(".");

        /// <summary>Gets the fixture's extracted resource root.</summary>
        public static StorageDirectory AssetsDirectory => FileSystem.Directory.Open(Path.Combine(RootDirectory.Location, "Assets"));

        /// <summary>Gets or sets a chart root on another backend, or null for the native directory below the application root.</summary>
        public static StorageDirectory? ChartDirectoryOverride { get; set; }

        /// <summary>Gets or sets a skin root on another backend, or null for the native directory below the application root.</summary>
        public static StorageDirectory? SkinDirectoryOverride { get; set; }

        /// <summary>Gets the fixture's player-managed chart root.</summary>
        public static StorageDirectory ChartDirectory => ChartDirectoryOverride ?? FileSystem.Directory.Open(Path.Combine(RootDirectory.Location, "MaiCharts"));

        /// <summary>Gets the fixture's player-managed skin root.</summary>
        public static StorageDirectory SkinDirectory => SkinDirectoryOverride ?? FileSystem.Directory.Open(Path.Combine(RootDirectory.Location, "Skins"));
    }
}

namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Provides controlled validated manifests without substituting any storage behavior.</summary>
    internal static class ResourceManifestLoader
    {
        /// <summary>Gets the test's official previous resource hashes.</summary>
        public static Dictionary<string, string> V1Hashes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the test's packaged resource hashes.</summary>
        public static Dictionary<string, string> V2Hashes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the test's changed packaged resource paths.</summary>
        public static HashSet<string> DiffPaths { get; } = new(StringComparer.Ordinal);

        /// <summary>Returns controlled manifests when the fixture supplies packaged resources.</summary>
        /// <param name="v1Hashes">Receives previous official hashes.</param>
        /// <param name="v2Hashes">Receives current packaged hashes.</param>
        /// <param name="diffPaths">Receives changed resource paths.</param>
        /// <returns>Whether a packaged manifest is available.</returns>
        public static bool TryGetUpdateManifests(out Dictionary<string, string> v1Hashes,
            out Dictionary<string, string> v2Hashes, out HashSet<string> diffPaths)
        {
            v1Hashes = V1Hashes;
            v2Hashes = V2Hashes;
            diffPaths = DiffPaths;
            return V2Hashes.Count != 0;
        }

        /// <summary>Returns the fixture's validated packaged resource hashes.</summary>
        /// <param name="hashes">Receives current packaged hashes.</param>
        /// <returns>Whether a packaged manifest is available.</returns>
        public static bool TryGetV2Hashes(out Dictionary<string, string> hashes)
        {
            hashes = V2Hashes;
            return hashes.Count != 0;
        }
    }

    /// <summary>Replaces the UniTask frame budget without requiring a player loop.</summary>
    internal sealed class FrameBudget
    {
        /// <summary>Gets how many times the linked workflows asked whether to yield.</summary>
        public int YieldChecks { get; private set; }

        /// <summary>Counts the check and never yields, as no player loop runs.</summary>
        /// <param name="cancellationToken">Cancels the workflow between work items.</param>
        /// <returns>A completed operation.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public ValueTask YieldIfExceededAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            YieldChecks++;
            return default;
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Captures resource failures while discarding routine informational messages.</summary>
    internal static class MajDebug
    {
        /// <summary>Gets all errors logged by the current case.</summary>
        public static ConcurrentQueue<string> Errors { get; } = new();

        /// <summary>Gets or sets the hook observing a completed extraction copy on its worker.</summary>
        public static Action? ExtractionCopyCompleted { get; set; }

        /// <summary>Accepts an informational message without affecting assertions.</summary>
        /// <param name="message">The production workflow's informational message.</param>
        public static void LogInfo(string message)
        {
        }

        /// <summary>Accepts a tagged informational message without affecting assertions.</summary>
        /// <param name="tag">The production class that logged the message.</param>
        /// <param name="message">The production workflow's informational message.</param>
        public static void LogInfo(string tag, string message)
        {
            if (tag == nameof(ResourceFiles) && message.StartsWith("Extract:", StringComparison.Ordinal))
            {
                ExtractionCopyCompleted?.Invoke();
            }
        }

        /// <summary>Accepts a warning without affecting assertions, as a tolerated transfer detail is not a failure.</summary>
        /// <param name="message">The production workflow's warning message.</param>
        public static void LogWarning(string message)
        {
        }

        /// <summary>Records a production failure for the case to assert.</summary>
        /// <param name="message">The failure and its local fixture context.</param>
        public static void LogError(string message)
        {
            Errors.Enqueue(message);
        }

        /// <summary>Records a tagged production failure for the case to assert.</summary>
        /// <param name="tag">The production class that logged the failure.</param>
        /// <param name="message">The failure and its local fixture context.</param>
        public static void LogError(string tag, string message)
        {
            Errors.Enqueue(message);
        }
    }
}

namespace UnityEngine
{
    /// <summary>Supplies the packaged fixture path used by the linked importer and checkers.</summary>
    internal static class Application
    {
        /// <summary>Stores the current fixture's packaged resource root.</summary>
        private static string s_streamingAssetsPath = string.Empty;

        /// <summary>Gets or sets the only thread allowed to query Unity's path, or null to disable the guard.</summary>
        public static int? StreamingAssetsOwnerThreadId { get; set; }

        /// <summary>Gets or sets the current fixture's packaged resource root.</summary>
        /// <exception cref="InvalidOperationException">A guarded Unity property is queried from another thread.</exception>
        public static string streamingAssetsPath
        {
            get
            {
                if (StreamingAssetsOwnerThreadId is int ownerThreadId && Environment.CurrentManagedThreadId != ownerThreadId)
                {
                    throw new InvalidOperationException("Unity's packaged path was queried from a background thread.");
                }
                return s_streamingAssetsPath;
            }
            set
            {
                s_streamingAssetsPath = value;
            }
        }
    }
}
