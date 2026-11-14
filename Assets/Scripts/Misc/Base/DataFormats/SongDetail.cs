using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Databases;
using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajSimai;
using Nito.AsyncEx;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay
{
    internal class SongDetail : ISongDetail, IDisposable
    {
        /// <summary>
        /// Gets the identity read from the chart directory's <c>.MajdataPlay/id</c> file, or derived from the directory's
        /// location while that file is not written yet (see <see cref="IsIdPersisted"/>).
        /// </summary>
        public string Id { get; }
        /// <summary>Gets whether <see cref="Id"/> is stored in the chart directory's marker file.</summary>
        public bool IsIdPersisted
        {
            get
            {
                return _isIdPersisted;
            }
        }
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public ReadOnlySpan<string> Designers { get => _simaiMetadata.Designers; }
        public ReadOnlySpan<string> Levels { get => _simaiMetadata.Levels; }
        /// <summary>Gets the normalized inote SHA-256 hashes computed during metadata scanning.</summary>
        public ReadOnlySpan<string?> LevelHashes => _levelHashes;
        public ReadOnlySpan<string> Tags { get => ReadOnlySpan<string>.Empty; }
        public string Hash { get => _simaiMetadata.Hash; }
        public DateTime Timestamp { get; init; }
        public ChartStorageLocation Location => ChartStorageLocation.Local;

        public bool IsVideoLoaded { get => _videoPath != null; }
        public bool IsCoverLoaded { get => Cache.Sprite.ContainsKey(_coverCacheKey); }
        public bool IsCompressedCoverLoaded { get => Cache.Sprite.ContainsKey(_coverCacheKey); }
        public bool IsAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_audioTrackCacheKey); }
        public bool IsPreviewAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_previewAudioTrackCacheKey); }
        public bool IsMaidataLoaded { get => _maidata != null; }

        /// <summary>The initial read buffer length for a metadata file whose length the backend does not report.</summary>
        private const int UnknownMetadataLengthBufferSize = 64 * 1024;

        /// <summary>Retains the chart directory and its original backend for writing the identity marker later.</summary>
        private readonly StorageDirectory _directory;
        /// <summary>Retains the authoritative metadata file and its original backend for subsequent chart parsing.</summary>
        private readonly StorageFile _maidataFile;
        /// <summary>Records whether <see cref="Id"/> is stored in the marker file; set once <see cref="PersistId"/> has written it.</summary>
        private volatile bool _isIdPersisted;
        private readonly string _trackPath = string.Empty;
        private readonly string _videoPath = string.Empty;
        private readonly string _coverPath = string.Empty;

        private bool _isPreloaded = false;
        private bool _isDisposed = false;

        /// <summary>The key of this song's cover in <see cref="Cache.Sprite"/>.</summary>
        private readonly string _coverCacheKey;
        /// <summary>The key of this song's music track in <see cref="Cache.AudioSample"/>.</summary>
        private readonly string _audioTrackCacheKey;
        /// <summary>The key of this song's preview track in <see cref="Cache.AudioSample"/>.</summary>
        private readonly string _previewAudioTrackCacheKey;
        private SimaiFile? _maidata = null;
        private SimaiMetadata _simaiMetadata;
        /// <summary>Stores one normalized inote hash per level without parsing note timings during scanning.</summary>
        private readonly string?[] _levelHashes;

        private volatile int _preloadJoinState = 0;

        private readonly bool _isEmptyCover = false;
        private readonly AsyncLock _previewAudioTrackLock = new();
        private readonly AsyncLock _audioTrackLock = new();
        private readonly AsyncLock _coverLock = new();
        private readonly AsyncLock _maidataLock = new();

        ~SongDetail()
        {
            Dispose(false);
        }
        /// <summary>Creates song metadata without replacing the directory's original storage backend.</summary>
        /// <param name="directory">The authoritative directory handle containing chart resources.</param>
        /// <remarks>Metadata reads retain this backend; audio, video and cover loaders still use resource locations as paths.</remarks>
        /// <param name="metadata">The parsed Simai metadata.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="System.IO.FileNotFoundException">Chart metadata or a supported audio track is missing.</exception>
        /// <exception cref="System.IO.IOException">The chart directory cannot be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies chart-directory access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot enumerate chart resources.</exception>
        public SongDetail(StorageDirectory directory, SimaiMetadata metadata) : this(directory, null, metadata)
        {
        }
        /// <summary>Creates song metadata from the chart directory's entries, enumerating them only when they are not supplied.</summary>
        /// <param name="directory">The authoritative directory handle containing chart resources.</param>
        /// <param name="entries">The directory's immediate entries from one enumeration, or null to enumerate them here.</param>
        /// <param name="metadata">The parsed Simai metadata.</param>
        /// <remarks>
        /// The identity marker is only read, never written: a chart without one uses
        /// <see cref="DirectoryIdentity.CreateLocationId(StorageDirectory)"/> until <see cref="PersistId"/> stores it.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="System.IO.FileNotFoundException">Chart metadata or a supported audio track is missing.</exception>
        /// <exception cref="System.IO.IOException">The chart directory cannot be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies chart-directory access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot enumerate chart resources.</exception>
        internal SongDetail(StorageDirectory directory, FileSystemEntry[]? entries, SimaiMetadata metadata)
        {
            // Assigned before anything can throw, because the finalizer still runs when the constructor fails.
            var cacheKeyScope = Cache.CreateKeyScope(nameof(SongDetail));
            _coverCacheKey = $"{cacheKeyScope}/cover";
            _audioTrackCacheKey = $"{cacheKeyScope}/track";
            _previewAudioTrackCacheKey = $"{cacheKeyScope}/preview";
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            entries ??= directory.EnumerateEntries().ToArray();
            var maidataEntry = FindMaidataEntry(entries)
                ?? throw new System.IO.FileNotFoundException($"The chart does not contain {SongStorage.ChartFileName}.");
            var trackEntry = FindFileEntry(entries, SongStorage.SupportedTrackFileNames)
                ?? throw new System.IO.FileNotFoundException("The chart does not contain a supported track.");
            _directory = directory;
            _maidataFile = new StorageFile(directory.FileSystem, maidataEntry.Location);
            _trackPath = trackEntry.Location;
            _videoPath = FindFileEntry(entries, SongStorage.SupportedVideoFileNames)?.Location ?? string.Empty;
            _coverPath = FindFileEntry(entries, SongStorage.SupportedCoverFileNames)?.Location ?? string.Empty;
            _maidata = null;

            if (string.IsNullOrEmpty(_coverPath))
            {
                _isEmptyCover = true;
            }
            _simaiMetadata = metadata;
            _levelHashes = SongDetailExtensions.GetLevelHashes(metadata);
            var storedId = ReadStoredId(directory, entries);
            Id = storedId ?? DirectoryIdentity.CreateLocationId(directory).ToString();
            _isIdPersisted = storedId is not null;
            Title = metadata.Title;
            Artist = metadata.Artist;
            Timestamp = maidataEntry.LastWriteTimeUtc?.ToLocalTime() ?? DateTime.UnixEpoch;
        }
        /// <summary>Checks whether a directory's entries contain both chart metadata and a supported track.</summary>
        /// <param name="entries">The directory's immediate entries.</param>
        /// <returns>True when the directory can be loaded as a chart.</returns>
        /// <exception cref="ArgumentNullException">The entries are null.</exception>
        internal static bool IsChartDirectory(FileSystemEntry[] entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }
            return FindMaidataEntry(entries) is not null && FindFileEntry(entries, SongStorage.SupportedTrackFileNames) is not null;
        }
        /// <summary>Reads, parses and creates a chart on the calling thread from one enumeration of its directory.</summary>
        /// <param name="directory">The authoritative directory handle containing the chart.</param>
        /// <param name="entries">The directory's immediate entries from one enumeration, or null to enumerate them here.</param>
        /// <param name="token">The token checked between the blocking read, parse and construction steps.</param>
        /// <returns>A local song detail initialized from the chart's metadata and original directory handle.</returns>
        /// <remarks>
        /// Blocks on storage I/O and parsing without any thread-pool hop, so scan workers can call it directly.
        /// Synchronous provider operations are checked between calls but cannot be interrupted while blocked.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="System.IO.FileNotFoundException">The chart does not contain maidata.txt or a supported track.</exception>
        /// <exception cref="System.IO.IOException">Chart discovery or reading failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies chart access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot read or enumerate chart resources.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The chart metadata is malformed.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was observed between steps.</exception>
        internal static SongDetail Load(StorageDirectory directory, FileSystemEntry[]? entries, CancellationToken token)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            token.ThrowIfCancellationRequested();
            entries ??= directory.EnumerateEntries().ToArray();
            var maidataEntry = FindMaidataEntry(entries)
                ?? throw new System.IO.FileNotFoundException($"The chart does not contain {SongStorage.ChartFileName}.");
            var maidataFile = new StorageFile(directory.FileSystem, maidataEntry.Location);
            var metadata = ReadMetadata(maidataFile, maidataEntry.Length, token);
            token.ThrowIfCancellationRequested();
            var song = new SongDetail(directory, entries, metadata);
            if (token.IsCancellationRequested)
            {
                song.Dispose();
                token.ThrowIfCancellationRequested();
            }
            return song;
        }
        /// <summary>Writes <see cref="Id"/> into the chart directory's marker file when no identity is stored there yet.</summary>
        /// <remarks>
        /// Scanning only reads markers, so code that stores <see cref="Id"/> calls this to keep the stored reference valid after
        /// the folder is renamed or moved. It blocks on the few storage writes of a missing marker and returns at once when the
        /// identity is already stored. An identity stored by someone else in the meantime is kept on disk; this instance keeps
        /// its <see cref="Id"/> until the next scan reads the stored one.
        /// </remarks>
        /// <exception cref="System.IO.DirectoryNotFoundException">The chart directory no longer exists.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating or writing the marker was denied.</exception>
        /// <exception cref="NotSupportedException">The backend cannot create or write the marker.</exception>
        /// <exception cref="System.IO.IOException">Creating or writing the marker failed.</exception>
        internal void PersistId()
        {
            if (_isIdPersisted)
            {
                return;
            }
            DirectoryIdentity.GetOrWriteId(_directory, Id);
            _isIdPersisted = true;
        }
        /// <summary>Reads the metadata file into a pooled buffer and parses its fields.</summary>
        /// <param name="maidataFile">The chart's metadata file.</param>
        /// <param name="length">The file length reported by enumeration, or null when the backend does not report one.</param>
        /// <param name="token">The token checked between reads.</param>
        /// <returns>The parsed metadata, whose hash covers exactly the bytes of the file.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The metadata file no longer exists.</exception>
        /// <exception cref="System.IO.IOException">Reading failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies reading.</exception>
        /// <exception cref="NotSupportedException">The file is not readable.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The metadata is malformed.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was observed between reads.</exception>
        internal static SimaiMetadata ReadMetadata(StorageFile maidataFile, long? length, CancellationToken token)
        {
            using var stream = maidataFile.OpenRead();
            // One spare byte lets the final zero-length read finish without growing a buffer of exactly the file's length.
            var initialLength = length is > 0 and < int.MaxValue ? (int)length.Value + 1 : UnknownMetadataLengthBufferSize;
            var buffer = Pool<byte>.Rent(initialLength);
            try
            {
                var count = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (count == buffer.Length)
                    {
                        buffer.Resize(count * 2);
                    }
                    var read = stream.Read(buffer.AsArray(), count, buffer.Length - count);
                    if (read == 0)
                    {
                        break;
                    }
                    count += read;
                }
                using var content = new System.IO.MemoryStream(buffer.AsArray(), 0, count, false);
                return SimaiParser.ParseMetadata(content);
            }
            finally
            {
                buffer.Dispose();
            }
        }
        /// <summary>Reads the identity from the chart's marker directory when enumeration found one.</summary>
        /// <param name="directory">The chart directory.</param>
        /// <param name="entries">The chart directory's immediate entries.</param>
        /// <returns>The stored identity, or null when no marker exists or it cannot be read.</returns>
        private static string? ReadStoredId(StorageDirectory directory, FileSystemEntry[] entries)
        {
            var markerEntry = FindEntry(entries, DirectoryIdentity.MarkerDirectoryName, true);
            if (markerEntry is null)
            {
                return null;
            }
            try
            {
                return DirectoryIdentity.ReadStoredId(new StorageDirectory(directory.FileSystem, markerEntry.Location));
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The location-derived identity keeps the chart usable; persisting it later reports a lasting failure.
                MajDebug.LogWarning(nameof(SongDetail), $"Failed to read the identity of chart '{directory.Location}': {exception.Message}");
                return null;
            }
        }
        /// <summary>Finds the chart's metadata file among enumerated entries.</summary>
        /// <param name="entries">The chart directory's immediate entries.</param>
        /// <returns>The metadata file entry, or null when the directory has none.</returns>
        private static FileSystemEntry? FindMaidataEntry(FileSystemEntry[] entries)
        {
            return FindEntry(entries, SongStorage.ChartFileName, false);
        }
        /// <summary>Finds an entry by name, preferring an exact match and otherwise ignoring case like the platform lookup did.</summary>
        /// <param name="entries">The directory's immediate entries.</param>
        /// <param name="name">The requested name.</param>
        /// <param name="isDirectory">Whether a directory rather than a file is requested.</param>
        /// <returns>The matching entry, or null when no entry of the requested kind has the name.</returns>
        /// <remarks>Windows and Android shared storage resolved names without regard to case, so a case-insensitive fallback keeps those charts.</remarks>
        private static FileSystemEntry? FindEntry(FileSystemEntry[] entries, string name, bool isDirectory)
        {
            FileSystemEntry? caseInsensitiveMatch = null;
            foreach (var entry in entries)
            {
                if (entry.IsDirectory != isDirectory)
                {
                    continue;
                }
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return entry;
                }
                if (caseInsensitiveMatch is null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = entry;
                }
            }
            return caseInsensitiveMatch;
        }
        /// <summary>Finds the first file, in enumeration order, whose name is one of the supported names.</summary>
        /// <param name="entries">The directory's immediate entries.</param>
        /// <param name="supportedNames">The accepted file names, compared without regard to case.</param>
        /// <returns>The first matching file entry, or null when there is none.</returns>
        private static FileSystemEntry? FindFileEntry(FileSystemEntry[] entries, string[] supportedNames)
        {
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                foreach (var supportedName in supportedNames)
                {
                    if (string.Equals(entry.Name, supportedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }
            return null;
        }
        /// <summary>Reads and parses a chart using the storage facade for its directory location.</summary>
        /// <param name="chartFolder">The chart directory's local path or registered provider location.</param>
        /// <param name="token">The token checked around provider operations and metadata parsing.</param>
        /// <returns>A local song detail initialized from the chart's metadata.</returns>
        /// <remarks>Cancellation cannot interrupt a synchronous provider call or the upstream tokenless metadata parser.</remarks>
        /// <exception cref="ArgumentNullException">The chart location is null.</exception>
        /// <exception cref="ArgumentException">The chart location is invalid.</exception>
        /// <exception cref="System.IO.FileNotFoundException">The chart does not contain maidata.txt.</exception>
        /// <exception cref="System.IO.IOException">Chart discovery or reading failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies chart access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot read or enumerate chart resources.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The chart metadata is malformed.</exception>
        /// <exception cref="OperationCanceledException">Reading was canceled or cancellation was observed around parsing.</exception>
        public static Task<SongDetail> ParseAsync(string chartFolder, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return ParseAsync(FileSystem.Directory.Open(chartFolder), token);
        }

        /// <summary>Reads and parses a chart on a thread-pool thread using the supplied directory's original backend.</summary>
        /// <param name="directory">The authoritative directory handle containing the chart.</param>
        /// <param name="token">The token checked between metadata reads and before and after parsing.</param>
        /// <returns>A local song detail initialized from the chart's metadata and original directory handle.</returns>
        /// <remarks>
        /// Runs <see cref="Load(StorageDirectory, FileSystemEntry[], CancellationToken)"/> as one work item, so the chart costs a
        /// single thread-pool hop. Synchronous provider operations are checked between calls but cannot be interrupted while blocked.
        /// MajSimai has no metadata-parser token overload, so parsing completes before a cancellation check is made.
        /// Buffering the original bytes also allows provider streams which do not expose a seekable length.
        /// Subsequent maidata reads retain the original backend; audio, video and cover loading remains path-based.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="System.IO.FileNotFoundException">The chart does not contain maidata.txt.</exception>
        /// <exception cref="System.IO.IOException">Chart discovery or reading failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies chart access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot read or enumerate chart resources.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The chart metadata is malformed.</exception>
        /// <exception cref="OperationCanceledException">Reading was canceled or cancellation was observed around parsing.</exception>
        public static async Task<SongDetail> ParseAsync(StorageDirectory directory, CancellationToken token = default)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => Load(directory, null, token), token).ConfigureAwait(false);
        }

        public async ValueTask PreloadAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref _preloadJoinState, 1, 0) != 0)
            {
                return;
            }
            try
            {
                if (_isPreloaded)
                {
                    return;
                }
                await UniTask.SwitchToThreadPool();
                await Task.WhenAll(GetMaidataAsync(token: token).AsTask(), GetCoverAsync(true, token: token).AsTask());
                _isPreloaded = true;
            }
            finally
            {
                _preloadJoinState = 0;
            }
        }
        public ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            return UniTask.FromResult(_videoPath);
        }
        public async ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (_isEmptyCover)
            {
                return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
            }
            if (Cache.Sprite.TryGetValue(_coverCacheKey, out var cover) && await cover.IsNativeAliveAsync())
            {
                return cover;
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                await UniTask.SwitchToThreadPool();
                using (await _coverLock.LockAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (Cache.Sprite.TryGetValue(_coverCacheKey, out cover) && await cover.IsNativeAliveAsync())
                    {
                        return cover;
                    }
                    progress?.Report(1);
                    cover = await SpriteLoader.LoadFromFileAsync(_coverPath, true, token);
                    await UniTask.SwitchToMainThread();
                    // A failed load returns the shared empty sprite, which the cache must not own and later destroy.
                    if (!ReferenceEquals(cover, SpriteLoader.EmptySprite))
                    {
                        Cache.Sprite.Set(_coverCacheKey, cover);
                        RemoveFromCacheIfDisposed(Cache.Sprite, _coverCacheKey);
                    }
                    return cover;
                }
            }
        }
        public async ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (Cache.AudioSample.TryGetValue(_audioTrackCacheKey, out var audioTrack))
            {
                return audioTrack;
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                await UniTask.SwitchToThreadPool();
                using (await _audioTrackLock.LockAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (Cache.AudioSample.TryGetValue(_audioTrackCacheKey, out audioTrack))
                    {
                        return audioTrack;
                    }
                    progress?.Report(1);
                    audioTrack = await MajInstances.AudioManager.LoadMusicAsync(_trackPath, true, true);
                    StoreAudioSample(_audioTrackCacheKey, audioTrack);
                    return audioTrack;
                }
            }
        }
        public async ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (Cache.AudioSample.TryGetValue(_previewAudioTrackCacheKey, out var audioTrack))
            {
                return audioTrack;
            }
            await UniTask.SwitchToThreadPool();
            using (await _previewAudioTrackLock.LockAsync(token))
            {
                token.ThrowIfCancellationRequested();
                if (Cache.AudioSample.TryGetValue(_previewAudioTrackCacheKey, out audioTrack))
                {
                    return audioTrack;
                }
                progress?.Report(1);
                audioTrack = await MajInstances.AudioManager.LoadMusicAsync(_trackPath, true, false);
                StoreAudioSample(_previewAudioTrackCacheKey, audioTrack);
                return audioTrack;
            }
        }
        /// <summary>Loads and parses full chart data using the metadata file's original storage backend.</summary>
        /// <param name="ignoreCache">Whether cached parsed chart data must be revalidated against the current file.</param>
        /// <param name="progress">The optional progress reporter notified after the file has been read.</param>
        /// <param name="token">The token controlling lock admission and file transfers and checked around metadata parsing.</param>
        /// <returns>The cached or newly parsed Simai chart file.</returns>
        /// <remarks>
        /// Provider streams are buffered so metadata parsing does not require a native path or a seekable provider stream.
        /// Cancellation cannot interrupt a blocked synchronous provider call or a tokenless upstream Simai parser.
        /// This backend-preserving behavior applies to maidata only; audio, video and cover loading remains path-based.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">This song detail has already been released.</exception>
        /// <exception cref="OperationCanceledException">Lock admission or reading was canceled, or cancellation was observed around parsing.</exception>
        /// <exception cref="System.IO.FileNotFoundException">The metadata file is no longer available.</exception>
        /// <exception cref="System.IO.IOException">The metadata file could not be read.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies metadata access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot read the metadata file.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The metadata is malformed.</exception>
        public async ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (!ignoreCache && _maidata is not null)
            {
                return _maidata;
            }
            await UniTask.SwitchToThreadPool();
            using (await _maidataLock.LockAsync(token))
            {
                token.ThrowIfCancellationRequested();
                if (!ignoreCache && _maidata is not null)
                {
                    return _maidata;
                }
                using var fileStream = _maidataFile.OpenRead();
                using var metadataStream = new System.IO.MemoryStream();
                await fileStream.CopyToAsync(metadataStream, 81920, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                metadataStream.Position = 0;
                progress?.Report(1);
                var metadata = await SimaiParser.ParseMetadataAsync(metadataStream).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (metadata.Hash == _simaiMetadata.Hash)
                {
                    _maidata ??= await SimaiParser.ParseAsync(metadata);
                    return _maidata;
                }
                else
                {
                    _maidata = await SimaiParser.ParseAsync(metadata);
                    _simaiMetadata = metadata;
                    return _maidata;
                }
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
        private void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            // Removing releases the values: samples are disposed and the cover is destroyed on the main thread.
            Cache.AudioSample.Remove(_audioTrackCacheKey);
            Cache.AudioSample.Remove(_previewAudioTrackCacheKey);
            Cache.Sprite.Remove(_coverCacheKey);
            _maidata = null;
        }
        public ValueTask DisposeAsync()
        {
            return DisposeAsync(true);
        }
        private async ValueTask DisposeAsync(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (Cache.AudioSample.TryRemove(_audioTrackCacheKey, out var audioTrack))
            {
                await audioTrack.DisposeAsync();
            }
            if (Cache.AudioSample.TryRemove(_previewAudioTrackCacheKey, out audioTrack))
            {
                await audioTrack.DisposeAsync();
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                // On the main thread the cover is destroyed before this method completes.
                await UniTask.SwitchToMainThread();
                Cache.Sprite.Remove(_coverCacheKey);
            }
            _maidata = null;
        }
        /// <summary>Stores a loaded sample in <see cref="Cache.AudioSample"/>, unless it is the shared empty sample.</summary>
        /// <param name="key">The cache key of the track.</param>
        /// <param name="audioTrack">The loaded sample, which the cache takes ownership of.</param>
        private void StoreAudioSample(string key, AudioSampleWrap audioTrack)
        {
            // A failed load returns the shared empty sample; leaving it uncached lets the next call retry.
            if (audioTrack.IsEmpty)
            {
                return;
            }
            Cache.AudioSample.Set(key, audioTrack);
            RemoveFromCacheIfDisposed(Cache.AudioSample, key);
        }
        /// <summary>Releases a value stored while this song was being disposed, which the disposal could not see yet.</summary>
        /// <typeparam name="TValue">The cached value type.</typeparam>
        /// <param name="cache">The cache the value was just stored in.</param>
        /// <param name="key">The key the value was stored under.</param>
        private void RemoveFromCacheIfDisposed<TValue>(LruCache<string, TValue> cache, string key)
        {
            if (_isDisposed)
            {
                cache.Remove(key);
            }
        }
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SongDetail));
            }
        }
    }
}
