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
            var chartRootMissing = !FileSystem.OpenDirectory(MajEnv.ChartPath).EnumerateEntries().Any();
            var skinRootMissing = !FileSystem.OpenDirectory(MajEnv.SkinPath).EnumerateEntries().Any();
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
                RestoreGroup("MaiCharts/", MajEnv.ChartPath, v2Hashes);
            }

            if (skinRootMissing)
            {
                RestoreGroup("Skins/", MajEnv.SkinPath, v2Hashes);
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
            string destinationRoot,
            IReadOnlyDictionary<string, string> v2Hashes)
        {
            FileSystem.CreateDirectory(destinationRoot);
            foreach (var relativePath in v2Hashes.Keys
                         .Where(path => path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var sourcePath = Path.Combine(
                    Application.streamingAssetsPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                var destinationPath = Path.Combine(
                    destinationRoot,
                    relativePath.Substring(sourcePrefix.Length)
                                .Replace('/', Path.DirectorySeparatorChar));
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    FileSystem.CreateDirectory(destinationDirectory);
                }

                try
                {
                    FileSystem.CopyFile(
                        sourcePath,
                        destinationPath,
                        overwrite: true);
                    MajDebug.LogInfo($"Restore managed asset(Windows): {sourcePath} -> {destinationPath}");
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
