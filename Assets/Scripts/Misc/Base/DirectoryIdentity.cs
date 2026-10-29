using MajdataPlay.IO.Storage;
using System;
using System.IO;

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
            var id = Guid.NewGuid().ToString();
            idFile ??= markerDirectory.CreateFile(IdFileName, "text/plain");
            idFile.WriteAllText(id);
            return id;
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
