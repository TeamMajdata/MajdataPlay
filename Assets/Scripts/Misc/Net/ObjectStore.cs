using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Stores downloaded online resources once per content, named by their SHA-256 digest, and shares every concurrent
    /// download of the same content. Any online component may keep its files here.
    /// </summary>
    /// <remarks>
    /// An object lives at <c>{first two digits}/{digest}</c>, where the digest is 64 lowercase hexadecimal digits, so one
    /// file serves every component, endpoint and record that uses the same content.
    /// <para>
    /// A download is written to the partial directory, <c>Cache/Temp/Objects</c> for the shared store, and moved to its
    /// object location once its digest has been verified, so a stored object is never partial. A partial download is
    /// named <c>{digest}.chunk</c> when its digest is known in advance, and <c>uri-{digest of the URI}.chunk</c> when the
    /// digest is learned from the content; either is kept when the download is interrupted, and the next download of the
    /// same content or URI resumes it with a range request.
    /// </para>
    /// <para>
    /// The store does not know which objects are still needed. Every component that keeps objects registers an
    /// <see cref="IObjectReferenceSource"/> before <see cref="ScanAsync"/> runs at startup, which deletes the objects no
    /// registered source references.
    /// </para>
    /// </remarks>
    internal sealed class ObjectStore
    {
        /// <summary>The name of the store directory inside <see cref="MajEnv.CacheDirectory"/>, and of the partial directory inside <see cref="MajEnv.TempDirectory"/>.</summary>
        public const string DirectoryName = "Objects";

        /// <summary>
        /// Gets the store of the application, with its objects in <see cref="MajEnv.CacheDirectory"/> and its partial
        /// downloads in <see cref="MajEnv.TempDirectory"/>, creating it on first access.
        /// </summary>
        /// <exception cref="IOException">A store directory could not be created.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating a store directory was denied.</exception>
        public static ObjectStore Shared
        {
            get
            {
                lock (s_sharedLock)
                {
                    return s_shared ??= new ObjectStore(
                        MajEnv.CacheDirectory.CreateDirectory(DirectoryName),
                        MajEnv.TempDirectory.CreateDirectory(DirectoryName));
                }
            }
        }

        /// <summary>Gets the directory holding the objects.</summary>
        public StorageDirectory Directory { get; }
        /// <summary>Gets the directory holding the partial downloads, which must share the backend of <see cref="Directory"/>.</summary>
        public StorageDirectory PartialDirectory { get; }
        /// <summary>Gets whether <see cref="Scan"/> has indexed the stored objects, so lookups no longer query the storage for absent objects.</summary>
        public bool IsScanned
        {
            get => Volatile.Read(ref _isScanned);
        }

        /// <summary>The suffix of a partial download.</summary>
        private const string ChunkSuffix = ".chunk";
        /// <summary>The prefix of a partial download whose digest is not known in advance, named by the digest of its URI.</summary>
        private const string UriChunkPrefix = "uri-";
        /// <summary>The age after which a partial download named by its URI is deleted, since no record keeps its content.</summary>
        private static readonly TimeSpan s_uriChunkLifetime = TimeSpan.FromDays(7);
        /// <summary>The number of bytes of a SHA-256 digest.</summary>
        private const int Sha256Length = 32;
        /// <summary>The number of bytes of an MD5 digest.</summary>
        private const int Md5Length = 16;
        /// <summary>The number of hexadecimal digits of a SHA-256 digest.</summary>
        private const int DigestLength = Sha256Length * 2;
        /// <summary>The longest hexadecimal text <see cref="ToHex"/> builds on the stack instead of in a pooled buffer.</summary>
        private const int StackHexLength = 256;
        /// <summary>The lowercase hexadecimal digits.</summary>
        private const string HexDigits = "0123456789abcdef";
        /// <summary>The number of leading digest digits naming an object's parent directory.</summary>
        private const int PrefixLength = 2;
        /// <summary>The response header in which MajdataNET endpoints send the Base64 SHA-256 digest of a redirected file.</summary>
        private const string HashHeaderName = "hash";

        /// <summary>Guards <see cref="s_shared"/>.</summary>
        private static readonly object s_sharedLock = new();
        /// <summary>Stores the store of the application, or null before the first access.</summary>
        private static ObjectStore? s_shared;

        /// <summary>Stores the registered reference sources, guarded by itself.</summary>
        private readonly List<IObjectReferenceSource> _referenceSources = new();
        /// <summary>Records whether <see cref="ScanAsync"/> has pruned the store, which happens once per store.</summary>
        private int _isPruned;
        /// <summary>Indexes the digests of the stored objects once <see cref="Scan"/> has run.</summary>
        private readonly ConcurrentDictionary<string, byte> _objects = new(StringComparer.Ordinal);
        /// <summary>Guards <see cref="_downloads"/> and the waiter state of every <see cref="SharedDownload"/>.</summary>
        private readonly object _downloadsLock = new();
        /// <summary>Stores the running download of each digest, or of each URI for downloads whose digest is not known.</summary>
        private readonly Dictionary<string, SharedDownload> _downloads = new(StringComparer.Ordinal);
        /// <summary>Records whether <see cref="Scan"/> has completed.</summary>
        private bool _isScanned;

        /// <summary>Creates a store over existing directories.</summary>
        /// <param name="directory">The directory holding the objects.</param>
        /// <param name="partialDirectory">The directory holding the partial downloads, on the backend of <paramref name="directory"/>.</param>
        /// <exception cref="ArgumentNullException">A directory is null.</exception>
        public ObjectStore(StorageDirectory directory, StorageDirectory partialDirectory)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            PartialDirectory = partialDirectory ?? throw new ArgumentNullException(nameof(partialDirectory));
        }

        /// <summary>Checks whether a value is a SHA-256 digest in lowercase hexadecimal.</summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True when the value has 64 lowercase hexadecimal digits; otherwise false.</returns>
        public static bool IsDigest(string? value)
        {
            if (value is null || value.Length != DigestLength)
            {
                return false;
            }
            foreach (var c in value)
            {
                if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Converts a digest to lowercase hexadecimal.</summary>
        /// <param name="bytes">The digest bytes.</param>
        /// <returns>The lowercase hexadecimal digest.</returns>
        public static string ToHex(ReadOnlySpan<byte> bytes)
        {
            var length = bytes.Length * 2;
            var rented = default(PooledArray<char>);
            try
            {
                Span<char> chars = length <= StackHexLength
                    ? stackalloc char[length]
                    : (rented = Pool<char>.Rent(length)).AsSpan(0, length);
                for (var i = 0; i < bytes.Length; i++)
                {
                    chars[i * 2] = HexDigits[bytes[i] >> 4];
                    chars[i * 2 + 1] = HexDigits[bytes[i] & 0xF];
                }
                return new string(chars);
            }
            finally
            {
                rented.Dispose();
            }
        }

        /// <summary>Registers a component whose references keep objects from being deleted by <see cref="ScanAsync"/>.</summary>
        /// <param name="source">The reference source.</param>
        /// <remarks>Registering the same source again has no effect.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public void AddReferenceSource(IObjectReferenceSource source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            lock (_referenceSources)
            {
                if (!_referenceSources.Contains(source))
                {
                    _referenceSources.Add(source);
                }
            }
        }

        /// <summary>
        /// Indexes the stored objects on the thread pool and, the first time, deletes the objects no registered
        /// reference source references.
        /// </summary>
        /// <param name="token">The token canceling the scan.</param>
        /// <returns>A task completing when the store has been indexed and pruned.</returns>
        /// <remarks>
        /// Pruning runs once, at startup, before any component downloads: a download whose digest is learned from its
        /// content is stored before its owner records the digest, so a later prune could delete it. Without a registered
        /// source nothing is pruned, and a source that fails to report its references cancels the pruning.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The scan was canceled.</exception>
        /// <exception cref="IOException">The store directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumerating the store directory was denied.</exception>
        public Task ScanAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                Scan(token);
                // The sources read the database, so they are asked outside the lock.
                using var sources = new PooledList<IObjectReferenceSource>();
                lock (_referenceSources)
                {
                    sources.AddRange(_referenceSources);
                }
                if (sources.Count == 0 || Interlocked.Exchange(ref _isPruned, 1) != 0)
                {
                    return;
                }
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in sources)
                {
                    try
                    {
                        referenced.UnionWith(source.GetReferencedDigests());
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError(nameof(ObjectStore), $"Skipped pruning, because {source.GetType().Name} could not report its references: {e}");
                        return;
                    }
                }
                Prune(referenced, token);
            }, token);
        }

        /// <summary>Indexes the stored objects.</summary>
        /// <param name="token">The token canceling the enumeration.</param>
        /// <returns>The number of stored objects.</returns>
        /// <remarks>
        /// Until the first scan, lookups query the storage for every object; afterwards an object missing from the index
        /// is reported absent without a query. Partial downloads are left untouched, so they can be resumed.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The scan was canceled.</exception>
        /// <exception cref="IOException">The store directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumerating the store directory was denied.</exception>
        public int Scan(CancellationToken token = default)
        {
            var found = new List<string>();
            foreach (var directoryEntry in Directory.EnumerateEntries())
            {
                token.ThrowIfCancellationRequested();
                if (!directoryEntry.IsDirectory)
                {
                    continue;
                }
                var directory = new StorageDirectory(Directory.FileSystem, directoryEntry.Location);
                foreach (var entry in directory.EnumerateEntries())
                {
                    if (!entry.IsDirectory && IsDigest(entry.Name) && entry.Name.StartsWith(directoryEntry.Name, StringComparison.Ordinal))
                    {
                        found.Add(entry.Name);
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            _objects.Clear();
            foreach (var digest in found)
            {
                _objects.TryAdd(digest, 0);
            }
            Volatile.Write(ref _isScanned, true);
            MajDebug.LogInfo(nameof(ObjectStore), $"Indexed {found.Count} cached objects in {Directory.Location}");
            return found.Count;
        }

        /// <summary>Checks whether an object is stored.</summary>
        /// <param name="sha256">The lowercase hexadecimal SHA-256 digest of the object.</param>
        /// <returns>True when the object is stored; otherwise false.</returns>
        /// <remarks>After <see cref="Scan"/> this reads only the in-memory index.</remarks>
        public bool Contains(string? sha256)
        {
            if (!IsDigest(sha256))
            {
                return false;
            }
            if (IsScanned)
            {
                return _objects.ContainsKey(sha256!);
            }
            return Find(sha256!) is not null;
        }

        /// <summary>Finds a stored object.</summary>
        /// <param name="sha256">The lowercase hexadecimal SHA-256 digest of the object.</param>
        /// <returns>The object file, or null when it is not stored.</returns>
        /// <remarks>An indexed object deleted from the storage meanwhile is dropped from the index and reported absent.</remarks>
        /// <exception cref="IOException">The storage could not be queried.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying the storage was denied.</exception>
        public StorageFile? Find(string sha256)
        {
            if (!IsDigest(sha256))
            {
                return null;
            }
            if (IsScanned && !_objects.ContainsKey(sha256))
            {
                return null;
            }
            var file = Directory.FindDirectory(sha256.Substring(0, PrefixLength))?.FindFile(sha256);
            if (file is null)
            {
                _objects.TryRemove(sha256, out _);
                return null;
            }
            _objects.TryAdd(sha256, 0);
            return file;
        }

        /// <summary>Deletes the stored objects and partial downloads no reference keeps.</summary>
        /// <param name="referenced">The lowercase hexadecimal digests still referenced.</param>
        /// <param name="token">The token canceling the enumeration.</param>
        /// <returns>The number of deleted files.</returns>
        /// <remarks>
        /// A partial download of a known digest is kept while the digest is referenced. A partial download named by its
        /// URI is kept for <see cref="s_uriChunkLifetime"/> after its last write, since no reference names its content.
        /// Files of running downloads are kept, and a file that cannot be deleted is logged and left in place.
        /// </remarks>
        /// <exception cref="OperationCanceledException">Pruning was canceled.</exception>
        /// <exception cref="IOException">A store directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">Enumerating a store directory was denied.</exception>
        public int Prune(ISet<string> referenced, CancellationToken token = default)
        {
            if (referenced is null)
            {
                throw new ArgumentNullException(nameof(referenced));
            }
            var deleted = 0;
            // Entries are copied before deleting, so no enumeration runs while its directory changes.
            using var directoryEntries = new PooledList<FileSystemEntry>(Directory.EnumerateEntries());
            foreach (var directoryEntry in directoryEntries)
            {
                token.ThrowIfCancellationRequested();
                if (!directoryEntry.IsDirectory)
                {
                    continue;
                }
                var directory = new StorageDirectory(Directory.FileSystem, directoryEntry.Location);
                using var entries = new PooledList<FileSystemEntry>(directory.EnumerateEntries());
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory || !IsDigest(entry.Name) || referenced.Contains(entry.Name) || IsDownloading(entry.Name))
                    {
                        continue;
                    }
                    if (TryDelete(entry))
                    {
                        _objects.TryRemove(entry.Name, out _);
                        deleted++;
                    }
                }
            }
            var staleBefore = DateTime.UtcNow - s_uriChunkLifetime;
            using var partialEntries = new PooledList<FileSystemEntry>(PartialDirectory.EnumerateEntries());
            foreach (var entry in partialEntries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory || !entry.Name.EndsWith(ChunkSuffix, StringComparison.Ordinal))
                {
                    continue;
                }
                var key = entry.Name.Substring(0, entry.Name.Length - ChunkSuffix.Length);
                var isStale = IsDigest(key)
                    ? !referenced.Contains(key) && !IsDownloading(key)
                    : key.StartsWith(UriChunkPrefix, StringComparison.Ordinal) && entry.LastWriteTimeUtc < staleBefore;
                if (isStale && TryDelete(entry))
                {
                    deleted++;
                }
            }
            if (deleted != 0)
            {
                MajDebug.LogInfo(nameof(ObjectStore), $"Deleted {deleted} unreferenced objects");
            }
            return deleted;
        }

        /// <summary>Gets a stored object, or downloads it, sharing the download with every concurrent request for the same content.</summary>
        /// <param name="uri">The URI answering with the object's content.</param>
        /// <param name="expectedSha256">
        /// The lowercase hexadecimal SHA-256 digest of the content, or null when it is not known; the content is then
        /// verified against the response's <c>hash</c> header when the server sends one, and stored under its own digest.
        /// </param>
        /// <param name="progress">The optional receiver of the download progress.</param>
        /// <param name="token">
        /// The token canceling this request. The shared download stops only when every request waiting for it has been
        /// canceled; its partial download is kept and resumed by the next download of the same content or URI.
        /// </param>
        /// <returns>The stored object, or a result without a file when the server reports that the content does not exist.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="expectedSha256"/> is not a lowercase hexadecimal SHA-256 digest.</exception>
        /// <exception cref="OperationCanceledException">The request was canceled.</exception>
        /// <exception cref="HttpException">The request or the integrity check failed after the applicable retries.</exception>
        /// <exception cref="IOException">The store could not be read or written.</exception>
        /// <exception cref="UnauthorizedAccessException">The storage denied access.</exception>
        public async Task<StoredObject> GetOrDownloadAsync(Uri uri, string? expectedSha256, INetProgress? progress, CancellationToken token)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (expectedSha256 is not null && !IsDigest(expectedSha256))
            {
                throw new ArgumentException("The expected digest is not a lowercase hexadecimal SHA-256 digest.", nameof(expectedSha256));
            }
            token.ThrowIfCancellationRequested();
            if (expectedSha256 is not null && Find(expectedSha256) is { } stored)
            {
                return StoredObject.Stored(stored, expectedSha256);
            }
            var download = Join(expectedSha256 ?? uri.AbsoluteUri, uri, expectedSha256, progress);
            try
            {
                return await download.Completion.WaitAsync(token).ConfigureAwait(false);
            }
            finally
            {
                Leave(download, progress);
            }
        }

        /// <summary>Checks whether a download of a digest is running.</summary>
        /// <param name="sha256">The lowercase hexadecimal digest.</param>
        /// <returns>True when a download of the digest is running; otherwise false.</returns>
        private bool IsDownloading(string sha256)
        {
            lock (_downloadsLock)
            {
                return _downloads.ContainsKey(sha256);
            }
        }

        /// <summary>Joins the running download of a key, or starts one.</summary>
        /// <param name="key">The digest, or the URI when the digest is not known.</param>
        /// <param name="uri">The URI answering with the content.</param>
        /// <param name="expectedSha256">The expected digest, or null.</param>
        /// <param name="progress">The optional progress receiver of the joining request.</param>
        /// <returns>The joined download.</returns>
        private SharedDownload Join(string key, Uri uri, string? expectedSha256, INetProgress? progress)
        {
            lock (_downloadsLock)
            {
                if (!_downloads.TryGetValue(key, out var download) || download.IsAbandoned)
                {
                    // An abandoned download may still be closing its chunk, so the new one starts after it.
                    var previous = download?.Completion;
                    var started = new SharedDownload(key);
                    // Task.Run keeps the download from completing, and removing itself, before it is registered.
                    started.Completion = Task.Run(() => RunAsync(started, previous, uri, expectedSha256));
                    _downloads[key] = started;
                    download = started;
                }
                download.WaiterCount++;
                download.Progress.Add(progress);
                return download;
            }
        }

        /// <summary>Leaves a download, stopping it when no request waits for it any more.</summary>
        /// <param name="download">The joined download.</param>
        /// <param name="progress">The progress receiver of the leaving request.</param>
        private void Leave(SharedDownload download, INetProgress? progress)
        {
            var isAbandoned = false;
            lock (_downloadsLock)
            {
                download.WaiterCount--;
                download.Progress.Remove(progress);
                if (download.WaiterCount == 0 && !download.Completion.IsCompleted)
                {
                    download.IsAbandoned = true;
                    isAbandoned = true;
                }
            }
            if (isAbandoned)
            {
                // Canceling outside the lock, because cancellation callbacks run synchronously.
                download.Cancellation.Cancel();
            }
        }

        /// <summary>Runs one shared download after the abandoned download it replaces, and unregisters it when it ends.</summary>
        /// <param name="download">The shared download.</param>
        /// <param name="previous">The abandoned download of the same key, or null.</param>
        /// <param name="uri">The URI answering with the content.</param>
        /// <param name="expectedSha256">The expected digest, or null.</param>
        /// <returns>The stored object, or a result without a file when the content does not exist.</returns>
        private async Task<StoredObject> RunAsync(SharedDownload download, Task? previous, Uri uri, string? expectedSha256)
        {
            try
            {
                if (previous is not null)
                {
                    try
                    {
                        await previous.ConfigureAwait(false);
                    }
                    catch
                    {
                        // The abandoned download's outcome belongs to the requests that left it.
                    }
                }
                return await DownloadAsync(uri, expectedSha256, download.Progress, download.Cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_downloadsLock)
                {
                    if (_downloads.TryGetValue(download.Key, out var current) && ReferenceEquals(current, download))
                    {
                        _downloads.Remove(download.Key);
                    }
                }
            }
        }

        /// <summary>Downloads content, verifies it and places it under its digest.</summary>
        /// <param name="uri">The URI answering with the content.</param>
        /// <param name="expectedSha256">The expected digest, or null to learn it from the content.</param>
        /// <param name="progress">The receiver of the download progress.</param>
        /// <param name="token">The token stopping the download.</param>
        /// <returns>The stored object, or a result without a file when the server reports that the content does not exist.</returns>
        /// <exception cref="OperationCanceledException">The download was stopped.</exception>
        /// <exception cref="HttpException">The request or the integrity check failed after the applicable retries.</exception>
        /// <exception cref="IOException">The store could not be read or written.</exception>
        private async Task<StoredObject> DownloadAsync(Uri uri, string? expectedSha256, INetProgress progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (expectedSha256 is not null && Find(expectedSha256) is { } stored)
            {
                return StoredObject.Stored(stored, expectedSha256);
            }
            var chunkName = GetChunkName(uri, expectedSha256);
            var chunkFile = PartialDirectory.FindFile(chunkName);
            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var existingLength = chunkFile?.Entry?.Length ?? 0;
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    if (existingLength > 0)
                    {
                        request.Headers.Range = HttpRange.From(existingLength).ToRangeHeaderValue();
                    }
                    using var response = await MajEnv.SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && chunkFile is not null)
                    {
                        // The range starts at the end of a partial download that already holds the whole content.
                        var expectedChunk = expectedSha256 ?? ReadHashHeader(response);
                        var digests = ComputeDigests(chunkFile);
                        if (digests.Sha256 == expectedChunk)
                        {
                            return StoredObject.Downloaded(Place(chunkFile, digests.Sha256), digests.Sha256, digests.Md5);
                        }
                        MajDebug.LogWarning(nameof(ObjectStore), $"Range {existingLength}- not satisfiable. Resetting chunk: {uri}");
                        DeleteIfExists(ref chunkFile);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // The content is gone, so a partial download of it can never be completed.
                        DeleteIfExists(ref chunkFile);
                        return StoredObject.NotFound;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpException(uri.OriginalString, HttpErrorCode.Unsuccessful, response.StatusCode);
                    }
                    MajDebug.LogDebug(nameof(ObjectStore), $"Received http response {(int)response.StatusCode} from: {uri}");

                    var isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                    if (!isPartial && existingLength > 0)
                    {
                        MajDebug.LogWarning(nameof(ObjectStore), "Server does not support partial download. Falling back to full download.");
                        existingLength = 0;
                        DeleteIfExists(ref chunkFile);
                    }
                    var expected = expectedSha256 ?? ReadHashHeader(response);
                    var receivedLength = response.Content.Headers.ContentLength ?? 0;
                    var totalLength = isPartial ? receivedLength + existingLength : receivedLength;
                    progress.TotalBytes = totalLength;

                    chunkFile ??= PartialDirectory.CreateFile(chunkName);
                    using (var chunkStream = chunkFile.OpenWrite(append: isPartial))
                    {
                        using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        await CopyWithProgressAsync(responseStream, chunkStream, progress, existingLength, totalLength, token).ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();

                    var computed = ComputeDigests(chunkFile);
                    if (expected is not null && computed.Sha256 != expected)
                    {
                        MajDebug.LogWarning(nameof(ObjectStore), $"Hash mismatch for online resource {uri}. Expected: {expected}, received: {computed.Sha256}");
                        DeleteIfExists(ref chunkFile);
                        if (i == MajEnv.HTTP_REQUEST_MAX_RETRY)
                        {
                            throw new HttpException(uri.OriginalString, HttpErrorCode.IntegrityCheckFailed);
                        }
                        continue;
                    }
                    token.ThrowIfCancellationRequested();
                    return StoredObject.Downloaded(Place(chunkFile, computed.Sha256), computed.Sha256, computed.Md5);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // The partial download is kept, so the next download of the same content resumes it.
                    MajDebug.LogWarning(nameof(ObjectStore), $"Request for resource \"{uri}\" was canceled");
                    throw;
                }
                catch (Exception e) when (e is not HttpException || ((HttpException)e).ErrorCode == HttpErrorCode.IntegrityCheckFailed)
                {
                    if (i == MajEnv.HTTP_REQUEST_MAX_RETRY)
                    {
                        if (e is HttpException)
                        {
                            throw;
                        }
                        if (e is InvalidOperationException)
                        {
                            throw new HttpException(uri.OriginalString, HttpErrorCode.InvalidRequest);
                        }
                        if (e is TaskCanceledException)
                        {
                            throw new HttpException(uri.OriginalString, HttpErrorCode.Timeout);
                        }
                        MajDebug.LogError(nameof(ObjectStore), $"Failed to request resource: {uri}\n{e}");
                        throw new HttpException(uri.OriginalString, HttpErrorCode.Unreachable);
                    }
                }
            }
            throw new HttpException(uri.OriginalString, HttpErrorCode.Unreachable);
        }

        /// <summary>Gets the name of the partial download of a content.</summary>
        /// <param name="uri">The URI answering with the content.</param>
        /// <param name="expectedSha256">The expected digest, or null when it is not known.</param>
        /// <returns><c>{digest}.chunk</c> for a known digest, otherwise <c>uri-{digest of the URI}.chunk</c>.</returns>
        private static string GetChunkName(Uri uri, string? expectedSha256)
        {
            if (expectedSha256 is not null)
            {
                return expectedSha256 + ChunkSuffix;
            }
            var text = uri.AbsoluteUri;
            using var utf8 = Pool<byte>.Rent(Encoding.UTF8.GetMaxByteCount(text.Length));
            var length = Encoding.UTF8.GetBytes(text.AsSpan(), utf8.AsSpan());
            using var sha256 = SHA256.Create();
            Span<byte> digest = stackalloc byte[Sha256Length];
            sha256.TryComputeHash(utf8.AsSpan(0, length), digest, out _);
            return UriChunkPrefix + ToHex(digest) + ChunkSuffix;
        }

        /// <summary>Moves a verified download to its object location, or discards it when the object is already stored.</summary>
        /// <param name="chunkFile">The verified download in the partial directory.</param>
        /// <param name="sha256">The digest of the download.</param>
        /// <returns>The stored object.</returns>
        /// <exception cref="IOException">The download could not be moved.</exception>
        private StorageFile Place(StorageFile chunkFile, string sha256)
        {
            var directory = Directory.CreateDirectory(sha256.Substring(0, PrefixLength));
            var existing = directory.FindFile(sha256);
            if (existing is null)
            {
                try
                {
                    existing = FileSystem.File.Move(chunkFile.Location, directory.Location, sha256);
                }
                catch (IOException) when (directory.FindFile(sha256) is not null)
                {
                    // Another download of the same content was placed first.
                    existing = directory.FindFile(sha256)!;
                    chunkFile.Delete();
                }
            }
            else
            {
                chunkFile.Delete();
            }
            _objects.TryAdd(sha256, 0);
            return existing;
        }

        /// <summary>Reads the Base64 SHA-256 digest a MajdataNET endpoint sends with a redirected file.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The digest as lowercase hexadecimal, or null when the header is missing or malformed.</returns>
        private static string? ReadHashHeader(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues(HashHeaderName, out var values))
            {
                return null;
            }
            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }
                Span<byte> digest = stackalloc byte[Sha256Length];
                if (Convert.TryFromBase64String(value, digest, out var written) && written == Sha256Length)
                {
                    return ToHex(digest);
                }
                MajDebug.LogWarning(nameof(ObjectStore), $"Ignored a malformed hash header: {value}");
                return null;
            }
            return null;
        }

        /// <summary>Computes the SHA-256 and MD5 digests of a file in one pass.</summary>
        /// <param name="file">The file.</param>
        /// <returns>The digests as lowercase hexadecimal.</returns>
        /// <exception cref="IOException">The file could not be read.</exception>
        private static (string Sha256, string Md5) ComputeDigests(StorageFile file)
        {
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using (var buffer = Pool<byte>.Rent(MajEnv.HTTP_BUFFER_SIZE))
            {
                using var stream = file.OpenRead();
                var span = buffer.AsSpan();
                int read;
                while ((read = stream.Read(span)) > 0)
                {
                    sha256.AppendData(span.Slice(0, read));
                    md5.AppendData(span.Slice(0, read));
                }
            }
            Span<byte> sha256Digest = stackalloc byte[Sha256Length];
            Span<byte> md5Digest = stackalloc byte[Md5Length];
            sha256.TryGetHashAndReset(sha256Digest, out _);
            md5.TryGetHashAndReset(md5Digest, out _);
            return (ToHex(sha256Digest), ToHex(md5Digest));
        }

        /// <summary>Copies response bytes to the chunk while reporting the download progress.</summary>
        /// <param name="source">The response stream.</param>
        /// <param name="destination">The chunk stream.</param>
        /// <param name="progress">The progress receiver.</param>
        /// <param name="existingLength">The bytes already in the chunk before this response.</param>
        /// <param name="totalLength">The length of the whole content when known, otherwise zero.</param>
        /// <param name="token">The token canceling the copy.</param>
        /// <returns>A task completing after every response byte has been written and flushed.</returns>
        /// <exception cref="OperationCanceledException">The copy was canceled.</exception>
        /// <exception cref="IOException">Reading or writing a stream failed.</exception>
        private static async Task CopyWithProgressAsync(Stream source, Stream destination, INetProgress progress, long existingLength, long totalLength, CancellationToken token)
        {
            using var buffer = Pool<byte>.Rent(MajEnv.HTTP_BUFFER_SIZE);
            var memory = buffer.AsMemory();
            var transferred = existingLength;
            int read;
            while ((read = await source.ReadAsync(memory, token).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(memory.Slice(0, read), token).ConfigureAwait(false);
                transferred += read;
                progress.TransferredBytes = transferred;
                progress.Report(totalLength != 0 ? Mathf.Clamp01((float)transferred / totalLength) : 0f);
            }
            await destination.FlushAsync(token).ConfigureAwait(false);
        }

        /// <summary>Deletes a file found while pruning, logging a failure instead of throwing.</summary>
        /// <param name="entry">The file entry.</param>
        /// <returns>True when the file was deleted; otherwise false.</returns>
        private bool TryDelete(FileSystemEntry entry)
        {
            try
            {
                new StorageFile(Directory.FileSystem, entry.Location).Delete();
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MajDebug.LogWarning(nameof(ObjectStore), $"Failed to delete the unreferenced file {entry.Location}: {e.Message}");
                return false;
            }
        }

        /// <summary>Deletes an optional file and clears its handle.</summary>
        /// <param name="file">The file, set to null afterwards.</param>
        /// <exception cref="IOException">The file could not be deleted.</exception>
        private static void DeleteIfExists(ref StorageFile? file)
        {
            file?.Delete();
            file = null;
        }

        /// <summary>One download shared by every request for the same key.</summary>
        private sealed class SharedDownload
        {
            /// <summary>Gets the digest, or the URI when the digest is not known.</summary>
            public string Key { get; }
            /// <summary>Gets the source stopping the download when every request has left it.</summary>
            public CancellationTokenSource Cancellation { get; } = new();
            /// <summary>Gets the receiver forwarding the progress to every waiting request.</summary>
            public ProgressFanout Progress { get; } = new();
            /// <summary>Gets or sets the running download.</summary>
            public Task<StoredObject> Completion { get; set; } = null!;
            /// <summary>Gets or sets the number of waiting requests, guarded by the store's download lock.</summary>
            public int WaiterCount { get; set; }
            /// <summary>Gets or sets whether every request left before completion, so new requests start another download.</summary>
            public bool IsAbandoned { get; set; }

            /// <summary>Creates a download that no request has joined yet.</summary>
            /// <param name="key">The digest, or the URI when the digest is not known.</param>
            public SharedDownload(string key)
            {
                Key = key;
            }
        }

        /// <summary>Forwards download progress to every request waiting for a shared download.</summary>
        /// <remarks>Receivers are called under a lock, so they must only record the values.</remarks>
        private sealed class ProgressFanout : INetProgress
        {
            /// <summary>Gets or sets the length of the whole content, forwarded to every receiver.</summary>
            public long TotalBytes
            {
                get
                {
                    lock (_receivers)
                    {
                        return _totalBytes;
                    }
                }
                set
                {
                    lock (_receivers)
                    {
                        _totalBytes = value;
                        foreach (var receiver in _receivers)
                        {
                            receiver.TotalBytes = value;
                        }
                    }
                }
            }
            /// <summary>Gets or sets the number of bytes received, forwarded to every receiver.</summary>
            public long TransferredBytes
            {
                get
                {
                    lock (_receivers)
                    {
                        return _transferredBytes;
                    }
                }
                set
                {
                    lock (_receivers)
                    {
                        _transferredBytes = value;
                        foreach (var receiver in _receivers)
                        {
                            receiver.TransferredBytes = value;
                        }
                    }
                }
            }

            /// <summary>The receivers of the waiting requests.</summary>
            private readonly List<INetProgress> _receivers = new();
            /// <summary>The last reported content length.</summary>
            private long _totalBytes;
            /// <summary>The last reported number of received bytes.</summary>
            private long _transferredBytes;
            /// <summary>The last reported completion ratio.</summary>
            private float _percent;

            /// <summary>Forwards a completion ratio to every receiver.</summary>
            /// <param name="value">The completion ratio between 0 and 1.</param>
            public void Report(float value)
            {
                lock (_receivers)
                {
                    _percent = value;
                    foreach (var receiver in _receivers)
                    {
                        receiver.Report(value);
                    }
                }
            }

            /// <summary>Adds the receiver of a joining request and brings it up to the current progress.</summary>
            /// <param name="receiver">The receiver, or null when the request does not observe progress.</param>
            public void Add(INetProgress? receiver)
            {
                if (receiver is null)
                {
                    return;
                }
                lock (_receivers)
                {
                    _receivers.Add(receiver);
                    if (_totalBytes != 0 || _transferredBytes != 0)
                    {
                        receiver.TotalBytes = _totalBytes;
                        receiver.TransferredBytes = _transferredBytes;
                        receiver.Report(_percent);
                    }
                }
            }

            /// <summary>Removes the receiver of a leaving request.</summary>
            /// <param name="receiver">The receiver, or null.</param>
            public void Remove(INetProgress? receiver)
            {
                if (receiver is null)
                {
                    return;
                }
                lock (_receivers)
                {
                    _receivers.Remove(receiver);
                }
            }
        }
    }

    /// <summary>The outcome of <see cref="ObjectStore.GetOrDownloadAsync"/>.</summary>
    internal readonly struct StoredObject
    {
        /// <summary>Gets a result reporting that the server does not have the content.</summary>
        public static StoredObject NotFound => default;

        /// <summary>Gets the stored object, or null when the server does not have the content.</summary>
        public StorageFile? File { get; }
        /// <summary>Gets the lowercase hexadecimal SHA-256 digest of the object, or null when the server does not have the content.</summary>
        public string? Sha256 { get; }
        /// <summary>Gets the lowercase hexadecimal MD5 digest of a downloaded object, or null when the object was already stored.</summary>
        public string? Md5 { get; }

        /// <summary>Creates a result.</summary>
        /// <param name="file">The stored object.</param>
        /// <param name="sha256">The SHA-256 digest of the object.</param>
        /// <param name="md5">The MD5 digest of a downloaded object, or null.</param>
        private StoredObject(StorageFile file, string sha256, string? md5)
        {
            File = file;
            Sha256 = sha256;
            Md5 = md5;
        }

        /// <summary>Creates the result of an object that was already stored.</summary>
        /// <param name="file">The stored object.</param>
        /// <param name="sha256">The SHA-256 digest of the object.</param>
        /// <returns>The result.</returns>
        public static StoredObject Stored(StorageFile file, string sha256)
        {
            return new StoredObject(file, sha256, null);
        }

        /// <summary>Creates the result of a downloaded object.</summary>
        /// <param name="file">The stored object.</param>
        /// <param name="sha256">The SHA-256 digest of the object.</param>
        /// <param name="md5">The MD5 digest of the object.</param>
        /// <returns>The result.</returns>
        public static StoredObject Downloaded(StorageFile file, string sha256, string md5)
        {
            return new StoredObject(file, sha256, md5);
        }
    }
}
