#nullable enable
using System;
using System.IO;
using System.Threading;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Automatically selects a storage backend for local paths and supported URIs without exposing the local implementation.</summary>
    public static class FileSystem
    {
        /// <summary>The optional provider for opaque content URIs.</summary>
        private static IFileSystem? s_contentProvider;

        /// <summary>Owns the shared local backend selected internally for native paths and file URIs.</summary>
        private static readonly LocalFileSystem s_local = new LocalFileSystem();

        /// <summary>Registers or replaces the provider used for subsequent content URI resolutions.</summary>
        /// <param name="provider">The backend responsible for content URI locations.</param>
        /// <exception cref="ArgumentNullException">The provider is null.</exception>
        public static void RegisterContentProvider(IFileSystem provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            Interlocked.Exchange(ref s_contentProvider, provider);
        }

        /// <summary>Creates a file handle without requiring the file to exist.</summary>
        /// <param name="location">A local path, file URI, or registered content URI.</param>
        /// <returns>A handle retaining the resolved provider and its location.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or URI is invalid.</exception>
        /// <exception cref="NotSupportedException">The URI scheme is unsupported or no content provider is registered.</exception>
        /// <exception cref="IOException">The local path cannot be normalized.</exception>
        public static StorageFile OpenFile(string location)
        {
            return new StorageFile(ResolveProvider(location), location);
        }

        /// <summary>Creates a directory handle without requiring the directory to exist.</summary>
        /// <param name="location">A local path, file URI, or registered content URI.</param>
        /// <returns>A handle retaining the resolved provider and its location.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or URI is invalid.</exception>
        /// <exception cref="NotSupportedException">The URI scheme is unsupported or no content provider is registered.</exception>
        /// <exception cref="IOException">The local path cannot be normalized.</exception>
        public static StorageDirectory OpenDirectory(string location)
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
        public static StorageDirectory CreateDirectory(string location)
        {
            var directory = OpenDirectory(location);
            if (directory.FileSystem is LocalFileSystem)
            {
                Directory.CreateDirectory(directory.Location);
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
        public static StorageDirectory CreateDirectory(string directoryLocation, string name)
        {
            return OpenDirectory(directoryLocation).CreateDirectory(name);
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
        public static StorageFile CreateFile(string location, bool overwrite = false)
        {
            var file = OpenFile(location);
            FileSystemEntry? entry = null;
            if (file.FileSystem is not LocalFileSystem)
            {
                entry = file.Entry ??
                    throw new NotSupportedException("Creating a provider file requires its parent directory and a child name.");
            }
            using var stream = OpenWrite(file, append: false, overwrite: overwrite, entry: entry);
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
        public static StorageFile CreateFile(string directoryLocation, string name,
            string mimeType = "application/octet-stream", bool overwrite = false)
        {
            StorageName.Validate(name);
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("A content type is required.", nameof(mimeType));
            }
            var directory = OpenDirectory(directoryLocation);
            var entry = directory.FileSystem.GetChildEntry(directory.Location, name);
            if (entry is not null && (entry.IsDirectory || !overwrite))
            {
                throw new IOException("The destination name is already occupied.");
            }
            if (directory.FileSystem is LocalFileSystem)
            {
                return CreateFile(Path.Combine(directory.Location, name), overwrite);
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
            return OpenFile(location).OpenRead();
        }

        /// <summary>Opens an output stream using the selected backend, creating local files as needed.</summary>
        /// <param name="location">A local path, file URI, or existing registered content URI.</param>
        /// <param name="append">Whether to append; a missing local file is created.</param>
        /// <param name="overwrite">Whether a non-append open may truncate an existing file.</param>
        /// <returns>A caller-owned writable stream; provider streams may not support seeking or length queries.</returns>
        /// <remarks>Local streams allow concurrent readers. New provider files require the parent/name CreateFile overload first.</remarks>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="NotSupportedException">The provider is unavailable or does not support the requested mode.</exception>
        /// <exception cref="FileNotFoundException">The provider file does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="IOException">The file exists without append/overwrite or opening failed.</exception>
        public static Stream OpenWrite(string location, bool append = false, bool overwrite = false)
        {
            return OpenWrite(OpenFile(location), append, overwrite);
        }

        /// <summary>Copies a file using native local copying or a bounded stream transfer between backends.</summary>
        /// <param name="source">The source path or URI.</param>
        /// <param name="destination">The destination path, file URI, or existing content file URI.</param>
        /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
        /// <returns>A handle for the destination file.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <remarks>Local copies retain native metadata. Provider copies are not atomic and failed overwrites may leave partial content.</remarks>
        /// <exception cref="NotSupportedException">A provider is unavailable or does not support a required operation.</exception>
        /// <exception cref="FileNotFoundException">The source or an explicit content destination does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Copying was denied.</exception>
        /// <exception cref="IOException">The destination exists without overwrite or copying failed.</exception>
        public static StorageFile CopyFile(string source, string destination, bool overwrite = false)
        {
            var sourceFile = OpenFile(source);
            var destinationFile = OpenFile(destination);
            if (sourceFile.FileSystem is LocalFileSystem && destinationFile.FileSystem is LocalFileSystem)
            {
                File.Copy(sourceFile.Location, destinationFile.Location, overwrite);
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
        public static StorageFile CopyFile(string source, string directoryLocation, string name, bool overwrite = false)
        {
            var sourceFile = OpenFile(source);
            var directory = OpenDirectory(directoryLocation);
            StorageName.Validate(name);
            if (sourceFile.FileSystem is LocalFileSystem && directory.FileSystem is LocalFileSystem)
            {
                // Query the child first to apply local name validation and require an existing parent.
                directory.FileSystem.GetChildEntry(directory.Location, name);
                return CopyFile(sourceFile.Location, Path.Combine(directory.Location, name), overwrite);
            }
            return sourceFile.CopyTo(directory, name, overwrite);
        }

        /// <summary>Moves a file without overwriting the destination when the selected backends support native moves.</summary>
        /// <param name="source">The source file path or URI.</param>
        /// <param name="destination">The destination file path or URI.</param>
        /// <returns>A handle for the file at its new location.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A backend cannot provide native moves, including content and cross-provider moves.</exception>
        /// <exception cref="FileNotFoundException">The source does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
        /// <exception cref="IOException">The destination exists or moving failed.</exception>
        public static StorageFile MoveFile(string source, string destination)
        {
            var sourceFile = OpenFile(source);
            var destinationFile = OpenFile(destination);
            RequireNativeOperation(sourceFile.FileSystem, "File moves");
            RequireNativeOperation(destinationFile.FileSystem, "File moves");
            File.Move(sourceFile.Location, destinationFile.Location);
            return destinationFile;
        }

        /// <summary>Moves a directory without copying or overwriting when the selected backends support native moves.</summary>
        /// <param name="source">The source directory path or URI.</param>
        /// <param name="destination">The destination directory path or URI; local moves require the same volume.</param>
        /// <returns>A handle for the directory at its new location.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A backend cannot provide native moves, including content and cross-provider moves.</exception>
        /// <exception cref="DirectoryNotFoundException">The source or destination parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
        /// <exception cref="IOException">The destination exists, volumes differ, or moving failed.</exception>
        public static StorageDirectory MoveDirectory(string source, string destination)
        {
            var sourceDirectory = OpenDirectory(source);
            var destinationDirectory = OpenDirectory(destination);
            RequireNativeOperation(sourceDirectory.FileSystem, "Directory moves");
            RequireNativeOperation(destinationDirectory.FileSystem, "Directory moves");
            Directory.Move(sourceDirectory.Location, destinationDirectory.Location);
            return destinationDirectory;
        }

        /// <summary>Atomically replaces a file when all selected backends support native replacement.</summary>
        /// <param name="source">The replacement path or URI, consumed on success.</param>
        /// <param name="destination">The existing destination path or URI.</param>
        /// <param name="backupPath">An optional backup path or URI for the original destination.</param>
        /// <returns>A handle for the replaced destination.</returns>
        /// <exception cref="ArgumentNullException">A required location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A backend cannot provide atomic replacement, including content providers.</exception>
        /// <exception cref="PlatformNotSupportedException">The platform does not support file replacement.</exception>
        /// <exception cref="FileNotFoundException">The source or destination does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Replacement was denied.</exception>
        /// <exception cref="IOException">Replacement failed, including incompatible volumes.</exception>
        public static StorageFile ReplaceFile(string source, string destination, string? backupPath = null)
        {
            var sourceFile = OpenFile(source);
            var destinationFile = OpenFile(destination);
            var backupFile = backupPath is null ? null : OpenFile(backupPath);
            RequireNativeOperation(sourceFile.FileSystem, "Atomic file replacement");
            RequireNativeOperation(destinationFile.FileSystem, "Atomic file replacement");
            if (backupFile is not null)
            {
                RequireNativeOperation(backupFile.FileSystem, "Atomic file replacement");
            }
            File.Replace(sourceFile.Location, destinationFile.Location, backupFile?.Location);
            return destinationFile;
        }

        /// <summary>Sets filesystem attributes when the selected backend supports native attribute flags.</summary>
        /// <param name="location">The entry path or URI.</param>
        /// <param name="attributes">The complete native attribute flags to apply.</param>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or attributes are invalid.</exception>
        /// <exception cref="NotSupportedException">The backend cannot set native attribute flags, including content providers.</exception>
        /// <exception cref="FileNotFoundException">The entry does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Changing attributes was denied.</exception>
        /// <exception cref="IOException">Changing attributes failed.</exception>
        public static void SetAttributes(string location, FileAttributes attributes)
        {
            var file = OpenFile(location);
            RequireNativeOperation(file.FileSystem, "Native filesystem attributes");
            File.SetAttributes(file.Location, attributes);
        }

        /// <summary>Sets the modification time when the selected backend supports native timestamps.</summary>
        /// <param name="location">The entry path or URI.</param>
        /// <param name="lastWriteTime">The modification time, interpreted according to its DateTime kind.</param>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or timestamp is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timestamp is outside the supported range.</exception>
        /// <exception cref="NotSupportedException">The backend cannot set timestamps, including content providers.</exception>
        /// <exception cref="FileNotFoundException">The entry does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Changing the timestamp was denied.</exception>
        /// <exception cref="IOException">Changing the timestamp failed.</exception>
        public static void SetLastWriteTime(string location, DateTime lastWriteTime)
        {
            var file = OpenFile(location);
            RequireNativeOperation(file.FileSystem, "Filesystem timestamps");
            File.SetLastWriteTime(file.Location, lastWriteTime);
        }

        /// <summary>Opens a resolved file without reselecting its backend during the operation.</summary>
        /// <param name="file">The file handle retaining the backend selected for this operation.</param>
        /// <param name="append">Whether to append instead of truncating.</param>
        /// <param name="overwrite">Whether an existing file may be truncated.</param>
        /// <param name="entry">Optional provider metadata already queried for this operation.</param>
        /// <returns>A caller-owned writable stream.</returns>
        /// <exception cref="FileNotFoundException">A provider file does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">A local parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Opening was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support the requested mode.</exception>
        /// <exception cref="IOException">The location is occupied without append/overwrite or opening failed.</exception>
        private static Stream OpenWrite(StorageFile file, bool append, bool overwrite, FileSystemEntry? entry = null)
        {
            if (file.FileSystem is LocalFileSystem)
            {
                var mode = append ? FileMode.Append : overwrite ? FileMode.Create : FileMode.CreateNew;
                return new FileStream(file.Location, mode, FileAccess.Write, FileShare.Read, 65536);
            }
            entry ??= file.Entry;
            if (entry is null)
            {
                throw new FileNotFoundException("The provider file does not exist; create it through its parent directory first.", file.Location);
            }
            if (entry.IsDirectory || (!append && !overwrite))
            {
                throw new IOException("The destination location is already occupied.");
            }
            return file.FileSystem.OpenWrite(entry.Location, append);
        }

        /// <summary>Rejects operations which the current provider contract cannot perform with native semantics.</summary>
        /// <param name="provider">The backend selected for an operand.</param>
        /// <param name="operation">The operation description included in the failure.</param>
        /// <exception cref="NotSupportedException">The backend cannot perform the native operation.</exception>
        private static void RequireNativeOperation(IFileSystem provider, string operation)
        {
            if (provider is not LocalFileSystem)
            {
                throw new NotSupportedException("The selected storage backend does not support " + operation + ".");
            }
        }

        /// <summary>Converts a local path or file URI into a normalized absolute path without trailing separators.</summary>
        /// <param name="location">The local path or file URI to normalize.</param>
        /// <returns>The absolute path, retaining separators only when required by its filesystem root.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or file URI is invalid.</exception>
        /// <exception cref="NotSupportedException">The location uses a non-file URI scheme.</exception>
        /// <exception cref="IOException">The path cannot be normalized.</exception>
        internal static string NormalizeLocalPath(string location)
        {
            var uri = GetLocationUri(location);
            var path = location;
            if (uri is not null)
            {
                if (!uri.IsFile)
                {
                    throw new NotSupportedException("Only local paths and file URIs belong to the local filesystem.");
                }
                if (uri.Query.Length != 0 || uri.Fragment.Length != 0 || !Path.IsPathRooted(uri.LocalPath))
                {
                    throw new ArgumentException("An absolute file URI without a query or fragment is required.", nameof(location));
                }
                path = uri.LocalPath;
            }
            var fullPath = Path.GetFullPath(path);
            var rootLength = (Path.GetPathRoot(fullPath) ?? string.Empty).Length;
            var length = fullPath.Length;
            while (length > rootLength &&
                (fullPath[length - 1] == Path.DirectorySeparatorChar || fullPath[length - 1] == Path.AltDirectorySeparatorChar))
            {
                length--;
            }
            return length == fullPath.Length ? fullPath : fullPath.Substring(0, length);
        }

        /// <summary>Selects the backend for a validated local path or supported URI.</summary>
        /// <param name="location">The caller's storage location.</param>
        /// <returns>The local backend or registered content provider.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or content URI is invalid.</exception>
        /// <exception cref="NotSupportedException">The URI scheme is unsupported or a content provider is unavailable.</exception>
        private static IFileSystem ResolveProvider(string location)
        {
            var uri = GetLocationUri(location);
            if (uri is null || uri.IsFile)
            {
                return s_local;
            }
            if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("The storage URI scheme is not supported.");
            }
            if (!location.StartsWith("content://", StringComparison.OrdinalIgnoreCase) || uri.Host.Length == 0)
            {
                throw new ArgumentException("A content URI with a provider authority is required.", nameof(location));
            }
            return Volatile.Read(ref s_contentProvider) ??
                throw new NotSupportedException("No content URI provider has been registered.");
        }

        /// <summary>Recognizes explicit URI schemes while leaving native local paths untouched.</summary>
        /// <param name="location">The storage location to classify.</param>
        /// <returns>The parsed explicit URI, or null for a local path.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location is empty or an explicit URI is malformed.</exception>
        private static Uri? GetLocationUri(string location)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("A storage location is required.", nameof(location));
            }
            if (Path.IsPathRooted(location) ||
                (Path.DirectorySeparatorChar == '\\' && location.Length >= 2 &&
                    ((location[0] >= 'A' && location[0] <= 'Z') || (location[0] >= 'a' && location[0] <= 'z')) && location[1] == ':'))
            {
                return null;
            }
            var colonIndex = location.IndexOf(':');
            if (colonIndex <= 0 || !Uri.CheckSchemeName(location.Substring(0, colonIndex)))
            {
                return null;
            }
            var scheme = location.Substring(0, colonIndex);
            var hasAuthorityMarker = location.Length > colonIndex + 2 &&
                location[colonIndex + 1] == '/' && location[colonIndex + 2] == '/';
            if (!hasAuthorityMarker)
            {
                if (string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "content", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("File and content URIs must use the :// form.", nameof(location));
                }
                return null;
            }
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The storage URI is malformed.", nameof(location));
            }
            return uri;
        }
    }
}
