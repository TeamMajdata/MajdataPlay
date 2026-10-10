using Cysharp.Threading.Tasks;
using MajdataPlay.Databases;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using MajSimai;
using Nito.AsyncEx;
using System;
using System.Linq;
using System.Runtime.InteropServices;
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
        public bool IsCoverLoaded { get => _coverRef.TryGetTarget(out _); }
        public bool IsCompressedCoverLoaded { get => _coverRef.TryGetTarget(out _); }
        public bool IsAudioTrackLoaded { get => _audioTrackRef.TryGetTarget(out _); }
        public bool IsPreviewAudioTrackLoaded { get => _previewAudioTrackRef.TryGetTarget(out _); }
        public bool IsMaidataLoaded { get => _maidata != null; }

        readonly string _maidataPath = string.Empty;
        readonly string _trackPath = string.Empty;
        readonly string _videoPath = string.Empty;
        readonly string _coverPath = string.Empty;

        bool _isPreloaded = false;
        bool _isDisposed = false;

        WeakReference<AudioSampleWrap> _audioTrackRef = new(null!);
        WeakReference<AudioSampleWrap> _previewAudioTrackRef = new(null!);
        WeakReference<Sprite> _coverRef = new(null!);
        SimaiFile? _maidata = null;
        SimaiMetadata _simaiMetadata;

        volatile int _preloadJoinState = 0;

        readonly bool _isEmptyCover = false;
        readonly AsyncLock _previewAudioTrackLock = new();
        readonly AsyncLock _audioTrackLock = new();
        readonly AsyncLock _coverLock = new();
        readonly AsyncLock _maidataLock = new();

        ~SongDetail()
        {
            Dispose(false);
        }
        public SongDetail(string chartFolder, SimaiMetadata metadata)
        {
            var chartDirectory = FileSystem.Directory.Open(chartFolder);
            var files = chartDirectory.EnumerateEntries().Where(x => !x.IsDirectory).ToArray();
            var videoBGFilename = new string[3]
            {
                "bg",
                "pv",
                "mv"
            };

            _maidataPath = chartDirectory.FindFile("maidata.txt")!.Location;
            _trackPath = files.FirstOrDefault(o => o.Name.ToLower() is "track.opus" or "track.mp3" or "track.ogg" or "track.aac" or "track.wav")!.Location;
            _videoPath = files.FirstOrDefault(o =>
            {
                var thisFilename = o.Name.ToLower();
                foreach (var filename in videoBGFilename)
                {
                    foreach(var ext in MajEnv.SUPPORTED_VIDEO_FORMAT)
                    {
                        if(thisFilename == filename + ext)
                        {
                            return true;
                        }
                    }
                }
                return false;
            })?.Location ?? string.Empty;
            _coverPath = files.FirstOrDefault(o => o.Name.ToLower() is "bg.png" or "bg.jpg")?.Location ?? string.Empty;
            _maidata = null;

            if (string.IsNullOrEmpty(_coverPath))
            {
                _isEmptyCover = true;
            }
            _simaiMetadata = metadata;
            Title = metadata.Title;
            Artist = metadata.Artist;
            Timestamp = files.FirstOrDefault(x => x.Name is "maidata.txt")?.LastWriteTimeUtc?.ToLocalTime() ?? DateTime.UnixEpoch;
        }
        public static async Task<SongDetail> ParseAsync(string chartFolder)
        {
            var maidataFile = FileSystem.Directory.Open(chartFolder).FindFile("maidata.txt")
                ?? throw new System.IO.FileNotFoundException("The chart does not contain maidata.txt.");
            using var maidataStream = maidataFile.OpenRead();
            var metadata = await SimaiParser.ParseMetadataAsync(maidataStream);

            return new SongDetail(chartFolder, metadata);
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
            if(_isEmptyCover)
            {
                return Majdata<GameRuntime>.Instance!.Sprite.EmptySongCover;
            }
            if (_coverRef.TryGetTarget(out var cover) && await cover.IsNativeAliveAsync())
            {
                return cover;
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                await UniTask.SwitchToThreadPool();
                using (await _coverLock.LockAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (_coverRef.TryGetTarget(out cover) && await cover.IsNativeAliveAsync())
                    {
                        return cover;
                    }
                    progress?.Report(1);
                    cover = await SpriteLoader.LoadFromFileAsync(_coverPath, true, token);
                    _coverRef.SetTarget(cover);
                    return cover;
                }
            }
        }
        public async ValueTask<AudioSampleWrap> GetAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (_audioTrackRef.TryGetTarget(out var audioTrack))
            {
                return audioTrack;
            }
            await using (UniTask.ReturnToCurrentSynchronizationContext())
            {
                await UniTask.SwitchToThreadPool();
                using (await _audioTrackLock.LockAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (_audioTrackRef.TryGetTarget(out audioTrack))
                    {
                        return audioTrack;
                    }
                    progress?.Report(1);
                    audioTrack = await MajInstances.AudioManager.LoadMusicAsync(_trackPath, true, true);
                    _audioTrackRef.SetTarget(audioTrack);
                    return audioTrack;
                }
            }
                
        }
        public async ValueTask<AudioSampleWrap> GetPreviewAudioTrackAsync(INetProgress? progress = null, CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (_previewAudioTrackRef.TryGetTarget(out var audioTrack))
            {
                return audioTrack;
            }
            await UniTask.SwitchToThreadPool();
            using (await _previewAudioTrackLock.LockAsync(token))
            {
                token.ThrowIfCancellationRequested();
                if (_previewAudioTrackRef.TryGetTarget(out audioTrack))
                {
                    return audioTrack;
                }
                progress?.Report(1);
                audioTrack = await MajInstances.AudioManager.LoadMusicAsync(_trackPath, true, false);
                _previewAudioTrackRef.SetTarget(audioTrack);
                return audioTrack;
            }
        }
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
                using var fileStream = FileSystem.File.Open(_maidataPath).OpenRead();
                progress?.Report(1);
                var metadata = await SimaiParser.ParseMetadataAsync(fileStream);
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
            });
            if(disposing)
            {
                _audioTrackRef.SetTarget(null!);
                _previewAudioTrackRef.SetTarget(null!);
                _coverRef.SetTarget(null!);
            }
            _maidata = null;
        }
        public ValueTask DisposeAsync()
        {
            return DisposeAsync(true);
        }
        async ValueTask DisposeAsync(bool disposing)
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
            }
            if(disposing)
            {
                _audioTrackRef.SetTarget(null!);
                _previewAudioTrackRef.SetTarget(null!);
                _coverRef.SetTarget(null!);
            }
            _maidata = null;
        }
        void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SongDetail));
            }
        }
    }
}
