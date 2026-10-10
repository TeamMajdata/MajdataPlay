#nullable enable
using System;
using System.IO;

namespace MajdataPlay.IO.Storage
{
    public static partial class FileSystem
    {
        /// <summary>Provides file operations over native paths, file URIs, and registered content URIs.</summary>
        /// <remarks>Locations are opaque and authoritative; never combine them with <see cref="Path"/>.</remarks>
        public static class File
        {
            /// <summary>Creates a file handle without requiring the file to exist.</summary>
            /// <param name="location">A local path, file URI, or registered content URI.</param>
            /// <returns>A handle retaining the resolved provider and its location.</returns>
            /// <exception cref="ArgumentNullException">The location is null.</exception>
            /// <exception cref="ArgumentException">The location or URI is invalid.</exception>
            /// <exception cref="NotSupportedException">The URI scheme is unsupported or no content provider is registered.</exception>
            /// <exception cref="IOException">The local path cannot be normalized.</exception>
            public static StorageFile Open(string location)
            {
                return new StorageFile(ResolveProvider(location), location);
            }

            /// <summary>Creates an empty local file or truncates an existing provider file when overwrite is requested.</summary>
            /// <param name="location">A local path, file URI, or existing registered content URI.</param>
            /// <param name="overwrite">Whether an existing file may be truncated.</param>
            /// <returns>A handle for the newly created or truncated file.</returns>
            /// <exception cref="ArgumentNullException">The location is null.</exception>
            /// <exception cref="ArgumentException">The location is invalid.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or creation requires a parent directory and name.</exception>
            /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">File creation was denied.</exception>
            /// <exception cref="IOException">The file exists without overwrite or creation failed.</exception>
            public static StorageFile Create(string location, bool overwrite = false)
            {
                var file = Open(location);
                FileSystemEntry? entry = null;
                if (file.FileSystem is not LocalFileSystem)
                {
                    entry = file.Entry ??
                        throw new NotSupportedException("Creating a provider file requires its parent directory and a child name.");
                }
                using var stream = OpenWriteCore(file, append: false, overwrite: overwrite, entry: entry);
                return entry is null ? file : new StorageFile(file.FileSystem, entry.Location);
            }

            /// <summary>Creates an immediate child file or truncates an existing child when overwrite is requested.</summary>
            /// <param name="directoryLocation">The existing parent directory path or URI.</param>
            /// <param name="name">One child name, never a relative path or URI.</param>
            /// <param name="mimeType">The requested content type; local storage ignores it.</param>
            /// <param name="overwrite">Whether an existing file may be truncated.</param>
            /// <returns>A handle with the authoritative file name and location assigned by the backend.</returns>
            /// <exception cref="ArgumentException">An argument is invalid.</exception>
            /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Creation or writing was denied.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or does not support the operation.</exception>
            /// <exception cref="IOException">The name is occupied without overwrite or the operation failed.</exception>
            public static StorageFile Create(string directoryLocation, string name,
                string mimeType = "application/octet-stream", bool overwrite = false)
            {
                StorageName.Validate(name);
                if (string.IsNullOrWhiteSpace(mimeType))
                {
                    throw new ArgumentException("A content type is required.", nameof(mimeType));
                }
                var directory = Directory.Open(directoryLocation);
                var entry = directory.FileSystem.GetChildEntry(directory.Location, name);
                if (entry is not null && (entry.IsDirectory || !overwrite))
                {
                    throw new IOException("The destination name is already occupied.");
                }
                if (directory.FileSystem is LocalFileSystem)
                {
                    return Create(Path.Combine(directory.Location, name), overwrite);
                }
                if (entry is null)
                {
                    return directory.CreateFile(name, mimeType);
                }
                var file = new StorageFile(directory.FileSystem, entry.Location);
                using var stream = file.OpenWrite();
                return file;
            }

            /// <summary>Opens an existing file for sequential reading using the selected backend.</summary>
            /// <param name="location">A local path, file URI, or registered content URI.</param>
            /// <returns>A caller-owned readable stream which may not support seeking or length queries.</returns>
            /// <exception cref="ArgumentException">The location is invalid.</exception>
            /// <exception cref="FileNotFoundException">The file does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or cannot open a readable stream.</exception>
            /// <exception cref="IOException">Opening failed.</exception>
            public static Stream OpenRead(string location)
            {
                return Open(location).OpenRead();
            }

