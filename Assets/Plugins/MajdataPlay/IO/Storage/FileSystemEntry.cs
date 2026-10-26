#nullable enable
using System;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Describes a storage entry without assuming that it has a native filesystem path.</summary>
    public sealed class FileSystemEntry
    {
        /// <summary>Gets the authoritative local path or opaque provider URI.</summary>
        public string Location { get; }

        /// <summary>Gets an optional globally scoped resource identity shared by different URI aliases of one entry.</summary>
        /// <remarks>This is for identity comparison only, never a location to open. Null means the backend supplies no identity.</remarks>
        public string? ResourceId { get; }

        /// <summary>Gets the backend's display name, which may differ from a requested creation name.</summary>
        public string Name { get; }

        /// <summary>Gets whether the entry is a directory.</summary>
        public bool IsDirectory { get; }

        /// <summary>Gets the file size in bytes, or null for directories and unknown provider sizes.</summary>
        public long? Length { get; }

        /// <summary>Gets the last modification time in UTC, or null if the backend does not report it.</summary>
        public DateTime? LastWriteTimeUtc { get; }

        /// <summary>Gets the creation time in UTC, or null if the backend does not report it.</summary>
        public DateTime? CreationTimeUtc { get; }

        /// <summary>Gets whether the backend reports the entry as hidden.</summary>
        public bool IsHidden { get; }

        /// <summary>Gets whether the backend reports the entry as a system entry.</summary>
        public bool IsSystem { get; }

        /// <summary>Gets whether this local entry is a symbolic link or another filesystem reparse point.</summary>
        public bool IsSymbolicLink { get; }

        /// <summary>Initializes immutable entry metadata.</summary>
        /// <param name="location">The authoritative local path or provider URI.</param>
        /// <param name="name">The display name.</param>
        /// <param name="isDirectory">Whether the entry is a directory.</param>
        /// <param name="length">The known nonnegative file size, or null.</param>
        /// <param name="lastWriteTimeUtc">The known UTC modification time, or null.</param>
        /// <param name="isSymbolicLink">Whether the entry is a local symbolic link or reparse point.</param>
        /// <param name="resourceId">An optional globally scoped identity independent of provider grant URI aliases.</param>
        /// <param name="creationTimeUtc">The known UTC creation time, or null.</param>
        /// <param name="isHidden">Whether the backend reports a hidden entry.</param>
        /// <param name="isSystem">Whether the backend reports a system entry.</param>
        /// <exception cref="ArgumentNullException">The name is null.</exception>
        /// <exception cref="ArgumentException">The location or supplied resource identity is empty, or the timestamp is not UTC.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
        public FileSystemEntry(string location, string name, bool isDirectory, long? length = null,
            DateTime? lastWriteTimeUtc = null, bool isSymbolicLink = false, string? resourceId = null,
            DateTime? creationTimeUtc = null, bool isHidden = false, bool isSystem = false)
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new ArgumentException("A storage location is required.", nameof(location));
            }
            if (resourceId is not null && resourceId.Length == 0)
            {
                throw new ArgumentException("A supplied resource identity must not be empty.", nameof(resourceId));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (lastWriteTimeUtc.HasValue && lastWriteTimeUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The modification time must be UTC.", nameof(lastWriteTimeUtc));
            }
            if (creationTimeUtc.HasValue && creationTimeUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The creation time must be UTC.", nameof(creationTimeUtc));
            }
            Location = location;
            ResourceId = resourceId;
            Name = name;
            IsDirectory = isDirectory;
            Length = isDirectory ? null : length;
            LastWriteTimeUtc = lastWriteTimeUtc;
            CreationTimeUtc = creationTimeUtc;
            IsHidden = isHidden;
            IsSystem = isSystem;
            IsSymbolicLink = isSymbolicLink;
        }
    }
}
