#nullable enable
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MajdataPlay.Tests
{
    /// <summary>Seeds opaque read-only fixtures without implementing chart discovery or parsing.</summary>
    /// <remarks>Only storage operations are doubled; the real directory handles, scanner and MajSimai parser execute.</remarks>
    internal sealed class ValidationFileSystem : IFileSystem
    {
        /// <summary>Stores entries by provider-assigned locations unrelated to their display names.</summary>
        private readonly Dictionary<string, FileSystemEntry> _entries = new Dictionary<string, FileSystemEntry>(StringComparer.Ordinal);
        /// <summary>Stores immediate children in deterministic fixture insertion order.</summary>
        private readonly Dictionary<string, List<FileSystemEntry>> _children = new Dictionary<string, List<FileSystemEntry>>(StringComparer.Ordinal);
        /// <summary>Stores only seeded file bytes; production operations cannot mutate them.</summary>
        private readonly Dictionary<string, byte[]> _contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>Counts actual stream openings by the original backend, including concurrent scanner workers.</summary>
        private readonly ConcurrentDictionary<string, int> _readCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Defines the backend's independent child-name comparison rules.</summary>
        private readonly StringComparison _nameComparison;
        /// <summary>Controls whether readable fixture streams deliberately reject seeking and length queries.</summary>
        private readonly bool _nonSeekableReads;
        /// <summary>Identifies this provider's opaque location namespace.</summary>
        private readonly string _locationPrefix = "content://song-storage-" + Guid.NewGuid().ToString("N") + "/node/";
        /// <summary>Allocates opaque location identifiers only while fixtures are being seeded.</summary>
        private int _nextId;
        /// <summary>Counts forbidden production mutation attempts.</summary>
        private int _mutationAttemptCount;
        /// <summary>Counts attempted production metadata, enumeration and stream access.</summary>
        private int _accessAttemptCount;

        /// <summary>Gets the already-created fixture root owned by this exact backend instance.</summary>
        public StorageDirectory Root { get; }
        /// <summary>Gets the number of forbidden production mutations, excluding explicit fixture seeding.</summary>
        public int MutationAttemptCount => Volatile.Read(ref _mutationAttemptCount);
        /// <summary>Gets the number of attempted provider reads and queries.</summary>
        public int AccessAttemptCount => Volatile.Read(ref _accessAttemptCount);
        /// <summary>Gets or sets whether reads must fail to detect unintended local access during an online refresh.</summary>
        public bool RejectAccess { get; set; }

        /// <summary>Creates an opaque fixture root with explicit backend name semantics.</summary>
        /// <param name="ignoreNameCase">Whether FindFile should accept case variants independently of resource tables.</param>
        /// <param name="nonSeekableReads">Whether reads must expose only sequential access, without Length or Position.</param>
        public ValidationFileSystem(bool ignoreNameCase = false, bool nonSeekableReads = false)
        {
            _nonSeekableReads = nonSeekableReads;
            _nameComparison = ignoreNameCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var location = _locationPrefix + _nextId++;
            _entries.Add(location, new FileSystemEntry(location, "Root", true));
            _children.Add(location, new List<FileSystemEntry>());
            Root = new StorageDirectory(this, location);
        }

        /// <summary>Seeds an already-created directory before production validation starts.</summary>
        /// <param name="parent">The directory in this backend receiving the fixture.</param>
        /// <param name="name">The display name, never used to construct its opaque location.</param>
        /// <returns>A real storage handle retaining this backend.</returns>
        /// <exception cref="ArgumentException">The parent is owned by another backend.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent is not a seeded directory.</exception>
        public StorageDirectory AddDirectory(StorageDirectory parent, string name)
        {
            var entry = AddEntry(parent, name, null);
            return new StorageDirectory(this, entry.Location);
        }

        /// <summary>Seeds a readable file without using the production creation API.</summary>
        /// <param name="parent">The seeded parent directory.</param>
        /// <param name="name">The independent file display name.</param>
        /// <param name="content">The UTF-8 fixture content, including genuine Simai markup for metadata files.</param>
        /// <returns>A real file handle retaining this backend.</returns>
        /// <exception cref="ArgumentException">The parent is owned by another backend.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent is not a seeded directory.</exception>
        public StorageFile AddFile(StorageDirectory parent, string name, string content = "placeholder")
        {
            var entry = AddEntry(parent, name, Encoding.UTF8.GetBytes(content));
            return new StorageFile(this, entry.Location);
        }

        /// <summary>Replaces fixture metadata after an awaited read to prove subsequent reads use the retained backend.</summary>
        /// <param name="file">The existing seeded file.</param>
        /// <param name="content">The replacement UTF-8 bytes.</param>
        /// <exception cref="ArgumentException">The file is owned by another backend.</exception>
        /// <exception cref="FileNotFoundException">The file was not seeded.</exception>
        public void ReplaceContents(StorageFile file, string content)
        {
            if (!ReferenceEquals(file.FileSystem, this))
            {
                throw new ArgumentException("The fixture file belongs to another backend.", nameof(file));
            }
            if (!_contents.ContainsKey(file.Location))
            {
                throw new FileNotFoundException("The fixture file was not seeded.", file.Location);
            }
            var bytes = Encoding.UTF8.GetBytes(content);
            _contents[file.Location] = bytes;
            var previous = _entries[file.Location];
            var replacement = new FileSystemEntry(previous.Location, previous.Name, false, bytes.LongLength,
                lastWriteTimeUtc: previous.LastWriteTimeUtc, creationTimeUtc: previous.CreationTimeUtc);
            _entries[file.Location] = replacement;
            foreach (var children in _children.Values)
            {
                var index = children.FindIndex(entry => entry.Location == file.Location);
                if (index >= 0)
                {
                    children[index] = replacement;
                    break;
                }
            }
        }

        /// <summary>Gets how often production opened a fixture file through this backend.</summary>
        /// <param name="location">The authoritative opaque file location.</param>
        /// <returns>The observed stream-opening count, or zero for an unread file.</returns>
        public int GetReadCount(string location)
        {
            return _readCounts.TryGetValue(location, out var count) ? count : 0;
        }

        /// <summary>Queries seeded metadata without consulting the globally registered provider.</summary>
        /// <param name="location">The opaque entry location.</param>
        /// <returns>The seeded entry, or null when absent.</returns>
        public FileSystemEntry? GetEntry(string location)
        {
            ObserveAccess();
            return _entries.TryGetValue(location, out var entry) ? entry : null;
        }

        /// <summary>Looks up immediate children using backend rules, not the song resource comparison.</summary>
        /// <param name="directoryLocation">The seeded parent location.</param>
        /// <param name="name">The requested child display name.</param>
        /// <returns>The matching entry, or null when the backend does not recognize the name.</returns>
        /// <exception cref="DirectoryNotFoundException">The parent is absent or is not a directory.</exception>
        /// <exception cref="IOException">Multiple entries match under the configured backend rules.</exception>
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            var matches = GetChildren(directoryLocation).Where(entry => string.Equals(entry.Name, name, _nameComparison)).ToArray();
            if (matches.Length > 1)
            {
                throw new IOException("The fixture has ambiguous child names.");
            }
            return matches.SingleOrDefault();
        }

        /// <summary>Returns seeded immediate children; the production scanner performs all discovery decisions.</summary>
        /// <param name="directoryLocation">The seeded directory location.</param>
        /// <returns>A child-entry snapshot without recursive traversal or chart filtering.</returns>
        /// <exception cref="DirectoryNotFoundException">The parent is absent or is not a directory.</exception>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            return GetChildren(directoryLocation).ToArray();
        }

        /// <summary>Opens the real UTF-8 fixture bytes and records which backend performed the read.</summary>
        /// <param name="location">The seeded file location.</param>
        /// <returns>A caller-owned, read-only stream with optional sequential-only semantics.</returns>
        /// <exception cref="FileNotFoundException">The location does not denote a seeded file.</exception>
        public Stream OpenRead(string location)
        {
            ObserveAccess();
            if (!_contents.TryGetValue(location, out var bytes))
            {
                throw new FileNotFoundException("No readable fixture exists at this location.", location);
            }
            _readCounts.AddOrUpdate(location, 1, (_, count) => count + 1);
            var stream = new MemoryStream(bytes, false);
            return _nonSeekableReads ? new ValidationSequentialReadStream(stream) : stream;
        }

        /// <summary>Rejects writes; cached metadata tests must not require backend mutation.</summary>
        /// <param name="location">The unused file location.</param>
        /// <param name="append">The unused append flag.</param>
        /// <returns>No stream; mutation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted a write.</exception>
        public Stream OpenWrite(string location, bool append = false)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects production file creation, including accidental download sidecars.</summary>
        /// <param name="directoryLocation">The unused parent location.</param>
        /// <param name="name">The unused file name.</param>
        /// <param name="mimeType">The unused content type.</param>
        /// <returns>No entry; mutation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted file creation.</exception>
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            throw RejectMutation();
        }

        /// <summary>Rejects production directory creation; the supplied song directory already exists.</summary>
        /// <param name="directoryLocation">The unused parent location.</param>
        /// <param name="name">The unused directory name.</param>
        /// <returns>No entry; mutation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted directory creation.</exception>
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects renaming seeded fixtures.</summary>
        /// <param name="location">The unused entry location.</param>
        /// <param name="name">The unused new name.</param>
        /// <returns>No entry; mutation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted a rename.</exception>
        public FileSystemEntry Rename(string location, string name)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects moving seeded fixtures.</summary>
        /// <param name="location">The unused source location.</param>
        /// <param name="directoryLocation">The unused destination directory.</param>
        /// <param name="name">The unused destination name.</param>
        /// <returns>No entry; mutation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Production attempted a move.</exception>
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects deleting seeded files.</summary>
        /// <param name="location">The unused file location.</param>
        /// <exception cref="NotSupportedException">Production attempted deletion.</exception>
        public void DeleteFile(string location)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects deleting seeded directories.</summary>
        /// <param name="location">The unused directory location.</param>
        /// <param name="recursive">The unused recursion flag.</param>
        /// <exception cref="NotSupportedException">Production attempted deletion.</exception>
        public void DeleteDirectory(string location, bool recursive = false)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects changing seeded attributes.</summary>
        /// <param name="location">The unused entry location.</param>
        /// <param name="attributes">The unused requested flags.</param>
        /// <exception cref="NotSupportedException">Production attempted an attribute change.</exception>
        public void SetAttributes(string location, FileAttributes attributes)
        {
            throw RejectMutation();
        }

        /// <summary>Rejects changing seeded timestamps.</summary>
        /// <param name="location">The unused entry location.</param>
        /// <param name="lastWriteTimeUtc">The unused UTC timestamp.</param>
        /// <exception cref="NotSupportedException">Production attempted a timestamp change.</exception>
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            throw RejectMutation();
        }

        /// <summary>Seeds one backend entry without embedding chart-discovery or name-matching policy.</summary>
        /// <param name="parent">The owning seeded parent directory.</param>
        /// <param name="name">The entry display name.</param>
        /// <param name="content">File bytes, or null for a directory.</param>
        /// <returns>The new provider-assigned entry metadata.</returns>
        /// <exception cref="ArgumentException">The parent is owned by another backend.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent is not a seeded directory.</exception>
        private FileSystemEntry AddEntry(StorageDirectory parent, string name, byte[]? content)
        {
            if (!ReferenceEquals(parent.FileSystem, this))
            {
                throw new ArgumentException("The fixture parent belongs to another backend.", nameof(parent));
            }
            var siblings = GetChildren(parent.Location);
            var location = _locationPrefix + _nextId++;
            var entry = new FileSystemEntry(location, name, content is null, content?.LongLength,
                lastWriteTimeUtc: DateTime.UnixEpoch, creationTimeUtc: DateTime.UnixEpoch);
            siblings.Add(entry);
            _entries.Add(location, entry);
            if (content is null)
            {
                _children.Add(location, new List<FileSystemEntry>());
            }
            else
            {
                _contents.Add(location, content);
            }
            return entry;
        }

        /// <summary>Requires a seeded directory without performing any chart filtering.</summary>
        /// <param name="location">The opaque directory location.</param>
        /// <returns>The directory's seeded immediate children.</returns>
        /// <exception cref="DirectoryNotFoundException">The location is not a seeded directory.</exception>
        private List<FileSystemEntry> GetChildren(string location)
        {
            ObserveAccess();
            if (!_children.TryGetValue(location, out var children))
            {
                throw new DirectoryNotFoundException("The original backend does not own directory " + location);
            }
            return children;
        }

        /// <summary>Observes provider reads and rejects access while the local source must remain untouched.</summary>
        /// <exception cref="IOException">An online refresh unexpectedly queried the local provider.</exception>
        private void ObserveAccess()
        {
            Interlocked.Increment(ref _accessAttemptCount);
            if (RejectAccess)
            {
                throw new IOException("The online refresh attempted local provider access.");
            }
        }

        /// <summary>Records a forbidden mutation even if production catches the resulting exception.</summary>
        /// <returns>The explicit boundary failure.</returns>
        private NotSupportedException RejectMutation()
        {
            Interlocked.Increment(ref _mutationAttemptCount);
            return new NotSupportedException("Fixture backend mutations are outside the cached-metadata harness.");
        }
    }
}
