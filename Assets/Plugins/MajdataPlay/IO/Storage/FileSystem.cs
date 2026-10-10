#nullable enable
using System;
using System.IO;
using System.Threading;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Automatically selects a storage backend for local paths and supported URIs without exposing the local implementation.</summary>
    public static partial class FileSystem
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

        /// <summary>Opens a stream for a resolved file handle without reselecting its backend during the operation.</summary>
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
        private static Stream OpenWriteCore(StorageFile file, bool append, bool overwrite, FileSystemEntry? entry = null)
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

        /// <summary>Moves one entry into an immediate child of a destination directory without any copy fallback.</summary>
        /// <param name="source">The existing source path or URI.</param>
        /// <param name="directoryLocation">The existing destination directory path or URI.</param>
        /// <param name="name">One destination child name, never a relative path or URI.</param>
        /// <param name="sourceIsDirectory">Whether the source must be a directory instead of a file.</param>
        /// <returns>The moved entry with its authoritative backend location.</returns>
        /// <exception cref="ArgumentNullException">A location is null.</exception>
        /// <exception cref="ArgumentException">A location or name is invalid.</exception>
        /// <exception cref="FileNotFoundException">The source entry does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying or moving was denied.</exception>
        /// <exception cref="NotSupportedException">The operands belong to different backends.</exception>
        /// <exception cref="IOException">The source has the wrong type, a sibling exists, or moving failed.</exception>
        private static FileSystemEntry MoveEntry(string source, string directoryLocation, string name, bool sourceIsDirectory)
        {
            StorageName.Validate(name);
            var sourceProvider = ResolveProvider(source);
            var entry = sourceProvider.GetEntry(source)
                ?? throw new FileNotFoundException("The source entry does not exist.", source);
            if (entry.IsDirectory != sourceIsDirectory)
            {
                throw new IOException(sourceIsDirectory
                    ? "The source entry is a file, not a directory."
                    : "The source entry is a directory, not a file.");
            }
            var directoryProvider = ResolveProvider(directoryLocation);
            if (!ReferenceEquals(sourceProvider, directoryProvider))
            {
                throw new NotSupportedException("Moving between different storage backends is not supported.");
            }
            return sourceProvider.Move(entry.Location, directoryLocation, name);
        }

        /// <summary>Rejects a location-based move whose operand cannot name a new provider child.</summary>
        /// <param name="provider">The backend selected for an operand.</param>
        /// <exception cref="NotSupportedException">The backend is not the local filesystem.</exception>
        private static void RequireNativeMove(IFileSystem provider)
        {
            if (provider is not LocalFileSystem)
            {
                throw new NotSupportedException(
                    "A provider location cannot name a new move destination; pass the destination directory and a child name instead.");
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
