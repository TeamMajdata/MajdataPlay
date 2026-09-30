using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Numerics;
using MajdataPlay.Utils;
using MajdataPlay.Drawing;
using MajSimai;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using MajdataPlay.Settings;
using Nito.AsyncEx;
using MajdataPlay.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MajdataPlay.Databases;

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

        readonly string _hashHexStr = string.Empty;
        readonly string _cachePath = string.Empty;
        readonly ApiEndpoint _serverInfo;

        readonly Uri _maidataUri;
        readonly Uri _trackUri;
        readonly Uri _videoUri;
        readonly Uri _fullSizeCoverUri;
        readonly Uri _coverUri;

        readonly string[] _designers = new string[7];
        readonly string[] _levels = new string[7];

        bool _isDisposed = false;
        bool _isPreloaded = false;

        string? _videoPath = null;
        WeakReference<AudioSampleWrap> _audioTrackRef = new(null!);
        WeakReference<AudioSampleWrap> _previewAudioTrackRef = new(null!);
        WeakReference<Sprite> _coverRef = new(null!);
        WeakReference<Sprite> _fullSizeCoverRef = new(null!);
        SimaiFile? _maidata = null;

        volatile int _preloadJoinState = 0;

        readonly AsyncLock _previewAudioTrackLock = new();
        readonly AsyncLock _audioTrackLock = new();
        readonly AsyncLock _videoPathLock = new();
        readonly AsyncLock _coverLock = new();
        readonly AsyncLock _fullSizeCoverLock = new();
        readonly AsyncLock _maidataLock = new();

        ~OnlineSongDetail()
        {
            Dispose(false);
        }
        public OnlineSongDetail(ApiEndpoint serverInfo, MajnetSongDetail songDetail)
        {
            var apiroot = serverInfo.Url.Combine($"maichart/{songDetail.Id}/");
            
            Title = songDetail.Title;
            Artist = songDetail.Artist;
            for (var i = 0; i < 7; i++)
            {
                if(i >= songDetail.Levels.Length)
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
            _hashHexStr = HashHelper.ToHexString(Convert.FromBase64String(Hash));
            _serverInfo = serverInfo;
            _cachePath = Path.Combine(MajEnv.CachePath, $"Net/{_serverInfo.Name}/{_hashHexStr}");
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
        public async ValueTask<string> GetVideoPathAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
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
                    var savePath = Path.Combine(_cachePath, "bg.mp4");
                    var cacheFlagPath = Path.Combine(_cachePath, $"bg.mp4.cache");

                    if (File.Exists(cacheFlagPath) && !File.Exists(savePath))
                    {
                        _videoPath = string.Empty;
                        return _videoPath;
                    }
                    var options = new DownloadOption()
                    {
                        RequestedUri = _videoUri,
                        SaveTo = _cachePath,
                        Filename = "bg.mp4",
                        Name = "bg",
                        Extension = ".mp4",
                        ForceReDownload = false,
                        Progress = progress
                    };
                    var result = await DownloadFile(options, token);
                    progress?.Report(1);
                    if (result == DownloadResult.ResourceNotFound)
                    {
                        using var _ = File.Create(cacheFlagPath);
                        _videoPath = string.Empty;
                    }
                    else
                    {
                        _videoPath = savePath;
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
        public async ValueTask<SimaiFile> GetMaidataAsync(bool ignoreCache = false, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
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
                    var savePath = Path.Combine(_cachePath, "maidata.txt");
                    var forceReDl = false;
                    var shouldFetch = false;

                    if (File.Exists(savePath))
                    {
                        using var fileStream = File.OpenRead(savePath);
                        var metadata = await SimaiParser.ParseMetadataAsync(fileStream);
                        if (metadata.Hash != Hash)
                        {
                            MajDebug.LogWarning($"Hash mismatch for maidata of {Id}, re-download");
                            forceReDl = true;
                            shouldFetch = true;
                        }
                    }
                    else
                    {
                        shouldFetch = true;
                    }
                    if(shouldFetch)
                    {
                        var options = new DownloadOption()
                        {
                            RequestedUri = _maidataUri,
                            SaveTo = _cachePath,
                            Filename = "maidata.txt",
                            Name = "maidata",
                            Extension = ".txt",
                            ForceReDownload = forceReDl,
                            Progress = progress
                        };
                        await DownloadFile(options, token);
                    }
                    progress?.Report(1);
                    _maidata = await SimaiParser.ParseAsync(File.OpenRead(savePath));
                    return _maidata;
                }
            }
            catch (HttpException ex)
            {
                MajDebug.LogException(ex);
                throw;
            }
            catch (Exception e)
            {
                if (e is not OperationCanceledException)
                {
                    MajDebug.LogException(e);
                }
                _maidata = null;
                throw new Exception("Maidata Load Failed");
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
        void Dispose(bool disposing)
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
            if(disposing)
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

        async ValueTask<AudioSampleWrap> GetAudioTrackAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
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
                        var savePath = Path.Combine(_cachePath, "track.mp3");
                        var options = new DownloadOption()
                        {
                            RequestedUri = _trackUri,
                            SaveTo = _cachePath,
                            Filename = "track.mp3",
                            Name = "track",
                            Extension = ".mp3",
                            ForceReDownload = false,
                            Progress = progress
                        };

                        await DownloadFile(options, token);
                        progress?.Report(1);
                        if (!loadIntoMemory)
                        {
                            return AudioSampleWrap.Empty;
                        }
                        var sampleWarp = await MajInstances.AudioManager.LoadMusicAsync(savePath, true, true);
                        _audioTrackRef.SetTarget(sampleWarp);

                        return sampleWarp;
                    }
                }
                catch (Exception e)
                {
                    _audioTrackRef.SetTarget(null!);
                    if (e is not OperationCanceledException)
                    {
                        MajDebug.LogException(e);
                        throw e;
                    }
                    
                    throw new InvalidAudioTrackException("Music track Load Failed", Path.Combine(_cachePath, "track.mp3"));
                }
            }
        }
        async ValueTask<Sprite> GetCompressedCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                try
                {
                    await UniTask.SwitchToThreadPool();
                    var @lock = await _coverLock.LockAsync(token);
                    using (@lock)
                    {
                        token.ThrowIfCancellationRequested();
                        if (_coverRef.TryGetTarget(out var cover) && await cover.IsNativeAliveAsync())
                        {
                            return cover;
                        }
                        var savePath = Path.Combine(_cachePath, "bg.jpg");

                        try
                        {
                            var options = new DownloadOption()
                            {
                                RequestedUri = _coverUri,
                                SaveTo = _cachePath,
                                Filename = "bg.jpg",
                                Name = "bg",
                                Extension = ".jpg",
                                ForceReDownload = false,
                                Progress = progress
                            };
                            await DownloadFile(options, token);
                            if (!loadIntoMemory)
                            {
                                return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
                            }
                        }
                        catch (HttpException e)
                        {
                            MajDebug.LogException(e);
                            return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
                        }
                        finally
                        {
                            progress?.Report(1);
                        }

                        token.ThrowIfCancellationRequested();
                        cover = await SpriteLoader.LoadFromFileAsync(savePath, true, token);

                        _coverRef.SetTarget(cover);
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
        async ValueTask<Sprite> GetFullSizeCoverAsync(bool loadIntoMemory, INetProgress? progress = null, CancellationToken token = default)
        {
            try
            {
                await UniTask.SwitchToThreadPool();
                var @lock = await _fullSizeCoverLock.LockAsync(token);
                using (@lock)
                {
                    token.ThrowIfCancellationRequested();
                    if (_fullSizeCoverRef.TryGetTarget(out var cover) && await cover.IsNativeAliveAsync())
                    {
                        return cover;
                    }
                    var savePath = Path.Combine(_cachePath, "bg_fullSize.jpg");
                    
                    try
                    {
                        var options = new DownloadOption()
                        {
                            RequestedUri = _fullSizeCoverUri,
                            SaveTo = _cachePath,
                            Filename = "bg_fullSize.jpg",
                            Name = "bg_fullSize",
                            Extension = ".jpg",
                            ForceReDownload = false,
                            Progress = progress
                        };
                        await DownloadFile(options, token);
                        if (!loadIntoMemory)
                        {
                            return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
                        }
                    }
                    catch (HttpException e)
                    {
                        MajDebug.LogException(e);
                        return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
                    }
                    finally
                    {
                        progress?.Report(1);
                    }
                    token.ThrowIfCancellationRequested();
                    cover = await SpriteLoader.LoadFromFileAsync(savePath, true, token);

                    _fullSizeCoverRef.SetTarget(cover);
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
        
        void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnlineSongDetail));
            }
        }
        private void EnsureCachePath()
        {
            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }
        }

        async Task<DownloadResult> DownloadFile(DownloadOption options, CancellationToken token = default)
        {
            EnsureCachePath();

            var savePath = Path.Combine(options.SaveTo, options.Filename);
            var chunkPath = Path.Combine(options.SaveTo, $"{options.Filename}.chunk");
            var hashPath = Path.Combine(options.SaveTo, $"{options.Filename}.sha256");

            var httpClient = MajEnv.SharedHttpClient;
            var expectedSHA256 = default(string?);

            if (options.ForceReDownload)
            {
                DeleteFileIfExists(savePath);
                DeleteFileIfExists(chunkPath);
                DeleteFileIfExists(hashPath);
            }
            else
            {
                if (File.Exists(hashPath))
                {
                    expectedSHA256 = await File.ReadAllTextAsync(hashPath, token);
                }

                if (File.Exists(savePath))
                {
                    if (VerifyFileIntegrity(savePath, expectedSHA256))
                    {
                        return DownloadResult.Success;
                    }

                    MajDebug.LogDebug(nameof(OnlineSongDetail), $"The complete file is corrupted, re-download it: {options.RequestedUri}");
                    DeleteFileIfExists(savePath);
                    DeleteFileIfExists(hashPath);
                    expectedSHA256 = null;
                }
                else if (File.Exists(chunkPath))
                {
                    if (VerifyFileIntegrity(chunkPath, expectedSHA256))
                    {
                        File.Move(chunkPath, savePath);
                        return DownloadResult.Success;
                    }

                    MajDebug.LogDebug(nameof(OnlineSongDetail), $"Found incomplete chunk file: {options.RequestedUri}");
                }
            }

            var requestUri = options.RequestedUri;
            var progress = options.Progress;

            for (var i = 0; i <= MajEnv.HTTP_REQUEST_MAX_RETRY; i++)
            {
                try
                {
                    var existingChunkLength = File.Exists(chunkPath) ? new FileInfo(chunkPath).Length : 0;

                    using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);

                    if (existingChunkLength > 0)
                    {
                        req.Headers.Range = HttpRange.From(existingChunkLength).ToRangeHeaderValue();
                    }

                    using var rsp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);

                    if (rsp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"Range {existingChunkLength}- not satisfiable. Resetting chunk.");
                        DeleteFileIfExists(chunkPath);
                        continue;
                    }
                    else if (rsp.StatusCode == HttpStatusCode.NotFound)
                    {
                        return DownloadResult.ResourceNotFound;
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
                        DeleteFileIfExists(chunkPath);
                    }

                    if (string.IsNullOrEmpty(expectedSHA256))
                    {
                        if((rsp.Headers.TryGetValues("hash", out var values) || rsp.Headers.TryGetValues("Hash", out values)))
                        {
                            expectedSHA256 = values.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                            if (!string.IsNullOrEmpty(expectedSHA256))
                            {
                                await File.WriteAllTextAsync(hashPath, expectedSHA256);
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
                    var fileMode = isPartial ? FileMode.OpenOrCreate : FileMode.Create;

                    using (var chunkFileStream = new FileStream(chunkPath, fileMode, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (isPartial)
                        {
                            chunkFileStream.Seek(0, SeekOrigin.End);
                        }

                        using var httpStream = await rsp.Content.ReadAsStreamAsync();

                        await DownloadStreamWithProgressAsync(httpStream, chunkFileStream, progress, existingChunkLength, totalFileBytes, token);

                        if (!string.IsNullOrEmpty(expectedSHA256))
                        {
                            isIntegrityValid = VerifyStreamIntegrity(chunkFileStream, expectedSHA256);
                        }
                    }

                    if (!isIntegrityValid)
                    {
                        MajDebug.LogWarning(nameof(OnlineSongDetail), $"Hash mismatch for online resource. Origin: {expectedSHA256}");
                        DeleteFileIfExists(chunkPath);

                        if (i == MajEnv.HTTP_REQUEST_MAX_RETRY)
                        {
                            throw new HttpException(requestUri.OriginalString, HttpErrorCode.IntegrityCheckFailed);
                        }
                        continue;
                    }

                    File.Move(chunkPath, savePath);

                    return DownloadResult.Success;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    MajDebug.LogWarning(nameof(OnlineSongDetail), $"Request for resource \"{requestUri}\" was canceled");
                    throw new HttpException(requestUri.OriginalString, HttpErrorCode.Canceled);
                }
                catch (Exception e) when (e is not HttpException || ((HttpException)e).ErrorCode == HttpErrorCode.IntegrityCheckFailed)
                {
                    if (i == MajEnv.HTTP_REQUEST_MAX_RETRY)
                    {
                        if (e is HttpException httpEx)
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
            return DownloadResult.Failed;
        }

        private async Task DownloadStreamWithProgressAsync(Stream src, Stream dst, INetProgress? progress, long existingLength, long totalFileBytes, CancellationToken token)
        {
            var bufferSize = MajEnv.HTTP_BUFFER_SIZE;
            using (var readBuffer = Pool<byte>.Rent(bufferSize, true))
            {
                var buffer = readBuffer.AsMemory();
                int read;
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

        private bool VerifyFileIntegrity(string filePath, string? targetHash)
        {
            if (string.IsNullOrEmpty(targetHash))
            {
                return true;
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return VerifyStreamIntegrity(fs, targetHash);
        }

        private bool VerifyStreamIntegrity(Stream stream, string targetHash)
        {
            stream.Position = 0;
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            var currentHash = Convert.ToBase64String(hashBytes);

            return string.Equals(targetHash, currentHash, StringComparison.OrdinalIgnoreCase);
        }

        private void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        readonly struct DownloadOption
        {
            public Uri RequestedUri { get; init; }
            public string SaveTo { get; init; }
            /// <summary>
            /// e.g: maidata.txt
            /// </summary>
            public string Filename { get; init; }
            /// <summary>
            /// e.g: maidata
            /// </summary>
            public string Name { get; init; }
            /// <summary>
            /// e.g: .txt
            /// </summary>
            public string Extension { get; init; }
            public bool ForceReDownload { get; init; }
            public INetProgress? Progress { get; init; }
        }

        enum DownloadResult
        {
            None,
            Success,
            Failed,
            ResourceNotFound
        }
    }
}
