#nullable enable
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay.Utils
{
    /// <summary>Copies and moves whole directory trees through storage handles, including across storage backends.</summary>
    /// <remarks>
    /// The storage facade moves an entry only inside one backend and never emulates a move by copying, so this helper
    /// holds the application's policy for trees which must leave native storage for a provider location, such as the
    /// bundled chart collection moving into an Android SAF chart root. Enumeration never follows symbolic links, and a
    /// provider needs one round trip per entry, so callers keep these operations off the game loop.
    /// </remarks>
    internal static class StorageDirectoryTransfer
    {
        /// <summary>Copies every file and subdirectory of a directory into an existing directory, keeping file modification times where the destination stores them.</summary>
        /// <param name="source">The existing source directory, which may belong to any storage backend.</param>
        /// <param name="destination">The existing destination directory and its owning backend; files with the same name are overwritten.</param>
        /// <remarks>A provider which refuses modification times keeps its own timestamps; local attributes and directory timestamps are not copied.</remarks>
        /// <exception cref="ArgumentNullException">A directory is null.</exception>
        /// <exception cref="DirectoryNotFoundException">A source directory disappeared during the copy or the destination does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading or writing was denied.</exception>
        /// <exception cref="NotSupportedException">The destination backend cannot create files or directories.</exception>
        /// <exception cref="IOException">Creating or copying an entry failed, or a file occupies a directory name.</exception>
        public static void Copy(StorageDirectory source, StorageDirectory destination)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            var directories = new Stack<(StorageDirectory Source, StorageDirectory Destination)>();
            directories.Push((source, destination));
            while (directories.Count > 0)
            {
                var (currentSource, currentDestination) = directories.Pop();
                foreach (var entry in currentSource.EnumerateEntries())
                {
                    if (entry.IsDirectory)
                    {
                        directories.Push((
                            new StorageDirectory(currentSource.FileSystem, entry.Location),
                            currentDestination.CreateDirectory(entry.Name)));
                        continue;
                    }
                    var sourceFile = new StorageFile(currentSource.FileSystem, entry.Location);
                    var destinationFile = sourceFile.CopyTo(currentDestination, entry.Name, overwrite: true);
                    if (entry.LastWriteTimeUtc is DateTime lastWriteTimeUtc)
                    {
                        // A stream copy does not carry the source timestamp on its own.
                        TrySetLastWriteTime(destinationFile, lastWriteTimeUtc);
                    }
                }
            }
        }

        /// <summary>Moves a directory into an immediate child of another directory, natively inside one backend or by copying and deleting across backends.</summary>
        /// <param name="source">The existing source directory.</param>
        /// <param name="destinationParent">The existing directory receiving the moved directory.</param>
        /// <param name="name">One destination child name, never a relative path or URI.</param>
        /// <returns>A handle using the destination backend's authoritative directory location.</returns>
        /// <remarks>
        /// Inside one backend the move is the facade's single native operation. Across backends the tree is first copied
        /// below a temporary sibling name in the destination, renamed to the requested name, and only then deleted at the
        /// source, so an interrupted transfer keeps the source intact and never leaves a partial tree under the final name.
        /// The temporary copy is removed best-effort after a failure. When deleting the source fails, the destination is
        /// already complete and the exception reports the leftover source. A cross-backend move is not atomic and does
        /// not carry local attributes or directory timestamps.
        /// </remarks>
        /// <exception cref="ArgumentNullException">A directory is null.</exception>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="FileNotFoundException">The source directory does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying, copying, renaming or deleting was denied.</exception>
        /// <exception cref="NotSupportedException">A backend cannot create, rename or delete entries.</exception>
        /// <exception cref="IOException">The destination name is occupied, the source is a file, or the transfer failed.</exception>
        public static StorageDirectory Move(StorageDirectory source, StorageDirectory destinationParent, string name)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (destinationParent is null)
            {
                throw new ArgumentNullException(nameof(destinationParent));
            }
            if (ReferenceEquals(source.FileSystem, destinationParent.FileSystem))
            {
                return FileSystem.Directory.Move(source.Location, destinationParent.Location, name);
            }
            if (destinationParent.FindEntry(name) is not null)
            {
                throw new IOException("The destination name is already occupied.");
            }
            var sourceEntry = source.Entry
                ?? throw new FileNotFoundException("The source directory does not exist.", source.Location);
            if (!sourceEntry.IsDirectory)
            {
                throw new IOException("The source entry is a file, not a directory.");
            }
            // The tree is staged under a temporary sibling so the final name never holds a partial copy.
            var stagingDirectory = destinationParent.CreateDirectory($"{name}.{Guid.NewGuid():N}.moving");
            StorageDirectory movedDirectory;
            try
            {
                Copy(source, stagingDirectory);
                movedDirectory = stagingDirectory.Rename(name);
            }
            catch
            {
                TryDelete(stagingDirectory);
                throw;
            }
            source.Delete(recursive: true);
            return movedDirectory;
        }

        /// <summary>Applies a copied file's modification time, accepting a provider which cannot store it.</summary>
        /// <param name="file">The copied destination file.</param>
        /// <param name="lastWriteTimeUtc">The source file's UTC modification time.</param>
        private static void TrySetLastWriteTime(StorageFile file, DateTime lastWriteTimeUtc)
        {
            try
            {
                file.SetLastWriteTime(lastWriteTimeUtc);
            }
            catch (NotSupportedException exception)
            {
                // The copied bytes are complete; the provider keeps its own timestamp.
                MajDebug.LogWarning($"[StorageDirectoryTransfer] The destination keeps its own modification time: {file.Location}: {exception.Message}");
            }
        }

        /// <summary>Removes a temporary copy after a failed transfer without masking the failure being reported.</summary>
        /// <param name="directory">The temporary directory created for the transfer.</param>
        private static void TryDelete(StorageDirectory directory)
        {
            try
            {
                if (directory.Exists)
                {
                    directory.Delete(recursive: true);
                }
            }
            catch (Exception exception)
            {
                // The transfer failure propagates; the leftover copy has a unique temporary name and never blocks a retry.
                MajDebug.LogWarning($"[StorageDirectoryTransfer] Failed to clean up {directory.Location}: {exception.Message}");
            }
        }
    }
}
