#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.ChartStorageValidation
{
    /// <summary>Maps ignored local fixtures to unregistered, opaque locations to detect backend loss.</summary>
    /// <remarks>This is not an Android SAF implementation; only the original backend can resolve its locations.</remarks>
    internal sealed class OpaqueFileSystem : IFileSystem
    {
        /// <summary>Owns the native storage implementation used exclusively behind this backend.</summary>
        private readonly IFileSystem _native;
        /// <summary>Defines the opaque URI scheme used by this fixture backend.</summary>
        private readonly string _locationPrefix;
        /// <summary>Counts every attempted backend operation, including rejected access.</summary>
        private int _accessCount;
        /// <summary>Maps opaque locations to their fixture-owned native paths.</summary>
        private readonly ConcurrentDictionary<string, string> _locations = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Keeps each native fixture entry's opaque location stable across refreshes.</summary>
        private readonly ConcurrentDictionary<string, string> _paths = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Gets injected directory-enumeration failures keyed by opaque location.</summary>
        public ConcurrentDictionary<string, Exception> EnumerationFailures { get; } = new ConcurrentDictionary<string, Exception>(StringComparer.Ordinal);
        /// <summary>Gets explicit chart-directory creation times used for stable-order assertions.</summary>
        public ConcurrentDictionary<string, DateTime> CreationTimes { get; } = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        /// <summary>Gets forced-hidden entries independently of the execution host's hidden-file rules.</summary>
        public ConcurrentDictionary<string, bool> Hidden { get; } = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        /// <summary>Gets explicit link metadata without creating operating-system symlinks in fixtures.</summary>
        public ConcurrentDictionary<string, bool> SymbolicLinks { get; } = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        /// <summary>Gets or sets whether native enumeration order should be reversed.</summary>
        public bool ReverseEnumeration { get; set; }
        /// <summary>Gets or sets whether every backend operation must fail to detect cross-source I/O.</summary>
        public bool RejectAccess { get; set; }
        /// <summary>Gets the total attempted backend operations.</summary>
        public int AccessCount => Volatile.Read(ref _accessCount);
        /// <summary>Gets the opaque fixture root.</summary>
        public StorageDirectory Root { get; }

        /// <summary>Creates an opaque provider over one test-owned local fixture tree.</summary>
        /// <param name="rootPath">The existing ignored fixture directory.</param>
        /// <param name="useContentUris">Whether to mint content URIs that can be explicitly registered for root-refresh tests.</param>
        public OpaqueFileSystem(string rootPath, bool useContentUris = false)
        {
            _locationPrefix = useContentUris ? "content://chart-validation/document/" : "chart-validation://unregistered/document/";
            var root = FileSystem.Directory.Open(rootPath);
            _native = root.FileSystem;
            Root = Open(rootPath);
        }

        /// <summary>Returns a handle whose provider location cannot be reopened through the global facade.</summary>
        /// <param name="nativePath">The native fixture path, never exposed to production scanning.</param>
        /// <returns>A directory handle retaining this exact backend instance.</returns>
        public StorageDirectory Open(string nativePath)
        {
            return new StorageDirectory(this, MapPath(Path.GetFullPath(nativePath)));
        }

        /// <summary>Queries entry metadata without converting the caller's provider URI into a path.</summary>
        /// <param name="location">The backend-owned opaque location.</param>
        /// <returns>Mapped metadata, or null when the native fixture has been removed.</returns>
        public FileSystemEntry? GetEntry(string location)
        {
            var entry = _native.GetEntry(Resolve(location));
            return entry is null ? null : MapEntry(entry);
        }

        /// <summary>Finds a fixture child through the native backend's name rules.</summary>
        /// <param name="directoryLocation">The opaque parent location.</param>
        /// <param name="name">The immediate child name.</param>
        /// <returns>The mapped child metadata, or null when absent.</returns>
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            var entry = _native.GetChildEntry(Resolve(directoryLocation), name);
            return entry is null ? null : MapEntry(entry);
        }

        /// <summary>Enumerates mapped fixture children or raises an injected directory-local failure.</summary>
        /// <param name="directoryLocation">The opaque directory location.</param>
        /// <returns>The immediate children in a deliberately controllable provider order.</returns>
        /// <exception cref="UnauthorizedAccessException">A regression case injected access denial.</exception>
        /// <exception cref="IOException">A regression case injected a provider I/O failure.</exception>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            if (EnumerationFailures.TryGetValue(directoryLocation, out var exception))
            {
                throw exception;
            }
            var entries = _native.EnumerateEntries(Resolve(directoryLocation)).Select(MapEntry).ToArray();
            if (ReverseEnumeration)
            {
                Array.Reverse(entries);
            }
            return entries;
        }

        /// <summary>Opens fixture content through the original backend.</summary>
        /// <param name="location">The opaque file location.</param>
        /// <returns>A caller-owned readable native fixture stream.</returns>
        public Stream OpenRead(string location)
        {
            return _native.OpenRead(Resolve(location));
        }

        /// <summary>Opens an existing fixture file with the production truncation semantics.</summary>
        /// <param name="location">The opaque file location.</param>
        /// <param name="append">Whether to append instead of truncate.</param>
        /// <returns>A caller-owned writable native fixture stream.</returns>
        public Stream OpenWrite(string location, bool append = false)
        {
            return _native.OpenWrite(Resolve(location), append);
        }

        /// <summary>Creates a mapped immediate fixture file.</summary>
        /// <param name="directoryLocation">The opaque parent directory.</param>
        /// <param name="name">The immediate file name.</param>
        /// <param name="mimeType">The requested MIME type.</param>
        /// <returns>The mapped newly created entry.</returns>
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            return MapEntry(_native.CreateFile(Resolve(directoryLocation), name, mimeType));
        }

        /// <summary>Creates a mapped immediate fixture directory.</summary>
        /// <param name="directoryLocation">The opaque parent directory.</param>
        /// <param name="name">The immediate directory name.</param>
        /// <returns>The mapped created or existing directory.</returns>
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            return MapEntry(_native.CreateDirectory(Resolve(directoryLocation), name));
        }

        /// <summary>Renames a fixture entry through the native storage boundary.</summary>
        /// <param name="location">The opaque source location.</param>
        /// <param name="name">The new immediate name.</param>
        /// <returns>The mapped renamed entry.</returns>
        public FileSystemEntry Rename(string location, string name)
        {
            return MapEntry(_native.Rename(Resolve(location), name));
        }

        /// <summary>Moves an entry through the native storage boundary.</summary>
        /// <param name="location">The opaque source location.</param>
        /// <param name="directoryLocation">The opaque target directory.</param>
        /// <param name="name">The target immediate name.</param>
        /// <returns>The mapped moved entry.</returns>
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            return MapEntry(_native.Move(Resolve(location), Resolve(directoryLocation), name));
        }

        /// <summary>Deletes a test-owned fixture file.</summary>
        /// <param name="location">The opaque fixture file location.</param>
        public void DeleteFile(string location)
        {
            _native.DeleteFile(Resolve(location));
        }

        /// <summary>Deletes a test-owned fixture directory through its known native backend.</summary>
        /// <param name="location">The opaque fixture directory location.</param>
        /// <param name="recursive">Whether native fixture deletion is recursive.</param>
        public void DeleteDirectory(string location, bool recursive = false)
        {
            _native.DeleteDirectory(Resolve(location), recursive);
        }

        /// <summary>Sets attributes while retaining controllable provider metadata.</summary>
        /// <param name="location">The opaque fixture location.</param>
        /// <param name="attributes">The attributes to set on the local fixture.</param>
        public void SetAttributes(string location, FileAttributes attributes)
        {
            _native.SetAttributes(Resolve(location), attributes);
            Hidden[location] = (attributes & FileAttributes.Hidden) != 0;
        }

        /// <summary>Sets a fixture modification timestamp through the native storage boundary.</summary>
        /// <param name="location">The opaque fixture location.</param>
        /// <param name="lastWriteTimeUtc">The UTC modification timestamp.</param>
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            _native.SetLastWriteTime(Resolve(location), lastWriteTimeUtc);
        }

        /// <summary>Resolves only locations minted by this specific backend instance.</summary>
        /// <param name="location">The opaque provider location.</param>
        /// <returns>The native path hidden behind this backend.</returns>
        /// <exception cref="IOException">Access is rejected or the location does not belong to this backend.</exception>
        private string Resolve(string location)
        {
            Interlocked.Increment(ref _accessCount);
            if (RejectAccess)
            {
                throw new IOException("The online refresh attempted to access the retained local filesystem.");
            }
            if (!_locations.TryGetValue(location, out var path))
            {
                throw new IOException("The scanner used a location not owned by its original opaque backend.");
            }
            return path;
        }

        /// <summary>Assigns a stable URI whose document identity contains no directory hierarchy.</summary>
        /// <param name="path">The native fixture path.</param>
        /// <returns>An opaque, unregistered provider location.</returns>
        private string MapPath(string path)
        {
            return _paths.GetOrAdd(path, nativePath =>
            {
                var location = _locationPrefix + Guid.NewGuid().ToString("N") + "%2Fopaque";
                _locations[location] = nativePath;
                return location;
            });
        }

        /// <summary>Projects native metadata without leaking the native path to production code.</summary>
        /// <param name="entry">The native metadata.</param>
        /// <returns>Metadata with an authoritative opaque location.</returns>
        private FileSystemEntry MapEntry(FileSystemEntry entry)
        {
            var location = MapPath(entry.Location);
            var creationTime = CreationTimes.TryGetValue(location, out var time) ? time : entry.CreationTimeUtc;
            var isHidden = Hidden.TryGetValue(location, out var hidden) ? hidden : entry.IsHidden;
            var isSymbolicLink = SymbolicLinks.TryGetValue(location, out var symbolicLink) ? symbolicLink : entry.IsSymbolicLink;
            return new FileSystemEntry(location, entry.Name, entry.IsDirectory, entry.Length,
                entry.LastWriteTimeUtc, isSymbolicLink, creationTimeUtc: creationTime,
                isHidden: isHidden, isSystem: entry.IsSystem);
        }
    }
}
