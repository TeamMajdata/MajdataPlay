#nullable enable
using System;
using System.IO;
using System.Threading;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Resolves local paths and supported storage URIs into typed storage handles.</summary>
    public static class FileSystem
    {
        /// <summary>The optional provider for opaque content URIs.</summary>
        private static IFileSystem? s_contentProvider;

        /// <summary>Gets the portable local filesystem backend.</summary>
        public static LocalFileSystem Local { get; } = new LocalFileSystem();

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
            return new StorageDirectory(Local, normalizedPath);
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
                return Local;
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
