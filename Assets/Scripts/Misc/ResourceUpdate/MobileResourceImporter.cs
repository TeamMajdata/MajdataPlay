using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using UnityEngine;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Extracts the packaged mobile resources on the first start, before the extracted resources exist.</summary>
    /// <remarks>
    /// Every packaged file is verified and staged in a sibling directory that is renamed into place only after all files
    /// succeeded, so an interrupted extraction is repeated in full on the next start. The bundled charts and skin then
    /// move into the player-managed roots through <see cref="MobileManagedAssetMover"/>.
    /// A bounded worker pool reads, verifies and writes independent files; progress and managed asset moves use the
    /// calling Unity context.
    /// </remarks>
    internal static class MobileResourceImporter
    {
        /// <summary>Limits simultaneous archive readers and whole-file buffers on mobile devices.</summary>
        private const int MaximumExtractionWorkers = 4;

        /// <summary>The interval in milliseconds between progress updates while background extraction is running.</summary>
        private const int ProgressPollMilliseconds = 16;

        /// <summary>The suffix of the staging directory beside the extracted resources.</summary>
        private const string StagingSuffix = ".extracting-v2";

        /// <summary>The loading message shown while packaged resources are extracted.</summary>
        private const string ExtractingMessage = "Extracting resources...";

        /// <summary>The loading message shown while the bundled charts and skin are installed.</summary>
        private const string InstallingMessage = "Installing charts and skins...";

        /// <summary>Extracts and verifies every packaged resource, then installs the bundled charts and skin.</summary>
        /// <param name="progress">Receives the progress of each step.</param>
        /// <param name="budget">Splits the subsequent managed asset installation and update work across frames.</param>
        /// <param name="cancellationToken">Cancels the extraction between files; the staged files are discarded on the next start.</param>
        /// <returns>An operation that completes with whether the extraction was committed; failures are logged.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        /// <exception cref="UnauthorizedAccessException">The extracted resources or a local destination cannot be queried.</exception>
        /// <exception cref="IOException">The extracted resources or a local destination cannot be queried.</exception>
        public static async ValueTask<bool> ImportAsync(IProgress<ProgressReport> progress, FrameBudget budget, CancellationToken cancellationToken)
        {
            if (!await ExtractAsync(progress, cancellationToken))
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
        /// <param name="cancellationToken">Cancels the extraction between files.</param>
        /// <returns>An operation that completes with whether all resources were verified and their staged directory was committed.</returns>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        private static async ValueTask<bool> ExtractAsync(IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ResourceManifestLoader.TryGetV2Hashes(out var v2Hashes))
            {
                return false;
            }

            var stagingDirectory = TryPrepareStagingDirectory();
            if (stagingDirectory is null)
            {
                return false;
            }

            // Capture Unity's path before launching workers. Each read owns its native file or APK archive stream.
            var packagedRoot = Application.streamingAssetsPath;
            var files = v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            progress.Report(new ProgressReport(ExtractingMessage, 0, v2Hashes.Count));
            if (!await ExtractFilesAsync(files, packagedRoot, stagingDirectory, progress, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
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

        /// <summary>Copies independent files with bounded workers while reporting progress on the calling context.</summary>
        /// <param name="files">The validated paths and expected hashes, snapshotted before background work starts.</param>
        /// <param name="packagedRoot">The packaged native or APK root captured on the Unity main thread.</param>
        /// <param name="stagingDirectory">The app-private native directory receiving the verified resources.</param>
        /// <param name="progress">Receives ordered completion counts on the calling context, never from workers.</param>
        /// <param name="cancellationToken">Stops workers between files and prevents commitment after cancellation.</param>
        /// <returns>Whether every file was successfully copied and verified.</returns>
        /// <remarks>All workers finish before this operation returns or throws, so retrying cannot race staged writes.</remarks>
        /// <exception cref="OperationCanceledException">The token was cancelled after any in-flight files finished.</exception>
        private static async ValueTask<bool> ExtractFilesAsync(
            KeyValuePair<string, string>[] files,
            string packagedRoot,
            StorageDirectory stagingDirectory,
            IProgress<ProgressReport> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workerCount = Math.Min(files.Length, Math.Clamp(Environment.ProcessorCount, 1, MaximumExtractionWorkers));
            var workers = new Task[workerCount];
            var nextIndex = -1;
            var completedCount = 0;
            var failed = 0;
            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Run(() =>
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var index = Interlocked.Increment(ref nextIndex);
                        if (index >= files.Length)
                        {
                            return;
                        }
                        var (relativePath, hash) = files[index];
                        if (!ResourceFiles.TryCopyVerified(relativePath, stagingDirectory, hash, "Extract", packagedRoot))
                        {
                            Interlocked.Exchange(ref failed, 1);
                        }
                        Interlocked.Increment(ref completedCount);
                    }
                });
            }

            var allWorkers = Task.WhenAll(workers);
            var reportedCount = 0;
            try
            {
                while (!allWorkers.IsCompleted)
                {
                    await Task.WhenAny(allWorkers, Task.Delay(ProgressPollMilliseconds));
                    var finishedCount = Volatile.Read(ref completedCount);
                    while (reportedCount < finishedCount)
                    {
                        reportedCount++;
                        progress.Report(new ProgressReport(ExtractingMessage, reportedCount, files.Length));
                    }
                }
            }
            finally
            {
                // Do not leave a writer using stagingDirectory when cancellation or a progress callback throws.
                await allWorkers;
            }

            while (reportedCount < completedCount)
            {
                reportedCount++;
                progress.Report(new ProgressReport(ExtractingMessage, reportedCount, files.Length));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return failed == 0;
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
