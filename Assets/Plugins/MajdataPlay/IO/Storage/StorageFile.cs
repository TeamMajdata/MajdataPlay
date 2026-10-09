#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Represents a file location owned by a local filesystem or an opaque storage provider.</summary>
    /// <remarks>
    /// Async operations offload synchronous provider queries, stream opening, and disposal to the thread pool.
    /// Cancellation is cooperative; a provider's blocking synchronous calls cannot be forcibly interrupted.
    /// </remarks>
    public sealed class StorageFile
    {
        /// <summary>The fixed byte buffer size used for streaming and cancellation checkpoints.</summary>
        private const int BufferSize = 65536;

        /// <summary>The bounded character buffer size used by text operations.</summary>
        private const int TextBufferSize = 4096;

        /// <summary>The default UTF-8 encoding, which does not emit a byte order mark.</summary>
        private static readonly Encoding s_defaultEncoding = new UTF8Encoding(false);

        /// <summary>Gets the backend responsible for this file location.</summary>
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

        /// <summary>Gets whether the location currently denotes an existing file rather than a directory.</summary>
        /// <exception cref="UnauthorizedAccessException">Querying the entry was denied.</exception>
        /// <exception cref="IOException">The backend could not query the entry.</exception>
        public bool Exists
        {
            get
            {
                return Entry is { IsDirectory: false };
            }
        }

        /// <summary>Creates a file handle without opening or creating the file.</summary>
        /// <param name="fileSystem">The backend owning the location.</param>
        /// <param name="location">A local path or file URI for a local backend, otherwise an opaque provider location.</param>
        /// <exception cref="ArgumentNullException">The backend or location is null.</exception>
        /// <exception cref="ArgumentException">The location is empty or the local path is invalid.</exception>
        /// <exception cref="NotSupportedException">A local backend was given a non-file URI.</exception>
        /// <exception cref="IOException">The local path cannot be normalized.</exception>
        public StorageFile(IFileSystem fileSystem, string location)
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
                throw new ArgumentException("A file location is required.", nameof(location));
            }
            FileSystem = fileSystem;
            Location = fileSystem is LocalFileSystem ? global::MajdataPlay.IO.Storage.FileSystem.NormalizeLocalPath(location) : location;
        }

        /// <summary>Opens the existing file for reading without requiring a seekable stream.</summary>
        /// <returns>A caller-owned readable stream.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry has no readable binary representation.</exception>
        /// <exception cref="IOException">Opening the stream failed.</exception>
        public Stream OpenRead()
        {
            return FileSystem.OpenRead(Location);
        }

        /// <summary>Opens an existing file for writing; creation must be performed separately.</summary>
        /// <param name="append">Whether to append rather than truncate existing content.</param>
        /// <returns>A caller-owned writable stream, which may not support seeking.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support the requested mode.</exception>
        /// <exception cref="IOException">Opening the stream failed.</exception>
        public Stream OpenWrite(bool append = false)
        {
            return FileSystem.OpenWrite(Location, append);
        }

        /// <summary>Reads all file bytes without querying the stream's length or seeking.</summary>
        /// <returns>The file's complete contents in a new byte array.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry is not readable.</exception>
        /// <exception cref="IOException">Reading failed.</exception>
        public byte[] ReadAllBytes()
        {
            using var input = OpenRead();
            using var output = new MemoryStream();
            CopyStreams(input, output, default);
            return output.ToArray();
        }

        /// <summary>Reads the complete text, detecting a byte order mark when present.</summary>
        /// <param name="encoding">The fallback encoding, or null for UTF-8.</param>
        /// <returns>The file's decoded text.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry is not readable.</exception>
        /// <exception cref="DecoderFallbackException">The requested encoding rejects the file's bytes.</exception>
        /// <exception cref="IOException">Reading failed.</exception>
        public string ReadAllText(Encoding? encoding = null)
        {
            using var stream = OpenRead();
            using var reader = new StreamReader(stream, encoding ?? s_defaultEncoding, true, TextBufferSize, true);
            return reader.ReadToEnd();
        }

        /// <summary>Replaces an existing file's content with the supplied bytes.</summary>
        /// <param name="bytes">The bytes to write.</param>
        /// <exception cref="ArgumentNullException">The byte array is null.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The entry cannot be replaced.</exception>
        /// <exception cref="IOException">Writing failed.</exception>
        public void WriteAllBytes(byte[] bytes)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            using var stream = OpenWrite();
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Replaces an existing file's content with encoded text.</summary>
        /// <param name="text">The text to write.</param>
        /// <param name="encoding">The encoding, or null for UTF-8 without a byte order mark.</param>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The entry cannot be replaced.</exception>
        /// <exception cref="EncoderFallbackException">The requested encoding rejects the text.</exception>
        /// <exception cref="IOException">Writing failed.</exception>
        public void WriteAllText(string text, Encoding? encoding = null)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            using var stream = OpenWrite();
            using var writer = new StreamWriter(stream, encoding ?? s_defaultEncoding, TextBufferSize, true);
            writer.Write(text);
        }

        /// <summary>Reads all bytes asynchronously, offloading blocking provider operations.</summary>
        /// <param name="cancellationToken">The token checked before opening and between stream transfers.</param>
        /// <returns>A task returning the complete contents in a new byte array.</returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry is not readable.</exception>
        /// <exception cref="IOException">Reading failed.</exception>
        public Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var input = OpenRead();
                using var output = new MemoryStream();
                await CopyStreamsAsync(input, output, cancellationToken).ConfigureAwait(false);
                return output.ToArray();
            }, cancellationToken);
        }

        /// <summary>Reads all text asynchronously, detecting a byte order mark and offloading provider operations.</summary>
        /// <param name="encoding">The fallback encoding, or null for UTF-8.</param>
        /// <param name="cancellationToken">The token checked before opening and between text reads.</param>
        /// <returns>A task returning the decoded file text.</returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The entry is not readable.</exception>
        /// <exception cref="DecoderFallbackException">The requested encoding rejects the file's bytes.</exception>
        /// <exception cref="IOException">Reading failed.</exception>
        public Task<string> ReadAllTextAsync(Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = OpenRead();
                using var reader = new StreamReader(stream, encoding ?? s_defaultEncoding, true, TextBufferSize, true);
                var buffer = new char[TextBufferSize];
                var text = new StringBuilder();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count == 0)
                    {
                        return text.ToString();
                    }
                    text.Append(buffer, 0, count);
                }
            }, cancellationToken);
        }

        /// <summary>Replaces an existing file's bytes asynchronously, offloading blocking provider operations.</summary>
        /// <param name="bytes">The bytes to write.</param>
        /// <param name="cancellationToken">The token checked before truncation and between bounded writes.</param>
        /// <returns>A task completing after writing and closing the stream.</returns>
        /// <exception cref="ArgumentNullException">The byte array is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The entry cannot be replaced.</exception>
        /// <exception cref="IOException">Writing failed.</exception>
        public Task WriteAllBytesAsync(byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = OpenWrite();
                for (var offset = 0; offset < bytes.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = Math.Min(BufferSize, bytes.Length - offset);
                    await stream.WriteAsync(bytes, offset, count, cancellationToken).ConfigureAwait(false);
                    offset += count;
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
        }

        /// <summary>Replaces an existing file's text asynchronously, offloading blocking provider operations.</summary>
        /// <param name="text">The text to write.</param>
        /// <param name="encoding">The encoding, or null for UTF-8 without a byte order mark.</param>
        /// <param name="cancellationToken">The token checked before truncation and between bounded writes.</param>
        /// <returns>A task completing after writing and closing the stream.</returns>
        /// <exception cref="ArgumentNullException">The text is null.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The entry cannot be replaced.</exception>
        /// <exception cref="EncoderFallbackException">The requested encoding rejects the text.</exception>
        /// <exception cref="IOException">Writing failed.</exception>
        public Task WriteAllTextAsync(string text, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = OpenWrite();
                using var writer = new StreamWriter(stream, encoding ?? s_defaultEncoding, TextBufferSize, true);
                for (var offset = 0; offset < text.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = Math.Min(TextBufferSize, text.Length - offset);
                    await writer.WriteAsync(text.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                    offset += count;
                }
                cancellationToken.ThrowIfCancellationRequested();
                await writer.FlushAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
        }

        /// <summary>Deletes this file; a missing file is a no-op and directories are never deleted.</summary>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support deletion.</exception>
        /// <exception cref="IOException">The entry is a directory or deletion failed.</exception>
        public void Delete()
        {
            FileSystem.DeleteFile(Location);
        }

        /// <summary>Renames the file within its parent without changing this handle or overwriting a sibling.</summary>
        /// <param name="name">The new single file name.</param>
        /// <returns>A new handle using the backend's renamed file location.</returns>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="FileNotFoundException">This location does not denote an existing file.</exception>
        /// <exception cref="UnauthorizedAccessException">Renaming was denied.</exception>
        /// <exception cref="NotSupportedException">The backend does not support renaming.</exception>
        /// <exception cref="IOException">A sibling exists or renaming failed.</exception>
        public StorageFile Rename(string name)
        {
            StorageName.Validate(name);
            if (!Exists)
            {
                throw new FileNotFoundException("The storage file does not exist.", Location);
            }
            var entry = FileSystem.Rename(Location, name);
            return new StorageFile(FileSystem, entry.Location);
        }

        /// <summary>Copies this file to an immediate child of another directory using a bounded stream buffer.</summary>
        /// <param name="destination">The destination directory, which may belong to another provider.</param>
        /// <param name="name">One destination child name, not a path or URI.</param>
        /// <param name="overwrite">Whether an existing destination file may be truncated and replaced.</param>
        /// <param name="cancellationToken">The token checked before side effects and between stream transfers.</param>
        /// <returns>A handle using the destination backend's authoritative file location.</returns>
        /// <remarks>
        /// Copying is not atomic. Failures may leave an overwritten file partially written.
        /// Only a newly created destination is deleted on failure, and that cleanup is best-effort.
        /// Self-copy checks use reported global resource IDs or normalized local paths, ignoring case on Windows.
        /// Hard-link and other symbolic-link aliases are not reliably detected; overwriting a symbolic-link destination is rejected.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The destination is null.</exception>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">A backend denied access.</exception>
        /// <exception cref="NotSupportedException">A backend does not support a required operation.</exception>
        /// <exception cref="IOException">The destination is the source, conflicts with an entry, or copying failed.</exception>
        public StorageFile CopyTo(StorageDirectory destination, string name, bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            StorageName.Validate(name);
            StorageFile? newlyCreated = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceEntry = GetCopySource();
                using var input = OpenRead();
                cancellationToken.ThrowIfCancellationRequested();
                var target = PrepareCopyDestination(destination, name, overwrite, sourceEntry, out var created);
                if (created)
                {
                    newlyCreated = target;
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var output = target.OpenWrite();
                CopyStreams(input, output, cancellationToken);
                output.Flush();
                cancellationToken.ThrowIfCancellationRequested();
                return target;
            }
            catch
            {
                TryDeleteNewDestination(newlyCreated);
                throw;
            }
        }

        /// <summary>Copies this file asynchronously across providers, offloading blocking provider operations.</summary>
        /// <param name="destination">The destination directory, which may belong to another provider.</param>
        /// <param name="name">One destination child name, not a path or URI.</param>
        /// <param name="overwrite">Whether an existing destination file may be truncated and replaced.</param>
        /// <param name="cancellationToken">The token checked before side effects and between stream transfers.</param>
        /// <returns>A task returning a handle with the destination backend's authoritative file location.</returns>
        /// <remarks>
        /// Streams need not support seeking or length queries. Copying is not atomic.
        /// On failure only a newly created destination is eligible for best-effort deletion.
        /// Self-copy checks use reported global resource IDs or normalized local paths, ignoring case on Windows.
        /// Hard-link and other symbolic-link aliases are not reliably detected; overwriting a symbolic-link destination is rejected.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The destination is null.</exception>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
        /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">A backend denied access.</exception>
        /// <exception cref="NotSupportedException">A backend does not support a required operation.</exception>
        /// <exception cref="IOException">The destination is the source, conflicts with an entry, or copying failed.</exception>
        public Task<StorageFile> CopyToAsync(StorageDirectory destination, string name, bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            StorageName.Validate(name);
            return Task.Run(async () =>
            {
                StorageFile? newlyCreated = null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceEntry = GetCopySource();
                    using var input = OpenRead();
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = PrepareCopyDestination(destination, name, overwrite, sourceEntry, out var created);
                    if (created)
                    {
                        newlyCreated = target;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    using var output = target.OpenWrite();
                    await CopyStreamsAsync(input, output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return target;
                }
                catch
                {
                    TryDeleteNewDestination(newlyCreated);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>Queries authoritative metadata for an existing copy source file.</summary>
        /// <returns>The current source file metadata.</returns>
        /// <exception cref="FileNotFoundException">This location does not denote an existing file.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying the source was denied.</exception>
        /// <exception cref="IOException">The source could not be queried.</exception>
        private FileSystemEntry GetCopySource()
        {
            var entry = Entry;
            if (entry is null || entry.IsDirectory)
            {
                throw new FileNotFoundException("The storage source file does not exist.", Location);
            }
            return entry;
        }

        /// <summary>Finds or creates a destination while rejecting conflicts and known self-copy aliases.</summary>
        /// <param name="destination">The existing destination directory.</param>
        /// <param name="name">The validated single child name.</param>
        /// <param name="overwrite">Whether an existing file may be replaced.</param>
        /// <param name="sourceEntry">The authoritative source metadata.</param>
        /// <param name="created">Whether the returned handle belongs to a newly created file.</param>
        /// <returns>A destination handle using the backend's actual entry location.</returns>
        /// <exception cref="ArgumentException">The backend rejects the name.</exception>
        /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied access.</exception>
        /// <exception cref="NotSupportedException">The backend does not support creation.</exception>
        /// <exception cref="IOException">The destination conflicts, is the source, or preparation failed.</exception>
        private StorageFile PrepareCopyDestination(StorageDirectory destination, string name, bool overwrite,
            FileSystemEntry sourceEntry, out bool created)
        {
            created = false;
            var entry = destination.FileSystem.GetChildEntry(destination.Location, name);
            if (entry is not null)
            {
                if (entry.IsDirectory || !overwrite)
                {
                    throw new IOException("The destination name is already occupied.");
                }
                if (entry.IsSymbolicLink)
                {
                    throw new IOException("Copying over a symbolic link is not supported safely.");
                }
                if (IsCopySource(destination.FileSystem, entry, sourceEntry))
                {
                    throw new IOException("A file cannot be copied over itself.");
                }
                return new StorageFile(destination.FileSystem, entry.Location);
            }
            entry = destination.FileSystem.CreateFile(destination.Location, name);
            if (IsCopySource(destination.FileSystem, entry, sourceEntry))
            {
                // A broken provider must not cause cleanup to delete the source file.
                throw new IOException("The provider returned the source file as the created destination.");
            }
            var target = new StorageFile(destination.FileSystem, entry.Location);
            created = true;
            return target;
        }

        /// <summary>Identifies matching global resource IDs, provider locations, and normalized local paths before truncation.</summary>
        /// <param name="destinationFileSystem">The backend owning the proposed destination.</param>
        /// <param name="destinationEntry">The authoritative destination metadata.</param>
        /// <param name="sourceEntry">The authoritative source metadata.</param>
        /// <returns>Whether the destination is a known alias for this source.</returns>
        /// <exception cref="ArgumentException">A local entry contains an invalid location.</exception>
        /// <exception cref="NotSupportedException">A local entry contains a non-file URI.</exception>
        /// <exception cref="IOException">A local path cannot be normalized.</exception>
        private bool IsCopySource(IFileSystem destinationFileSystem, FileSystemEntry destinationEntry, FileSystemEntry sourceEntry)
        {
            if (sourceEntry.ResourceId is not null && destinationEntry.ResourceId is not null &&
                string.Equals(sourceEntry.ResourceId, destinationEntry.ResourceId, StringComparison.Ordinal))
            {
                return true;
            }
            if (FileSystem is LocalFileSystem && destinationFileSystem is LocalFileSystem)
            {
                return LocalFileSystem.LocationsEqual(Location, destinationEntry.Location) ||
                    LocalFileSystem.LocationsEqual(sourceEntry.Location, destinationEntry.Location);
            }
            if (ReferenceEquals(FileSystem, destinationFileSystem) &&
                (string.Equals(Location, destinationEntry.Location, StringComparison.Ordinal) ||
                    string.Equals(sourceEntry.Location, destinationEntry.Location, StringComparison.Ordinal)))
            {
                return true;
            }
            // Content URIs identify the same global resource even across separate backend instances.
            return Uri.TryCreate(sourceEntry.Location, UriKind.Absolute, out var sourceUri) &&
                Uri.TryCreate(destinationEntry.Location, UriKind.Absolute, out var destinationUri) &&
                string.Equals(sourceUri.Scheme, "content", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(destinationUri.Scheme, "content", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sourceUri.AbsoluteUri, destinationUri.AbsoluteUri, StringComparison.Ordinal);
        }

        /// <summary>Attempts to delete a newly created copy destination without masking the original failure.</summary>
        /// <param name="destination">The new destination, or null if an existing entry was being overwritten.</param>
        private static void TryDeleteNewDestination(StorageFile? destination)
        {
            if (destination is null)
            {
                return;
            }
            try
            {
                destination.Delete();
            }
            catch
            {
                // Cleanup is best-effort; the copy exception remains authoritative.
            }
        }

        /// <summary>Transfers bytes synchronously with a fixed-size buffer and cooperative cancellation.</summary>
        /// <param name="input">The readable source stream, which need not be seekable.</param>
        /// <param name="output">The writable destination stream.</param>
        /// <param name="cancellationToken">The token checked before reads and writes.</param>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="UnauthorizedAccessException">A stream denied access.</exception>
        /// <exception cref="NotSupportedException">A stream does not support the requested operation.</exception>
        /// <exception cref="IOException">A stream transfer failed.</exception>
        private static void CopyStreams(Stream input, Stream output, CancellationToken cancellationToken)
        {
            var buffer = new byte[BufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = input.Read(buffer, 0, buffer.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    return;
                }
                output.Write(buffer, 0, count);
            }
        }

        /// <summary>Transfers bytes asynchronously with a fixed-size buffer and cooperative cancellation.</summary>
        /// <param name="input">The readable source stream, which need not be seekable.</param>
        /// <param name="output">The writable destination stream.</param>
        /// <param name="cancellationToken">The token passed to reads and writes and checked between transfers.</param>
        /// <returns>A task completing when all input bytes have been written.</returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="UnauthorizedAccessException">A stream denied access.</exception>
        /// <exception cref="NotSupportedException">A stream does not support the requested operation.</exception>
        /// <exception cref="IOException">A stream transfer failed.</exception>
        private static async Task CopyStreamsAsync(Stream input, Stream output, CancellationToken cancellationToken)
        {
            var buffer = new byte[BufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    return;
                }
                await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
