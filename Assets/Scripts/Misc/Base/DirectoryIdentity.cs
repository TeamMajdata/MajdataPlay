using MajdataPlay.IO.Storage;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Reads and writes the identity stored in a directory's hidden <c>.MajdataPlay/id</c> marker file.</summary>
    internal static class DirectoryIdentity
    {
        /// <summary>The name of the hidden directory holding MajdataPlay's per-directory markers.</summary>
        public const string MarkerDirectoryName = ".MajdataPlay";
        /// <summary>The name of the marker file holding the directory's identity.</summary>
        public const string IdFileName = "id";

        /// <summary>Opens the marker directory, creating it with the hidden attribute when it does not exist yet.</summary>
        /// <param name="directory">The directory owning the marker directory.</param>
        /// <returns>The handle of the marker directory.</returns>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying or creating the marker directory was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support directory creation or attribute flags.</exception>
        /// <exception cref="IOException">A file occupies the marker name or creation failed.</exception>
        public static StorageDirectory OpenMarkerDirectory(StorageDirectory directory)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            var markerDirectory = directory.FindDirectory(MarkerDirectoryName);
            if (markerDirectory is null)
            {
                markerDirectory = directory.CreateDirectory(MarkerDirectoryName);
                markerDirectory.SetAttributes(FileAttributes.Directory | FileAttributes.Hidden);
            }
            return markerDirectory;
        }

        /// <summary>Reads the directory's identity, generating and persisting a new GUID when none is stored.</summary>
        /// <param name="directory">The directory whose identity is requested.</param>
        /// <returns>The stored identity with surrounding whitespace removed, or the newly generated GUID.</returns>
        /// <remarks>Any non-blank content is accepted, because online chart caches store server-assigned identities.</remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Accessing the marker files was denied.</exception>
        /// <exception cref="NotSupportedException">The backend cannot create or write the marker files.</exception>
        /// <exception cref="IOException">Reading or writing the marker files failed.</exception>
        public static string GetOrCreateId(StorageDirectory directory)
        {
            return GetOrWriteId(directory, Guid.NewGuid().ToString());
        }

        /// <summary>Reads the directory's identity, persisting the supplied identity when none is stored.</summary>
        /// <param name="directory">The directory whose identity is requested.</param>
        /// <param name="id">The identity to store when the marker is missing or blank.</param>
        /// <returns>The stored identity with surrounding whitespace removed, or <paramref name="id"/> after it was written.</returns>
        /// <remarks>An existing non-blank identity is never replaced, so a marker written elsewhere in the meantime wins.</remarks>
        /// <exception cref="ArgumentNullException">The directory or identity is null.</exception>
        /// <exception cref="ArgumentException">The identity is blank.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Accessing the marker files was denied.</exception>
        /// <exception cref="NotSupportedException">The backend cannot create or write the marker files.</exception>
        /// <exception cref="IOException">Reading or writing the marker files failed.</exception>
        public static string GetOrWriteId(StorageDirectory directory, string id)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A non-blank identity is required.", nameof(id));
            }
            var markerDirectory = OpenMarkerDirectory(directory);
            var idFile = markerDirectory.FindFile(IdFileName);
            if (idFile is not null)
            {
                var storedId = idFile.ReadAllText().Trim();
                if (storedId.Length != 0)
                {
                    return storedId;
                }
            }
            idFile ??= markerDirectory.CreateFile(IdFileName, "text/plain");
            idFile.WriteAllText(id);
            return id;
        }

        /// <summary>Reads the identity stored in an existing marker directory without creating or changing anything.</summary>
        /// <param name="markerDirectory">The directory's existing <c>.MajdataPlay</c> marker directory.</param>
        /// <returns>The stored identity with surrounding whitespace removed, or null when the marker file is missing or blank.</returns>
        /// <exception cref="ArgumentNullException">The marker directory is null.</exception>
        /// <exception cref="DirectoryNotFoundException">The marker directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading the marker file was denied.</exception>
        /// <exception cref="NotSupportedException">The backend cannot read the marker file.</exception>
        /// <exception cref="IOException">Reading the marker file failed.</exception>
        public static string? ReadStoredId(StorageDirectory markerDirectory)
        {
            if (markerDirectory is null)
            {
                throw new ArgumentNullException(nameof(markerDirectory));
            }
            var idFile = markerDirectory.FindFile(IdFileName);
            if (idFile is null)
            {
                return null;
            }
            var storedId = idFile.ReadAllText().Trim();
            return storedId.Length == 0 ? null : storedId;
        }

        /// <summary>Derives a repeatable identity from a directory's location, for directories whose marker is not written yet.</summary>
        /// <param name="directory">The directory whose location is hashed.</param>
        /// <returns>A GUID containing the MD5 digest of the location, identical for every scan of the same location.</returns>
        /// <remarks>
        /// Markers are created lazily, only when an identity is stored somewhere. Writing exactly this identity then keeps
        /// references taken before the marker existed valid, while the marker keeps the identity stable when the directory is
        /// moved or renamed.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public static Guid CreateLocationId(StorageDirectory directory)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            using var md5 = MD5.Create();
            var digest = md5.ComputeHash(Encoding.UTF8.GetBytes($"directory:{directory.Location}"));
            return new Guid(digest);
        }

        /// <summary>Persists the supplied identity, leaving the marker file untouched when it already holds that identity.</summary>
        /// <param name="directory">The directory whose identity is written.</param>
        /// <param name="id">The identity to store.</param>
        /// <exception cref="ArgumentNullException">The directory or identity is null.</exception>
        /// <exception cref="ArgumentException">The identity is blank.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Accessing the marker files was denied.</exception>
        /// <exception cref="NotSupportedException">The backend cannot create or write the marker files.</exception>
        /// <exception cref="IOException">Reading or writing the marker files failed.</exception>
        public static void WriteId(StorageDirectory directory, string id)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A non-blank identity is required.", nameof(id));
            }
            var markerDirectory = OpenMarkerDirectory(directory);
            var idFile = markerDirectory.FindFile(IdFileName);
            if (idFile is not null && idFile.ReadAllText().Trim() == id)
            {
                return;
            }
            idFile ??= markerDirectory.CreateFile(IdFileName, "text/plain");
            idFile.WriteAllText(id);
        }
    }
}
