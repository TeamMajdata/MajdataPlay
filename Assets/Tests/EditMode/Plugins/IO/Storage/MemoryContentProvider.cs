#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>
    /// An in-memory content provider double whose opaque document URIs carry no path hierarchy, used to exercise the
    /// facade's provider branches without Android or a real document provider.
    /// </summary>
    internal sealed class MemoryContentProvider : IFileSystem
    {
        /// <summary>The URI authority shared by every provider instance.</summary>
        public const string Authority = "majdataplay.tests";

        /// <summary>Maps every known location, including aliases, to its document.</summary>
        private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

        /// <summary>The volume segment that separates locations of different provider instances.</summary>
        private readonly string _volume;

        /// <summary>The next numeric document ID.</summary>
        private int _nextId;

        /// <summary>Creates a provider containing only an empty root directory.</summary>
        /// <param name="volume">The volume segment of every location; a new GUID when null.</param>
        public MemoryContentProvider(string? volume = null)
        {
            _volume = volume ?? Guid.NewGuid().ToString("N");
            RootLocation = AddDocument(null, "root", true).Location;
        }

        /// <summary>Gets the opaque location of the root directory.</summary>
        public string RootLocation { get; }

        /// <summary>Gets or sets whether entries report a resource identity shared by all aliases of a document.</summary>
        public bool ReportResourceIds { get; set; }

        /// <summary>Gets or sets a one-shot display name the provider assigns to the next created file.</summary>
        public string? NextCreatedFileName { get; set; }

        /// <summary>Gets or sets an existing location that file creation wrongly returns, modelling a broken provider.</summary>
        public string? CreatedFileOverride { get; set; }

        /// <summary>Gets or sets whether write streams fail on their first write.</summary>
        public bool FailWrites { get; set; }

        /// <summary>Gets or sets whether read streams fail on their first read.</summary>
        public bool FailReads { get; set; }

        /// <summary>Gets or sets a callback invoked after every successful read from a provider stream.</summary>
        public Action? AfterRead { get; set; }

        /// <summary>Gets or sets an exception thrown by every metadata query, modelling a revoked grant.</summary>
        public Exception? QueryException { get; set; }

        /// <summary>Gets or sets whether modification time updates are stored instead of reported as unsupported.</summary>
        public bool AcceptsTimestamps { get; set; } = true;

        /// <summary>Gets the content type passed to the last file creation.</summary>
        public string? LastMimeType { get; private set; }

        /// <summary>Gets the number of streams opened for writing.</summary>
        public int OpenedWriteStreams { get; private set; }

        /// <summary>Gets the locations of files deleted through <see cref="DeleteFile"/>.</summary>
        public List<string> DeletedLocations { get; } = new();

        /// <summary>Adds a file with the given content below an existing directory.</summary>
        /// <param name="directoryLocation">The parent directory location.</param>
        /// <param name="name">The file name.</param>
        /// <param name="data">The file content.</param>
        /// <returns>The new file location.</returns>
        public string AddFile(string directoryLocation, string name, byte[] data)
        {
            var document = AddDocument(directoryLocation, name, false);
            document.Data = (byte[])data.Clone();
            return document.Location;
        }

        /// <summary>Adds a directory below an existing directory.</summary>
        /// <param name="directoryLocation">The parent directory location.</param>
        /// <param name="name">The directory name.</param>
        /// <returns>The new directory location.</returns>
        public string AddDirectory(string directoryLocation, string name)
        {
            return AddDocument(directoryLocation, name, true).Location;
        }

        /// <summary>Creates a second location naming the same document, like a different grant URI.</summary>
        /// <param name="location">The existing document location.</param>
        /// <returns>The alias location.</returns>
        public string AddAlias(string location)
        {
            var document = _documents[location];
            var alias = NewLocation();
            _documents.Add(alias, document);
            return alias;
        }

        /// <summary>Gets a copy of a file's current content.</summary>
        /// <param name="location">The file location.</param>
        /// <returns>The stored bytes.</returns>
        public byte[] GetData(string location)
        {
            return (byte[])_documents[location].Data.Clone();
        }

        /// <summary>Gets whether a location currently names a document.</summary>
        /// <param name="location">The location to look up.</param>
        /// <returns>True when the document exists.</returns>
        public bool Contains(string location)
        {
            return _documents.ContainsKey(location);
        }

        /// <inheritdoc />
        public FileSystemEntry? GetEntry(string location)
        {
            ValidateLocation(location);
            ThrowQueryError();
            return _documents.TryGetValue(location, out var document) ? CreateEntry(location, document) : null;
        }

        /// <inheritdoc />
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            StorageName.Validate(name);
            RequireDirectory(directoryLocation);
            ThrowQueryError();
            var child = FindChild(directoryLocation, name);
            return child is null ? null : CreateEntry(child.Location, child);
        }

        /// <inheritdoc />
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            RequireDirectory(directoryLocation);
            var entries = new List<FileSystemEntry>();
            foreach (var pair in _documents)
            {
                if (pair.Key == pair.Value.Location && pair.Value.ParentLocation == directoryLocation)
                {
                    entries.Add(CreateEntry(pair.Key, pair.Value));
                }
            }
            return entries;
        }

        /// <inheritdoc />
        public Stream OpenRead(string location)
        {
            var document = RequireFile(location);
            return new ProviderStream(this, document, false, document.Data);
        }

        /// <inheritdoc />
        public Stream OpenWrite(string location, bool append = false)
        {
            var document = RequireFile(location);
            OpenedWriteStreams++;
            if (!append)
            {
                document.Data = Array.Empty<byte>();
            }
            return new ProviderStream(this, document, true, Array.Empty<byte>());
        }

        /// <inheritdoc />
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            StorageName.Validate(name);
            RequireDirectory(directoryLocation);
            LastMimeType = mimeType;
            if (CreatedFileOverride is not null)
            {
                return GetEntry(CreatedFileOverride) ?? throw new FileNotFoundException("The override does not exist.");
            }
            if (FindChild(directoryLocation, name) is not null)
            {
                throw new IOException("A sibling already occupies the requested name.");
            }
            var actualName = NextCreatedFileName ?? name;
            NextCreatedFileName = null;
            var document = AddDocument(directoryLocation, actualName, false);
            return CreateEntry(document.Location, document);
        }

        /// <inheritdoc />
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            StorageName.Validate(name);
            RequireDirectory(directoryLocation);
            var existing = FindChild(directoryLocation, name);
            if (existing is not null)
            {
                if (!existing.IsDirectory)
                {
                    throw new IOException("A file occupies the requested directory name.");
                }
                return CreateEntry(existing.Location, existing);
            }
            var document = AddDocument(directoryLocation, name, true);
            return CreateEntry(document.Location, document);
        }

        /// <inheritdoc />
        public FileSystemEntry Rename(string location, string name)
        {
            StorageName.Validate(name);
            var document = RequireDocument(location);
            if (FindChild(document.ParentLocation!, name) is not null)
            {
                throw new IOException("A sibling already occupies the requested name.");
            }
            Relocate(document, document.ParentLocation!, name);
            return CreateEntry(document.Location, document);
        }

        /// <inheritdoc />
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            StorageName.Validate(name);
            var document = RequireDocument(location);
            RequireDirectory(directoryLocation);
            for (var current = directoryLocation; current is not null; current = _documents[current].ParentLocation)
            {
                if (current == document.Location)
                {
                    throw new IOException("A directory cannot be moved into its own subtree.");
                }
            }
            if (FindChild(directoryLocation, name) is not null)
            {
                throw new IOException("A sibling already occupies the requested name.");
            }
            Relocate(document, directoryLocation, name);
            return CreateEntry(document.Location, document);
        }

        /// <inheritdoc />
        public void DeleteFile(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                return;
            }
            if (document.IsDirectory)
            {
                throw new IOException("The document is a directory.");
            }
            _documents.Remove(location);
            DeletedLocations.Add(location);
        }

        /// <inheritdoc />
        public void DeleteDirectory(string location, bool recursive = false)
        {
            RequireDirectory(location);
            var children = new List<string>();
            foreach (var pair in _documents)
            {
                if (pair.Value.ParentLocation == location)
                {
                    children.Add(pair.Key);
                }
            }
            if (children.Count != 0 && !recursive)
            {
                throw new IOException("The directory is not empty.");
            }
            foreach (var child in children)
            {
                if (_documents[child].IsDirectory)
                {
                    DeleteDirectory(child, true);
                }
                else
                {
                    _documents.Remove(child);
                }
            }
            _documents.Remove(location);
        }

        /// <inheritdoc />
        public void SetAttributes(string location, FileAttributes attributes)
        {
            RequireDocument(location);
        }

        /// <inheritdoc />
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            if (lastWriteTimeUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The modification time must be UTC.", nameof(lastWriteTimeUtc));
            }
            var document = RequireDocument(location);
            if (!AcceptsTimestamps)
            {
                throw new NotSupportedException("The provider does not store modification times.");
            }
            document.LastWriteTimeUtc = lastWriteTimeUtc;
        }

        /// <summary>Creates a new location in this provider's volume.</summary>
        /// <returns>The unused opaque location.</returns>
        private string NewLocation()
        {
            return $"content://{Authority}/{_volume}/document/{_nextId++}";
        }

        /// <summary>Adds a document under a new location.</summary>
        /// <param name="parentLocation">The parent directory location, or null for the root.</param>
        /// <param name="name">The display name.</param>
        /// <param name="isDirectory">Whether the document is a directory.</param>
        /// <returns>The new document.</returns>
        private Document AddDocument(string? parentLocation, string name, bool isDirectory)
        {
            if (parentLocation is not null)
            {
                RequireDirectory(parentLocation);
            }
            var document = new Document(NewLocation(), parentLocation, name, isDirectory);
            _documents.Add(document.Location, document);
            return document;
        }

        /// <summary>Gives a document a new location, parent and name, updating the parents of its children.</summary>
        /// <param name="document">The document to relocate.</param>
        /// <param name="parentLocation">The new parent directory location.</param>
        /// <param name="name">The new display name.</param>
        private void Relocate(Document document, string parentLocation, string name)
        {
            var oldLocation = document.Location;
            _documents.Remove(oldLocation);
            document.Location = NewLocation();
            document.ParentLocation = parentLocation;
            document.Name = name;
            _documents.Add(document.Location, document);
            foreach (var child in _documents.Values)
            {
                if (child.ParentLocation == oldLocation)
                {
                    child.ParentLocation = document.Location;
                }
            }
        }

        /// <summary>Finds a child by its exact name.</summary>
        /// <param name="directoryLocation">The parent directory location.</param>
        /// <param name="name">The child name.</param>
        /// <returns>The child document, or null.</returns>
        private Document? FindChild(string directoryLocation, string name)
        {
            foreach (var pair in _documents)
            {
                if (pair.Key == pair.Value.Location && pair.Value.ParentLocation == directoryLocation &&
                    pair.Value.Name == name)
                {
                    return pair.Value;
                }
            }
            return null;
        }

        /// <summary>Creates an entry snapshot for a location.</summary>
        /// <param name="location">The location the entry is reported under.</param>
        /// <param name="document">The document.</param>
        /// <returns>The entry metadata.</returns>
        private FileSystemEntry CreateEntry(string location, Document document)
        {
            return new FileSystemEntry(location, document.Name, document.IsDirectory,
                document.IsDirectory ? null : document.Data.LongLength, document.LastWriteTimeUtc,
                resourceId: ReportResourceIds ? document.ResourceId : null);
        }

        /// <summary>Rejects locations that do not belong to any memory provider.</summary>
        /// <param name="location">The location to check.</param>
        /// <exception cref="ArgumentException">The location is not a memory provider URI.</exception>
        private static void ValidateLocation(string location)
        {
            if (location is null || !location.StartsWith($"content://{Authority}/", StringComparison.Ordinal))
            {
                throw new ArgumentException("The location does not belong to the memory provider.", nameof(location));
            }
        }

        /// <summary>Throws the configured query exception, if any.</summary>
        private void ThrowQueryError()
        {
            if (QueryException is not null)
            {
                throw QueryException;
            }
        }

        /// <summary>Finds an existing document.</summary>
        /// <param name="location">The document location.</param>
        /// <returns>The document.</returns>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        private Document RequireDocument(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                throw new FileNotFoundException("The document does not exist.", location);
            }
            return document;
        }

        /// <summary>Finds an existing file document.</summary>
        /// <param name="location">The file location.</param>
        /// <returns>The file document.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="IOException">The document is a directory.</exception>
        private Document RequireFile(string location)
        {
            var document = RequireDocument(location);
            if (document.IsDirectory)
            {
                throw new IOException("The document is a directory.");
            }
            return document;
        }

        /// <summary>Finds an existing directory document.</summary>
        /// <param name="location">The directory location.</param>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="IOException">The document is a file.</exception>
        private void RequireDirectory(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                throw new DirectoryNotFoundException("The directory does not exist.");
            }
            if (!document.IsDirectory)
            {
                throw new IOException("The document is a file.");
            }
        }

        /// <summary>One stored file or directory.</summary>
        private sealed class Document
        {
            /// <summary>Initializes a document.</summary>
            /// <param name="location">The authoritative location.</param>
            /// <param name="parentLocation">The parent location, or null for the root.</param>
            /// <param name="name">The display name.</param>
            /// <param name="isDirectory">Whether the document is a directory.</param>
            public Document(string location, string? parentLocation, string name, bool isDirectory)
            {
                Location = location;
                ParentLocation = parentLocation;
                Name = name;
                IsDirectory = isDirectory;
                ResourceId = "resource:" + location;
            }

            /// <summary>Gets or sets the authoritative location.</summary>
            public string Location { get; set; }

            /// <summary>Gets or sets the parent directory location.</summary>
            public string? ParentLocation { get; set; }

            /// <summary>Gets or sets the display name.</summary>
            public string Name { get; set; }

            /// <summary>Gets whether the document is a directory.</summary>
            public bool IsDirectory { get; }

            /// <summary>Gets the identity shared by every alias of this document.</summary>
            public string ResourceId { get; }

            /// <summary>Gets or sets the file content.</summary>
            public byte[] Data { get; set; } = Array.Empty<byte>();

            /// <summary>Gets or sets the stored modification time.</summary>
            public DateTime? LastWriteTimeUtc { get; set; }
        }

        /// <summary>A nonseekable provider stream that reads a snapshot or appends writes to a document.</summary>
        private sealed class ProviderStream : Stream
        {
            /// <summary>The provider supplying failure switches and callbacks.</summary>
            private readonly MemoryContentProvider _provider;

            /// <summary>The document written by a write stream.</summary>
            private readonly Document _document;

            /// <summary>Whether this is a write stream.</summary>
            private readonly bool _writable;

            /// <summary>The snapshot read by a read stream.</summary>
            private readonly byte[] _snapshot;

            /// <summary>The next snapshot offset to read.</summary>
            private int _offset;

            /// <summary>Initializes a provider stream.</summary>
            /// <param name="provider">The owning provider.</param>
            /// <param name="document">The document.</param>
            /// <param name="writable">Whether the stream writes instead of reading.</param>
            /// <param name="snapshot">The bytes a read stream returns.</param>
            public ProviderStream(MemoryContentProvider provider, Document document, bool writable, byte[] snapshot)
            {
                _provider = provider;
                _document = document;
                _writable = writable;
                _snapshot = snapshot;
            }

            /// <inheritdoc />
            public override bool CanRead => !_writable;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => _writable;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_writable)
                {
                    throw new NotSupportedException();
                }
                if (_provider.FailReads)
                {
                    throw new IOException("The provider read failed.");
                }
                var read = Math.Min(count, _snapshot.Length - _offset);
                Array.Copy(_snapshot, _offset, buffer, offset, read);
                _offset += read;
                _provider.AfterRead?.Invoke();
                return read;
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (!_writable)
                {
                    throw new NotSupportedException();
                }
                if (_provider.FailWrites)
                {
                    throw new IOException("The provider write failed.");
                }
                var data = new byte[_document.Data.Length + count];
                Array.Copy(_document.Data, data, _document.Data.Length);
                Array.Copy(buffer, offset, data, _document.Data.Length, count);
                _document.Data = data;
            }

            /// <inheritdoc />
            public override void Flush()
            {
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
        }
    }
}