            /// <summary>Opens an output stream using the selected backend, creating local files as needed.</summary>
            /// <param name="location">A local path, file URI, or existing registered content URI.</param>
            /// <param name="append">Whether to append; a missing local file is created.</param>
            /// <param name="overwrite">Whether a non-append open may truncate an existing file.</param>
            /// <returns>A caller-owned writable stream; provider streams may not support seeking or length queries.</returns>
            /// <remarks>Local streams allow concurrent readers. New provider files require the parent-and-name Create overload first.</remarks>
            /// <exception cref="ArgumentNullException">The location is null.</exception>
            /// <exception cref="ArgumentException">The location is invalid.</exception>
            /// <exception cref="NotSupportedException">The provider is unavailable or does not support the requested mode.</exception>
            /// <exception cref="FileNotFoundException">The provider file does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
            /// <exception cref="IOException">The file exists without append/overwrite or opening failed.</exception>
            public static Stream OpenWrite(string location, bool append = false, bool overwrite = false)
            {
                return OpenWriteCore(Open(location), append, overwrite);
            }

            /// <summary>Copies a file using native local copying or a bounded stream transfer between backends.</summary>
            /// <param name="source">The source path or URI.</param>
            /// <param name="destination">The destination path, file URI, or existing content file URI.</param>
            /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
            /// <returns>A handle for the destination file.</returns>
            /// <remarks>Local copies retain native metadata. Provider copies are not atomic and failed overwrites may leave partial content.</remarks>
            /// <exception cref="ArgumentNullException">A location is null.</exception>
            /// <exception cref="ArgumentException">A location is invalid.</exception>
            /// <exception cref="NotSupportedException">A provider is unavailable or does not support a required operation.</exception>
            /// <exception cref="FileNotFoundException">The source or an explicit content destination does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Copying was denied.</exception>
            /// <exception cref="IOException">The destination exists without overwrite or copying failed.</exception>
            public static StorageFile Copy(string source, string destination, bool overwrite = false)
            {
                var sourceFile = Open(source);
                var destinationFile = Open(destination);
                if (sourceFile.FileSystem is LocalFileSystem && destinationFile.FileSystem is LocalFileSystem)
                {
                    System.IO.File.Copy(sourceFile.Location, destinationFile.Location, overwrite);
                    return destinationFile;
                }
                if (destinationFile.FileSystem is LocalFileSystem)
                {
                    var parent = Path.GetDirectoryName(destinationFile.Location);
                    if (string.IsNullOrEmpty(parent))
                    {
                        throw new IOException("A filesystem root cannot be a destination file.");
                    }
                    return sourceFile.CopyTo(new StorageDirectory(destinationFile.FileSystem, parent),
                        Path.GetFileName(destinationFile.Location), overwrite);
                }
                return sourceFile.CopyTo(destinationFile, overwrite);
            }

            /// <summary>Copies a file into an immediate child of a directory using the selected backends.</summary>
            /// <param name="source">The source file path or URI.</param>
            /// <param name="directoryLocation">The existing destination directory path or URI.</param>
            /// <param name="name">One destination child name, never a relative path or URI.</param>
            /// <param name="overwrite">Whether an existing file may be replaced.</param>
            /// <returns>A handle using the destination backend's authoritative file location.</returns>
            /// <remarks>Local copies retain native metadata. Provider transfers are not atomic; newly created files are removed best-effort on failure.</remarks>
            /// <exception cref="ArgumentException">A location or name is invalid.</exception>
            /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Copying was denied.</exception>
            /// <exception cref="NotSupportedException">A provider is unavailable or does not support a required operation.</exception>
            /// <exception cref="IOException">The destination conflicts, is the source, or copying failed.</exception>
            public static StorageFile Copy(string source, string directoryLocation, string name, bool overwrite = false)
            {
                var sourceFile = Open(source);
                var directory = Directory.Open(directoryLocation);
                StorageName.Validate(name);
                if (sourceFile.FileSystem is LocalFileSystem && directory.FileSystem is LocalFileSystem)
                {
                    // Query the child first to apply local name validation and require an existing parent.
                    directory.FileSystem.GetChildEntry(directory.Location, name);
                    return Copy(sourceFile.Location, Path.Combine(directory.Location, name), overwrite);
                }
                return sourceFile.CopyTo(directory, name, overwrite);
            }

