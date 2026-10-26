#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.ImportValidation
{
    /// <summary>Maps ignored local fixtures to unregistered, opaque locations to detect backend loss.</summary>
    /// <remarks>This is not an Android SAF implementation; only the original backend can resolve its locations.</remarks>
    internal sealed class OpaqueFileSystem : IFileSystem
    {
        /// <summary>Owns the native storage implementation used exclusively behind this backend.</summary>
        private readonly IFileSystem _native;
        /// <summary>Maps opaque locations to their fixture-owned native paths.</summary>
        private readonly Dictionary<string, string> _locations = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Keeps each native fixture entry's opaque location stable across queries.</summary>
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Gets the opaque fixture root.</summary>
        public StorageDirectory Root { get; }

        /// <summary>Creates an opaque provider over one test-owned local fixture tree.</summary>
        /// <param name="rootPath">The existing ignored fixture directory.</param>
        public OpaqueFileSystem(string rootPath)
        {
            var root = FileSystem.Directory.Open(rootPath);
            _native = root.FileSystem;
            Root = new StorageDirectory(this, MapPath(root.Location));
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

        /// <summary>Enumerates fixture children without exposing native paths to the importer.</summary>
        /// <param name="directoryLocation">The opaque directory location.</param>
        /// <returns>The immediate children with authoritative provider locations.</returns>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            foreach (var entry in _native.EnumerateEntries(Resolve(directoryLocation)))
            {
                yield return MapEntry(entry);
            }
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

        /// <summary>Sets native fixture attributes through the retained backend.</summary>
        /// <param name="location">The opaque fixture location.</param>
        /// <param name="attributes">The attributes to set on the local fixture.</param>
        public void SetAttributes(string location, FileAttributes attributes)
        {
            _native.SetAttributes(Resolve(location), attributes);
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
        /// <exception cref="IOException">The location does not belong to this backend.</exception>
        private string Resolve(string location)
        {
            if (!_locations.TryGetValue(location, out var path))
            {
                throw new IOException("The importer used a location not owned by its original opaque backend.");
            }
            return path;
        }

        /// <summary>Assigns a stable URI whose document identity contains no directory hierarchy.</summary>
        /// <param name="path">The native fixture path.</param>
        /// <returns>An opaque, unregistered provider location.</returns>
        private string MapPath(string path)
        {
            if (_paths.TryGetValue(path, out var location))
            {
                return location;
            }
            location = "import-validation://unregistered/document/" + Guid.NewGuid().ToString("N") + "%2Fopaque";
            _locations.Add(location, path);
            _paths.Add(path, location);
            return location;
        }

        /// <summary>Projects native metadata without leaking the native path to production code.</summary>
        /// <param name="entry">The native metadata.</param>
        /// <returns>Metadata with an authoritative opaque location.</returns>
        private FileSystemEntry MapEntry(FileSystemEntry entry)
        {
            var location = MapPath(entry.Location);
            return new FileSystemEntry(location, entry.Name, entry.IsDirectory, entry.Length,
                entry.LastWriteTimeUtc, entry.IsSymbolicLink, creationTimeUtc: entry.CreationTimeUtc,
                isHidden: entry.IsHidden, isSystem: entry.IsSystem);
        }
    }
}
