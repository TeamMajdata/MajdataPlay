#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Restores bundled charts and skins on desktop players only when their player-managed roots are empty.</summary>
    /// <remarks>Desktop players read every other resource straight from the packaged streaming assets, so nothing else is checked.</remarks>
    internal static class DesktopResourceChecker
    {
        /// <summary>The loading message shown while the bundled charts are restored.</summary>
        private const string RestoringChartsMessage = "Restoring charts...";

        /// <summary>The loading message shown while the bundled skins are restored.</summary>
        private const string RestoringSkinsMessage = "Restoring skins...";

        /// <summary>Copies official managed resources into empty chart and skin roots.</summary>
        /// <param name="progress">Receives the progress of each restored group.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the restore between files.</param>
        /// <returns>An operation that completes after the check; failures of single files are logged and skipped.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="DirectoryNotFoundException">A managed root does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">A managed root cannot be enumerated.</exception>
        /// <exception cref="IOException">A managed root cannot be enumerated or prepared.</exception>
        public static async ValueTask CheckAsync(IProgress<ProgressReport> progress, FrameBudget budget, CancellationToken cancellationToken)
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
                await RestoreGroupAsync("MaiCharts/", MajEnv.ChartDirectory, v2Hashes, RestoringChartsMessage, progress, budget, cancellationToken);
            }

            if (skinRootMissing)
            {
                await RestoreGroupAsync("Skins/", MajEnv.SkinDirectory, v2Hashes, RestoringSkinsMessage, progress, budget, cancellationToken);
            }
        }

        /// <summary>Copies one manifest group from packaged resources, retaining nested relative paths.</summary>
        /// <param name="sourcePrefix">The manifest prefix identifying the managed group.</param>
        /// <param name="destinationRoot">The local root receiving that group's resources.</param>
        /// <param name="v2Hashes">The validated manifest identifying official packaged resources.</param>
        /// <param name="message">The loading message reported for the group.</param>
        /// <param name="progress">Receives the number of processed files.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the restore between files.</param>
        /// <returns>An operation that completes after every listed file of the group was processed.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">A destination directory cannot be prepared.</exception>
        /// <exception cref="IOException">A destination directory cannot be prepared.</exception>
        private static async ValueTask RestoreGroupAsync(
            string sourcePrefix,
            StorageDirectory destinationRoot,
            IReadOnlyDictionary<string, string> v2Hashes,
            string message,
            IProgress<ProgressReport> progress,
            FrameBudget budget,
            CancellationToken cancellationToken)
        {
            FileSystem.Directory.Create(destinationRoot.Location);
            var packagedRoot = PackagedResources.OpenRoot();
            var relativePaths = v2Hashes.Keys
                .Where(path => path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            progress.Report(new ProgressReport(message, 0, relativePaths.Length));
            for (var i = 0; i < relativePaths.Length; i++)
            {
                await budget.YieldIfExceededAsync(cancellationToken);
                RestoreFile(relativePaths[i], sourcePrefix, packagedRoot, destinationRoot);
                progress.Report(new ProgressReport(message, i + 1, relativePaths.Length));
            }
        }

        /// <summary>Copies one packaged file of a managed group below its local root.</summary>
        /// <param name="relativePath">The manifest path of the packaged file.</param>
        /// <param name="sourcePrefix">The manifest prefix of the group, removed from the destination path.</param>
        /// <param name="packagedRoot">The packaged streaming assets root.</param>
        /// <param name="destinationRoot">The local root receiving the group.</param>
        /// <returns>Whether the file was restored; failures are logged.</returns>
        private static bool RestoreFile(
            string relativePath,
            string sourcePrefix,
            StorageDirectory packagedRoot,
            StorageDirectory destinationRoot)
        {
            try
            {
                var sourceFile = packagedRoot.FindFileAt(relativePath);
                if (sourceFile is null)
                {
                    MajDebug.LogError(nameof(DesktopResourceChecker), $"Restore managed asset failed: packaged resource not found: {relativePath}");
                    return false;
                }
                var names = FileSystem.Path.Split(relativePath.Substring(sourcePrefix.Length));
                if (names.Length == 0)
                {
                    MajDebug.LogError(nameof(DesktopResourceChecker), $"Restore managed asset failed: manifest path names no file: {relativePath}");
                    return false;
                }
                var destinationDirectory = destinationRoot.CreateDirectoryAt(names.AsSpan(0, names.Length - 1));
                var restored = FileSystem.File.Copy(
                    sourceFile.Location,
                    destinationDirectory.Location,
                    names[names.Length - 1],
                    overwrite: true);
                MajDebug.LogInfo(nameof(DesktopResourceChecker), $"Restore managed asset: {sourceFile.Location} -> {restored.Location}");
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(DesktopResourceChecker), $"Restore managed asset failed: {relativePath}\n{exception}");
                return false;
            }
        }
    }
}
