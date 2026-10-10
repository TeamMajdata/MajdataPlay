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

        /// <summary>Creates a local directory and any missing parent directories, or opens the existing directory.</summary>
        /// <param name="path">A local path or file URI, never a content URI.</param>
        /// <returns>A handle with a normalized, absolute local directory location.</returns>
        /// <exception cref="ArgumentNullException">The path is null.</exception>
        /// <exception cref="ArgumentException">The path or URI is invalid.</exception>
        /// <exception cref="NotSupportedException">The path uses a non-file URI scheme.</exception>
        /// <exception cref="UnauthorizedAccessException">Directory creation was denied.</exception>
        /// <exception cref="IOException">A file occupies the path or directory creation failed.</exception>
        public static StorageDirectory CreateLocalDirectory(string path)
        {
            var normalizedPath = NormalizeLocalPath(path);
            Directory.CreateDirectory(normalizedPath);
            return new StorageDirectory(s_local, normalizedPath);
        }

        /// <summary>Creates an empty local file without creating parent directories.</summary>
        /// <param name="path">A local path or file URI, never a content URI.</param>
        /// <param name="overwrite">Whether an existing file may be truncated.</param>
        /// <returns>A handle for the newly created or truncated local file.</returns>
        /// <exception cref="ArgumentNullException">The path is null.</exception>
        /// <exception cref="ArgumentException">The path is invalid.</exception>
        /// <exception cref="NotSupportedException">The path uses a non-file URI scheme.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">File creation was denied.</exception>
        /// <exception cref="IOException">The file exists without overwrite or creation failed.</exception>
        public static StorageFile CreateLocalFile(string path, bool overwrite = false)
        {
            var normalizedPath = NormalizeLocalPath(path);
            using var stream = OpenLocalWrite(normalizedPath, overwrite: overwrite);
            return new StorageFile(s_local, normalizedPath);
        }

        /// <summary>Opens a seekable local output stream, allowing concurrent readers.</summary>
        /// <param name="path">A local path or file URI, never a content URI.</param>
        /// <param name="append">Whether to append to an existing file or create it if missing.</param>
        /// <param name="overwrite">Whether a non-append open may truncate an existing file.</param>
        /// <returns>A writable local stream owned by the caller.</returns>
        /// <exception cref="ArgumentNullException">The path is null.</exception>
        /// <exception cref="ArgumentException">The path is invalid.</exception>
        /// <exception cref="NotSupportedException">The path uses a non-file URI scheme.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="IOException">The file exists without append/overwrite or opening failed.</exception>
        public static Stream OpenLocalWrite(string path, bool append = false, bool overwrite = false)
        {
            var normalizedPath = NormalizeLocalPath(path);
            var mode = append ? FileMode.Append : overwrite ? FileMode.Create : FileMode.CreateNew;
            return new FileStream(normalizedPath, mode, FileAccess.Write, FileShare.Read, 65536);
        }

        /// <summary>Copies a local file using the platform copy operation, retaining local file metadata.</summary>
        /// <param name="source">The local source path or file URI.</param>
        /// <param name="destination">The local destination path or file URI.</param>
        /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
        /// <returns>A handle for the destination file.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A location uses a non-file URI scheme.</exception>
        /// <exception cref="FileNotFoundException">The source does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Copying was denied.</exception>
        /// <exception cref="IOException">The destination exists without overwrite or copying failed.</exception>
        public static StorageFile CopyLocalFile(string source, string destination, bool overwrite = false)
        {
            var sourcePath = NormalizeLocalPath(source);
            var destinationPath = NormalizeLocalPath(destination);
            File.Copy(sourcePath, destinationPath, overwrite);
            return new StorageFile(s_local, destinationPath);
        }

        /// <summary>Moves a local file without overwriting the destination.</summary>
        /// <param name="source">The local source path or file URI.</param>
        /// <param name="destination">The local destination path or file URI.</param>
        /// <returns>A handle for the file at its new location.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A location uses a non-file URI scheme.</exception>
        /// <exception cref="FileNotFoundException">The source does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">A parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
        /// <exception cref="IOException">The destination exists or moving failed.</exception>
        public static StorageFile MoveLocalFile(string source, string destination)
        {
            var sourcePath = NormalizeLocalPath(source);
            var destinationPath = NormalizeLocalPath(destination);
            File.Move(sourcePath, destinationPath);
            return new StorageFile(s_local, destinationPath);
        }

        /// <summary>Moves a local directory without copying or overwriting the destination.</summary>
        /// <param name="source">The local source path or file URI.</param>
        /// <param name="destination">The local destination path or file URI on the same volume.</param>
        /// <returns>A handle for the directory at its new location.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A location uses a non-file URI scheme.</exception>
        /// <exception cref="DirectoryNotFoundException">The source or destination parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Moving was denied.</exception>
        /// <exception cref="IOException">The destination exists, volumes differ, or moving failed.</exception>
        public static StorageDirectory MoveLocalDirectory(string source, string destination)
        {
            var sourcePath = NormalizeLocalPath(source);
            var destinationPath = NormalizeLocalPath(destination);
            Directory.Move(sourcePath, destinationPath);
            return new StorageDirectory(s_local, destinationPath);
        }

        /// <summary>Atomically replaces an existing local file using the platform's file replacement operation.</summary>
        /// <param name="source">The local replacement path or file URI, consumed on success.</param>
        /// <param name="destination">The existing local destination path or file URI.</param>
        /// <param name="backupPath">An optional local backup path or file URI for the original destination.</param>
        /// <returns>A handle for the replaced destination.</returns>
        /// <exception cref="ArgumentNullException">A required location is null.</exception>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A location uses a non-file URI scheme.</exception>
        /// <exception cref="PlatformNotSupportedException">The platform does not support file replacement.</exception>
        /// <exception cref="FileNotFoundException">The source or destination does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Replacement was denied.</exception>
        /// <exception cref="IOException">Replacement failed, including incompatible volumes.</exception>
        public static StorageFile ReplaceLocalFile(string source, string destination, string? backupPath = null)
        {
            var sourcePath = NormalizeLocalPath(source);
            var destinationPath = NormalizeLocalPath(destination);
            var normalizedBackup = backupPath is null ? null : NormalizeLocalPath(backupPath);
            File.Replace(sourcePath, destinationPath, normalizedBackup);
            return new StorageFile(s_local, destinationPath);
        }

        /// <summary>Sets local filesystem attributes without applying native path semantics to provider URIs.</summary>
        /// <param name="location">The local entry path or file URI.</param>
        /// <param name="attributes">The complete local attribute flags to apply.</param>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or attributes are invalid.</exception>
        /// <exception cref="NotSupportedException">The location uses a non-file URI scheme.</exception>
        /// <exception cref="FileNotFoundException">The entry does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Changing attributes was denied.</exception>
        /// <exception cref="IOException">Changing attributes failed.</exception>
        public static void SetLocalAttributes(string location, FileAttributes attributes)
        {
            File.SetAttributes(NormalizeLocalPath(location), attributes);
        }

        /// <summary>Sets the local modification time, including timestamps restored from ZIP entries.</summary>
        /// <param name="location">The local entry path or file URI.</param>
        /// <param name="lastWriteTime">The modification time, interpreted according to its DateTime kind.</param>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The location or timestamp is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timestamp is outside the supported range.</exception>
        /// <exception cref="NotSupportedException">The location uses a non-file URI scheme.</exception>
        /// <exception cref="FileNotFoundException">The entry does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Changing the timestamp was denied.</exception>
        /// <exception cref="IOException">Changing the timestamp failed.</exception>
        public static void SetLocalLastWriteTime(string location, DateTime lastWriteTime)
        {
            File.SetLastWriteTime(NormalizeLocalPath(location), lastWriteTime);
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
