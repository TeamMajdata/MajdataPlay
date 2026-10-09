#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay.IO.Storage
{
    /// <summary>
    /// Provides file and directory operations over opaque storage locations.
    /// Locations returned by this backend must not be combined with <see cref="Path"/>.
    /// Operations are synchronous; callers should perform potentially slow I/O off the game loop.
    /// </summary>
    public interface IFileSystem
    {
        /// <summary>Queries an existing file or directory without conflating permission failures with absence.</summary>
        /// <param name="location">The local path or provider URI owned by this backend.</param>
        /// <returns>The entry metadata, or null when the entry does not exist.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Access was denied or a URI grant was revoked.</exception>
        /// <exception cref="IOException">The storage backend could not complete the query.</exception>
        FileSystemEntry? GetEntry(string location);

        /// <summary>Finds an immediate child using the backend's name comparison rules.</summary>
        /// <param name="directoryLocation">The existing parent directory location.</param>
        /// <param name="name">One child name, not a relative path.</param>
        /// <returns>The child metadata, or null when no child has that name.</returns>
        /// <exception cref="ArgumentException">The location or name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Access was denied.</exception>
        /// <exception cref="IOException">The query failed or a provider returned ambiguous duplicate names.</exception>
        FileSystemEntry? GetChildEntry(string directoryLocation, string name);

        /// <summary>Enumerates immediate children without following symbolic links or recursing.</summary>
        /// <param name="directoryLocation">The existing directory location.</param>
        /// <returns>The children with their authoritative locations and metadata.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Access was denied.</exception>
        /// <exception cref="IOException">The directory could not be queried.</exception>
        IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation);

        /// <summary>Opens an existing file for sequential reading.</summary>
        /// <param name="location">The existing file location.</param>
        /// <returns>A caller-owned stream which may not support seeking or length queries.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry has no readable binary representation.</exception>
        /// <exception cref="IOException">Opening the file failed.</exception>
        Stream OpenRead(string location);

        /// <summary>Opens an existing file for writing, truncating it unless append is requested.</summary>
        /// <param name="location">The existing file location.</param>
        /// <param name="append">Whether writes append instead of replacing existing content.</param>
        /// <returns>A caller-owned stream which may not support seeking or length queries.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support the requested mode.</exception>
        /// <exception cref="IOException">Opening the file failed.</exception>
        Stream OpenWrite(string location, bool append = false);

        /// <summary>Creates a new, empty immediate child file without overwriting a sibling.</summary>
        /// <param name="directoryLocation">The existing parent directory location.</param>
        /// <param name="name">One child name, not a relative path.</param>
        /// <param name="mimeType">The requested content type; local storage may ignore it.</param>
        /// <returns>The created file, including the actual name and location assigned by the provider.</returns>
        /// <exception cref="ArgumentException">An argument is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Creation was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support file creation.</exception>
        /// <exception cref="IOException">A sibling exists or creation failed.</exception>
        FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream");

        /// <summary>Creates an immediate child directory, or returns an existing directory with that name.</summary>
        /// <param name="directoryLocation">The existing parent directory location.</param>
        /// <param name="name">One child name, not a relative path.</param>
        /// <returns>The existing or created directory with its authoritative location.</returns>
        /// <exception cref="ArgumentException">An argument is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Creation was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support directory creation.</exception>
        /// <exception cref="IOException">A file occupies the name or creation failed.</exception>
        FileSystemEntry CreateDirectory(string directoryLocation, string name);

        /// <summary>Renames an entry within its parent without overwriting a sibling.</summary>
        /// <param name="location">The existing entry location.</param>
        /// <param name="name">The new single entry name.</param>
        /// <returns>The renamed entry; its location may differ from the original URI.</returns>
        /// <exception cref="ArgumentException">An argument is invalid.</exception>
        /// <exception cref="FileNotFoundException">The entry does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Renaming was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support renaming.</exception>
        /// <exception cref="IOException">A sibling exists or renaming failed.</exception>
        FileSystemEntry Rename(string location, string name);

        /// <summary>Deletes a file; a missing file is a no-op, but a directory is never deleted.</summary>
        /// <param name="location">The file location.</param>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support deletion.</exception>
        /// <exception cref="IOException">The location is a directory or deletion failed.</exception>
        void DeleteFile(string location);

        /// <summary>Deletes a directory without following symbolic links.</summary>
        /// <param name="location">The existing directory location.</param>
        /// <param name="recursive">Whether non-empty directories may be deleted.</param>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support deletion.</exception>
        /// <exception cref="IOException">The entry is not a directory, is non-empty without recursion, or deletion failed.</exception>
        void DeleteDirectory(string location, bool recursive = false);
    }
}
