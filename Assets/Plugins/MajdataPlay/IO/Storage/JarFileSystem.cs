#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Reads ZIP and APK entries through local <c>jar:file:///archive.apk!/entry</c> locations.</summary>
    /// <remarks>
    /// Archive names are case-sensitive and directories may be implicit. Every operation opens a fresh archive;
    /// returned read streams retain their own archive until disposal. Archives are never modified or unpacked implicitly.
    /// </remarks>
    internal sealed class JarFileSystem : IFileSystem
    {
        /// <summary>The bounded buffer size used when opening archive files.</summary>
        private const int BufferSize = 65536;

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">The archive does not contain valid ZIP data.</exception>
        public FileSystemEntry? GetEntry(string location)
        {
            var parsed = ParseLocation(location);
            using var archive = TryOpenArchive(parsed.ArchivePath);
            if (archive is null)
            {
                return null;
            }
            var entries = IndexArchive(archive);
            return entries.TryGetValue(parsed.EntryPath, out var entry) ? CreateEntry(parsed, parsed.EntryPath, entry) : null;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">The archive does not contain valid ZIP data.</exception>
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            StorageName.Validate(name);
            var parsed = ParseLocation(directoryLocation);
            using var archive = RequireArchive(parsed, true);
            var entries = IndexArchive(archive);
            RequireDirectory(entries, parsed.EntryPath);
            var childPath = parsed.EntryPath.Length == 0 ? name : parsed.EntryPath + "/" + name;
            return entries.TryGetValue(childPath, out var entry) ? CreateEntry(parsed, childPath, entry) : null;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">The archive does not contain valid ZIP data.</exception>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            var parsed = ParseLocation(directoryLocation);
            using var archive = RequireArchive(parsed, true);
            var entries = IndexArchive(archive);
            RequireDirectory(entries, parsed.EntryPath);
            var prefix = parsed.EntryPath.Length == 0 ? string.Empty : parsed.EntryPath + "/";
            var result = new List<FileSystemEntry>();
            foreach (var pair in entries)
            {
                if (pair.Key.Length > prefix.Length && pair.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                    pair.Key.IndexOf('/', prefix.Length) < 0)
                {
                    result.Add(CreateEntry(parsed, pair.Key, pair.Value));
                }
            }
            return result;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">The archive or the selected compressed entry is invalid.</exception>
        public Stream OpenRead(string location)
        {
            var parsed = ParseLocation(location);
            var archive = RequireArchive(parsed, false);
            try
            {
                var entries = IndexArchive(archive);
                if (!entries.TryGetValue(parsed.EntryPath, out var entry))
                {
                    throw new FileNotFoundException("The archive file entry does not exist.", location);
                }
                if (entry.IsDirectory || entry.Entry is null)
                {
                    throw new NotSupportedException("An archive directory cannot be opened for reading.");
                }
                return new ArchiveReadStream(entry.Entry.Open(), archive);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public Stream OpenWrite(string location, bool append = false)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public FileSystemEntry Rename(string location, string name)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public void DeleteFile(string location)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public void DeleteDirectory(string location, bool recursive = false)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public void SetAttributes(string location, FileAttributes attributes)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <inheritdoc />
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            throw new NotSupportedException("Archive entries are read-only.");
        }

        /// <summary>Validates a jar location and returns its canonical, escaped spelling without opening the archive.</summary>
        /// <param name="location">An absolute jar location backed by a local file URI.</param>
        /// <returns>The canonical jar URI, retaining <c>!/</c> for the archive root.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The URI or entry path is malformed or ambiguous.</exception>
        /// <exception cref="NotSupportedException">The archive is nested or is not a local file.</exception>
        /// <exception cref="IOException">The local archive path cannot be normalized.</exception>
        internal static string NormalizeLocation(string location)
        {
            var parsed = ParseLocation(location);
            return BuildLocation(parsed.ArchiveUri, parsed.EntryPath);
        }

        /// <summary>Separates and validates the local archive URI and decoded archive entry path.</summary>
        /// <param name="location">The jar URI supplied by the caller.</param>
        /// <returns>The normalized archive path, URI, and once-decoded entry path.</returns>
        /// <exception cref="ArgumentNullException">The location is null.</exception>
        /// <exception cref="ArgumentException">The jar URI or entry path is malformed.</exception>
        /// <exception cref="NotSupportedException">The archive is nested or is not a local file.</exception>
        /// <exception cref="IOException">The local archive path cannot be normalized.</exception>
        private static JarLocation ParseLocation(string location)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            if (!location.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("An absolute jar URI is required.", nameof(location));
            }
            var separator = location.IndexOf("!/", 4, StringComparison.Ordinal);
            if (separator < 0 || location.IndexOf('\\') >= 0 || location.IndexOf('?') >= 0 || location.IndexOf('#') >= 0)
            {
                throw new ArgumentException("A jar URI without a query, fragment, or backslash is required.", nameof(location));
            }
            var archiveText = location.Substring(4, separator - 4);
            if (!Uri.TryCreate(archiveText, UriKind.Absolute, out var archiveUri))
            {
                throw new ArgumentException("An absolute archive URI is required.", nameof(location));
            }
            if (!archiveUri.IsFile)
            {
                throw new NotSupportedException("Only jar URIs backed by one local file archive are supported.");
            }
            if (!archiveText.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                archiveText.Length <= "file://".Length || archiveUri.IsUnc ||
                (archiveUri.Host.Length != 0 && !string.Equals(archiveUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("An absolute local file URI is required for the archive.", nameof(location));
            }
            ValidateEscapes(archiveText);
            var archivePath = FileSystem.NormalizeLocalPath(archiveText);
            var entryText = location.Substring(separator + 2);
            if (entryText.EndsWith("/", StringComparison.Ordinal))
            {
                entryText = entryText.Substring(0, entryText.Length - 1);
                if (entryText.Length == 0)
                {
                    throw new ArgumentException("An archive entry path cannot contain empty segments.", nameof(location));
                }
            }
            var segments = entryText.Length == 0 ? Array.Empty<string>() : entryText.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                ValidateEscapes(segments[i]);
                segments[i] = Uri.UnescapeDataString(segments[i]);
                StorageName.Validate(segments[i]);
            }
            // Reusing the escaped URI avoids treating literal percent sequences in LocalPath as URI escapes again.
            var canonicalArchiveUri = archiveUri.AbsoluteUri.Replace("!", "%21");
            return new JarLocation(archivePath, canonicalArchiveUri, string.Join("/", segments));
        }

        /// <summary>Rejects malformed percent escapes before URI decoding can silently retain them.</summary>
        /// <param name="value">The escaped archive URI or one entry segment.</param>
        /// <exception cref="ArgumentException">A percent escape is incomplete or contains a nonhexadecimal digit.</exception>
        private static void ValidateEscapes(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '%')
                {
                    continue;
                }
                if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))
                {
                    throw new ArgumentException("URI percent escapes must contain two hexadecimal digits.", nameof(value));
                }
                i += 2;
            }
        }

        /// <summary>Builds an authoritative jar location without interpreting literal percent characters in entry names.</summary>
        /// <param name="archiveUri">The normalized, escaped local archive URI.</param>
        /// <param name="entryPath">The decoded archive-relative path with slash separators.</param>
        /// <returns>The escaped jar URI for the requested archive entry.</returns>
        private static string BuildLocation(string archiveUri, string entryPath)
        {
            var result = new StringBuilder("jar:").Append(archiveUri).Append("!/");
            if (entryPath.Length != 0)
            {
                var segments = entryPath.Split('/');
                for (var i = 0; i < segments.Length; i++)
                {
                    if (i != 0)
                    {
                        result.Append('/');
                    }
                    result.Append(Uri.EscapeDataString(segments[i]));
                }
            }
            return result.ToString();
        }

        /// <summary>Opens a fresh archive, distinguishing an absent file from denied access or corrupt ZIP data.</summary>
        /// <param name="archivePath">The normalized absolute local archive path.</param>
        /// <returns>The caller-owned archive, or null if the archive file does not exist.</returns>
        /// <exception cref="UnauthorizedAccessException">Reading the archive was denied.</exception>
        /// <exception cref="IOException">The archive cannot be opened.</exception>
        /// <exception cref="InvalidDataException">The archive does not contain valid ZIP data.</exception>
        private static ZipArchive? TryOpenArchive(string archivePath)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            try
            {
                return new ZipArchive(stream, ZipArchiveMode.Read, false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Opens an existing archive using an absence exception appropriate for the requested operation.</summary>
        /// <param name="location">The parsed caller location.</param>
        /// <param name="directory">Whether a directory operation requires the archive.</param>
        /// <returns>The caller-owned existing archive.</returns>
        /// <exception cref="FileNotFoundException">A file operation targets a missing archive.</exception>
        /// <exception cref="DirectoryNotFoundException">A directory operation targets a missing archive.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading the archive was denied.</exception>
        /// <exception cref="IOException">The archive cannot be opened.</exception>
        /// <exception cref="InvalidDataException">The archive does not contain valid ZIP data.</exception>
        private static ZipArchive RequireArchive(JarLocation location, bool directory)
        {
            var archive = TryOpenArchive(location.ArchivePath);
            if (archive is not null)
            {
                return archive;
            }
            if (directory)
            {
                throw new DirectoryNotFoundException("The containing archive does not exist.");
            }
            throw new FileNotFoundException("The containing archive does not exist.", location.ArchivePath);
        }

        /// <summary>Indexes safe archive names and synthesizes omitted parent directory records.</summary>
        /// <param name="archive">The open archive whose entries remain alive while the index is used.</param>
        /// <returns>A case-sensitive index containing the archive root and valid entries.</returns>
        /// <exception cref="IOException">Duplicate records or file/directory collisions make entry resolution ambiguous.</exception>
        /// <exception cref="InvalidDataException">The ZIP directory cannot be read.</exception>
        /// <remarks>Unsafe ZIP paths are ignored rather than normalized into aliases of valid entries.</remarks>
        private static Dictionary<string, ArchiveNode> IndexArchive(ZipArchive archive)
        {
            var entries = new Dictionary<string, ArchiveNode>(StringComparer.Ordinal)
            {
                { string.Empty, new ArchiveNode(true, null) }
            };
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName;
                var isDirectory = path.EndsWith("/", StringComparison.Ordinal);
                if (isDirectory)
                {
                    path = path.Substring(0, path.Length - 1);
                }
                if (!IsSafeEntryPath(path))
                {
                    continue;
                }
                var separator = path.IndexOf('/');
                while (separator >= 0)
                {
                    AddNode(entries, path.Substring(0, separator), true, null);
                    separator = path.IndexOf('/', separator + 1);
                }
                AddNode(entries, path, isDirectory, entry);
            }
            return entries;
        }

        /// <summary>Checks an undecoded ZIP path without applying URI escape rules to literal ZIP names.</summary>
        /// <param name="path">The ZIP path after removing one optional directory terminator.</param>
        /// <returns>Whether every segment is safe and can be represented by the storage child APIs.</returns>
        private static bool IsSafeEntryPath(string path)
        {
            if (path.Length == 0 || path.IndexOf('\\') >= 0 || path.IndexOf('\0') >= 0)
            {
                return false;
            }
            var start = 0;
            while (start <= path.Length)
            {
                var end = path.IndexOf('/', start);
                if (end < 0)
                {
                    end = path.Length;
                }
                var length = end - start;
                if (length == 0 || (length == 1 && path[start] == '.') ||
                    (length == 2 && path[start] == '.' && path[start + 1] == '.'))
                {
                    return false;
                }
                if (end == path.Length)
                {
                    return true;
                }
                start = end + 1;
            }
            return false;
        }

        /// <summary>Adds an archive entry or merges an explicit directory with an existing implicit parent.</summary>
        /// <param name="entries">The case-sensitive archive index being populated.</param>
        /// <param name="path">The safe, decoded archive-relative path.</param>
        /// <param name="isDirectory">Whether this record denotes a directory.</param>
        /// <param name="entry">The explicit ZIP record, or null for an implicit directory.</param>
        /// <exception cref="IOException">The archive contains an ambiguous duplicate or file/directory collision.</exception>
        private static void AddNode(Dictionary<string, ArchiveNode> entries, string path, bool isDirectory, ZipArchiveEntry? entry)
        {
            if (!entries.TryGetValue(path, out var existing))
            {
                entries.Add(path, new ArchiveNode(isDirectory, entry));
                return;
            }
            if (!existing.IsDirectory || !isDirectory || (existing.Entry is not null && entry is not null))
            {
                throw new IOException("The archive contains ambiguous duplicate names or a file/directory collision.");
            }
            if (entry is not null)
            {
                existing.Entry = entry;
            }
        }

        /// <summary>Checks that a validated archive-relative path resolves to a directory.</summary>
        /// <param name="entries">The current archive's case-sensitive index.</param>
        /// <param name="path">The decoded directory path, or empty for the archive root.</param>
        /// <exception cref="DirectoryNotFoundException">The directory path does not exist.</exception>
        /// <exception cref="IOException">The path resolves to a file.</exception>
        private static void RequireDirectory(Dictionary<string, ArchiveNode> entries, string path)
        {
            if (!entries.TryGetValue(path, out var entry))
            {
                throw new DirectoryNotFoundException("The archive directory does not exist.");
            }
            if (!entry.IsDirectory)
            {
                throw new IOException("The archive entry is a file, not a directory.");
            }
        }

        /// <summary>Copies archive metadata into a snapshot with an escaped authoritative location.</summary>
        /// <param name="location">The parsed location containing the canonical archive URI.</param>
        /// <param name="path">The decoded archive-relative entry path.</param>
        /// <param name="node">The indexed explicit file/directory or synthesized directory.</param>
        /// <returns>The entry metadata, with unknown timestamps for implicit directories.</returns>
        private static FileSystemEntry CreateEntry(JarLocation location, string path, ArchiveNode node)
        {
            var separator = path.LastIndexOf('/');
            var name = path.Length == 0 ? Path.GetFileName(location.ArchivePath) : path.Substring(separator + 1);
            return new FileSystemEntry(BuildLocation(location.ArchiveUri, path), name, node.IsDirectory,
                node.IsDirectory ? null : node.Entry?.Length, node.Entry?.LastWriteTime.UtcDateTime);
        }

        /// <summary>Stores the canonical archive location and a once-decoded archive-relative entry path.</summary>
        private readonly struct JarLocation
        {
            /// <summary>Gets the absolute local path used to open the archive.</summary>
            public string ArchivePath { get; }

            /// <summary>Gets the normalized file URI used to build authoritative jar locations.</summary>
            public string ArchiveUri { get; }

            /// <summary>Gets the decoded ZIP path, or empty for the archive root.</summary>
            public string EntryPath { get; }

            /// <summary>Initializes the validated pieces of a jar location.</summary>
            /// <param name="archivePath">The normalized absolute local archive path.</param>
            /// <param name="archiveUri">The normalized and escaped local file URI.</param>
            /// <param name="entryPath">The decoded archive-relative path.</param>
            public JarLocation(string archivePath, string archiveUri, string entryPath)
            {
                ArchivePath = archivePath;
                ArchiveUri = archiveUri;
                EntryPath = entryPath;
            }
        }

        /// <summary>Represents one file or explicit/implicit directory in the currently open archive.</summary>
        private sealed class ArchiveNode
        {
            /// <summary>Gets whether this node represents a directory.</summary>
            public bool IsDirectory { get; }

            /// <summary>Gets or sets the explicit ZIP record, or null for an implicit directory.</summary>
            public ZipArchiveEntry? Entry { get; set; }

            /// <summary>Initializes the node independently of the archive-relative path used as its index key.</summary>
            /// <param name="isDirectory">Whether this node represents a directory.</param>
            /// <param name="entry">The explicit ZIP record, or null for an implicit directory.</param>
            public ArchiveNode(bool isDirectory, ZipArchiveEntry? entry)
            {
                IsDirectory = isDirectory;
                Entry = entry;
            }
        }

        /// <summary>Owns a readable ZIP entry stream and the containing archive for exactly the same lifetime.</summary>
        private sealed class ArchiveReadStream : Stream
        {
            /// <summary>The entry stream used for bounded reads and supported seek operations.</summary>
            private readonly Stream _entry;

            /// <summary>The archive that owns the underlying local file handle.</summary>
            private readonly ZipArchive _archive;

            /// <summary>Tracks disposal so capabilities and repeated disposal follow stream conventions.</summary>
            private bool _disposed;

            /// <inheritdoc />
            public override bool CanRead => !_disposed && _entry.CanRead;

            /// <inheritdoc />
            public override bool CanSeek => !_disposed && _entry.CanSeek;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => _entry.Length;

            /// <inheritdoc />
            public override long Position
            {
                get => _entry.Position;
                set => _entry.Position = value;
            }

            /// <summary>Takes ownership of an opened entry stream and the archive that must outlive it.</summary>
            /// <param name="entry">The readable ZIP entry stream.</param>
            /// <param name="archive">The archive owning the local file handle.</param>
            public ArchiveReadStream(Stream entry, ZipArchive archive)
            {
                _entry = entry;
                _archive = archive;
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                return _entry.Read(buffer, offset, count);
            }

            /// <inheritdoc />
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return _entry.ReadAsync(buffer, offset, count, cancellationToken);
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                return _entry.Seek(offset, origin);
            }

            /// <inheritdoc />
            public override void Flush()
            {
                _entry.Flush();
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                throw new NotSupportedException("Archive entries are read-only.");
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException("Archive entries are read-only.");
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing && !_disposed)
                    {
                        _disposed = true;
                        try
                        {
                            _entry.Dispose();
                        }
                        finally
                        {
                            _archive.Dispose();
                        }
                    }
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}
