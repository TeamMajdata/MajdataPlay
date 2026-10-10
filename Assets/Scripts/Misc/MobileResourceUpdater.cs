#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using UnityEngine;
using UnityEngine.Networking;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Extracts mobile packaged resources while preserving player-managed files and customizations.</summary>
    internal static class MobileResourceUpdater
    {
        /// <summary>Identifies extraction commits whose chart and skin directory moves must be resumed.</summary>
        private const string PendingManagedAssetMovesMarkerName = ".pending-managed-asset-moves";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOrUpdate()
        {
            if (!FileSystem.Directory.Open(MajEnv.AssetsPath).Exists)
            {
                if (ExtractAssets())
                {
                    var preservedExistingManagedAssets = MoveCharts();
                    preservedExistingManagedAssets |= MoveSkins();
                    ClearPendingManagedAssetMovesMarkerIfCompleted();
                    if (preservedExistingManagedAssets &&
                        ResourceManifestLoader.TryGetUpdateManifests(
                            out var existingManagedV1Hashes,
                            out var existingManagedV2Hashes,
                            out var existingManagedDiffPaths))
                    {
                        // AssetsPath can be missing while player-managed charts/skins still exist.
                        // Run the normal v1 hash gate for those existing files.
                        ApplyV2Diff(
                            existingManagedV1Hashes,
                            existingManagedV2Hashes,
                            existingManagedDiffPaths);
                    }
                }
                else
                {
                    MajDebug.LogError(
                        "Initial resource extraction was incomplete; managed assets were not moved.");
                }
                return;
            }

            CompletePendingManagedAssetMoves();
            if (!ResourceManifestLoader.TryGetUpdateManifests(
                    out var v1Hashes,
                    out var v2Hashes,
                    out var diffPaths))
            {
                return;
            }

            ApplyV2Diff(v1Hashes, v2Hashes, diffPaths);
            SyncMissingAssets(v2Hashes, diffPaths);
        }

        /// <summary>Replaces eligible official v1 files with verified packaged v2 bytes.</summary>
        /// <param name="v1Hashes">The previous official hashes used to protect customizations.</param>
        /// <param name="v2Hashes">The current hashes used to verify packaged replacements.</param>
        /// <param name="diffPaths">The official paths changed by this resource update.</param>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        private static void ApplyV2Diff(
            IReadOnlyDictionary<string, string> v1Hashes,
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths)
        {
            var updatedCount = 0;
            var preservedCount = 0;
            foreach (var relativePath in diffPaths)
            {
                var v2Hash = v2Hashes[relativePath];
                var destinationPath = ResolveDestinationPath(relativePath);
                var isRootManagedAsset = IsRootManagedAsset(relativePath);
                if (v1Hashes.TryGetValue(relativePath, out var v1Hash))
                {
                    if (!FileSystem.File.Open(destinationPath).Exists)
                    {
                        if (isRootManagedAsset)
                        {
                            MajDebug.LogInfo(
                                $"Resource update preserved player deletion: {relativePath}");
                            preservedCount++;
                            continue;
                        }
                    }
                    else if (!TryComputeFileSha256(destinationPath, out var localHash) ||
                             !string.Equals(localHash, v1Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        MajDebug.LogInfo(
                            $"Resource update preserved player customization: {relativePath}");
                        preservedCount++;
                        continue;
                    }
                }
                else if (FileSystem.File.Open(destinationPath).Exists)
                {
                    // This path did not exist in v1. An existing local file is player-owned.
                    MajDebug.LogInfo(
                        $"Resource update preserved player file at new v2 path: {relativePath}");
                    preservedCount++;
                    continue;
                }
                else if (isRootManagedAsset)
                {
                    // Do not repopulate player-managed chart/skin trees during an upgrade.
                    MajDebug.LogInfo(
                        $"Resource update skipped new managed asset to preserve player layout: {relativePath}");
                    preservedCount++;
                    continue;
                }

                if (!TryReadPackagedResource(relativePath, out var v2Data))
                {
                    continue;
                }

                var packagedHash = ComputeSha256(v2Data);
                if (!string.Equals(packagedHash, v2Hash, StringComparison.OrdinalIgnoreCase))
                {
                    MajDebug.LogError(
                        $"Resource update skipped (packaged v2 hash mismatch): {relativePath}");
                    continue;
                }

                try
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        FileSystem.Directory.Create(destinationDirectory);
                    }

                    WriteAllBytesAtomically(destinationPath, v2Data);
                    updatedCount++;
                    MajDebug.LogInfo($"Resource updated from official v1 to v2: {relativePath}");
                }
                catch (Exception exception)
                {
                    MajDebug.LogError(
                        $"Resource update failed while writing: {relativePath}\n{exception}");
                }
            }

            MajDebug.LogInfo(
                $"Mobile resource update finished: {updatedCount} updated, " +
                $"{preservedCount} customized or missing file(s) preserved.");
        }

        /// <summary>Stages and verifies every packaged resource before committing the extracted resource root.</summary>
        /// <returns>Whether all resources were verified and their staged directory was committed.</returns>
        private static bool ExtractAssets()
        {
            if (!ResourceManifestLoader.TryGetV2Hashes(out var v2Hashes))
            {
                return false;
            }

            var extractionRoot = MajEnv.AssetsPath.TrimEnd(
                                     Path.DirectorySeparatorChar,
                                     Path.AltDirectorySeparatorChar) + ".extracting-v2";
            try
            {
                if (FileSystem.Directory.Open(extractionRoot).Exists)
                {
                    FileSystem.Directory.Open(extractionRoot).Delete(recursive: true);
                }
                else if (FileSystem.File.Open(extractionRoot).Exists)
                {
                    FileSystem.File.Open(extractionRoot).Delete();
                }

                FileSystem.Directory.Create(extractionRoot);
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to prepare resource extraction directory:\n{exception}");
                return false;
            }

            var succeeded = true;
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var destinationPath = Path.Combine(
                    extractionRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!CopyPackagedResource(relativePath, destinationPath, hash, "Extract"))
                {
                    succeeded = false;
                }
            }

            if (!succeeded)
            {
                return false;
            }

            try
            {
                FileSystem.File.Create(
                    Path.Combine(extractionRoot, PendingManagedAssetMovesMarkerName))
                    .WriteAllText(string.Empty);
                FileSystem.Directory.Move(extractionRoot, MajEnv.AssetsPath);
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to finish initial resource extraction:\n{exception}");
                return false;
            }
        }

        /// <summary>Copies missing unmanaged resources that are outside the explicit update diff.</summary>
        /// <param name="v2Hashes">The validated current packaged resource hashes.</param>
        /// <param name="diffPaths">The changed paths governed by the official v1 update gate.</param>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        private static void SyncMissingAssets(
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths)
        {
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (IsRootManagedAsset(relativePath) || diffPaths.Contains(relativePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(
                    MajEnv.AssetsPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!FileSystem.File.Open(destinationPath).Exists)
                {
                    CopyPackagedResource(relativePath, destinationPath, hash, "Sync missing");
                }
            }
        }

        /// <summary>Moves bundled charts from extraction into the player-managed chart root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveCharts()
        {
            return MoveExtractedDirectory(
                Path.Combine(MajEnv.AssetsPath, "MaiCharts", "Original"),
                Path.Combine(MajEnv.ChartPath, "Original"));
        }

        /// <summary>Moves the bundled skin from extraction into the player-managed skin root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveSkins()
        {
            return MoveExtractedDirectory(
                Path.Combine(MajEnv.AssetsPath, "Skins", "default"),
                Path.Combine(MajEnv.SkinPath, "default"));
        }

        /// <summary>Moves one extracted managed directory without replacing an existing player-owned destination.</summary>
        /// <param name="sourcePath">The local extracted directory to move.</param>
        /// <param name="destinationPath">The intended local player-managed destination.</param>
        /// <returns>Whether an existing destination was preserved and the extracted duplicate removed.</returns>
        /// <exception cref="UnauthorizedAccessException">The extracted source cannot be queried.</exception>
        /// <exception cref="IOException">The extracted source cannot be queried.</exception>
        private static bool MoveExtractedDirectory(string sourcePath, string destinationPath)
        {
            if (!FileSystem.Directory.Open(sourcePath).Exists)
            {
                MajDebug.LogError($"Move failed: source not found: {sourcePath}");
                return false;
            }

            try
            {
                if (FileSystem.Directory.Open(destinationPath).Exists || FileSystem.File.Open(destinationPath).Exists)
                {
                    // Never replace a pre-existing player-managed directory during extraction.
                    FileSystem.Directory.Open(sourcePath).Delete(recursive: true);
                    MajDebug.LogInfo(
                        $"Preserved existing player-managed path during extraction: {destinationPath}");
                    return true;
                }

                FileSystem.Directory.Move(sourcePath, destinationPath);
                MajDebug.LogInfo($"Moved: {sourcePath} -> {destinationPath}");
                return false;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Move failed: {sourcePath} -> {destinationPath}\n{exception}");
                return false;
            }
        }

        /// <summary>Resumes pending chart and skin moves recorded by an earlier extraction commit.</summary>
        /// <exception cref="UnauthorizedAccessException">The marker or extracted sources cannot be queried.</exception>
        /// <exception cref="IOException">The marker or extracted sources cannot be queried.</exception>
        private static void CompletePendingManagedAssetMoves()
        {
            var markerPath = Path.Combine(
                MajEnv.AssetsPath,
                PendingManagedAssetMovesMarkerName);
            if (!FileSystem.File.Open(markerPath).Exists)
            {
                return;
            }

            var chartSourcePath = Path.Combine(MajEnv.AssetsPath, "MaiCharts", "Original");
            if (FileSystem.Directory.Open(chartSourcePath).Exists)
            {
                MoveCharts();
            }

            var skinSourcePath = Path.Combine(MajEnv.AssetsPath, "Skins", "default");
            if (FileSystem.Directory.Open(skinSourcePath).Exists)
            {
                MoveSkins();
            }

            ClearPendingManagedAssetMovesMarkerIfCompleted();
        }

        /// <summary>Removes the pending marker after both extracted managed source directories are gone.</summary>
        /// <exception cref="UnauthorizedAccessException">An extracted source cannot be queried.</exception>
        /// <exception cref="IOException">An extracted source cannot be queried.</exception>
        private static void ClearPendingManagedAssetMovesMarkerIfCompleted()
        {
            var chartSourcePath = Path.Combine(MajEnv.AssetsPath, "MaiCharts", "Original");
            var skinSourcePath = Path.Combine(MajEnv.AssetsPath, "Skins", "default");
            if (FileSystem.Directory.Open(chartSourcePath).Exists || FileSystem.Directory.Open(skinSourcePath).Exists)
            {
                return;
            }

            var markerPath = Path.Combine(
                MajEnv.AssetsPath,
                PendingManagedAssetMovesMarkerName);
            try
            {
                if (FileSystem.File.Open(markerPath).Exists)
                {
                    FileSystem.File.Open(markerPath).Delete();
                }
            }
            catch (Exception exception)
            {
                MajDebug.LogError(
                    $"Failed to clean pending managed asset move marker: {markerPath}\n{exception}");
            }
        }

        /// <summary>Verifies packaged bytes and commits one extracted or missing local resource atomically.</summary>
        /// <param name="relativePath">The portable path inside packaged resources.</param>
        /// <param name="destinationPath">The local resource file receiving the verified bytes.</param>
        /// <param name="expectedHash">The official SHA-256 hash expected for the packaged bytes.</param>
        /// <param name="operation">The operation label used in resource logs.</param>
        /// <returns>Whether the packaged bytes were verified and committed.</returns>
        private static bool CopyPackagedResource(
            string relativePath,
            string destinationPath,
            string expectedHash,
            string operation)
        {
            if (!TryReadPackagedResource(relativePath, out var data))
            {
                return false;
            }

            if (!string.Equals(ComputeSha256(data), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                MajDebug.LogError($"{operation} failed (packaged hash mismatch): {relativePath}");
                return false;
            }

            try
            {
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    FileSystem.Directory.Create(destinationDirectory);
                }

                WriteAllBytesAtomically(destinationPath, data);
                MajDebug.LogInfo($"{operation}: {relativePath} -> {destinationPath}");
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"{operation} failed: {relativePath}\n{exception}");
                return false;
            }
        }

        /// <summary>Stages resource bytes beside the destination before a local move or atomic replacement.</summary>
        /// <param name="destinationPath">The local resource destination.</param>
        /// <param name="data">The complete verified bytes to commit.</param>
        /// <exception cref="UnauthorizedAccessException">Creating, writing, or committing the local file was denied.</exception>
        /// <exception cref="IOException">Staging or committing the local file failed.</exception>
        /// <exception cref="NotSupportedException">The platform does not support atomic replacement.</exception>
        private static void WriteAllBytesAtomically(string destinationPath, byte[] data)
        {
            var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                FileSystem.File.Create(temporaryPath).WriteAllBytes(data);
                if (FileSystem.File.Open(destinationPath).Exists)
                {
                    FileSystem.File.Replace(temporaryPath, destinationPath);
                }
                else
                {
                    FileSystem.File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                if (FileSystem.File.Open(temporaryPath).Exists)
                {
                    try
                    {
                        FileSystem.File.Open(temporaryPath).Delete();
                    }
                    catch (Exception exception)
                    {
                        MajDebug.LogError(
                            $"Failed to clean temporary resource file: {temporaryPath}\n{exception}");
                    }
                }
            }
        }

        /// <summary>Identifies chart and skin paths owned by the player-managed root.</summary>
        /// <param name="path">The portable manifest-relative path.</param>
        /// <returns>Whether the path belongs to a chart or skin tree.</returns>
        private static bool IsRootManagedAsset(string path)
        {
            return path.StartsWith("MaiCharts/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("Skins/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Maps a portable manifest path to its appropriate local resource or player-managed root.</summary>
        /// <param name="relativePath">The validated portable manifest path.</param>
        /// <returns>The intended local resource destination.</returns>
        private static string ResolveDestinationPath(string relativePath)
        {
            var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return IsRootManagedAsset(relativePath)
                ? Path.Combine(MajEnv.RootPath, platformPath)
                : Path.Combine(MajEnv.AssetsPath, platformPath);
        }

        /// <summary>Reads packaged bytes through local iOS storage or the Android streaming asset request.</summary>
        /// <param name="relativePath">The portable packaged resource path.</param>
        /// <param name="data">Receives the packaged bytes, or an empty array after failure.</param>
        /// <returns>Whether the packaged bytes were read successfully.</returns>
        private static bool TryReadPackagedResource(string relativePath, out byte[] data)
        {
#if UNITY_IOS
            var sourcePath = Path.Combine(
                Application.streamingAssetsPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                data = FileSystem.File.Open(sourcePath).ReadAllBytes();
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(
                    $"Resource update failed while reading packaged iOS resource: " +
                    $"{relativePath}\n{exception}");
                data = Array.Empty<byte>();
                return false;
            }
#elif UNITY_ANDROID
            var sourceUrl = Path.Combine(Application.streamingAssetsPath, relativePath).Replace("\\", "/");
            try
            {
                using var request = UnityWebRequest.Get(sourceUrl);
                request.downloadHandler = new DownloadHandlerBuffer();
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    System.Threading.Thread.Sleep(1);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    MajDebug.LogError(
                        $"Resource update failed while reading packaged Android resource: " +
                        $"{relativePath}\n{request.error}");
                    data = Array.Empty<byte>();
                    return false;
                }

                data = request.downloadHandler.data ?? Array.Empty<byte>();
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(
                    $"Resource update failed while reading packaged Android resource: " +
                    $"{relativePath}\n{exception}");
                data = Array.Empty<byte>();
                return false;
            }
#endif
        }

        /// <summary>Hashes a local resource through its storage stream without buffering the whole file.</summary>
        /// <param name="path">The local resource file to hash.</param>
        /// <param name="hash">Receives the lowercase SHA-256 hash, or an empty string after failure.</param>
        /// <returns>Whether the resource was successfully opened and hashed.</returns>
        private static bool TryComputeFileSha256(string path, out string hash)
        {
            try
            {
                using var stream = FileSystem.File.Open(path).OpenRead();
                using var sha256 = SHA256.Create();
                hash = ToHexString(sha256.ComputeHash(stream));
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to hash local resource: {path}\n{exception}");
                hash = string.Empty;
                return false;
            }
        }

        /// <summary>Hashes already buffered packaged resource bytes.</summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The lowercase SHA-256 hash.</returns>
        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return ToHexString(sha256.ComputeHash(data));
        }

        /// <summary>Formats hash bytes for comparison with validated resource manifest entries.</summary>
        /// <param name="hash">The hash bytes to format.</param>
        /// <returns>The lowercase hexadecimal hash without separators.</returns>
        private static string ToHexString(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
#endif
