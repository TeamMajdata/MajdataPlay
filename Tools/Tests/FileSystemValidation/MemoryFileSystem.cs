#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.FileSystemValidation
{
    /// <summary>Models a content provider whose encoded document IDs have no path hierarchy.</summary>
    internal sealed class MemoryFileSystem : IFileSystem
    {
        /// <summary>Identifies the test provider without depending on Android or Unity.</summary>
        private const string LocationPrefix = "content://filesystem.validation/document/";

        /// <summary>Stores documents by their exact authoritative URI.</summary>
        private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

        /// <summary>Distinguishes locations issued by separate provider instances.</summary>
        private readonly string _volume = Guid.NewGuid().ToString("N");

        /// <summary>Assigns unrelated document IDs rather than child paths.</summary>
        private int _nextId;

        /// <summary>Gets the root document's opaque location.</summary>
        public string RootLocation { get; }

        /// <summary>Gets the MIME type forwarded by the last file creation.</summary>
        public string? LastMimeType { get; private set; }

        /// <summary>Gets or sets a one-shot provider-assigned display name for a new file.</summary>
        public string? NextCreatedFileName { get; set; }

        /// <summary>Gets or sets the byte threshold at which newly created files reject writes.</summary>
        public int? NewFileWriteFailureAfterBytes { get; set; }

        /// <summary>Gets or sets a query error used to model a revoked document grant.</summary>
        public Exception? QueryException { get; set; }

        /// <summary>Gets or sets whether the provider accepts modification time updates instead of reporting them unsupported.</summary>
        public bool AcceptsTimestamps { get; set; } = true;

        /// <summary>Gets the number of caller-owned streams which have not been disposed.</summary>
        public int OpenStreamCount { get; private set; }

        /// <summary>Gets the total number of streams opened by this provider.</summary>
        public int OpenedStreamCount { get; private set; }

        /// <summary>Gets the number of writable streams opened, including potentially truncating opens.</summary>
        public int OpenedWriteStreamCount { get; private set; }

        /// <summary>Gets the total number of streams disposed by their callers.</summary>
        public int DisposedStreamCount { get; private set; }

        /// <summary>Gets file locations deleted by the facade, including copy rollback.</summary>
        public List<string> DeletedLocations { get; } = new();

        /// <summary>Creates an isolated provider containing only its root directory.</summary>
        public MemoryFileSystem()
        {
            var root = AddDocument(null, "root", true);
            RootLocation = root.Location;
        }

        /// <inheritdoc />
        public FileSystemEntry? GetEntry(string location)
        {
            ValidateLocation(location);
            ThrowQueryError();
            return _documents.TryGetValue(location, out var document) ? GetMetadata(document) : null;
        }

        /// <inheritdoc />
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            StorageName.Validate(name);
            GetDirectory(directoryLocation);
            ThrowQueryError();
            var child = FindChild(directoryLocation, name);
            return child is null ? null : GetMetadata(child);
        }

        /// <inheritdoc />
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            GetDirectory(directoryLocation);
            ThrowQueryError();
            var entries = new List<FileSystemEntry>();
            foreach (var document in _documents.Values)
            {
                if (document.ParentLocation == directoryLocation)
                {
                    entries.Add(GetMetadata(document));
                }
            }
            return entries;
        }

        /// <inheritdoc />
        public Stream OpenRead(string location)
        {
            return OpenStream(GetFile(location), false, false);
        }

        /// <inheritdoc />
        public Stream OpenWrite(string location, bool append = false)
        {
            return OpenStream(GetFile(location), true, append);
        }

        /// <inheritdoc />
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            StorageName.Validate(name);
            GetDirectory(directoryLocation);
            if (FindChild(directoryLocation, name) is not null)
            {
                throw new IOException("A sibling already occupies the requested name.");
            }
            var actualName = NextCreatedFileName ?? name;
            StorageName.Validate(actualName);
            NextCreatedFileName = null;
            if (FindChild(directoryLocation, actualName) is not null)
            {
                throw new IOException("A sibling already occupies the provider-assigned name.");
            }
            LastMimeType = mimeType;
            var document = AddDocument(directoryLocation, actualName, false);
            document.WriteFailureAfterBytes = NewFileWriteFailureAfterBytes;
            return GetMetadata(document);
        }

        /// <inheritdoc />
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            StorageName.Validate(name);
            GetDirectory(directoryLocation);
            var existing = FindChild(directoryLocation, name);
            if (existing is not null)
            {
                if (!existing.IsDirectory)
                {
                    throw new IOException("A file already occupies the requested directory name.");
                }
                return GetMetadata(existing);
            }
            return GetMetadata(AddDocument(directoryLocation, name, true));
        }

        /// <inheritdoc />
        public FileSystemEntry Rename(string location, string name)
        {
            StorageName.Validate(name);
            var document = GetDocument(location);
            if (document.ParentLocation is null)
            {
                throw new NotSupportedException("The test provider's root cannot be renamed.");
            }
            return MoveWithin(document, document.ParentLocation, name);
        }

        /// <inheritdoc />
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            StorageName.Validate(name);
            var document = GetDocument(location);
            GetDirectory(directoryLocation);
            if (document.ParentLocation is null)
            {
                throw new NotSupportedException("The test provider's root cannot be moved.");
            }
            return MoveWithin(document, directoryLocation, name);
        }

        /// <inheritdoc />
        public void SetAttributes(string location, FileAttributes attributes)
        {
            // The test provider models a SAF provider, which reports no hidden or system flags.
            GetDocument(location);
        }

        /// <inheritdoc />
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            if (lastWriteTimeUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The modification time must be UTC.", nameof(lastWriteTimeUtc));
            }
            var document = GetDocument(location);
            if (!AcceptsTimestamps)
            {
                throw new NotSupportedException("The test provider ignores modification time updates.");
            }
            document.LastWriteTimeUtc = lastWriteTimeUtc;
        }

        /// <summary>Reparents or renames one document and issues a new opaque identity, as a provider may.</summary>
        /// <param name="document">The existing document to move.</param>
        /// <param name="directoryLocation">The destination parent's exact ID.</param>
        /// <param name="name">The requested display name in the destination parent.</param>
        /// <returns>Metadata for the document at its new identity.</returns>
        /// <exception cref="IOException">A different sibling already occupies the requested name.</exception>
        private FileSystemEntry MoveWithin(Document document, string directoryLocation, string name)
        {
            var collision = FindChild(directoryLocation, name);
            if (collision is not null && !ReferenceEquals(collision, document))
            {
                throw new IOException("Moving cannot replace an existing sibling.");
            }
            if (ReferenceEquals(collision, document))
            {
                return GetMetadata(document);
            }
            var previousLocation = document.Location;
            var newLocation = NewLocation();
            _documents.Remove(previousLocation);
            document.Location = newLocation;
            document.Name = name;
            document.ParentLocation = directoryLocation;
            _documents.Add(newLocation, document);
            foreach (var child in _documents.Values)
            {
                if (child.ParentLocation == previousLocation)
                {
                    child.ParentLocation = newLocation;
                }
            }
            return GetMetadata(document);
        }

        /// <inheritdoc />
        public void DeleteFile(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                return;
            }
            if (document.IsDirectory || document.OpenStreamCount != 0)
            {
                throw new IOException("A directory or open document cannot be deleted as a file.");
            }
            _documents.Remove(location);
            DeletedLocations.Add(location);
        }

        /// <inheritdoc />
        public void DeleteDirectory(string location, bool recursive = false)
        {
            GetDirectory(location);
            var children = new List<Document>();
            foreach (var document in _documents.Values)
            {
                if (document.ParentLocation == location)
                {
                    children.Add(document);
                }
            }
            if (!recursive && children.Count != 0)
            {
                throw new IOException("A non-empty directory requires recursive deletion.");
            }
            foreach (var child in children)
            {
                if (child.IsDirectory)
                {
                    DeleteDirectory(child.Location, true);
                }
                else
                {
                    DeleteFile(child.Location);
                }
            }
            _documents.Remove(location);
        }

        /// <summary>Configures deterministic source failures or cancellation without timing-dependent delays.</summary>
        /// <param name="location">The source document's exact URI.</param>
        /// <param name="failAfterBytes">The successful byte count before an I/O error, or null.</param>
        /// <param name="afterFirstRead">An optional callback invoked once after the stream's first successful read.</param>
        /// <exception cref="FileNotFoundException">The source document does not exist.</exception>
        /// <exception cref="IOException">The location identifies a directory.</exception>
        public void SetReadBehavior(string location, int? failAfterBytes = null, Action? afterFirstRead = null)
        {
            var document = GetFile(location);
            document.ReadFailureAfterBytes = failAfterBytes;
            document.AfterFirstRead = afterFirstRead;
        }

        /// <summary>Configures a deterministic destination failure after a partial write.</summary>
        /// <param name="location">The destination document's exact URI.</param>
        /// <param name="failAfterBytes">The successful byte count before an I/O error, or null.</param>
        /// <exception cref="FileNotFoundException">The destination document does not exist.</exception>
        /// <exception cref="IOException">The location identifies a directory.</exception>
        public void SetWriteFailure(string location, int? failAfterBytes)
        {
            GetFile(location).WriteFailureAfterBytes = failAfterBytes;
        }

        /// <summary>Models globally scoped identity shared by distinct grant URI aliases.</summary>
        /// <param name="location">The existing document's authoritative openable URI.</param>
        /// <param name="resourceId">The comparison-only resource identity, or null when unknown.</param>
        /// <exception cref="ArgumentException">The document location is invalid.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        public void SetResourceId(string location, string? resourceId)
        {
            GetDocument(location).ResourceId = resourceId;
        }
        /// <summary>Returns an entry with unknown file length and only the modification time the caller previously wrote.</summary>
        /// <param name="document">The document whose authoritative metadata is requested.</param>
        /// <returns>Metadata that deliberately does not expose stream length.</returns>
        private static FileSystemEntry GetMetadata(Document document)
        {
            return new FileSystemEntry(document.Location, document.Name, document.IsDirectory,
                lastWriteTimeUtc: document.LastWriteTimeUtc, resourceId: document.ResourceId);
        }

        /// <summary>Rejects synthetic child paths or decoded/re-encoded document IDs.</summary>
        /// <param name="location">The exact opaque URI supplied by the facade.</param>
        /// <exception cref="ArgumentException">The URI is not an encoded provider document ID.</exception>
        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrEmpty(location) || !location.StartsWith(LocationPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("The provider requires its authoritative content URI.", nameof(location));
            }
            var id = location.Substring(LocationPrefix.Length);
            if (id.Length == 0 || id.IndexOfAny(new[] { '/', '\\', '?', '#' }) >= 0)
            {
                throw new ArgumentException("Opaque document IDs must never be treated as child paths.", nameof(location));
            }
        }

        /// <summary>Propagates provider errors instead of translating them into missing entries.</summary>
        /// <exception cref="UnauthorizedAccessException">A configured access failure is active.</exception>
        /// <exception cref="IOException">A configured provider I/O failure is active.</exception>
        private void ThrowQueryError()
        {
            if (QueryException is not null)
            {
                throw QueryException;
            }
        }

        /// <summary>Issues a URI containing encoded colons, separators, query markers, and percent signs.</summary>
        /// <returns>An ID unrelated to the document's display name or parent ID.</returns>
        private string NewLocation()
        {
            _nextId++;
            return LocationPrefix + Uri.EscapeDataString($"volume:{_volume}/object#{_nextId}?grant=%2F");
        }

        /// <summary>Adds one document to the provider's identity map.</summary>
        /// <param name="parentLocation">The parent's independent ID, or null for the root.</param>
        /// <param name="name">The display name, never a path component.</param>
        /// <param name="isDirectory">Whether the document contains children instead of bytes.</param>
        /// <returns>The newly stored document.</returns>
        private Document AddDocument(string? parentLocation, string name, bool isDirectory)
        {
            var document = new Document(NewLocation(), parentLocation, name, isDirectory);
            _documents.Add(document.Location, document);
            return document;
        }

        /// <summary>Looks up a child by a separate parent relationship and ordinal display name.</summary>
        /// <param name="parentLocation">The parent's exact ID.</param>
        /// <param name="name">The requested single display name.</param>
        /// <returns>The child, or null when absent.</returns>
        private Document? FindChild(string parentLocation, string name)
        {
            foreach (var document in _documents.Values)
            {
                if (document.ParentLocation == parentLocation && document.Name == name)
                {
                    return document;
                }
            }
            return null;
        }

        /// <summary>Resolves an existing document by identity.</summary>
        /// <param name="location">The document's exact URI.</param>
        /// <returns>The existing document.</returns>
        /// <exception cref="ArgumentException">The URI is not authoritative.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        private Document GetDocument(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                throw new FileNotFoundException("The document does not exist.", location);
            }
            return document;
        }

        /// <summary>Resolves a binary document without accepting a directory.</summary>
        /// <param name="location">The file's exact URI.</param>
        /// <returns>The existing file document.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="IOException">The document is a directory.</exception>
        private Document GetFile(string location)
        {
            var document = GetDocument(location);
            if (document.IsDirectory)
            {
                throw new IOException("A directory has no binary stream.");
            }
            return document;
        }

        /// <summary>Resolves a directory without accepting a file or missing parent.</summary>
        /// <param name="location">The directory's exact URI.</param>
        /// <returns>The existing directory document.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="IOException">The document is a file.</exception>
        private Document GetDirectory(string location)
        {
            ValidateLocation(location);
            if (!_documents.TryGetValue(location, out var document))
            {
                throw new DirectoryNotFoundException("The parent document does not exist.");
            }
            if (!document.IsDirectory)
            {
                throw new IOException("The parent document is a file.");
            }
            return document;
        }

        /// <summary>Opens a counted, non-seekable stream and records deterministic disposal.</summary>
        /// <param name="document">The binary document to open.</param>
        /// <param name="write">Whether the stream writes instead of reads.</param>
        /// <param name="append">Whether a writable stream preserves its existing prefix.</param>
        /// <returns>A caller-owned stream which rejects length and position queries.</returns>
        private Stream OpenStream(Document document, bool write, bool append)
        {
            OpenStreamCount++;
            OpenedStreamCount++;
            if (write)
            {
                OpenedWriteStreamCount++;
            }
            document.OpenStreamCount++;
            return new DocumentStream(document, write, append, () =>
            {
                OpenStreamCount--;
                DisposedStreamCount++;
                document.OpenStreamCount--;
            });
        }

        /// <summary>Stores identity, display metadata, and independently configurable stream behavior.</summary>
        private sealed class Document
        {
            /// <summary>Gets or sets the current independent document ID.</summary>
            public string Location { get; set; }

            /// <summary>Gets or sets the separate parent relationship.</summary>
            public string? ParentLocation { get; set; }

            /// <summary>Gets or sets the comparison-only global identity shared by grant aliases.</summary>
            public string? ResourceId { get; set; }

            /// <summary>Gets or sets the display name.</summary>
            public string Name { get; set; }

            /// <summary>Gets whether this document contains children.</summary>
            public bool IsDirectory { get; }

            /// <summary>Gets or sets the committed byte representation.</summary>
            public byte[] Content { get; set; } = Array.Empty<byte>();

            /// <summary>Gets or sets the provider-owned modification time, or null while the provider reports none.</summary>
            public DateTime? LastWriteTimeUtc { get; set; }

            /// <summary>Gets or sets the source fault threshold.</summary>
            public int? ReadFailureAfterBytes { get; set; }

            /// <summary>Gets or sets the destination fault threshold.</summary>
            public int? WriteFailureAfterBytes { get; set; }

            /// <summary>Gets or sets the first-read cancellation callback.</summary>
            public Action? AfterFirstRead { get; set; }

            /// <summary>Gets or sets the number of open streams on this document.</summary>
            public int OpenStreamCount { get; set; }

            /// <summary>Initializes the identity and display metadata of a document.</summary>
            /// <param name="location">The independent document URI.</param>
            /// <param name="parentLocation">The independent parent URI, or null.</param>
            /// <param name="name">The document's display name.</param>
            /// <param name="isDirectory">Whether this is a directory.</param>
            public Document(string location, string? parentLocation, string name, bool isDirectory)
            {
                Location = location;
                ParentLocation = parentLocation;
                Name = name;
                IsDirectory = isDirectory;
            }
        }

        /// <summary>Exposes partial sequential I/O while making seek and metadata assumptions fail loudly.</summary>
        private sealed class DocumentStream : Stream
        {
            /// <summary>Stores bytes without exposing its seekable capabilities to callers.</summary>
            private readonly MemoryStream _buffer;

            /// <summary>Identifies the binary document backed by this stream.</summary>
            private readonly Document _document;

            /// <summary>Records whether this stream writes.</summary>
            private readonly bool _write;

            /// <summary>Updates provider disposal counts exactly once.</summary>
            private readonly Action _onDispose;

            /// <summary>Invokes a deterministic mid-operation cancellation callback once.</summary>
            private Action? _afterFirstRead;

            /// <summary>Counts successfully transferred bytes for fault injection.</summary>
            private int _transferredBytes;

            /// <summary>Records whether caller ownership has ended.</summary>
            private bool _disposed;

            /// <inheritdoc />
            public override bool CanRead
            {
                get
                {
                    return !_write && !_disposed;
                }
            }

            /// <inheritdoc />
            public override bool CanWrite
            {
                get
                {
                    return _write && !_disposed;
                }
            }

            /// <inheritdoc />
            public override bool CanSeek
            {
                get
                {
                    return false;
                }
            }

            /// <inheritdoc />
            public override long Length
            {
                get
                {
                    throw new NotSupportedException("Provider stream lengths are unknown.");
                }
            }

            /// <inheritdoc />
            public override long Position
            {
                get
                {
                    throw new NotSupportedException("Provider streams do not expose a position.");
                }
                set
                {
                    throw new NotSupportedException("Provider streams cannot seek.");
                }
            }

            /// <summary>Opens an independently counted sequential representation.</summary>
            /// <param name="document">The file being read or written.</param>
            /// <param name="write">Whether writing is allowed.</param>
            /// <param name="append">Whether existing bytes are retained before writes.</param>
            /// <param name="onDispose">The ownership-release callback.</param>
            public DocumentStream(Document document, bool write, bool append, Action onDispose)
            {
                _document = document;
                _write = write;
                _onDispose = onDispose;
                _afterFirstRead = document.AfterFirstRead;
                _buffer = new MemoryStream();
                if (!write || append)
                {
                    _buffer.Write(document.Content, 0, document.Content.Length);
                }
                if (!write)
                {
                    _buffer.Position = 0;
                }
                else if (!append)
                {
                    document.Content = Array.Empty<byte>();
                }
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_write)
                {
                    throw new NotSupportedException("The stream is write-only.");
                }
                var limit = Math.Min(count, 4096);
                if (_document.ReadFailureAfterBytes is int threshold)
                {
                    if (_transferredBytes >= threshold)
                    {
                        throw new IOException("Injected source read failure.");
                    }
                    limit = Math.Min(limit, threshold - _transferredBytes);
                }
                var read = _buffer.Read(buffer, offset, limit);
                _transferredBytes += read;
                if (read != 0 && _afterFirstRead is not null)
                {
                    var callback = _afterFirstRead;
                    _afterFirstRead = null;
                    callback();
                }
                return read;
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_write)
                {
                    throw new NotSupportedException("The stream is read-only.");
                }
                if (_document.WriteFailureAfterBytes is int threshold && count > threshold - _transferredBytes)
                {
                    var partialCount = Math.Max(0, threshold - _transferredBytes);
                    _buffer.Write(buffer, offset, partialCount);
                    _transferredBytes += partialCount;
                    throw new IOException("Injected destination write failure.");
                }
                _buffer.Write(buffer, offset, count);
                _transferredBytes += count;
            }

            /// <inheritdoc />
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = Read(buffer, offset, count);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(read);
            }

            /// <inheritdoc />
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public override void Flush()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_write)
                {
                    _document.Content = _buffer.ToArray();
                }
            }

            /// <inheritdoc />
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Flush();
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException("Provider streams cannot seek.");
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                throw new NotSupportedException("Provider streams cannot set length.");
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    if (_write)
                    {
                        _document.Content = _buffer.ToArray();
                    }
                    _disposed = true;
                    _buffer.Dispose();
                    _onDispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
