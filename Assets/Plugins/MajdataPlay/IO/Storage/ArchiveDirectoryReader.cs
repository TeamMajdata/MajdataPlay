#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Reads many files below one jar directory location through a single open archive.</summary>
    /// <remarks>
    /// A jar location opened through <see cref="FileSystem"/> opens and indexes the whole archive for every operation,
    /// which dominates bulk reads from an APK that contains thousands of entries. This reader opens and indexes the
    /// archive once. It is not thread-safe because the archive shares one file stream; give each worker its own reader.
    /// </remarks>
    public sealed class ArchiveDirectoryReader : IDisposable
    {
        /// <summary>The archive that owns the local file handle.</summary>
        private readonly ZipArchive _archive;

        /// <summary>The files of the archive, keyed by their decoded archive-relative paths.</summary>
        private readonly Dictionary<string, ZipArchiveEntry> _files;

        /// <summary>The decoded archive-relative directory path, or empty for the archive root.</summary>
        private readonly string _directoryPath;

        /// <summary>Tracks disposal so reads after disposal fail predictably.</summary>
        private bool _disposed;

        /// <summary>Gets the canonical jar location of the directory read by this reader.</summary>
        public string Location { get; }

        /// <summary>Takes ownership of an open archive whose directory has already been validated.</summary>
        /// <param name="archive">The open archive, disposed together with this reader.</param>
        /// <param name="files">The archive's files keyed by their decoded archive-relative paths.</param>
        /// <param name="directoryPath">The decoded archive-relative directory path, or empty for the archive root.</param>
        /// <param name="location">The canonical jar location of the directory.</param>
        internal ArchiveDirectoryReader(
            ZipArchive archive,
            Dictionary<string, ZipArchiveEntry> files,
            string directoryPath,
            string location)
        {
            _archive = archive;
            _files = files;
            _directoryPath = directoryPath;
            Location = location;
        }

        /// <summary>Reads the complete bytes of one file below the directory.</summary>
        /// <param name="relativePath">The portable path below the directory whose segments contain literal, unescaped names.</param>
        /// <returns>The file's complete contents in a new byte array.</returns>
        /// <exception cref="ObjectDisposedException">The reader was disposed.</exception>
        /// <exception cref="ArgumentNullException">The relative path is null.</exception>
        /// <exception cref="ArgumentException">The relative path is rooted, traverses upwards, or names no file.</exception>
        /// <exception cref="FileNotFoundException">No file exists at the relative path.</exception>
        /// <exception cref="InvalidDataException">The compressed entry is invalid or shorter than its recorded length.</exception>
        /// <exception cref="IOException">Reading the archive failed.</exception>
        public byte[] ReadAllBytes(string relativePath)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArchiveDirectoryReader));
            }
            var names = FileSystem.Path.Split(relativePath);
            if (names.Length == 0)
            {
                throw new ArgumentException("A relative path ending with a file name is required.", nameof(relativePath));
            }
            var entryPath = string.Join("/", names);
            if (_directoryPath.Length != 0)
            {
                entryPath = _directoryPath + "/" + entryPath;
            }
            if (!_files.TryGetValue(entryPath, out var entry))
            {
                throw new FileNotFoundException("The archive file entry does not exist.", relativePath);
            }

            using var stream = entry.Open();
            var data = new byte[checked((int)entry.Length)];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read == 0)
                {
                    throw new InvalidDataException("The archive file entry is shorter than its recorded length.");
                }
                offset += read;
            }
            return data;
        }

        /// <summary>Closes the archive and its local file handle.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _archive.Dispose();
        }
    }
}
