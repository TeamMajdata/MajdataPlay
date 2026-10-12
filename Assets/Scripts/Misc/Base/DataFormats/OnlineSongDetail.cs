using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Databases;
using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajSimai;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
        public ReadOnlySpan<string> Designers
        {
            get
            {
                return _designers;
            }
        }
        public ReadOnlySpan<string> Levels
        {
            get
            {
                return _levels;
            }
        }
        public ChartStorageLocation Location { get; } = ChartStorageLocation.Online;
        public DateTime Timestamp { get; init; }
        public string Hash { get; init; }
        public ApiEndpoint ServerInfo => _serverInfo;

        public bool IsVideoLoaded { get => _videoPath != null; }
        public bool IsCoverLoaded { get => _fullSizeCoverRef.TryGetTarget(out _); }
        public bool IsCompressedCoverLoaded { get => _coverRef.TryGetTarget(out _); }
        public bool IsAudioTrackLoaded { get => _audioTrackRef.TryGetTarget(out _); }
        public bool IsPreviewAudioTrackLoaded { get => _previewAudioTrackRef.TryGetTarget(out _); }
        public bool IsMaidataLoaded { get => _maidata != null; }

        /// <summary>Retains the caller-owned per-song cache directory and its original storage backend.</summary>
        private readonly StorageDirectory _cacheDirectory;
        private readonly ApiEndpoint _serverInfo;

        private readonly Uri _maidataUri;
        private readonly Uri _trackUri;
        private readonly Uri _videoUri;
        private readonly Uri _fullSizeCoverUri;
        private readonly Uri _coverUri;

        private readonly string[] _designers = new string[7];
        private readonly string[] _levels = new string[7];

        private bool _isDisposed = false;
        private bool _isPreloaded = false;

        private string? _videoPath = null;
        private WeakReference<AudioSampleWrap> _audioTrackRef = new(null!);
        private WeakReference<AudioSampleWrap> _previewAudioTrackRef = new(null!);
        private WeakReference<Sprite> _coverRef = new(null!);
        private WeakReference<Sprite> _fullSizeCoverRef = new(null!);
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
        /// <summary>Creates an online song backed by the caller's existing, song-specific cache directory.</summary>
        /// <param name="directory">The already-created cache directory whose original backend owns all cached resources.</param>
        /// <param name="serverInfo">The endpoint used to request missing chart resources.</param>
        /// <param name="songDetail">The server-provided song metadata.</param>
        /// <remarks>
        /// Cache operations retain storage handles rather than resolving their locations through the storage facade.
        /// Media loaders still receive the provider's actual file location and must support it or require a local copy.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The cache directory, endpoint or song metadata is null.</exception>
        public OnlineSongDetail(StorageDirectory directory, ApiEndpoint serverInfo, MajnetSongDetail songDetail)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            if (serverInfo is null)
            {
                throw new ArgumentNullException(nameof(serverInfo));
            }
            if (songDetail is null)
            {
                throw new ArgumentNullException(nameof(songDetail));
            }
            _cacheDirectory = directory;
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
            Id = songDetail.Id;
            Timestamp = songDetail.Timestamp;

            using (var sb = ZString.CreateStringBuilder())
            {
                sb.AppendLine(Description);
                foreach (var tag in songDetail.Tags.Concat(songDetail.PublicTags))
                {
                    sb.AppendLine(tag);
                }
                Description = sb.ToString();
                sb.Clear();
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
                    if (_audioTrackRef.TryGetTarget(out var audioTrack) || _previewAudioTrackRef.TryGetTarget(out audioTrack))
                    {
                        return audioTrack;
                    }

                    var audioManager = MajInstances.AudioManager;
                    var sample = await audioManager.LoadMusicFromUriAsync(_trackUri);

                    _previewAudioTrackRef.SetTarget(sample);

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
        /// <summary>Gets a supported cached video location or downloads the endpoint's default background video.</summary>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission and resource download.</param>
        /// <returns>The provider's actual video location, or an empty string when the endpoint has no video.</returns>
        /// <remarks>The video loader must support the returned location or first obtain an app-local copy.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The video request failed.</exception>
        /// <exception cref="IOException">The cache could not be queried or updated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied cache access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot perform a required cache operation.</exception>
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
                    var videoFile = FindCachedFile(SongStorage.SupportedVideoFileNames);
                    var cacheFlagFile = _cacheDirectory.FindFile("bg.mp4.cache");

                    if (cacheFlagFile is not null && videoFile is null)
                    {
                        token.ThrowIfCancellationRequested();
                        _videoPath = string.Empty;
                        return _videoPath;
                    }
                    var options = new DownloadOption()
                    {
                        RequestedUri = _videoUri,
                        Filename = "bg.mp4",
                        CachedFile = videoFile,
                        ForceReDownload = false,
                        Progress = progress
                    };
                    var download = await DownloadFile(options, token);
                    token.ThrowIfCancellationRequested();
                    progress?.Report(1);
                    if (download.Result == DownloadResult.ResourceNotFound)
                    {
                        cacheFlagFile ??= _cacheDirectory.CreateFile("bg.mp4.cache");
                        using var cacheFlagStream = cacheFlagFile.OpenWrite();
                        _videoPath = string.Empty;
                    }
                    else
                    {
                        _videoPath = (download.File ?? throw new FileNotFoundException("The downloaded video is unavailable.")).Location;
                    }
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
        /// <summary>Reads cached chart metadata and downloads the chart when it is missing or belongs to another hash.</summary>
        /// <param name="ignoreCache">Whether to bypass the already parsed in-memory chart.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission and download, and checked around parsing.</param>
        /// <returns>The parsed chart read through its original storage backend.</returns>
        /// <remarks>Cancellation cannot interrupt synchronous provider calls or the upstream tokenless parser.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The chart request failed.</exception>
        /// <exception cref="Exception">The cached chart could not be read or parsed.</exception>
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
                    var maidataFile = _cacheDirectory.FindFile(SongStorage.ChartFileName);
                    SimaiMetadata? metadata = null;
                    if (maidataFile is not null)
                    {
                        metadata = await ReadMetadataAsync(maidataFile, token).ConfigureAwait(false);
                    }
                    var forceReDownload = metadata.HasValue && metadata.Value.Hash != Hash;
                    if (forceReDownload)
                    {
                        MajDebug.LogWarning($"Hash mismatch for maidata of {Id}, re-download");
                    }
                    if (metadata is null || forceReDownload)
                    {
                        var options = new DownloadOption()
                        {
                            RequestedUri = _maidataUri,
                            Filename = SongStorage.ChartFileName,
                            CachedFile = maidataFile,
                            ForceReDownload = forceReDownload,
                            Progress = progress
                        };
                        var download = await DownloadFile(options, token).ConfigureAwait(false);
                        maidataFile = download.File ?? throw new FileNotFoundException("The downloaded chart is unavailable.");
                        metadata = await ReadMetadataAsync(maidataFile, token).ConfigureAwait(false);
                    }
                    token.ThrowIfCancellationRequested();
                    progress?.Report(1);
                    var maidata = await SimaiParser.ParseAsync(metadata.Value).ConfigureAwait(false);
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
            if (_audioTrackRef.TryGetTarget(out var audioTrack))
            {
                audioTrack.Dispose();
            }
            if (_previewAudioTrackRef.TryGetTarget(out audioTrack))
            {
                audioTrack.Dispose();
            }
            UniTask.Post(() =>
            {
                if (_coverRef.TryGetTarget(out var cover) && cover.IsNativeAlive())
                {
                    var tex = cover.texture;
                    GameObject.DestroyImmediate(cover, true);
                    GameObject.DestroyImmediate(tex, true);
                }
                if (_fullSizeCoverRef.TryGetTarget(out cover) && cover.IsNativeAlive())
                {
                    var tex = cover.texture;
                    GameObject.DestroyImmediate(cover, true);
                    GameObject.DestroyImmediate(tex, true);
                }
            });
            _maidata = null;
            if (disposing)
            {
                _audioTrackRef.SetTarget(null!);
                _previewAudioTrackRef.SetTarget(null!);
                _coverRef.SetTarget(null!);
                _fullSizeCoverRef.SetTarget(null!);
            }
            _videoPath = null;
        }
        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (_audioTrackRef.TryGetTarget(out var audioTrack))
            {
                await audioTrack.DisposeAsync();
            }
            if (_previewAudioTrackRef.TryGetTarget(out audioTrack))
            {
                await audioTrack.DisposeAsync();
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                await UniTask.SwitchToMainThread();
                if (_coverRef.TryGetTarget(out var cover) && cover.IsNativeAlive())
                {
                    var tex = cover.texture;
                    GameObject.DestroyImmediate(cover, true);
                    GameObject.DestroyImmediate(tex, true);
                }
                if (_fullSizeCoverRef.TryGetTarget(out cover) && cover.IsNativeAlive())
                {
                    var tex = cover.texture;
                    GameObject.DestroyImmediate(cover, true);
                    GameObject.DestroyImmediate(tex, true);
                }
            }
            _maidata = null;
            _audioTrackRef.SetTarget(null!);
            _previewAudioTrackRef.SetTarget(null!);
            _coverRef.SetTarget(null!);
            _fullSizeCoverRef.SetTarget(null!);
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

        /// <summary>Gets a supported cached track or downloads and optionally decodes the endpoint's MP3 track.</summary>
        /// <param name="loadIntoMemory">Whether the completed download should also be decoded as audio.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling lock admission, download and main-thread admission.</param>
        /// <returns>The decoded track, or an empty audio sample when only downloading.</returns>
        /// <remarks>Audio decoding still requires a loader-compatible resource location.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="HttpException">The track request failed.</exception>
        /// <exception cref="Exception">The cache or audio loader could not provide the requested track.</exception>
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
                        if (_audioTrackRef.TryGetTarget(out var audioTrack))
                        {
                            return audioTrack;
                        }
                        var options = new DownloadOption()
                        {
                            RequestedUri = _trackUri,
                            Filename = "track.mp3",
                            CachedFile = FindCachedFile(SongStorage.SupportedTrackFileNames),
                            ForceReDownload = false,
                            Progress = progress
                        };
                        var download = await DownloadFile(options, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        progress?.Report(1);
                        if (!loadIntoMemory)
                        {
                            return AudioSampleWrap.Empty;
                        }
                        var trackFile = download.File ?? throw new FileNotFoundException("The downloaded track is unavailable.");
                        await UniTask.SwitchToMainThread(token);
                        var sampleWrap = await MajInstances.AudioManager.LoadMusicAsync(trackFile.Location, true, true);
                        _audioTrackRef.SetTarget(sampleWrap);
                        return sampleWrap;
                    }
                }
                catch (Exception e)
                {
                    _audioTrackRef.SetTarget(null!);
                    if (e is not OperationCanceledException)
                    {
                        MajDebug.LogException(e);
                    }
                    throw;
                }
            }
        }
        /// <summary>Gets or downloads the compressed cover through the caller's cache backend.</summary>
        /// <param name="loadIntoMemory">Whether to decode the completed cover download.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and cover loading.</param>
        /// <returns>The compressed cover or the runtime's empty-cover sprite.</returns>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">A cache operation or image loader failed.</exception>
        private ValueTask<Sprite> GetCompressedCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            return GetCoverResourceAsync(false, loadIntoMemory, progress, token);
        }

        /// <summary>Gets or downloads the separate, full-resolution cover through the caller's cache backend.</summary>
        /// <param name="loadIntoMemory">Whether to decode the completed cover download.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and cover loading.</param>
        /// <returns>The full-resolution cover or the runtime's empty-cover sprite.</returns>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">A cache operation or image loader failed.</exception>
        private ValueTask<Sprite> GetFullSizeCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            return GetCoverResourceAsync(true, loadIntoMemory, progress, token);
        }

        /// <summary>Shares backend-preserving cover download logic without mixing full-resolution and compressed caches.</summary>
        /// <param name="isFullSize">Whether to use the full-resolution endpoint and dedicated cache file.</param>
        /// <param name="loadIntoMemory">Whether the selected image should be decoded.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="token">The token canceling admission, download and image decoding.</param>
        /// <returns>The selected cover or the runtime's empty-cover sprite.</returns>
        /// <remarks>Image decoding still requires a loader-compatible file location.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
        /// <exception cref="Exception">A cache operation or image loader failed.</exception>
        private async ValueTask<Sprite> GetCoverResourceAsync(bool isFullSize, bool loadIntoMemory, INetProgress? progress, CancellationToken token)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            var coverReference = isFullSize ? _fullSizeCoverRef : _coverRef;
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
                        if (coverReference.TryGetTarget(out var cover) && await cover.IsNativeAliveAsync())
                        {
                            return cover;
                        }
                        var filename = isFullSize ? "bg_fullSize.jpg" : "bg.jpg";
                        StorageFile? coverFile;
                        try
                        {
                            var options = new DownloadOption()
                            {
                                RequestedUri = isFullSize ? _fullSizeCoverUri : _coverUri,
                                Filename = filename,
                                CachedFile = isFullSize ? _cacheDirectory.FindFile(filename) : FindCachedFile(SongStorage.SupportedCoverFileNames),
                                ForceReDownload = false,
                                Progress = progress
                            };
                            var download = await DownloadFile(options, token).ConfigureAwait(false);
                            coverFile = download.File;
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
                        coverReference.SetTarget(cover);
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

        /// <summary>Rejects resource operations after this song's owned media has been released.</summary>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnlineSongDetail));
            }
        }
        /// <summary>Finds a cached file by the centralized, case-insensitive supported-resource names.</summary>
        /// <param name="supportedFileNames">The shared filename table for the requested resource kind.</param>
        /// <returns>The original-backend file handle, or null when no supported resource is cached.</returns>
        /// <exception cref="DirectoryNotFoundException">The cache directory no longer exists.</exception>
        /// <exception cref="IOException">The cache cannot be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">Cache enumeration was denied.</exception>
        private StorageFile? FindCachedFile(IEnumerable<string> supportedFileNames)
        {
            foreach (var entry in _cacheDirectory.EnumerateEntries())
            {
                if (!entry.IsDirectory && supportedFileNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                {
                    return new StorageFile(_cacheDirectory.FileSystem, entry.Location);
                }
            }
            return null;
        }

        /// <summary>Buffers original-backend chart bytes before invoking the parser which requires a seekable stream.</summary>
        /// <param name="file">The chart file retaining the original cache backend.</param>
        /// <param name="token">The token canceling reads and checked around the tokenless metadata parser.</param>
        /// <returns>The parsed Simai metadata, including its computed chart hash.</returns>
        /// <exception cref="OperationCanceledException">Reading was canceled or cancellation was observed around parsing.</exception>
        /// <exception cref="IOException">The provider could not read the chart.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied chart access.</exception>
        /// <exception cref="NotSupportedException">The provider does not support reading.</exception>
        /// <exception cref="InvalidSimaiMarkupException">The cached chart metadata is malformed.</exception>
        private static async Task<SimaiMetadata> ReadMetadataAsync(StorageFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bytes = await file.ReadAllBytesAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes, false);
            var metadata = await SimaiParser.ParseMetadataAsync(stream).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return metadata;
        }

        /// <summary>Reuses or downloads one resource and returns its authoritative cache handle after any rename.</summary>
        /// <param name="options">The resource URI, default filename and optional supported cached-file handle.</param>
        /// <param name="token">The token canceling provider reads, HTTP requests and stream transfers.</param>
        /// <returns>The download outcome and completed file handle, or null when no resource was obtained.</returns>
        /// <remarks>Synchronous provider and hash operations cannot be interrupted while blocked.</remarks>
        /// <exception cref="ObjectDisposedException">This song has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The caller canceled the transfer.</exception>
        /// <exception cref="DirectoryNotFoundException">The caller's cache directory no longer exists.</exception>
        /// <exception cref="HttpException">The request or integrity validation failed after applicable retries.</exception>
        /// <exception cref="IOException">Initial cache discovery or cleanup failed.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied initial cache access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot perform a required initial cache operation.</exception>
        private async Task<(DownloadResult Result, StorageFile? File)> DownloadFile(DownloadOption options, CancellationToken token = default)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            if (!_cacheDirectory.Exists)
            {
                throw new DirectoryNotFoundException("The caller's song-cache directory does not exist.");
            }
            var storageDirectory = _cacheDirectory;
            var saveFile = options.CachedFile ?? storageDirectory.FindFile(options.Filename);
            var cachedName = saveFile?.Entry?.Name ?? options.Filename;
            var chunkFile = storageDirectory.FindFile($"{options.Filename}.chunk");
            var hashFile = storageDirectory.FindFile($"{cachedName}.sha256");

            var httpClient = MajEnv.SharedHttpClient;
            var expectedSHA256 = default(string?);

            if (options.ForceReDownload)
            {
                DeleteFileIfExists(saveFile);
                DeleteFileIfExists(chunkFile);
                chunkFile = null;
                DeleteFileIfExists(hashFile);
                hashFile = null;
            }
            else
            {
                if (hashFile is not null)
                {
                    expectedSHA256 = await hashFile.ReadAllTextAsync(cancellationToken: token);
                }

                if (saveFile is not null && saveFile.Exists)
                {
                    if (VerifyFileIntegrity(saveFile, expectedSHA256))
                    {
                        token.ThrowIfCancellationRequested();
                        return (DownloadResult.Success, saveFile);
                    }

                    MajDebug.LogDebug(nameof(OnlineSongDetail), $"The complete file is corrupted, re-download it: {options.RequestedUri}");
                    DeleteFileIfExists(saveFile);
                    DeleteFileIfExists(hashFile);
                    hashFile = null;
                    expectedSHA256 = null;
                }
                else if (chunkFile is not null && chunkFile.Exists)
                {
                    // A chunk without a server digest may still be incomplete and must not be promoted.
                    if (!string.IsNullOrEmpty(expectedSHA256) && VerifyFileIntegrity(chunkFile, expectedSHA256))
                    {
                        token.ThrowIfCancellationRequested();
                        return (DownloadResult.Success, chunkFile.Rename(options.Filename));
                    }

                    MajDebug.LogDebug(nameof(OnlineSongDetail), $"Found incomplete chunk file: {options.RequestedUri}");
                }
            }

            var requestUri = options.RequestedUri;
            var progress = options.Progress;

            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var existingChunkLength = chunkFile?.Entry?.Length ?? 0;

                    using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);

                    if (existingChunkLength > 0)
                    {
                        req.Headers.Range = HttpRange.From(existingChunkLength).ToRangeHeaderValue();
                    }

                    using var rsp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);

                    if (rsp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"Range {existingChunkLength}- not satisfiable. Resetting chunk.");
                        DeleteFileIfExists(chunkFile);
                        chunkFile = null;
                        continue;
                    }
                    else if (rsp.StatusCode == HttpStatusCode.NotFound)
                    {
                        return (DownloadResult.ResourceNotFound, null);
                    }
                    else if (!rsp.IsSuccessStatusCode)
                    {
                        throw new HttpException(requestUri.OriginalString, HttpErrorCode.Unsuccessful, rsp.StatusCode);
                    }

                    MajDebug.LogDebug(nameof(OnlineSongDetail), $"Received http response {(int)rsp.StatusCode} from: {requestUri}");

                    var isPartial = rsp.StatusCode == HttpStatusCode.PartialContent;
                    if (!isPartial && existingChunkLength > 0)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"Server does not support partial download. Falling back to full download.");
                        existingChunkLength = 0;
                        DeleteFileIfExists(chunkFile);
                        chunkFile = null;
                    }

                    if (string.IsNullOrEmpty(expectedSHA256))
                    {
                        if (rsp.Headers.TryGetValues("hash", out var values))
                        {
                            expectedSHA256 = values.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                            if (!string.IsNullOrEmpty(expectedSHA256))
                            {
                                var hashStorageFile = storageDirectory.FindFile($"{options.Filename}.sha256")
                                    ?? storageDirectory.CreateFile($"{options.Filename}.sha256", "text/plain");
                                await hashStorageFile.WriteAllTextAsync(expectedSHA256, cancellationToken: token);
                            }
                        }
                    }

                    var totalBytesToReceive = rsp.Content.Headers.ContentLength ?? 0;
                    var totalFileBytes = isPartial ? totalBytesToReceive + existingChunkLength : totalBytesToReceive;
                    if (progress is not null)
                    {
                        progress.TotalBytes = totalFileBytes;
                    }

                    var isIntegrityValid = true;
                    var chunkStorageFile = chunkFile ?? storageDirectory.FindFile($"{options.Filename}.chunk")
                        ?? storageDirectory.CreateFile($"{options.Filename}.chunk");
                    chunkFile = chunkStorageFile;
                    using (var chunkFileStream = chunkStorageFile.OpenWrite(append: isPartial))
                    {
                        using var httpStream = await rsp.Content.ReadAsStreamAsync();

                        await DownloadStreamWithProgressAsync(httpStream, chunkFileStream, progress, existingChunkLength, totalFileBytes, token);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(expectedSHA256))
                    {
                        isIntegrityValid = VerifyFileIntegrity(chunkStorageFile, expectedSHA256);
                    }

                    if (!isIntegrityValid)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"Hash mismatch for online resource. Origin: {expectedSHA256}");
                        DeleteFileIfExists(chunkFile);
                        chunkFile = null;

                        if (i == MajEnv.HTTP_REQUEST_MAX_RETRY)
                        {
                            throw new HttpException(requestUri.OriginalString, HttpErrorCode.IntegrityCheckFailed);
                        }
                        continue;
                    }

                    token.ThrowIfCancellationRequested();
                    return (DownloadResult.Success, chunkStorageFile.Rename(options.Filename));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    MajDebug.LogWarning(nameof(OnlineSongDetail), $"Request for resource \"{requestUri}\" was canceled");
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
                            throw new HttpException(requestUri.OriginalString, HttpErrorCode.InvalidRequest);
                        }
                        if (e is TaskCanceledException)
                        {
                            throw new HttpException(requestUri.OriginalString, HttpErrorCode.Timeout);
                        }

                        MajDebug.LogError(nameof(OnlineSongDetail), $"Failed to request resource: {requestUri}\n{e}");
                        throw new HttpException(requestUri.OriginalString, HttpErrorCode.Unreachable);
                    }
                }
            }
            return (DownloadResult.Failed, null);
        }

        /// <summary>Copies response bytes to the provider stream while reporting bounded download progress.</summary>
        /// <param name="src">The readable HTTP response stream.</param>
        /// <param name="dst">The writable original-backend chunk stream.</param>
        /// <param name="progress">The optional download progress receiver.</param>
        /// <param name="existingLength">The already cached bytes preceding this response.</param>
        /// <param name="totalFileBytes">The complete resource length when known, otherwise zero.</param>
        /// <param name="token">The token canceling stream transfers and the final flush.</param>
        /// <returns>A task completing after all response bytes have been transferred and flushed.</returns>
        /// <exception cref="OperationCanceledException">The transfer was canceled.</exception>
        /// <exception cref="IOException">Reading or writing a stream failed.</exception>
        private async Task DownloadStreamWithProgressAsync(Stream src, Stream dst, INetProgress? progress, long existingLength, long totalFileBytes, CancellationToken token)
        {
            var bufferSize = MajEnv.HTTP_BUFFER_SIZE;
            using (var readBuffer = Pool<byte>.Rent(bufferSize, true))
            {
                var buffer = readBuffer.AsMemory();
                var read = 0;
                var totalRead = existingLength;

                while ((read = await src.ReadAsync(buffer, token)) > 0)
                {
                    await dst.WriteAsync(buffer.Slice(0, read), token);
                    totalRead += read;

                    if (progress is not null)
                    {
                        progress.TransferredBytes = totalRead;
                        var percent = totalFileBytes != 0 ? (float)progress.TransferredBytes / totalFileBytes : 0f;
                        progress.Report(Mathf.Clamp01(percent));
                    }
                }
                await dst.FlushAsync(token);
            }
        }

        /// <summary>Verifies a cached file against its optional server-provided digest without rebinding its backend.</summary>
        /// <param name="file">The original-backend file to verify.</param>
        /// <param name="targetHash">The expected Base64-encoded SHA-256 digest, or null when the server supplied none.</param>
        /// <returns>True when no digest is supplied or the file's digest matches; otherwise false.</returns>
        /// <exception cref="IOException">The backend could not read the cached file.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied file access.</exception>
        /// <exception cref="NotSupportedException">The backend cannot open the cached file.</exception>
        private static bool VerifyFileIntegrity(StorageFile file, string? targetHash)
        {
            if (string.IsNullOrEmpty(targetHash))
            {
                return true;
            }
            using var stream = file.OpenRead();
            return VerifyStreamIntegrity(stream, targetHash!);
        }

        /// <summary>Computes and compares the stream's SHA-256 digest with a case-sensitive Base64 digest.</summary>
        /// <param name="stream">The readable stream positioned at its resource bytes.</param>
        /// <param name="targetHash">The expected Base64-encoded SHA-256 digest.</param>
        /// <returns>True when the computed digest matches the expected digest; otherwise false.</returns>
        /// <exception cref="IOException">Reading the stream failed.</exception>
        /// <exception cref="CryptographicException">The SHA-256 implementation failed.</exception>
        private static bool VerifyStreamIntegrity(Stream stream, string targetHash)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            var currentHash = Convert.ToBase64String(hashBytes);
            return string.Equals(targetHash, currentHash, StringComparison.Ordinal);
        }

        /// <summary>Deletes an optional original-backend cache handle without reconstructing its location.</summary>
        /// <param name="file">The file to remove, or null when no file was found.</param>
        /// <exception cref="IOException">The backend could not delete the file.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied deletion.</exception>
        /// <exception cref="NotSupportedException">The backend does not support deletion.</exception>
        private static void DeleteFileIfExists(StorageFile? file)
        {
            file?.Delete();
        }

        /// <summary>Describes one endpoint resource and its optional supported cached file.</summary>
        private readonly struct DownloadOption
        {
            /// <summary>Gets the endpoint URI used when no valid cached resource is available.</summary>
            public Uri RequestedUri { get; init; }
            /// <summary>Gets the single default child name used for a newly downloaded resource.</summary>
            public string Filename { get; init; }
            /// <summary>Gets an existing supported cache handle retaining the caller's original backend.</summary>
            public StorageFile? CachedFile { get; init; }
            /// <summary>Gets whether cached complete, partial and digest files must be discarded.</summary>
            public bool ForceReDownload { get; init; }
            /// <summary>Gets the optional download progress receiver.</summary>
            public INetProgress? Progress { get; init; }
        }

        /// <summary>Identifies whether an endpoint resource was obtained.</summary>
        private enum DownloadResult
        {
            /// <summary>No download outcome has been assigned.</summary>
            None,
            /// <summary>A valid cached or downloaded resource is available.</summary>
            Success,
            /// <summary>Applicable retries were exhausted without obtaining a resource.</summary>
            Failed,
            /// <summary>The endpoint reported that the optional resource does not exist.</summary>
            ResourceNotFound
        }
    }
}
