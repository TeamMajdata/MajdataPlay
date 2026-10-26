using System;
using System.IO;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Utils;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Moves the bundled charts and skin out of the extracted mobile resources into the player-managed roots.</summary>
    /// <remarks>
    /// An extraction commit leaves a marker file in the extracted resources. The marker stays until both bundled
    /// directories have left the extracted resources, so an interrupted move is resumed on the next start.
    /// </remarks>
    internal static class MobileManagedAssetMover
    {
        /// <summary>Identifies extraction commits whose chart and skin directory moves must be resumed.</summary>
        public const string PendingMovesMarkerName = ".pending-managed-asset-moves";

        /// <summary>The bundled chart collection relative to the extracted streaming assets.</summary>
        public const string BundledChartsRelativePath = "MaiCharts/Original";

        /// <summary>The bundled default skin relative to the extracted streaming assets.</summary>
        public const string BundledSkinRelativePath = "Skins/default";

        /// <summary>Records in a staged extraction that its bundled directories still have to be moved.</summary>
        /// <param name="stagingDirectory">The staged extraction directory, before it is committed.</param>
        /// <exception cref="UnauthorizedAccessException">Creating the marker was denied.</exception>
        /// <exception cref="IOException">Creating the marker failed.</exception>
        public static void MarkPendingMoves(StorageDirectory stagingDirectory)
        {
            stagingDirectory.CreateFile(PendingMovesMarkerName);
        }

        /// <summary>Moves both bundled directories and removes the pending marker once they are gone.</summary>
        /// <returns>Whether an existing player-managed chart or skin directory was preserved instead of replaced.</returns>
        /// <exception cref="UnauthorizedAccessException">The extracted resources cannot be queried.</exception>
        /// <exception cref="IOException">The extracted resources cannot be queried.</exception>
        public static bool MoveBundledAssets()
        {
            var preserved = MoveCharts();
            preserved |= MoveSkin();
            ClearPendingMovesMarkerIfCompleted();
            return preserved;
        }

        /// <summary>Resumes pending chart and skin moves recorded by an earlier extraction commit.</summary>
        /// <exception cref="UnauthorizedAccessException">The marker or extracted sources cannot be queried.</exception>
        /// <exception cref="IOException">The marker or extracted sources cannot be queried.</exception>
        public static void ResumePendingMoves()
        {
            var assetsDirectory = MajEnv.AssetsDirectory;
            if (assetsDirectory.FindFile(PendingMovesMarkerName) is null)
            {
                return;
            }

            if (assetsDirectory.FindDirectoryAt(BundledChartsRelativePath) is not null)
            {
                MoveCharts();
            }

            if (assetsDirectory.FindDirectoryAt(BundledSkinRelativePath) is not null)
            {
                MoveSkin();
            }

            ClearPendingMovesMarkerIfCompleted();
        }

        /// <summary>Moves bundled charts from extraction into the player-managed chart root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveCharts()
        {
            return MoveExtractedDirectory(BundledChartsRelativePath, MajEnv.ChartDirectory, "Original");
        }

        /// <summary>Moves the bundled skin from extraction into the player-managed skin root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveSkin()
        {
            return MoveExtractedDirectory(BundledSkinRelativePath, MajEnv.SkinDirectory, "default");
        }

        /// <summary>Moves one extracted managed directory without replacing an existing player-owned destination.</summary>
        /// <param name="sourceRelativePath">The extracted directory relative to the extracted streaming assets.</param>
        /// <param name="destinationParent">The existing player-managed root receiving the directory.</param>
        /// <param name="destinationName">The directory name inside the player-managed root.</param>
        /// <returns>Whether an existing destination was preserved and the extracted duplicate removed.</returns>
        /// <remarks>
        /// The move is one native operation while the extracted assets and the player-managed root share a backend. A root
        /// on another backend, such as an Android SAF tree, receives a copy of the tree before the extracted source is
        /// deleted, so a failed transfer keeps the source and the pending marker for the next start.
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">The extracted source cannot be queried.</exception>
        /// <exception cref="IOException">The extracted source cannot be queried.</exception>
        private static bool MoveExtractedDirectory(string sourceRelativePath, StorageDirectory destinationParent, string destinationName)
        {
            var source = MajEnv.AssetsDirectory.FindDirectoryAt(sourceRelativePath);
            if (source is null)
            {
                MajDebug.LogError(nameof(MobileManagedAssetMover), $"Move failed: source not found: {sourceRelativePath}");
                return false;
            }

            try
            {
                if (destinationParent.FindEntry(destinationName) is not null)
                {
                    // Never replace a pre-existing player-managed directory during extraction.
                    source.Delete(recursive: true);
                    MajDebug.LogInfo(nameof(MobileManagedAssetMover),
                        $"Preserved existing player-managed path during extraction: {destinationName} in {destinationParent.Location}");
                    return true;
                }

                var moved = StorageDirectoryTransfer.Move(source, destinationParent, destinationName);
                MajDebug.LogInfo(nameof(MobileManagedAssetMover), $"Moved: {source.Location} -> {moved.Location}");
                return false;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(MobileManagedAssetMover),
                    $"Move failed: {source.Location} -> {destinationName} in {destinationParent.Location}\n{exception}");
                return false;
            }
        }

        /// <summary>Removes the pending marker after both extracted managed source directories are gone.</summary>
        /// <exception cref="UnauthorizedAccessException">An extracted source cannot be queried.</exception>
        /// <exception cref="IOException">An extracted source cannot be queried.</exception>
        private static void ClearPendingMovesMarkerIfCompleted()
        {
            var assetsDirectory = MajEnv.AssetsDirectory;
            if (assetsDirectory.FindDirectoryAt(BundledChartsRelativePath) is not null ||
                assetsDirectory.FindDirectoryAt(BundledSkinRelativePath) is not null)
            {
                return;
            }

            try
            {
                assetsDirectory.FindFile(PendingMovesMarkerName)?.Delete();
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(MobileManagedAssetMover),
                    $"Failed to clean pending managed asset move marker: {PendingMovesMarkerName}\n{exception}");
            }
        }
    }
}
