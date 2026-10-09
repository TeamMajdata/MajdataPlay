#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Provides portable storage operations over normalized, absolute local paths.</summary>
    /// <remarks>
    /// Recursive deletion removes symbolic links and reparse points rather than their targets.
    /// Operations are not transactional and cannot protect against concurrent replacement of path components.
    /// </remarks>
    public sealed class LocalFileSystem : IFileSystem
    {
        /// <summary>The bounded buffer size used by local file streams.</summary>
        private const int BufferSize = 65536;

        /// <summary>The characters prohibited in a child name on the current platform.</summary>
        private static readonly char[] s_invalidFileNameChars = Path.GetInvalidFileNameChars();

        /// <inheritdoc />
        public FileSystemEntry? GetEntry(string location)
        {
            var path = FileSystem.NormalizeLocalPath(location);
            try
            {
                var attributes = File.GetAttributes(path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isSymbolicLink = (attributes & FileAttributes.ReparsePoint) != 0;
                FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
                long? length = null;
                DateTime? lastWriteTimeUtc = null;
                if (!isSymbolicLink)
                {
                    if (info is FileInfo fileInfo)
                    {
                        length = fileInfo.Length;
                    }
                    lastWriteTimeUtc = info.LastWriteTimeUtc;
                }
                return new FileSystemEntry(path, info.Name, isDirectory, length, lastWriteTimeUtc, isSymbolicLink);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            ValidateName(name);
            var directoryPath = RequireDirectory(directoryLocation);
            return GetEntry(Path.Combine(directoryPath, name));
        }

        /// <inheritdoc />
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            var directoryPath = RequireDirectory(directoryLocation);
            foreach (var path in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                var entry = GetEntry(path);
                if (entry is not null)
                {
                    yield return entry;
                }
            }
        }

        /// <inheritdoc />
        public Stream OpenRead(string location)
        {
            var path = FileSystem.NormalizeLocalPath(location);
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw new FileNotFoundException("The local file does not exist.", path, exception);
            }
        }

        /// <inheritdoc />
        public Stream OpenWrite(string location, bool append = false)
        {
            var path = FileSystem.NormalizeLocalPath(location);
            FileStream stream;
            try
            {
                // Open first, so missing files are never created and sharing failures cannot truncate a file.
                stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw new FileNotFoundException("The local file does not exist.", path, exception);
            }
            try
            {
                if (append)
                {
                    stream.Seek(0, SeekOrigin.End);
                }
                else
                {
                    stream.SetLength(0);
                }
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            ValidateName(name);
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("A content type is required.", nameof(mimeType));
            }
            var directoryPath = RequireDirectory(directoryLocation);
            var path = Path.Combine(directoryPath, name);
            if (GetEntry(path) is not null)
            {
                throw new IOException("An entry with the requested name already exists.");
            }
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            return GetEntry(path) ?? throw new FileNotFoundException("The created local file disappeared.", path);
        }

        /// <inheritdoc />
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            ValidateName(name);
            var directoryPath = RequireDirectory(directoryLocation);
            var path = Path.Combine(directoryPath, name);
            Directory.CreateDirectory(path);
            var entry = GetEntry(path);
            if (entry is null || !entry.IsDirectory)
            {
                throw new IOException("The created local directory is no longer available.");
            }
            return entry;
        }

        /// <inheritdoc />
        public FileSystemEntry Rename(string location, string name)
        {
            ValidateName(name);
            var entry = GetEntry(location) ?? throw new FileNotFoundException("The local entry does not exist.", location);
            var parentPath = Path.GetDirectoryName(entry.Location);
            if (string.IsNullOrEmpty(parentPath))
            {
                throw new IOException("A filesystem root cannot be renamed.");
            }
            var targetPath = Path.Combine(parentPath, name);
            if (string.Equals(entry.Location, targetPath, StringComparison.Ordinal))
            {
                return entry;
            }
            if (GetEntry(targetPath) is not null && !LocationsEqual(entry.Location, targetPath))
            {
                throw new IOException("An entry with the requested name already exists.");
            }
            if (entry.IsDirectory)
            {
                Directory.Move(entry.Location, targetPath);
            }
            else
            {
                File.Move(entry.Location, targetPath);
            }
            return GetEntry(targetPath) ?? throw new FileNotFoundException("The renamed local entry disappeared.", targetPath);
        }

        /// <inheritdoc />
        public void DeleteFile(string location)
        {
            var path = FileSystem.NormalizeLocalPath(location);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new IOException("The local entry is a directory, not a file.");
            }
            File.Delete(path);
        }

        /// <inheritdoc />
        public void DeleteDirectory(string location, bool recursive = false)
        {
            var path = RequireDirectory(location);
            DeleteDirectoryCore(path, recursive);
        }

        /// <summary>Compares normalized local paths, ignoring case on Windows without resolving filesystem identities.</summary>
        /// <param name="first">The first local path or file URI.</param>
        /// <param name="second">The second local path or file URI.</param>
        /// <returns>Whether both locations have the same normalized spelling.</returns>
        /// <exception cref="ArgumentException">A location is invalid.</exception>
        /// <exception cref="NotSupportedException">A location uses a non-file URI scheme.</exception>
        /// <exception cref="IOException">A path cannot be normalized.</exception>
        internal static bool LocationsEqual(string first, string second)
        {
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(FileSystem.NormalizeLocalPath(first), FileSystem.NormalizeLocalPath(second), comparison);
        }

        /// <summary>Validates a single name, including platform-specific restrictions.</summary>
        /// <param name="name">The proposed immediate child name.</param>
        /// <exception cref="ArgumentNullException">The name is null.</exception>
        /// <exception cref="ArgumentException">The name is invalid or is a Windows trailing-dot or trailing-space alias.</exception>
        private static void ValidateName(string name)
        {
            StorageName.Validate(name);
            if (name.IndexOfAny(s_invalidFileNameChars) >= 0 ||
                (Path.DirectorySeparatorChar == '\\' && (name[name.Length - 1] == '.' || name[name.Length - 1] == ' ')))
            {
                throw new ArgumentException("A valid single local child name is required.", nameof(name));
            }
        }

        /// <summary>Checks that a local location denotes an existing directory.</summary>
        /// <param name="location">The local directory path or file URI.</param>
        /// <returns>The normalized, absolute directory path.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying the directory was denied.</exception>
        /// <exception cref="NotSupportedException">The location uses a non-file URI scheme.</exception>
        /// <exception cref="IOException">The entry is not a directory or querying it failed.</exception>
        private string RequireDirectory(string location)
        {
            var entry = GetEntry(location);
            if (entry is null)
            {
                throw new DirectoryNotFoundException("The local directory does not exist.");
            }
            if (!entry.IsDirectory)
            {
                throw new IOException("The local entry is a file, not a directory.");
            }
            return entry.Location;
        }

        /// <summary>Deletes a directory tree without traversing encountered reparse points.</summary>
        /// <param name="path">The normalized directory path.</param>
        /// <param name="recursive">Whether to remove the directory's children.</param>
        /// <exception cref="DirectoryNotFoundException">A directory disappeared during deletion.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="IOException">An entry changed type, a directory is non-empty, or deletion failed.</exception>
        private static void DeleteDirectoryCore(string path, bool recursive)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException("The local entry is no longer a directory.");
            }
            if (!recursive || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(path, false);
                return;
            }
            foreach (var childPath in Directory.EnumerateFileSystemEntries(path))
            {
                var childAttributes = File.GetAttributes(childPath);
                if ((childAttributes & FileAttributes.Directory) != 0)
                {
                    // The recursive call rechecks the link flag before enumerating this child.
                    DeleteDirectoryCore(childPath, true);
                }
                else
                {
                    File.Delete(childPath);
                }
            }
            Directory.Delete(path, false);
        }
    }
}
