#nullable enable
#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using UnityEngine;

namespace MajdataPlay
{
    /// <summary>Restores bundled charts and skins only when their player-managed roots are empty.</summary>
    internal static class DesktopResourceRestorer
    {
        /// <summary>Copies official managed resources into empty Windows chart and skin roots.</summary>
        /// <exception cref="DirectoryNotFoundException">A managed root does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">A managed root cannot be enumerated.</exception>
        /// <exception cref="IOException">A managed root cannot be enumerated or prepared.</exception>
        public static void RestoreManagedAssetsIfMissing()
        {
            var chartRootMissing = !MajEnv.ChartDirectory.EnumerateEntries().Any();
            var skinRootMissing = !MajEnv.SkinDirectory.EnumerateEntries().Any();
            if (!chartRootMissing && !skinRootMissing)
            {
                return;
            }

            if (!ResourceManifestLoader.TryGetV2Hashes(out var v2Hashes))
            {
                return;
            }

            if (chartRootMissing)
            {
                RestoreGroup("MaiCharts/", MajEnv.ChartDirectory, v2Hashes);
            }

            if (skinRootMissing)
            {
                RestoreGroup("Skins/", MajEnv.SkinDirectory, v2Hashes);
            }
        }

        /// <summary>Copies one manifest group from packaged resources, retaining nested relative paths.</summary>
        /// <param name="sourcePrefix">The manifest prefix identifying the managed group.</param>
        /// <param name="destinationRoot">The local root receiving that group's resources.</param>
        /// <param name="v2Hashes">The validated manifest identifying official packaged resources.</param>
        /// <exception cref="UnauthorizedAccessException">A destination directory cannot be prepared.</exception>
        /// <exception cref="IOException">A destination directory cannot be prepared.</exception>
        private static void RestoreGroup(
            string sourcePrefix,
            StorageDirectory destinationRoot,
            IReadOnlyDictionary<string, string> v2Hashes)
        {
            FileSystem.Directory.Create(destinationRoot.Location);
            var packagedRoot = FileSystem.Directory.Open(Application.streamingAssetsPath);
            foreach (var relativePath in v2Hashes.Keys
                         .Where(path => path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    var sourceFile = packagedRoot.FindFileAt(relativePath);
                    if (sourceFile is null)
                    {
                        MajDebug.LogError($"Restore managed asset failed(Windows): packaged resource not found: {relativePath}");
                        continue;
                    }
                    var names = FileSystem.Path.Split(relativePath.Substring(sourcePrefix.Length));
                    if (names.Length == 0)
                    {
                        MajDebug.LogError($"Restore managed asset failed(Windows): manifest path names no file: {relativePath}");
                        continue;
                    }
                    var destinationDirectory = destinationRoot.CreateDirectoryAt(names.AsSpan(0, names.Length - 1));
                    var restored = FileSystem.File.Copy(
                        sourceFile.Location,
                        destinationDirectory.Location,
                        names[names.Length - 1],
                        overwrite: true);
                    MajDebug.LogInfo($"Restore managed asset(Windows): {sourceFile.Location} -> {restored.Location}");
                }
                catch (Exception exception)
                {
                    MajDebug.LogError(
                        $"Restore managed asset failed(Windows): {relativePath}\n{exception}");
                }
            }
        }
    }
}
#endif