            /// <summary>Moves a file without overwriting the destination when the selected backend performs native moves.</summary>
            /// <param name="source">The source file path or URI.</param>
            /// <param name="destination">The destination file path or URI.</param>
            /// <returns>A handle for the file at its new location.</returns>
            /// <remarks>A provider destination cannot name a new document URI; use the directory-and-name overload for provider moves.</remarks>
            /// <exception cref="ArgumentNullException">A location is null.</exception>
            /// <exception cref="ArgumentException">A location is invalid.</exception>
            /// <exception cref="NotSupportedException">A backend cannot provide native moves, including content and cross-provider moves.</exception>
            /// <exception cref="FileNotFoundException">The source does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
            /// <exception cref="IOException">The destination exists or moving failed.</exception>
            public static StorageFile Move(string source, string destination)
            {
                var sourceFile = Open(source);
                var destinationFile = Open(destination);
                RequireNativeMove(sourceFile.FileSystem);
                RequireNativeMove(destinationFile.FileSystem);
                System.IO.File.Move(sourceFile.Location, destinationFile.Location);
                return destinationFile;
            }

            /// <summary>Moves a file into an immediate child of a directory using one native backend operation.</summary>
            /// <param name="source">The existing source file path or URI.</param>
            /// <param name="directoryLocation">The existing destination directory path or URI.</param>
            /// <param name="name">One destination child name, never a relative path or URI.</param>
            /// <returns>A handle using the destination backend's authoritative file location.</returns>
            /// <remarks>
            /// The source and destination must belong to the same backend; a move is never emulated by copying and deleting.
            /// Provider moves are one provider operation, so a partially moved entry is never cleaned up by this facade.
            /// </remarks>
            /// <exception cref="ArgumentNullException">A location is null.</exception>
            /// <exception cref="ArgumentException">A location or name is invalid.</exception>
            /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
            /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Querying or moving was denied.</exception>
            /// <exception cref="NotSupportedException">The operands belong to different backends or the backend cannot move natively.</exception>
            /// <exception cref="IOException">The source is a directory, a sibling exists, or moving failed.</exception>
            public static StorageFile Move(string source, string directoryLocation, string name)
            {
                var entry = MoveEntry(source, directoryLocation, name, sourceIsDirectory: false);
                return new StorageFile(ResolveProvider(directoryLocation), entry.Location);
            }

            /// <summary>Replaces a file's content with a source file and consumes that source.</summary>
            /// <param name="source">The replacement path or URI, consumed on success.</param>
            /// <param name="destination">The existing destination path or URI.</param>
            /// <param name="backupPath">An optional backup path for the original destination; local operands only.</param>
            /// <returns>A handle for the replaced destination.</returns>
            /// <remarks>
            /// Local operands use native atomic replacement. A provider operand instead streams the source into the
            /// existing destination and then deletes the source, preserving the destination's storage identity.
            /// That provider replacement is not atomic: a failure can leave the destination truncated or partially written,
            /// the source is never deleted on failure, and a backup path is refused before any mutation.
            /// </remarks>
            /// <exception cref="ArgumentNullException">A required location is null.</exception>
            /// <exception cref="ArgumentException">A location is invalid.</exception>
            /// <exception cref="NotSupportedException">A provider operand was combined with a backup path, or the platform cannot replace files.</exception>
            /// <exception cref="PlatformNotSupportedException">The platform does not support file replacement.</exception>
            /// <exception cref="FileNotFoundException">The source or destination does not exist.</exception>
            /// <exception cref="UnauthorizedAccessException">Replacement was denied.</exception>
            /// <exception cref="IOException">Replacement failed, the operands are the same file, or volumes are incompatible.</exception>
            public static StorageFile Replace(string source, string destination, string? backupPath = null)
            {
                var sourceFile = Open(source);
                var destinationFile = Open(destination);
                var backupFile = backupPath is null ? null : Open(backupPath);
                if (sourceFile.FileSystem is LocalFileSystem && destinationFile.FileSystem is LocalFileSystem &&
                    (backupFile is null || backupFile.FileSystem is LocalFileSystem))
                {
                    System.IO.File.Replace(sourceFile.Location, destinationFile.Location, backupFile?.Location);
                    return destinationFile;
                }
                if (backupFile is not null)
                {
                    throw new NotSupportedException("Replacing into a provider location does not support a backup path.");
                }
                var replaced = sourceFile.CopyTo(destinationFile, overwrite: true);
                sourceFile.Delete();
                return replaced;
            }
        }
    }
}
