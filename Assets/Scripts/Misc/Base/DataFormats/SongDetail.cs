using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Databases;
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
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public ReadOnlySpan<string> Designers
        {
            get
            {
                return _simaiMetadata.Designers;
            }
        }
        public ReadOnlySpan<string> Levels
        {
            get
            {
                return _simaiMetadata.Levels;
            }
        }
        public string Hash
        {
            get => _simaiMetadata.Hash;
        }
        public DateTime Timestamp { get; init; }
        public ChartStorageLocation Location => ChartStorageLocation.Local;

        public bool IsVideoLoaded { get => _videoPath != null; }
        public bool IsCoverLoaded { get => Cache.Sprite.ContainsKey(_coverCacheKey); }
        public bool IsCompressedCoverLoaded { get => Cache.Sprite.ContainsKey(_coverCacheKey); }
        public bool IsAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_audioTrackCacheKey); }
        public bool IsPreviewAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_previewAudioTrackCacheKey); }
        public bool IsMaidataLoaded { get => _maidata != null; }

        /// <summary>Retains the authoritative metadata file and its original backend for subsequent chart parsing.</summary>
        private readonly StorageFile _maidataFile;
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
        public SongDetail(StorageDirectory directory, SimaiMetadata metadata)
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
            var files = directory.EnumerateEntries().Where(x => !x.IsDirectory).ToArray();
            _maidataFile = directory.FindFile(SongStorage.ChartFileName)
                ?? throw new System.IO.FileNotFoundException($"The chart does not contain {SongStorage.ChartFileName}.");
            _trackPath = files.FirstOrDefault(entry => SongStorage.SupportedTrackFileNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))?.Location
                ?? throw new System.IO.FileNotFoundException("The chart does not contain a supported track.");
            _videoPath = files.FirstOrDefault(entry => SongStorage.SupportedVideoFileNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))?.Location ?? string.Empty;
            _coverPath = files.FirstOrDefault(entry => SongStorage.SupportedCoverFileNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))?.Location ?? string.Empty;
            _maidata = null;

            if (string.IsNullOrEmpty(_coverPath))
            {
                _isEmptyCover = true;
            }
            _simaiMetadata = metadata;
            Title = metadata.Title;
            Artist = metadata.Artist;
            Timestamp = _maidataFile.Entry?.LastWriteTimeUtc?.ToLocalTime() ?? DateTime.UnixEpoch;
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

        /// <summary>Reads cancellable metadata bytes and parses them using the supplied directory's original backend.</summary>
        /// <param name="directory">The authoritative directory handle containing the chart.</param>
        /// <param name="token">The token passed to metadata reading and checked before and after parsing.</param>
        /// <returns>A local song detail initialized from the chart's metadata and original directory handle.</returns>
        /// <remarks>
        /// Synchronous provider operations are checked between calls but cannot be interrupted while blocked.
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
            var maidataFile = directory.FindFile(SongStorage.ChartFileName)
                ?? throw new System.IO.FileNotFoundException($"The chart does not contain {SongStorage.ChartFileName}.");
            token.ThrowIfCancellationRequested();
            var maidataBytes = await maidataFile.ReadAllBytesAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using var maidataStream = new System.IO.MemoryStream(maidataBytes, false);
            var metadata = await SimaiParser.ParseMetadataAsync(maidataStream).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var song = new SongDetail(directory, metadata);
            if (token.IsCancellationRequested)
            {
                song.Dispose();
                token.ThrowIfCancellationRequested();
            }
            return song;
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
