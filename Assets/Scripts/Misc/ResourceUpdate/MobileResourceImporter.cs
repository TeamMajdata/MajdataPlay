using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Extracts the packaged mobile resources on the first start, before the extracted resources exist.</summary>
    /// <remarks>
    /// Every packaged file is verified and staged in a sibling directory that is renamed into place only after all files
    /// succeeded, so an interrupted extraction is repeated in full on the next start. The bundled charts and skin then
    /// move into the player-managed roots through <see cref="MobileManagedAssetMover"/>.
    /// </remarks>
    internal static class MobileResourceImporter
    {
        /// <summary>The suffix of the staging directory beside the extracted resources.</summary>
        private const string StagingSuffix = ".extracting-v2";

        /// <summary>The loading message shown while packaged resources are extracted.</summary>
        private const string ExtractingMessage = "Extracting resources...";

        /// <summary>The loading message shown while the bundled charts and skin are installed.</summary>
        private const string InstallingMessage = "Installing charts and skins...";

        /// <summary>Extracts and verifies every packaged resource, then installs the bundled charts and skin.</summary>
        /// <param name="progress">Receives the progress of each step.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the extraction between files; the staged files are discarded on the next start.</param>
        /// <returns>An operation that completes with whether the extraction was committed; failures are logged.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The extracted resources or a local destination cannot be queried.</exception>
        /// <exception cref="IOException">The extracted resources or a local destination cannot be queried.</exception>
        public static async ValueTask<bool> ImportAsync(IProgress<ProgressReport> progress, FrameBudget budget, CancellationToken cancellationToken)
        {
            if (!await ExtractAsync(progress, budget, cancellationToken))
            {
                MajDebug.LogError(nameof(MobileResourceImporter), "Initial resource extraction was incomplete; managed assets were not moved.");
                return false;
            }

            progress.Report(new ProgressReport(InstallingMessage));
            await budget.YieldIfExceededAsync(cancellationToken);
            var preservedExistingManagedAssets = MobileManagedAssetMover.MoveBundledAssets();
            if (preservedExistingManagedAssets &&
                ResourceManifestLoader.TryGetUpdateManifests(out var v1Hashes, out var v2Hashes, out var diffPaths))
            {
                // The extracted resources can be missing while player-managed charts and skins still exist.
                // Run the normal v1 hash gate for those existing files.
                await MobileResourceChecker.ApplyV2DiffAsync(v1Hashes, v2Hashes, diffPaths, progress, budget, cancellationToken);
            }
            return true;
        }

        /// <summary>Stages and verifies every packaged resource before committing the extracted resource root.</summary>
        /// <param name="progress">Receives the number of extracted files.</param>
        /// <param name="budget">Splits the work across frames.</param>
        /// <param name="cancellationToken">Cancels the extraction between files.</param>
        /// <returns>An operation that completes with whether all resources were verified and their staged directory was committed.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        private static async ValueTask<bool> ExtractAsync(IProgress<ProgressReport> progress, FrameBudget budget, CancellationToken cancellationToken)
        {
            if (!ResourceManifestLoader.TryGetV2Hashes(out var v2Hashes))
            {
                return false;
            }

            var stagingDirectory = TryPrepareStagingDirectory();
            if (stagingDirectory is null)
            {
                return false;
            }

            var succeeded = true;
            var extractedCount = 0;
            progress.Report(new ProgressReport(ExtractingMessage, 0, v2Hashes.Count));
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                await budget.YieldIfExceededAsync(cancellationToken);
                if (!ResourceFiles.TryCopyVerified(relativePath, stagingDirectory, hash, "Extract"))
                {
                    succeeded = false;
                }
                extractedCount++;
                progress.Report(new ProgressReport(ExtractingMessage, extractedCount, v2Hashes.Count));
            }

            if (!succeeded)
            {
                return false;
            }

            try
            {
                MobileManagedAssetMover.MarkPendingMoves(stagingDirectory);
                FileSystem.Directory.Move(stagingDirectory.Location, MajEnv.AssetsDirectory.Location);
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(MobileResourceImporter), $"Failed to finish initial resource extraction:\n{exception}");
                return false;
            }
        }

        /// <summary>Replaces any leftover staging entry with a fresh, empty staging directory.</summary>
        /// <returns>The staging directory, or null after a logged failure.</returns>
        /// <remarks>The extracted resources are app-private native storage, so a staging sibling can be renamed into place atomically.</remarks>
        private static StorageDirectory? TryPrepareStagingDirectory()
        {
            var stagingLocation = MajEnv.AssetsDirectory.Location.TrimEnd(
                                      Path.DirectorySeparatorChar,
                                      Path.AltDirectorySeparatorChar) + StagingSuffix;
            try
            {
                var leftoverDirectory = FileSystem.Directory.Open(stagingLocation);
                if (leftoverDirectory.Exists)
                {
                    leftoverDirectory.Delete(recursive: true);
                }
                else
                {
                    var leftoverFile = FileSystem.File.Open(stagingLocation);
                    if (leftoverFile.Exists)
                    {
                        leftoverFile.Delete();
                    }
                }

                return FileSystem.Directory.Create(stagingLocation);
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(MobileResourceImporter), $"Failed to prepare resource extraction directory:\n{exception}");
                return null;
            }
        }
    }
}
