using Cysharp.Text;
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay
{
    internal class OnlineSongDetail : ISongDetail, IDisposable
    {
        public string Id { get; init; }
        public string Title { get; init; }
        public string Artist { get; init; }
        public string Description { get; init; } = string.Empty;
        public ReadOnlySpan<string> Designers { get => _designers; }
        public ReadOnlySpan<string> Levels { get => _levels; }
        public ReadOnlySpan<string> Tags { get => _tags; }
        public ChartStorageLocation Location { get; } = ChartStorageLocation.Online;
        public DateTime Timestamp { get; init; }
        public string Hash { get; init; }
        public ApiEndpoint ServerInfo => _serverInfo;
        /// <summary>Gets the record of this chart's file digests, which locates its files in the object store.</summary>
        public OnlineChartRecord Record => _record;

        public bool IsVideoLoaded { get => _videoPath != null; }
        public bool IsCoverLoaded { get => Cache.LargeSprite.ContainsKey(_fullSizeCoverCacheKey); }
        public bool IsCompressedCoverLoaded { get => Cache.Sprite.ContainsKey(_coverCacheKey); }
        public bool IsAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_audioTrackCacheKey); }
        public bool IsPreviewAudioTrackLoaded { get => Cache.AudioSample.ContainsKey(_previewAudioTrackCacheKey); }
        public bool IsMaidataLoaded { get => _maidata != null; }

        /// <summary>Gets the store holding this chart's files.</summary>
        private ObjectStore Objects => _record.Cache.Objects;

        /// <summary>The initial length of the pooled buffer a stored chart is read into; it doubles while the chart does not fit.</summary>
        private const int ChartBufferLength = 64 * 1024;

        /// <summary>Stores the digests of this chart's files.</summary>
        private readonly OnlineChartRecord _record;
        private readonly ApiEndpoint _serverInfo;

        private readonly Uri _maidataUri;
        private readonly Uri _trackUri;
        private readonly Uri _videoUri;
        private readonly Uri _fullSizeCoverUri;
        private readonly Uri _coverUri;

        private readonly string[] _designers = new string[7];
        private readonly string[] _levels = new string[7];
        private readonly string[] _tags = Array.Empty<string>();

        private bool _isDisposed = false;
        private bool _isPreloaded = false;

        private string? _videoPath = null;
        /// <summary>The key of this song's music track in <see cref="Cache.AudioSample"/>.</summary>
        private readonly string _audioTrackCacheKey;
        /// <summary>The key of this song's streamed preview track in <see cref="Cache.AudioSample"/>.</summary>
        private readonly string _previewAudioTrackCacheKey;
        /// <summary>The key of this song's compressed cover in <see cref="Cache.Sprite"/>.</summary>
        private readonly string _coverCacheKey;
        /// <summary>The key of this song's full-size cover in <see cref="Cache.LargeSprite"/>.</summary>
        private readonly string _fullSizeCoverCacheKey;
        private SimaiFile? _maidata = null;

        private volatile int _preloadJoinState = 0;

        private readonly AsyncLock _previewAudioTrackLock = new();
        private readonly AsyncLock _audioTrackLock = new();
        private readonly AsyncLock _videoPathLock = new();
        private readonly AsyncLock _coverLock = new();
        private readonly AsyncLock _fullSizeCoverLock = new();
        private readonly AsyncLock _maidataLock = new();

        ~OnlineSongDetail()
        {
            Dispose(false);
        }
        /// <summary>Creates an online song whose files are kept in the object store of its record's cache.</summary>
        /// <param name="serverInfo">The endpoint used to request missing chart resources.</param>
        /// <param name="songDetail">The server-provided song metadata.</param>
        /// <param name="record">The digests of the chart's files, from <see cref="OnlineChartCache"/>.</param>
        /// <remarks>
        /// Files are shared by content: a file another chart or endpoint already stored is not downloaded again.
        /// Media loaders receive the object's actual file location, which has no file extension.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The endpoint, song metadata or record is null.</exception>
        public OnlineSongDetail(ApiEndpoint serverInfo, MajnetSongDetail songDetail, OnlineChartRecord record)
        {
            // Assigned before anything can throw, because the finalizer still runs when the constructor fails.
            var cacheKeyScope = Cache.CreateKeyScope(nameof(OnlineSongDetail));
            _audioTrackCacheKey = $"{cacheKeyScope}/track";
            _previewAudioTrackCacheKey = $"{cacheKeyScope}/preview";
            _coverCacheKey = $"{cacheKeyScope}/cover";
            _fullSizeCoverCacheKey = $"{cacheKeyScope}/fullSizeCover";
            if (serverInfo is null)
            {
                throw new ArgumentNullException(nameof(serverInfo));
            }
            if (songDetail is null)
            {
                throw new ArgumentNullException(nameof(songDetail));
            }
            _record = record ?? throw new ArgumentNullException(nameof(record));
            var apiroot = serverInfo.Url.Combine($"maichart/{songDetail.Id}/");

            Title = songDetail.Title;
            Artist = songDetail.Artist;
            for (var i = 0; i < 7; i++)
            {
                if (i >= songDetail.Levels.Length)
                {
                    break;
                }
                _levels[i] = songDetail.Levels[i];
            }
            _maidataUri = apiroot.Combine("chart");
            _trackUri = apiroot.Combine("track");
            _fullSizeCoverUri = apiroot.Combine("image?fullimage=true");
            _videoUri = apiroot.Combine("video");
            _coverUri = apiroot.Combine("image");

            Hash = songDetail.Hash;
            _serverInfo = serverInfo;
            Id = songDetail.Id ?? string.Empty;
            Timestamp = songDetail.Timestamp;

            var tagCount = songDetail.Tags.Length + songDetail.PublicTags.Length;
            if (tagCount != 0)
            {
                _tags = songDetail.Tags.Concat(songDetail.PublicTags)
                                       .ToArray();
            }
            Description = songDetail.Description;
            using (var sb = ZString.CreateStringBuilder())
            {
                sb.Append(songDetail.Uploader);
                sb.Append('@');
                sb.Append(songDetail.Designer);
                var designer = sb.ToString();
                for (var i = 0; i < _designers.Length; i++)
                {
                    _designers[i] = designer;

                }
                sb.Clear();
            }
        }

        #region Public
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
                await Task.WhenAll(GetMaidataAsync(token: token).AsTask(), GetCompressedCoverAsync(false, progress, token).AsTask());
                _isPreloaded = true;
            }
            finally
            {
                _preloadJoinState = 0;
            }
        }
        /// <summary>Gets the preview track, loading the stored track or else streaming it from the endpoint.</summary>
        /// <param name="progress">The optional progress receiver; streaming does not report progress.</param>
        /// <param name="token">The token canceling lock admission and streaming.</param>
        /// <returns>The loaded full track when present, otherwise the preview track.</returns>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">The audio loader could not load the track.</exception>
        public async ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            try
            {
                await UniTask.SwitchToThreadPool();
                var @lock = await _previewAudioTrackLock.LockAsync(token);
                using (@lock)
                {
                    token.ThrowIfCancellationRequested();
                    if (Cache.AudioSample.TryGetValue(_audioTrackCacheKey, out var audioTrack)
                        || Cache.AudioSample.TryGetValue(_previewAudioTrackCacheKey, out audioTrack))
                    {
                        return audioTrack;
                    }

                    var audioManager = MajInstances.AudioManager;
                    var trackFile = FindStoredFile(OnlineChartResourceKind.Track);
                    // A stored track plays without the network, so cached charts can be previewed offline.
                    var sample = trackFile is not null
                        ? await audioManager.LoadMusicAsync(trackFile.Location, true, false)
                        : await audioManager.LoadMusicFromUriAsync(_trackUri, token);

                    StoreAudioSample(_previewAudioTrackCacheKey, sample);

                    return sample;
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                throw;
            }
        }
        public ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            return GetAudioTrackAsync(true, progress, token);
        }
        /// <summary>Gets the stored background video or downloads it.</summary>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission and resource download.</param>
        /// <returns>The video object's location, or an empty string when the server has no video.</returns>
        /// <remarks>The video loader must support the returned location, which has no file extension.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The video request failed.</exception>
        /// <exception cref="IOException">The object store could not be queried or updated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied object store access.</exception>
        public async ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            try
            {
                if (_videoPath is not null)
                {
                    return _videoPath;
                }
                await UniTask.SwitchToThreadPool();
                var @lock = await _videoPathLock.LockAsync(token);
                using (@lock)
                {
                    token.ThrowIfCancellationRequested();

                    if (_videoPath is not null)
                    {
                        return _videoPath;
                    }
                    var videoFile = await GetResourceFileAsync(OnlineChartResourceKind.Video, _videoUri, progress, token);
                    token.ThrowIfCancellationRequested();
                    progress?.Report(1);
                    _videoPath = videoFile?.Location ?? string.Empty;
                    return _videoPath;
                }
            }
            catch (Exception e)
            {
                if (e is not OperationCanceledException)
                {
                    MajDebug.LogException(e);
                }
                throw;
            }
        }
        public ValueTask<Sprite> GetCoverAsync(bool isCompressed, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (isCompressed)
            {
                return GetCompressedCoverAsync(true, progress, token);
            }
            else
            {
                return GetFullSizeCoverAsync(true, progress, token);
            }
        }
        /// <summary>Reads the stored chart, downloading it when it is not stored.</summary>
        /// <param name="ignoreCache">Whether to bypass the already parsed in-memory chart.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission and download, and checked around parsing.</param>
        /// <returns>The parsed chart.</returns>
        /// <remarks>Cancellation cannot interrupt synchronous provider calls or the upstream tokenless parser.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The chart request failed.</exception>
        /// <exception cref="Exception">The stored chart could not be read or parsed.</exception>
        public async ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            if (!ignoreCache && _maidata is not null)
            {
                return _maidata;
            }
            try
            {
                await UniTask.SwitchToThreadPool();
                var @lock = await _maidataLock.LockAsync(token);
                using (@lock)
                {
                    token.ThrowIfCancellationRequested();
                    var maidataFile = await GetResourceFileAsync(OnlineChartResourceKind.Maidata, _maidataUri, progress, token).ConfigureAwait(false)
                        ?? throw new FileNotFoundException("The chart is unavailable.");
                    var metadata = await ReadMetadataAsync(maidataFile, token).ConfigureAwait(false);
                    if (metadata.Hash != Hash)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"The stored chart of {Id} does not match the chart hash of the chart list");
                    }
                    token.ThrowIfCancellationRequested();
                    progress?.Report(1);
                    var maidata = await SimaiParser.ParseAsync(metadata).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    _maidata = maidata;
                    return _maidata;
                }
            }
            catch (HttpException ex)
            {
                MajDebug.LogException(ex);
                throw;
            }
            catch (OperationCanceledException)
            {
                _maidata = null;
                throw;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                _maidata = null;
                throw new Exception("Maidata Load Failed", e);
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
            // Removing releases the values: samples are disposed and the covers are destroyed on the main thread.
            Cache.AudioSample.Remove(_audioTrackCacheKey);
            Cache.AudioSample.Remove(_previewAudioTrackCacheKey);
            Cache.Sprite.Remove(_coverCacheKey);
            Cache.LargeSprite.Remove(_fullSizeCoverCacheKey);
            _maidata = null;
            _videoPath = null;
        }
        public async ValueTask DisposeAsync()
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
                // On the main thread the covers are destroyed before this method completes.
                await UniTask.SwitchToMainThread();
                Cache.Sprite.Remove(_coverCacheKey);
                Cache.LargeSprite.Remove(_fullSizeCoverCacheKey);
            }
            _maidata = null;
            _videoPath = null;
        }
        //public async ValueTask UnloadUnityAssetsAsync(CancellationToken token = default)
        //{
        //    await using (UniTask.ReturnToCurrentSynchronizationContext())
        //    {
        //        var waiting4LockTask = _coverLock.LockAsync(token);
        //        await Task.WhenAny(waiting4LockTask, Task.Delay(Timeout.Infinite, token));
        //        var @lock = waiting4LockTask.Result;
        //        using (@lock)
        //        {
        //            if(_cover is not null)
        //            {
        //                var texture = _cover.texture;
        //                UnityEngine.Object.DestroyImmediate(_cover, true);
        //                UnityEngine.Object.DestroyImmediate(texture, true);
        //                _cover = null;
        //            }
        //        }
        //        token.ThrowIfCancellationRequested();
        //        var waiting4LockTask2 = _fullSizeCoverLock.LockAsync(token);
        //        await Task.WhenAny(waiting4LockTask2, Task.Delay(Timeout.Infinite, token));
        //        var @lock2 = waiting4LockTask2.Result;
        //        using (@lock2)
        //        {
        //            if (_fullSizeCover is not null)
        //            {
        //                var texture = _fullSizeCover.texture;
        //                UnityEngine.Object.DestroyImmediate(_fullSizeCover, true);
        //                UnityEngine.Object.DestroyImmediate(texture, true);
        //                _fullSizeCover = null;
        //            }
        //        }
        //    }
        //}
        #endregion

        /// <summary>Gets the stored track or downloads it, and optionally decodes it.</summary>
        /// <param name="loadIntoMemory">Whether the track should also be decoded as audio.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission, download and main-thread admission.</param>
        /// <returns>The decoded track, or an empty audio sample when only downloading.</returns>
        /// <remarks>Audio decoding receives the object's location, which has no file extension.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The track request failed.</exception>
        /// <exception cref="Exception">The object store or audio loader could not provide the requested track.</exception>
        private async ValueTask<AudioSampleWrap> GetAudioTrackAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                try
                {
                    await UniTask.SwitchToThreadPool();
                    var @lock = await _audioTrackLock.LockAsync(token);
                    using (@lock)
                    {
                        token.ThrowIfCancellationRequested();
                        if (Cache.AudioSample.TryGetValue(_audioTrackCacheKey, out var audioTrack))
                        {
                            return audioTrack;
                        }
                        var trackFile = await GetResourceFileAsync(OnlineChartResourceKind.Track, _trackUri, progress, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        progress?.Report(1);
                        if (!loadIntoMemory)
                        {
                            return AudioSampleWrap.Empty;
                        }
                        if (trackFile is null)
                        {
                            throw new FileNotFoundException("The track is unavailable.");
                        }
                        await UniTask.SwitchToMainThread(token);
                        var sampleWrap = await MajInstances.AudioManager.LoadMusicAsync(trackFile.Location, true, true);
                        StoreAudioSample(_audioTrackCacheKey, sampleWrap);
                        return sampleWrap;
                    }
                }
                catch (Exception e)
                {
                    if (e is not OperationCanceledException)
                    {
                        MajDebug.LogException(e);
                    }
                    throw;
                }
            }
        }
        /// <summary>Gets or downloads the compressed cover.</summary>
        /// <param name="loadIntoMemory">Whether to decode the completed cover download.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and cover loading.</param>
        /// <returns>The compressed cover or the runtime's empty-cover sprite.</returns>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">The object store or image loader failed.</exception>
        private ValueTask<Sprite> GetCompressedCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            return GetCoverResourceAsync(false, loadIntoMemory, progress, token);
        }

        /// <summary>Gets or downloads the separate, full-resolution cover.</summary>
        /// <param name="loadIntoMemory">Whether to decode the completed cover download.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and cover loading.</param>
        /// <returns>The full-resolution cover or the runtime's empty-cover sprite.</returns>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">The object store or image loader failed.</exception>
        private ValueTask<Sprite> GetFullSizeCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            return GetCoverResourceAsync(true, loadIntoMemory, progress, token);
        }

        /// <summary>Shares cover download logic without mixing full-resolution and compressed caches.</summary>
        /// <param name="isFullSize">Whether to get the original cover rather than the compressed one.</param>
        /// <param name="loadIntoMemory">Whether the selected image should be decoded.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and image decoding.</param>
        /// <returns>The selected cover or the runtime's empty-cover sprite.</returns>
        /// <remarks>Image decoding receives the object's location, which has no file extension.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">The object store or image loader failed.</exception>
        private async ValueTask<Sprite> GetCoverResourceAsync(bool isFullSize, bool loadIntoMemory, INetProgress? progress, CancellationToken token)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            var coverCache = isFullSize ? Cache.LargeSprite : Cache.Sprite;
            var coverCacheKey = isFullSize ? _fullSizeCoverCacheKey : _coverCacheKey;
            var coverLock = isFullSize ? _fullSizeCoverLock : _coverLock;
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                try
                {
                    await UniTask.SwitchToThreadPool();
                    var @lock = await coverLock.LockAsync(token);
                    using (@lock)
                    {
                        token.ThrowIfCancellationRequested();
                        if (coverCache.TryGetValue(coverCacheKey, out var cover) && await cover.IsNativeAliveAsync())
                        {
                            return cover;
                        }
                        StorageFile? coverFile;
                        try
                        {
                            coverFile = await GetCoverFileAsync(isFullSize, progress, token).ConfigureAwait(false);
                        }
                        catch (HttpException e)
                        {
                            MajDebug.LogException(e);
                            return await GetEmptyCoverAsync(token);
                        }
                        finally
                        {
                            progress?.Report(1);
                        }
                        token.ThrowIfCancellationRequested();
                        if (!loadIntoMemory || coverFile is null)
                        {
                            return await GetEmptyCoverAsync(token);
                        }
                        cover = await SpriteLoader.LoadFromFileAsync(coverFile.Location, true, token);
                        await UniTask.SwitchToMainThread();
                        // A failed load returns the shared empty sprite, which the cache must not own and later destroy.
                        if (!ReferenceEquals(cover, SpriteLoader.EmptySprite))
                        {
                            coverCache.Set(coverCacheKey, cover);
                            RemoveFromCacheIfDisposed(coverCache, coverCacheKey);
                        }
                        return cover;
                    }
                }
                catch (Exception e)
                {
                    if (e is not OperationCanceledException)
                    {
                        MajDebug.LogException(e);
                    }
                    throw;
                }
            }
        }

        /// <summary>Gets the stored file of a cover, or downloads it.</summary>
        /// <param name="isFullSize">Whether to get the original cover rather than the compressed one.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling the download.</param>
        /// <returns>The cover file, or null when the server has neither cover.</returns>
        /// <remarks>
        /// Like the image endpoint, the other cover is used when the server is known to lack the requested one, so a chart
        /// without a compressed cover still shows its original cover.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The download was canceled.</exception>
        /// <exception cref="HttpException">The cover request failed.</exception>
        /// <exception cref="IOException">The object store could not be read or written.</exception>
        private Task<StorageFile?> GetCoverFileAsync(bool isFullSize, INetProgress? progress, CancellationToken token)
        {
            var preferred = isFullSize ? OnlineChartResourceKind.CoverArt : OnlineChartResourceKind.Thumbnail;
            var alternative = isFullSize ? OnlineChartResourceKind.Thumbnail : OnlineChartResourceKind.CoverArt;
            var kind = _record.IsMissing(preferred) && !_record.IsMissing(alternative) ? alternative : preferred;
            return GetResourceFileAsync(kind, isFullSize ? _fullSizeCoverUri : _coverUri, progress, token);
        }

        /// <summary>Gets the stored file of a resource, or downloads it once for every chart and endpoint sharing its content.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <param name="endpointUri">The chart endpoint URI of the resource, which redirects to its current file.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling the download.</param>
        /// <returns>The resource file, or null when the server does not have the resource.</returns>
        /// <remarks>
        /// A recorded digest is downloaded from the info API's direct URI and verified against the digest; when the
        /// verification fails the server has replaced the file, so the current file is downloaded from the endpoint.
        /// Without a recorded digest the file is downloaded from the endpoint and its digest is recorded.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The download was canceled.</exception>
        /// <exception cref="HttpException">The request failed.</exception>
        /// <exception cref="IOException">The object store could not be read or written.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied object store access.</exception>
        private async Task<StorageFile?> GetResourceFileAsync(OnlineChartResourceKind kind, Uri endpointUri, INetProgress? progress, CancellationToken token)
        {
            ThrowIfDisposed();
            if (_record.TryGetDigest(kind, out var digest))
            {
                var stored = Objects.Find(digest.Sha256);
                if (stored is not null)
                {
                    return stored;
                }
                // The chart endpoint counts a play before it redirects, so the chart is never fetched from its direct URI.
                var uri = kind == OnlineChartResourceKind.Maidata ? endpointUri : _record.GetUrl(kind) ?? endpointUri;
                try
                {
                    var download = await Objects.GetOrDownloadAsync(uri, digest.Sha256, progress, token).ConfigureAwait(false);
                    return download.File;
                }
                catch (HttpException e) when (e.ErrorCode == HttpErrorCode.IntegrityCheckFailed)
                {
                    MajDebug.LogWarning(nameof(OnlineSongDetail), $"The recorded {kind} of {Id} is outdated, downloading the current file");
                }
            }
            else if (_record.IsMissing(kind))
            {
                return null;
            }
            var result = await Objects.GetOrDownloadAsync(endpointUri, null, progress, token).ConfigureAwait(false);
            if (result.File is null)
            {
                return null;
            }
            if (result.Md5 is not null)
            {
                _record.SetDigest(kind, new ChartResourceDigest(result.Md5, result.Sha256!));
            }
            return result.File;
        }

        /// <summary>Finds the stored file of a resource without downloading it.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The resource file, or null when its digest is not recorded or its object is not stored.</returns>
        /// <exception cref="IOException">The object store could not be queried.</exception>
        private StorageFile? FindStoredFile(OnlineChartResourceKind kind)
        {
            return _record.TryGetDigest(kind, out var digest) ? Objects.Find(digest.Sha256) : null;
        }

        /// <summary>Reads the runtime's fallback sprite only after switching to Unity's main thread.</summary>
        /// <param name="token">The token canceling main-thread admission.</param>
        /// <returns>The empty-song cover owned by the active game runtime.</returns>
        /// <exception cref="OperationCanceledException">The operation was canceled before accessing the runtime.</exception>
        private static async ValueTask<Sprite> GetEmptyCoverAsync(CancellationToken token)
        {
            await UniTask.SwitchToMainThread(token);
            token.ThrowIfCancellationRequested();
            return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
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

        /// <summary>Rejects resource operations after this song's owned media has been released.</summary>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnlineSongDetail));
            }
        }

        /// <summary>Buffers the stored chart in a pooled buffer before invoking the parser which requires a seekable stream.</summary>
        /// <param name="file">The stored chart file.</param>
        /// <param name="token">The token canceling reads and checked around the tokenless metadata parser.</param>
        /// <returns>The parsed Simai metadata, including its computed chart hash.</returns>
        /// <remarks>The metadata holds only strings, so the buffer is returned to the pool once parsing completes.</remarks>
        /// <exception cref="OperationCanceledException">Reading was canceled or cancellation was observed around parsing.</exception>
        /// <exception cref="IOException">The provider could not read the chart.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied chart access.</exception>
        /// <exception cref="NotSupportedException">The provider does not support reading.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The cached chart metadata is malformed.</exception>
        private static async Task<SimaiMetadata> ReadMetadataAsync(StorageFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Not a using variable: Resize replaces the lease, which a read-only using variable would only do on a copy.
            var buffer = Pool<byte>.Rent(ChartBufferLength);
            try
            {
                var length = 0;
                using (var fileStream = file.OpenRead())
                {
                    int read;
                    while ((read = await fileStream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false)) > 0)
                    {
                        length += read;
                        if (length == buffer.Length)
                        {
                            buffer.Resize(length * 2);
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                using var metadataStream = new MemoryStream(buffer.AsArray(), 0, length, false);
                var metadata = await SimaiParser.ParseMetadataAsync(metadataStream).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return metadata;
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }
}
