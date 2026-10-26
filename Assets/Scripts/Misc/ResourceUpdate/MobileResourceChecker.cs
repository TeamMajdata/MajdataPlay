using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Checks already extracted mobile resources against the packaged ones while preserving player customizations.</summary>
    /// <remarks>
    /// Official v1 files that the player left unchanged are replaced by verified v2 bytes, and missing resources outside
    /// the player-managed chart and skin roots are restored. Charts and skins the player changed, deleted or added are
    /// never touched.
    /// </remarks>
    internal static class MobileResourceChecker
    {
        /// <summary>The loading message shown while changed resources are updated.</summary>
        private const string UpdatingMessage = "Updating resources...";

        /// <summary>The loading message shown while missing resources are restored.</summary>
        private const string CheckingMessage = "Checking resources...";

        /// <summary>Resumes pending moves, updates official files changed since v1, and restores missing resources.</summary>
        /// <param name="progress">Receives the progress of each step.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the check between files.</param>
        /// <returns>An operation that completes after the check; failures of single files are logged and skipped.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The extracted resources or a local destination cannot be queried.</exception>
        /// <exception cref="IOException">The extracted resources or a local destination cannot be queried.</exception>
        public static async ValueTask CheckAsync(IProgress<ProgressReport> progress, FrameBudget budget, CancellationToken cancellationToken)
        {
            progress.Report(new ProgressReport(CheckingMessage));
            MobileManagedAssetMover.ResumePendingMoves();
            if (!ResourceManifestLoader.TryGetUpdateManifests(out var v1Hashes, out var v2Hashes, out var diffPaths))
            {
                return;
            }

            await ApplyV2DiffAsync(v1Hashes, v2Hashes, diffPaths, progress, budget, cancellationToken);
            await SyncMissingAssetsAsync(v2Hashes, diffPaths, progress, budget, cancellationToken);
        }

        /// <summary>Replaces eligible official v1 files with verified packaged v2 bytes.</summary>
        /// <param name="v1Hashes">The previous official hashes used to protect customizations.</param>
        /// <param name="v2Hashes">The current hashes used to verify packaged replacements.</param>
        /// <param name="diffPaths">The official paths changed by this resource update.</param>
        /// <param name="progress">Receives the number of checked paths.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the update between files.</param>
        /// <returns>An operation that completes after every changed path was checked.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        public static async ValueTask ApplyV2DiffAsync(
            IReadOnlyDictionary<string, string> v1Hashes,
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths,
            IProgress<ProgressReport> progress,
            FrameBudget budget,
            CancellationToken cancellationToken)
        {
            var updatedCount = 0;
            var preservedCount = 0;
            var checkedCount = 0;
            progress.Report(new ProgressReport(UpdatingMessage, 0, diffPaths.Count));
            foreach (var relativePath in diffPaths)
            {
                await budget.YieldIfExceededAsync(cancellationToken);
                switch (UpdateFile(relativePath, v1Hashes, v2Hashes[relativePath]))
                {
                    case FileUpdateResult.Updated:
                        updatedCount++;
                        break;
                    case FileUpdateResult.Preserved:
                        preservedCount++;
                        break;
                }
                checkedCount++;
                progress.Report(new ProgressReport(UpdatingMessage, checkedCount, diffPaths.Count));
            }

            MajDebug.LogInfo(nameof(MobileResourceChecker),
                $"Mobile resource update finished: {updatedCount} updated, " +
                $"{preservedCount} customized or missing file(s) preserved.");
        }

        /// <summary>Describes what happened to one changed path.</summary>
        private enum FileUpdateResult
        {
            /// <summary>The verified v2 bytes were written.</summary>
            Updated,

            /// <summary>A player customization, deletion or layout was preserved.</summary>
            Preserved,

            /// <summary>The packaged bytes could not be read, verified or written; the failure was logged.</summary>
            Failed,
        }

        /// <summary>Decides whether one changed path may be updated and writes the verified v2 bytes when it may.</summary>
        /// <param name="relativePath">The changed portable path.</param>
        /// <param name="v1Hashes">The previous official hashes used to protect customizations.</param>
        /// <param name="v2Hash">The current official hash of the path.</param>
        /// <returns>What happened to the path.</returns>
        /// <exception cref="UnauthorizedAccessException">The local destination cannot be queried.</exception>
        /// <exception cref="IOException">The local destination cannot be queried.</exception>
        private static FileUpdateResult UpdateFile(
            string relativePath,
            IReadOnlyDictionary<string, string> v1Hashes,
            string v2Hash)
        {
            var destinationRoot = GetDestinationRoot(relativePath);
            var destinationFile = destinationRoot.FindFileAt(relativePath);
            var isRootManagedAsset = IsRootManagedAsset(relativePath);
            if (v1Hashes.TryGetValue(relativePath, out var v1Hash))
            {
                if (destinationFile is null)
                {
                    if (isRootManagedAsset)
                    {
                        MajDebug.LogInfo(nameof(MobileResourceChecker), $"Resource update preserved player deletion: {relativePath}");
                        return FileUpdateResult.Preserved;
                    }
                }
                else if (!ResourceFiles.TryComputeSha256(destinationFile, out var localHash) ||
                         !string.Equals(localHash, v1Hash, StringComparison.OrdinalIgnoreCase))
                {
                    MajDebug.LogInfo(nameof(MobileResourceChecker), $"Resource update preserved player customization: {relativePath}");
                    return FileUpdateResult.Preserved;
                }
            }
            else if (destinationFile is not null)
            {
                // This path did not exist in v1. An existing local file is player-owned.
                MajDebug.LogInfo(nameof(MobileResourceChecker), $"Resource update preserved player file at new v2 path: {relativePath}");
                return FileUpdateResult.Preserved;
            }
            else if (isRootManagedAsset)
            {
                // Do not repopulate player-managed chart/skin trees during an upgrade.
                MajDebug.LogInfo(nameof(MobileResourceChecker),
                    $"Resource update skipped new managed asset to preserve player layout: {relativePath}");
                return FileUpdateResult.Preserved;
            }

            if (!ResourceFiles.TryReadVerified(relativePath, v2Hash, "Resource update", out var v2Data))
            {
                return FileUpdateResult.Failed;
            }

            try
            {
                ResourceFiles.WriteAllBytesAtomically(destinationRoot, relativePath, v2Data);
                MajDebug.LogInfo(nameof(MobileResourceChecker), $"Resource updated from official v1 to v2: {relativePath}");
                return FileUpdateResult.Updated;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(MobileResourceChecker), $"Resource update failed while writing: {relativePath}\n{exception}");
                return FileUpdateResult.Failed;
            }
        }

        /// <summary>Copies missing unmanaged resources that are outside the explicit update diff.</summary>
        /// <param name="v2Hashes">The validated current packaged resource hashes.</param>
        /// <param name="diffPaths">The changed paths governed by the official v1 update gate.</param>
        /// <param name="progress">Receives the number of checked paths.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the check between files.</param>
        /// <returns>An operation that completes after every packaged path was checked.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        private static async ValueTask SyncMissingAssetsAsync(
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths,
            IProgress<ProgressReport> progress,
            FrameBudget budget,
            CancellationToken cancellationToken)
        {
            var checkedCount = 0;
            progress.Report(new ProgressReport(CheckingMessage, 0, v2Hashes.Count));
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                await budget.YieldIfExceededAsync(cancellationToken);
                if (!IsRootManagedAsset(relativePath) &&
                    !diffPaths.Contains(relativePath) &&
                    MajEnv.AssetsDirectory.FindFileAt(relativePath) is null)
                {
                    ResourceFiles.TryCopyVerified(relativePath, MajEnv.AssetsDirectory, hash, "Sync missing");
                }
                checkedCount++;
                progress.Report(new ProgressReport(CheckingMessage, checkedCount, v2Hashes.Count));
            }
        }

        /// <summary>Identifies chart and skin paths owned by the player-managed root.</summary>
        /// <param name="path">The portable manifest-relative path.</param>
        /// <returns>Whether the path belongs to a chart or skin tree.</returns>
        internal static bool IsRootManagedAsset(string path)
        {
            return path.StartsWith("MaiCharts/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("Skins/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Maps a portable manifest path to the resource root or player-managed root below which it lives.</summary>
        /// <param name="relativePath">The validated portable manifest path.</param>
        /// <returns>The root directory whose backend resolves the manifest path.</returns>
        private static StorageDirectory GetDestinationRoot(string relativePath)
        {
            return IsRootManagedAsset(relativePath) ? MajEnv.RootDirectory : MajEnv.AssetsDirectory;
        }
    }
}
