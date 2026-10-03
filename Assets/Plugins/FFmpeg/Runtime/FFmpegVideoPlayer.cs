using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Video.Internal;
using MajdataPlay.Diagnostics;
using UnityEngine;

namespace MajdataPlay.Video
{
    public enum VideoPlaybackState { Idle, Preparing, Prepared, Playing, Paused, Seeking, Stopped, Ended, Error }
    public enum VideoDecoderType { Software, Hardware }

    /// <summary>
    /// Silent video player. All public methods/events run on the Unity main thread.
    /// Decode and input run on a bounded background worker. Time is milliseconds; time is seconds.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Video/FFmpeg Video Player")]
    public sealed partial class FFmpegVideoPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("Local path or FFmpeg-supported URL. Android packaged StreamingAssets must first be extracted.")]
        string _source = "";
        [SerializeField] bool _playOnAwake;
        [SerializeField] bool _loop;
        [SerializeField, Range(0.0625f, 16)] float _playbackRate = 1;
        [SerializeField, Range(1, 8)] int _bufferedFrameLimit = 3;
        [SerializeField, Min(1)] int _ioTimeoutSeconds = 15;
        // Retain the serialized bool so existing scenes keep their decoder preference.
        [SerializeField, HideInInspector]
        bool _preferHardwareDecoding = true;
        [SerializeField, Tooltip("Prefer native GPU texture sharing. If unavailable, retain hardware decoding with CPU upload when supported. Applies when opening media.")]
        bool _preferNativeTextures = true;
        [SerializeField, Tooltip("Require native GPU frames. Unsupported codecs/devices report an error instead of uploading CPU pixels. Applies when opening media.")]
        bool _requireHardwareDecoding;
        [SerializeField] Renderer _targetRenderer;
        [SerializeField] string _textureProperty = "_MainTex";
        [SerializeField, Tooltip("Optional output; otherwise use Texture or TextureChanged.")]
        RenderTexture _targetTexture;

        readonly PlaybackClock _clock = new PlaybackClock();
        VideoDecodeSession _session;
        VideoInfo _info;
        Texture2D _uploadTexture;
        Texture _texture;
        MaterialPropertyBlock _materialProperties;
        TaskCompletionSource<bool> _prepareCompletion, _seekCompletion;
        CancellationToken _prepareCancellation;
        bool _playWhenReady, _waitingForFrame, _prepared, _hardwareActive, _hardwareRequired, _hardwareCpuUploadAttempted, _stepRequested;
        string _reportedTransferMode;
        VideoPlaybackState _afterSeek;
        double _seekTarget, _lastFrameEnd;
        long _frameNumber, _lastReportedTime = -1, _controlRevision;
        int _mainThread;

        public event Action<FFmpegVideoPlayer> Prepared;
        public event Action<FFmpegVideoPlayer> Started;
        public event Action<FFmpegVideoPlayer> Paused;
        public event Action<FFmpegVideoPlayer> Stopped;
        public event Action<FFmpegVideoPlayer> EndReached;
        public event Action<FFmpegVideoPlayer> SeekCompleted;
        public event Action<FFmpegVideoPlayer, string> ErrorReceived;
        public event Action<FFmpegVideoPlayer, Texture> TextureChanged;
        public event Action<FFmpegVideoPlayer, long> FrameReady;
        public event Action<FFmpegVideoPlayer, long> TimeChanged;

        public VideoPlaybackState State { get; private set; }
        public string LastError { get; private set; }
        public string Url { get => _source; set { CheckThread(); if (_source != value) { Close(); _source = value ?? ""; } } }
        public Texture Texture => _texture;
        public Texture texture => Texture;
        public bool IsPrepared => _prepared;
        public bool isPrepared => IsPrepared;
        public bool IsPlaying => State == VideoPlaybackState.Playing;
        public bool isPlaying => IsPlaying;
        public bool IsSeekable => _prepared && _info != null && _info.CanSeek;
        public bool IsBuffering => State == VideoPlaybackState.Preparing || State == VideoPlaybackState.Seeking || _waitingForFrame;
        public int BufferedFrames => _session?.BufferedFrames ?? 0;
        public uint Width => (uint)(_texture != null ? _texture.width : _info?.Width ?? 0);
        public uint Height => (uint)(_texture != null ? _texture.height : _info?.Height ?? 0);
        public double FrameRate => _info?.FrameRate ?? 0;
        public string CodecName => _info?.Codec ?? "";
        public string DecoderName => _info?.DecoderName ?? "";
        public string DecoderDevice => _info?.DecoderDevice ?? "";
        public VideoDecoderType DecoderType => _info?.HardwareDecoding == true ? VideoDecoderType.Hardware : VideoDecoderType.Software;
        public double LengthSeconds => _info?.Duration ?? 0;
        public long Length => (long)(LengthSeconds * 1000);
        public long Time { get => (long)(time * 1000); set => SeekTo(TimeSpan.FromMilliseconds(value)); }
        public double time
        {
            get => ClampTime(_clock.Position);
            set => BeginSeek(value, ContinueState());
        }
        public float Position
        {
            get => LengthSeconds > 0 ? (float)(time / LengthSeconds) : 0;
            set { if (LengthSeconds <= 0) throw new InvalidOperationException("This input has no known duration."); time = Mathf.Clamp01(value) * LengthSeconds; }
        }
        public float Rate
        {
            get => _playbackRate;
            set
            {
                CheckThread(); _clock.Rate = value;
                if (_playbackRate != value) MajDebug.LogDebug("FFmpeg", "[Player] Playback rate=" + value + ".");
                _playbackRate = value;
            }
        }
        public float playbackSpeed { get => Rate; set => Rate = value; }
        public bool Loop { get => _loop; set => _loop = value; }
        public bool PreferHardwareDecoding { get => _preferHardwareDecoding; set => _preferHardwareDecoding = value; }
        /// <summary>Applies to the next media open. Existing PreferHardwareDecoding is an alias.</summary>
        public VideoDecoderType PreferredDecoderType
        {
            get => _preferHardwareDecoding ? VideoDecoderType.Hardware : VideoDecoderType.Software;
            set
            {
                if (value != VideoDecoderType.Hardware && value != VideoDecoderType.Software)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _preferHardwareDecoding = value == VideoDecoderType.Hardware;
            }
        }
        /// <summary>False requests hardware decode with CPU upload; strict GPU mode overrides this.</summary>
        public bool PreferNativeTextures { get => _preferNativeTextures; set => _preferNativeTextures = value; }
        /// <summary>Overrides PreferHardwareDecoding; takes effect the next time media opens.</summary>
        public bool RequireHardwareDecoding { get => _requireHardwareDecoding; set => _requireHardwareDecoding = value; }
        public RenderTexture TargetTexture { get => _targetTexture; set => _targetTexture = value; }
        public string TransferMode { get; private set; } = "Software RGBA upload";
        public string HardwareFallbackReason { get; private set; }

        void Awake()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            _playbackRate = float.IsNaN(_playbackRate) || float.IsInfinity(_playbackRate)
                ? 1 : Math.Max(0.0625f, Math.Min(16, _playbackRate));
            _clock.Rate = _playbackRate;
        }
        void Start() { if (_playOnAwake && !string.IsNullOrWhiteSpace(_source) && _session == null) Play(); }
        void OnDisable() { if (IsPlaying) Pause(); }
        void OnDestroy() { Close(); }

        /// <summary>Returns after the first frame has been decoded and presented; does not start playback.</summary>
        public Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            CheckThread();
            if (_prepareCompletion != null) return _prepareCompletion.Task;
            if (IsPrepared) return Task.CompletedTask;
            if (string.IsNullOrWhiteSpace(_source)) throw new InvalidOperationException("Set Url before preparing video.");
            cancellationToken.ThrowIfCancellationRequested();
            Close();
            _prepareCancellation = cancellationToken;
            _prepareCompletion = NewCompletion();
            var task = _prepareCompletion.Task;
            State = VideoPlaybackState.Preparing;
            LastError = null;
            HardwareFallbackReason = null;
            _reportedTransferMode = null;
            try
            {
                _hardwareRequired = _requireHardwareDecoding;
                var options = new DecoderOptions { IOTimeoutMilliseconds = Math.Max(1, _ioTimeoutSeconds) * 1000,
                    RequireHardwareDecoding = _hardwareRequired, AllowHardwareCpuUpload = !_hardwareRequired };
                MajDebug.LogInfo("FFmpeg", "[Player] Preparing video; preferred decoder=" + PreferredDecoderType +
                    ", prefer native textures=" + _preferNativeTextures + ", require GPU-only=" + _hardwareRequired + ".");
                if (_preferHardwareDecoding || _hardwareRequired)
                    ConfigureHardware(options, _preferNativeTextures || _hardwareRequired);
                if (_hardwareRequired && !options.KeepNativeFrames)
                    throw new NotSupportedException(HardwareFallbackReason ?? "Native GPU video playback is unavailable.");
                _hardwareActive = options.HardwareDeviceType != FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                _hardwareCpuUploadAttempted = !options.KeepNativeFrames;
                _session = new VideoDecodeSession(NormalizeSource(_source), options, _bufferedFrameLimit);
            }
            catch (Exception error) { Fail(error); }
            return task;
        }
        public Task PreloadAsync(string path, CancellationToken cancellationToken = default)
        {
            Url = path;
            return PrepareAsync(cancellationToken);
        }
        public void Prepare() { Observe(PrepareAsync()); }
        public void Preload(string path) { Observe(PreloadAsync(path)); }
        public void Play(string path) { Url = path; Play(); }
        public void Play()
        {
            CheckThread();
            _controlRevision++;
            if (!IsPrepared)
            {
                Observe(PrepareAsync());
                _playWhenReady = true;
                return;
            }
            if (State == VideoPlaybackState.Seeking) { _afterSeek = VideoPlaybackState.Playing; return; }
            if (State == VideoPlaybackState.Ended) { BeginSeek(0, VideoPlaybackState.Playing); return; }
            if (IsPlaying) return;
            State = VideoPlaybackState.Playing;
            _clock.Start();
            MajDebug.LogDebug("FFmpeg", "[Player] Play at " + time.ToString("F3") + " s.");
            Started?.Invoke(this);
        }
        public void Pause()
        {
            CheckThread(); _controlRevision++; _playWhenReady = false;
            if (State == VideoPlaybackState.Seeking) { _afterSeek = VideoPlaybackState.Paused; return; }
            if (!IsPrepared) return;
            _clock.Pause(); _waitingForFrame = false;
            State = VideoPlaybackState.Paused;
            MajDebug.LogDebug("FFmpeg", "[Player] Pause at " + time.ToString("F3") + " s.");
            Paused?.Invoke(this);
        }
        public void SetPause(bool pause) { if (pause) Pause(); else Play(); }
        /// <summary>Pause and present one decoded frame on the next Update.</summary>
        public void NextFrame() { CheckThread(); if (!IsPrepared) return; Pause(); _stepRequested = true; }
        public bool SetRate(float rate) { if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0.0625f || rate > 16) return false; Rate = rate; return true; }
        public void Stop()
        {
            CheckThread(); _playWhenReady = false;
            MajDebug.LogDebug("FFmpeg", "[Player] Stop requested.");
            if (!IsPrepared || !IsSeekable)
            {
                var closeRevision = _controlRevision + 1;
                Close();
                if (_controlRevision != closeRevision) return;
                State = VideoPlaybackState.Stopped;
            }
            else BeginSeek(0, VideoPlaybackState.Stopped);
            Stopped?.Invoke(this);
        }
        public void SeekTo(TimeSpan position) { BeginSeek(position.TotalSeconds, ContinueState()); }
        public Task SeekAsync(double seconds)
        {
            BeginSeek(seconds, ContinueState());
            return _seekCompletion.Task;
        }
        public void Close()
        {
            CheckThread();
            if (_session != null) MajDebug.LogDebug("FFmpeg", "[Player] Closing decoder session.");
            _controlRevision++;
            _session?.Dispose(); _session = null;
            _prepareCompletion?.TrySetCanceled(); _prepareCompletion = null;
            _prepareCancellation = default;
            _seekCompletion?.TrySetCanceled(); _seekCompletion = null;
            _info = null; _prepared = false; _playWhenReady = false; _waitingForFrame = false; _stepRequested = false; _hardwareActive = false;
            _clock.Pause(); _clock.Set(0); _lastFrameEnd = 0; _lastReportedTime = -1; _frameNumber = 0;
            State = VideoPlaybackState.Idle;
            ReleasePresentation();
            if (_uploadTexture != null) Destroy(_uploadTexture);
            _uploadTexture = null;
            SetTexture(null);
        }

        void BeginSeek(double seconds, VideoPlaybackState afterSeek)
        {
            CheckThread();
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsSeekable) throw new InvalidOperationException("The input is not prepared or seekable.");
            _controlRevision++;
            _seekCompletion?.TrySetCanceled();
            _seekCompletion = NewCompletion();
            // Observe failures for fire-and-forget VLC-style setters too.
            Observe(_seekCompletion.Task);
            _seekTarget = ClampTime(seconds);
            MajDebug.LogDebug("FFmpeg", "[Player] Seek to " + _seekTarget.ToString("F3") + " s; resume=" + afterSeek + ".");
            _clock.Pause(); _clock.Set(_seekTarget);
            _afterSeek = afterSeek; _waitingForFrame = false; _stepRequested = false;
            State = VideoPlaybackState.Seeking;
            _session.Seek(_seekTarget);
        }

        void Update()
        {
            if (_session == null) return;
#if ENABLE_PROFILER && (UNITY_EDITOR || DEBUG)
            using var profile = UnityProfiler.Create("FFmpeg.Player.Update");
#endif
            var session = _session;
            var revision = _controlRevision;
            try
            {
                CheckHardwareErrors();
                if (State == VideoPlaybackState.Preparing && _prepareCancellation.IsCancellationRequested) { Close(); return; }
                if (_session.Error != null)
                {
                    if (_hardwareActive && _session.Info?.HardwareDecoding != false) RecoverHardwarePlayback(_session.Error);
                    else Fail(_session.Error);
                    return;
                }
                _info = _session.Info ?? _info;
                if (!string.IsNullOrEmpty(_info?.HardwareFallbackReason) && string.IsNullOrEmpty(HardwareFallbackReason))
                    HardwareFallbackReason = _info.HardwareFallbackReason;
                if (State == VideoPlaybackState.Preparing || State == VideoPlaybackState.Seeking)
                {
                    var frame = _session.TakeFrame();
                    if (frame != null)
                    {
                        using (frame) { if (!Present(frame)) return; }
                        if (State == VideoPlaybackState.Preparing)
                        {
                            // A first-texture/frame listener may Pause or Play the same
                            // preparing session. Its frame still completes preparation;
                            // only a replacement session invalidates it.
                            revision = _controlRevision;
                            _clock.Set(0); _prepared = true; State = VideoPlaybackState.Prepared;
                            MajDebug.LogInfo("FFmpeg", "[Player] Prepared; encoding=" + CodecName + ", decoder=" + DecoderName +
                                ", type=" + DecoderType + ", device=" + DecoderDevice + ".");
                            var completion = _prepareCompletion; _prepareCompletion = null;
                            _prepareCancellation = default;
                            completion?.TrySetResult(true);
                            Prepared?.Invoke(this);
                            if (!IsCurrent(session, revision)) return;
                            if (_playWhenReady && _session != null) Play();
                        }
                        else FinishSeek();
                    }
                    else if (_session.EndOfStream)
                    {
                        if (State == VideoPlaybackState.Preparing) throw new InvalidDataException("The input contains no decodable video frames.");
                        FinishSeek();
                    }
                }
                if (!IsCurrent(session, revision)) return;
                if (_session != null && IsPlaying) AdvancePlayback();
                if (!IsCurrent(session, revision)) return;
                if (_session != null && _stepRequested && State == VideoPlaybackState.Paused)
                {
                    var frame = _session.TakeFrame();
                    if (frame != null)
                    {
                        using (frame) { if (!Present(frame)) return; _clock.Set(frame.PresentationTime); }
                        _stepRequested = false;
                    }
                    else if (_session.EndOfStream) _stepRequested = false;
                }
                var position = Time;
                if (position != _lastReportedTime) { _lastReportedTime = position; TimeChanged?.Invoke(this, position); }
            }
            catch (NotSupportedException error) when (_hardwareActive) { RecoverHardwarePlayback(error); }
            catch (Exception error) { Fail(error); }
        }
        void FinishSeek()
        {
            _clock.Set(_seekTarget); State = _afterSeek;
            if (IsPlaying) _clock.Start();
            var completion = _seekCompletion; _seekCompletion = null;
            completion?.TrySetResult(true);
            MajDebug.LogDebug("FFmpeg", "[Player] Seek completed at " + _seekTarget.ToString("F3") + " s.");
            SeekCompleted?.Invoke(this);
        }
        void AdvancePlayback()
        {
            // Skip stale frames at high speed without holding more than the configured queue capacity.
            DecodedVideoFrame newest = null;
            var now = _clock.Position;
            // A fast worker can refill while this loop consumes frames. Bound work per
            // Update independently of queue capacity to keep high-rate playback responsive.
            var budget = Math.Max(1, Math.Min(8, _bufferedFrameLimit));
            while (budget-- > 0 && _session.NextPresentationTime <= now + 0.001)
            {
                newest?.Dispose();
                newest = _session.TakeFrame();
            }
            if (newest != null)
            {
                using (newest) { if (!Present(newest)) return; }
                if (_waitingForFrame) { _waitingForFrame = false; _clock.Start(); }
            }
            if (_session.EndOfStream && _clock.Position >= _lastFrameEnd)
            {
                _clock.Pause(); _clock.Set(LengthSeconds > 0 ? LengthSeconds : _lastFrameEnd);
                _waitingForFrame = false; State = VideoPlaybackState.Ended;
                MajDebug.LogDebug("FFmpeg", "[Player] End reached; loop=" + _loop + ".");
                EndReached?.Invoke(this);
                if (_loop && IsSeekable && State == VideoPlaybackState.Ended) BeginSeek(0, VideoPlaybackState.Playing);
            }
            else if (_session.BufferedFrames == 0 && !_session.EndOfStream && now > _lastFrameEnd)
            {
                _clock.Pause(); _waitingForFrame = true;
            }
            else if (_waitingForFrame && _session.BufferedFrames > 0)
            {
                _waitingForFrame = false; _clock.Start();
            }
        }
        bool Present(DecodedVideoFrame frame)
        {
#if ENABLE_PROFILER && (UNITY_EDITOR || DEBUG)
            using var profile = UnityProfiler.Create("FFmpeg.Player.Present");
#endif
            var session = _session;
            var revision = _controlRevision;
            Texture output = null;
            PresentHardware(frame, ref output);
            if (output == null)
            {
                if (frame.Data == IntPtr.Zero) throw new NotSupportedException("The graphics bridge could not present this hardware frame.");
                if (_uploadTexture == null || _uploadTexture.width != frame.Width || _uploadTexture.height != frame.Height)
                {
                    if (_uploadTexture != null) Destroy(_uploadTexture);
                    _uploadTexture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
                    { name = "FFmpeg video", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                }
#if ENABLE_PROFILER && (UNITY_EDITOR || DEBUG)
                using (UnityProfiler.Create("FFmpeg.Player.CpuUpload"))
#endif
                {
                    _uploadTexture.LoadRawTextureData(frame.Data, frame.DataSize);
                    _uploadTexture.Apply(false, false);
                }
                output = _uploadTexture;
                TransferMode = frame.HardwareDecoded ? "Hardware decode + CPU RGBA upload" : "Software RGBA upload";
            }
            if (_reportedTransferMode != TransferMode)
            {
                _reportedTransferMode = TransferMode;
                MajDebug.LogInfo("FFmpeg", "[Player] Texture transfer=" + TransferMode + ".");
            }
            if (_targetTexture != null) { Graphics.Blit(output, _targetTexture); output = _targetTexture; }
            SetTexture(output);
            if (!IsPresentationCurrent(session, revision)) return false;
            _lastFrameEnd = frame.PresentationTime + Math.Max(0.001, frame.Duration);
            FrameReady?.Invoke(this, ++_frameNumber);
            return IsPresentationCurrent(session, revision);
        }
        bool IsPresentationCurrent(VideoDecodeSession session, long revision) =>
            IsCurrent(session, revision) || (_session == session && State == VideoPlaybackState.Preparing);
        bool IsCurrent(VideoDecodeSession session, long revision) => _session == session && _controlRevision == revision;
        void SetTexture(Texture value)
        {
            if (_texture == value) return;
            _texture = value;
            if (_targetRenderer != null)
            {
                if (_materialProperties == null) _materialProperties = new MaterialPropertyBlock();
                _targetRenderer.GetPropertyBlock(_materialProperties);
                _materialProperties.SetTexture(_textureProperty, value);
                _targetRenderer.SetPropertyBlock(_materialProperties);
            }
            TextureChanged?.Invoke(this, value);
        }
        void Fail(Exception error)
        {
            var prepare = _prepareCompletion; var seek = _seekCompletion;
            _prepareCompletion = null; _seekCompletion = null;
            var closeRevision = _controlRevision + 1;
            Close();
            prepare?.TrySetException(error); seek?.TrySetException(error);
            // TextureChanged(null) may have already started a replacement media.
            // Complete the failed operation without overwriting that new session.
            if (_controlRevision == closeRevision)
            {
                State = VideoPlaybackState.Error; LastError = error.Message;
                ErrorReceived?.Invoke(this, LastError);
            }
            MajDebug.LogError("FFmpeg", "[Player] Playback failed: " + error);
        }
        void RecoverHardwarePlayback(Exception reason)
        {
            HardwareFallbackReason = reason.Message;
            if (_hardwareRequired) { Fail(reason); return; }
            if (_hardwareCpuUploadAttempted) { RecoverInSoftware(reason); return; }
            _hardwareCpuUploadAttempted = true;
            MajDebug.LogWarning("FFmpeg", "[Player] Hardware playback path failed; retrying hardware decoding with CPU upload. " + reason.Message);
            RecoverPlayback(reason, true);
        }
        void RecoverInSoftware(Exception reason)
        {
            HardwareFallbackReason = reason.Message;
            if (_hardwareRequired) { Fail(reason); return; }
            MajDebug.LogWarning("FFmpeg", "[Player] Hardware playback unavailable; retrying software decoding with CPU upload. " + reason.Message);
            RecoverPlayback(reason, false);
        }
        void RecoverPlayback(Exception reason, bool hardwareCpuUpload)
        {
            _hardwareActive = hardwareCpuUpload;
            var resume = State == VideoPlaybackState.Seeking ? _afterSeek : State;
            double position = time;
            _clock.Pause();
            _waitingForFrame = false;
            try
            {
                _session?.Dispose();
                _session = null;
                ReleasePresentation();
                TransferMode = hardwareCpuUpload ? "Reopening hardware decoder for CPU upload" : "Reopening software decoder";
                _reportedTransferMode = null;
                var options = new DecoderOptions { IOTimeoutMilliseconds = Math.Max(1, _ioTimeoutSeconds) * 1000 };
                if (hardwareCpuUpload) ConfigureHardware(options, false);
                _session = new VideoDecodeSession(NormalizeSource(_source), options, _bufferedFrameLimit);
                if (_prepared && _info != null && _info.CanSeek)
                {
                    _seekTarget = position; _afterSeek = resume;
                    State = VideoPlaybackState.Seeking;
                    _session.Seek(position);
                }
                else if (_prepared)
                {
                    // Live sources reopen at their current live edge, rather than
                    // trying to seek to a position the demuxer cannot restore.
                    _prepared = false;
                    _playWhenReady = resume == VideoPlaybackState.Playing;
                    _info = null;
                    State = VideoPlaybackState.Preparing;
                }
                // Publish only after controls can safely operate on the replacement.
                // A TextureChanged listener may Pause, Seek, Close or open another URL;
                // no recovery work after this callback may override that decision.
                SetTexture(null);
            }
            catch (Exception recoveryError)
            {
                if (hardwareCpuUpload) RecoverInSoftware(recoveryError);
                else Fail(recoveryError);
            }
            // The original preload completion remains pending while the replacement opens.
        }
        double ClampTime(double seconds) => Math.Max(0, LengthSeconds > 0 ? Math.Min(seconds, LengthSeconds) : seconds);
        VideoPlaybackState ContinueState() => IsPlaying || (State == VideoPlaybackState.Seeking && _afterSeek == VideoPlaybackState.Playing)
            ? VideoPlaybackState.Playing : VideoPlaybackState.Paused;
        void CheckThread()
        {
            if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread)
                throw new InvalidOperationException("FFmpegVideoPlayer must be controlled on Unity's main thread.");
        }
        static TaskCompletionSource<bool> NewCompletion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        static async void Observe(Task task) { try { await task; } catch { /* Update reports errors through ErrorReceived. */ } }
        static string NormalizeSource(string path)
        {
            path = path.Trim().Trim('"');
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : path;
        }
        partial void ConfigureHardware(DecoderOptions options, bool preferNative);
        partial void PresentHardware(DecodedVideoFrame frame, ref Texture output);
        partial void CheckHardwareErrors();
        partial void ReleasePresentation();
    }
}
