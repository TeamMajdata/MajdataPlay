#nullable enable
using System;
using System.IO;

namespace MajdataPlay.IO.Storage
{
    public static partial class FileSystem
    {
        /// <summary>Provides directory operations over native paths, file URIs, and registered content URIs.</summary>
        /// <remarks>Locations are opaque and authoritative; never combine them with <see cref="Path"/>.</remarks>
        public static class Directory
        {
            /// <summary>Creates a directory handle without requiring the directory to exist.</summary>
            /// <param name="location">A local path, file URI, or registered content URI.</param>
            /// <returns>A handle retaining the resolved provider and its location.</returns>
            /// <exception cref="ArgumentNullException">The location is null.</exception>
            /// <exception cref="ArgumentException">The location or URI is invalid.</exception>
            /// <exception cref="NotSupportedException">The URI scheme is unsupported or no content provider is registered.</exception>
            /// <exception cref="IOException">The local path cannot be normalized.</exception>
            public static StorageDirectory Open(string location)
            {
                return new StorageDirectory(ResolveProvider(location), location);
            }

            /// <summary>Creates a local directory and missing parents, or opens an existing provider directory.</summary>
            /// <param name="location">A local path, file URI, or registered content URI.</param>
            /// <returns>A handle for the existing or created directory.</returns>
            /// <exception cref="ArgumentNullException">The location is null.</exception>
            /// <exception cref="ArgumentException">The location or URI is invalid.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or creation requires a parent directory and name.</exception>
            /// <exception cref="UnauthorizedAccessException">Directory creation was denied.</exception>
            /// <exception cref="IOException">A file occupies the path or directory creation failed.</exception>
            public static StorageDirectory Create(string location)
            {
                var directory = Open(location);
                if (directory.FileSystem is LocalFileSystem)
                {
                    System.IO.Directory.CreateDirectory(directory.Location);
                    return directory;
                }
                var entry = directory.Entry;
                if (entry is null)
                {
                    throw new NotSupportedException("Creating a provider directory requires its parent directory and a child name.");
                }
                if (!entry.IsDirectory)
                {
                    throw new IOException("A file occupies the requested directory location.");
                }
                return new StorageDirectory(directory.FileSystem, entry.Location);
            }

            /// <summary>Creates or opens an immediate child directory using the selected backend.</summary>
            /// <param name="directoryLocation">The existing parent directory path or URI.</param>
            /// <param name="name">One child name, never a relative path or URI.</param>
            /// <returns>A handle with the authoritative directory location assigned by the backend.</returns>
            /// <exception cref="ArgumentException">The location or name is invalid.</exception>
            /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Creation was denied.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or does not support creation.</exception>
            /// <exception cref="IOException">A file occupies the name or creation failed.</exception>
            public static StorageDirectory Create(string directoryLocation, string name)
            {
                return Open(directoryLocation).CreateDirectory(name);
            }

            /// <summary>Moves a directory without copying or overwriting when the selected backend performs native moves.</summary>
            /// <param name="source">The source directory path or URI.</param>
            /// <param name="destination">The destination directory path or URI; local moves require the same volume.</param>
            /// <returns>A handle for the directory at its new location.</returns>
            /// <remarks>A provider destination cannot name a new document URI; use the directory-and-name overload for provider moves.</remarks>
            /// <exception cref="ArgumentNullException">A location is null.</exception>
            /// <exception cref="ArgumentException">A location is invalid.</exception>
            /// <exception cref="NotSupportedException">A backend cannot provide native moves, including content and cross-provider moves.</exception>
            /// <exception cref="DirectoryNotFoundException">The source or destination parent does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
            /// <exception cref="IOException">The destination exists, volumes differ, or moving failed.</exception>
            public static StorageDirectory Move(string source, string destination)
            {
                var sourceDirectory = Open(source);
                var destinationDirectory = Open(destination);
                RequireNativeMove(sourceDirectory.FileSystem);
                RequireNativeMove(destinationDirectory.FileSystem);
                System.IO.Directory.Move(sourceDirectory.Location, destinationDirectory.Location);
                return destinationDirectory;
            }

            /// <summary>Moves a directory into an immediate child of another directory using one native backend operation.</summary>
            /// <param name="source">The existing source directory path or URI.</param>
            /// <param name="directoryLocation">The existing destination parent directory path or URI.</param>
            /// <param name="name">One destination child name, never a relative path or URI.</param>
            /// <returns>A handle using the destination backend's authoritative directory location.</returns>
            /// <remarks>
            /// The source and destination must belong to the same backend; a move is never emulated by copying and deleting.
            /// Moving a directory inside its own subtree is rejected by the backend rather than by this facade.
            /// </remarks>
            /// <exception cref="ArgumentNullException">A location is null.</exception>
            /// <exception cref="ArgumentException">A location or name is invalid.</exception>
            /// <exception cref="FileNotFoundException">The source directory does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Querying or moving was denied.</exception>
            /// <exception cref="NotSupportedException">The operands belong to different backends or the backend cannot move natively.</exception>
            /// <exception cref="IOException">The source is a file, a sibling exists, or moving failed.</exception>
            public static StorageDirectory Move(string source, string directoryLocation, string name)
            {
                var entry = MoveEntry(source, directoryLocation, name, sourceIsDirectory: true);
                return new StorageDirectory(ResolveProvider(directoryLocation), entry.Location);
            }
        }
    }
}
