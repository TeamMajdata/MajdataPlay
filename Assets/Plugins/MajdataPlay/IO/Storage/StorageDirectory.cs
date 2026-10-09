#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Represents a directory location owned by a local filesystem or an opaque storage provider.</summary>
    /// <remarks>Handles do not cache metadata. Enumeration and provider operations are synchronous and may block.</remarks>
    public sealed class StorageDirectory
    {
        /// <summary>Gets the backend responsible for this directory location.</summary>
        public IFileSystem FileSystem { get; }

        /// <summary>Gets the normalized local path or unchanged opaque provider location.</summary>
        public string Location { get; }

        /// <summary>Gets current entry metadata, or null when the location does not exist.</summary>
        /// <exception cref="UnauthorizedAccessException">Querying the entry was denied.</exception>
        /// <exception cref="IOException">The backend could not query the entry.</exception>
        public FileSystemEntry? Entry
        {
            get
            {
                return FileSystem.GetEntry(Location);
            }
        }

        /// <summary>Gets whether the location currently denotes an existing directory.</summary>
        /// <exception cref="UnauthorizedAccessException">Querying the entry was denied.</exception>
        /// <exception cref="IOException">The backend could not query the entry.</exception>
        public bool Exists
        {
            get
            {
                return Entry is { IsDirectory: true };
            }
        }

        /// <summary>Creates a directory handle without opening or creating the directory.</summary>
        /// <param name="fileSystem">The backend owning the location.</param>
        /// <param name="location">A local path or file URI for a local backend, otherwise an opaque provider location.</param>
        /// <exception cref="ArgumentNullException">The backend or location is null.</exception>
        /// <exception cref="ArgumentException">The location is empty or the local path is invalid.</exception>
        /// <exception cref="NotSupportedException">A local backend was given a non-file URI.</exception>
        /// <exception cref="IOException">The local path cannot be normalized.</exception>
        public StorageDirectory(IFileSystem fileSystem, string location)
        {
            if (fileSystem is null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("A directory location is required.", nameof(location));
            }
            FileSystem = fileSystem;
            Location = fileSystem is LocalFileSystem ? global::MajdataPlay.IO.Storage.FileSystem.NormalizeLocalPath(location) : location;
        }

        /// <summary>Enumerates only the immediate children using their authoritative provider locations.</summary>
        /// <returns>The immediate file and directory metadata; symbolic links are not traversed.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumeration was denied.</exception>
        /// <exception cref="IOException">Enumeration failed or the location is not a directory.</exception>
        public IEnumerable<FileSystemEntry> EnumerateEntries()
        {
            return FileSystem.EnumerateEntries(Location);
        }

        /// <summary>Enumerates handles for immediate file children without recursion.</summary>
        /// <returns>Handles for immediate files, retaining this directory's backend.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumeration was denied.</exception>
        /// <exception cref="IOException">Enumeration failed or the location is not a directory.</exception>
        public IEnumerable<StorageFile> EnumerateFiles()
        {
            foreach (var entry in EnumerateEntries())
            {
                if (!entry.IsDirectory)
                {
                    yield return new StorageFile(FileSystem, entry.Location);
                }
            }
        }

        /// <summary>Enumerates handles for immediate directory children without recursion.</summary>
        /// <returns>Handles for immediate directories, retaining this directory's backend.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumeration was denied.</exception>
        /// <exception cref="IOException">Enumeration failed or the location is not a directory.</exception>
        public IEnumerable<StorageDirectory> EnumerateDirectories()
        {
            foreach (var entry in EnumerateEntries())
            {
                if (entry.IsDirectory)
                {
                    yield return new StorageDirectory(FileSystem, entry.Location);
                }
            }
        }

        /// <summary>Finds an immediate file child by the backend's name comparison rules.</summary>
        /// <param name="name">One child name, not a path or URI.</param>
        /// <returns>A file handle, or null if no file has the requested name.</returns>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying the child was denied.</exception>
        /// <exception cref="IOException">The query failed or found ambiguous names.</exception>
        public StorageFile? FindFile(string name)
        {
            StorageName.Validate(name);
            var entry = FileSystem.GetChildEntry(Location, name);
            if (entry is null || entry.IsDirectory)
            {
                return null;
            }
            return new StorageFile(FileSystem, entry.Location);
        }

        /// <summary>Finds an immediate directory child by the backend's name comparison rules.</summary>
        /// <param name="name">One child name, not a path or URI.</param>
        /// <returns>A directory handle, or null if no directory has the requested name.</returns>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying the child was denied.</exception>
        /// <exception cref="IOException">The query failed or found ambiguous names.</exception>
        public StorageDirectory? FindDirectory(string name)
        {
            StorageName.Validate(name);
            var entry = FileSystem.GetChildEntry(Location, name);
            if (entry is null || !entry.IsDirectory)
            {
                return null;
            }
            return new StorageDirectory(FileSystem, entry.Location);
        }

        /// <summary>Creates a new empty immediate child file without overwriting a sibling.</summary>
        /// <param name="name">One child name, not a path or URI.</param>
        /// <param name="mimeType">The requested content type, which the local backend may ignore.</param>
        /// <returns>A handle using the backend's actual created name and location.</returns>
        /// <exception cref="ArgumentException">The name or content type is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">File creation was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support file creation.</exception>
        /// <exception cref="IOException">A sibling exists or file creation failed.</exception>
        public StorageFile CreateFile(string name, string mimeType = "application/octet-stream")
        {
            StorageName.Validate(name);
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("A content type is required.", nameof(mimeType));
            }
            var entry = FileSystem.CreateFile(Location, name, mimeType);
            return new StorageFile(FileSystem, entry.Location);
        }

        /// <summary>Creates an immediate child directory or opens an existing directory with that name.</summary>
        /// <param name="name">One child name, not a path or URI.</param>
        /// <returns>A handle using the backend's authoritative directory location.</returns>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Directory creation was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support directory creation.</exception>
        /// <exception cref="IOException">A file occupies the name or creation failed.</exception>
        public StorageDirectory CreateDirectory(string name)
        {
            StorageName.Validate(name);
            var entry = FileSystem.CreateDirectory(Location, name);
            return new StorageDirectory(FileSystem, entry.Location);
        }

        /// <summary>Deletes the directory, without following symbolic links.</summary>
        /// <param name="recursive">Whether non-empty directories may be deleted.</param>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support directory deletion.</exception>
        /// <exception cref="IOException">The location is not a directory, is non-empty without recursion, or deletion failed.</exception>
        public void Delete(bool recursive = false)
        {
            FileSystem.DeleteDirectory(Location, recursive);
        }

        /// <summary>Renames the directory within its parent without changing this handle or overwriting a sibling.</summary>
        /// <param name="name">The new single directory name.</param>
        /// <returns>A new handle with the backend's renamed directory location.</returns>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">This location does not denote an existing directory.</exception>
        /// <exception cref="UnauthorizedAccessException">Renaming was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support renaming.</exception>
        /// <exception cref="IOException">A sibling exists or renaming failed.</exception>
        public StorageDirectory Rename(string name)
        {
            StorageName.Validate(name);
            if (!Exists)
            {
                throw new DirectoryNotFoundException("The storage directory does not exist.");
            }
            var entry = FileSystem.Rename(Location, name);
            return new StorageDirectory(FileSystem, entry.Location);
        }
    }
}
