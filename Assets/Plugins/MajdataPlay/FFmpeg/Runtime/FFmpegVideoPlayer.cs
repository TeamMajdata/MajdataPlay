#nullable enable
using System;
using System.IO;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Describes the current state of a video player.</summary>
    public enum VideoPlaybackState
    {
        /// <summary>No media is open.</summary>
        Idle,
        /// <summary>The player is opening media and preparing its first frame.</summary>
        Preparing,
        /// <summary>The first frame is displayed and playback has not started.</summary>
        Prepared,
        /// <summary>Playback is active, including temporary buffering.</summary>
        Playing,
        /// <summary>Playback is paused.</summary>
        Paused,
        /// <summary>The player is decoding toward a requested position.</summary>
        Seeking,
        /// <summary>Playback has stopped.</summary>
        Stopped,
        /// <summary>Playback has reached the end of the video.</summary>
        Ended,
        /// <summary>Opening or playing the video failed.</summary>
        Error
    }

    /// <summary>Identifies the preferred or active video decoder backend.</summary>
    public enum VideoDecoderType
    {
        /// <summary>Uses an FFmpeg software decoder.</summary>
        Software,
        /// <summary>Uses a platform hardware decoding backend.</summary>
        /// <remarks>The platform may select a software implementation for some codecs.</remarks>
        Hardware
    }

    /// <summary>
    /// Plays video without audio using FFmpeg and a bounded background decoding worker.
    /// </summary>
    /// <remarks>
    /// Call component APIs on the Unity main thread; events are also raised on that thread.
    /// Keep the component enabled while preparing, seeking, or playing so that Unity can present frames.
    /// <see cref="Time"/> uses milliseconds; <see cref="TimeSeconds"/> uses seconds.
    /// </remarks>
    [DisallowMultipleComponent, AddComponentMenu("Video/FFmpeg Video Player")]
    public sealed partial class FFmpegVideoPlayer : MonoBehaviour
    {
        /// <summary>Stores the path or URL of the video to open.</summary>
        [SerializeField, FormerlySerializedAs("Source"), Tooltip("Local path or FFmpeg-supported URL. Android packaged StreamingAssets must first be extracted.")]
        private string _source = "";
        /// <summary>Controls whether playback begins when the component awakens.</summary>
        [field: SerializeField, FormerlySerializedAs("PlayOnAwake")]
        public bool PlayOnAwake { get; set; }
        /// <summary>Gets or sets whether seekable media restarts when playback reaches its end.</summary>
        [field: SerializeField, FormerlySerializedAs("Loop")]
        public bool Loop { get; set; }
        /// <summary>Stores the requested playback rate.</summary>
        [SerializeField, FormerlySerializedAs("PlaybackRate"), Range(0.0625f, 16)]
        private float _playbackRate = 1;
        /// <summary>Limits the number of decoded frames buffered for presentation.</summary>
        [field: SerializeField, FormerlySerializedAs("BufferedFrameLimit"), Range(1, 8)]
        public int BufferedFrameLimit { get; set; } = 3;
        /// <summary>Sets the timeout for blocking input operations in seconds.</summary>
        [field: SerializeField, FormerlySerializedAs("IOTimeoutSeconds"), Min(1)]
        public int IOTimeoutSeconds { get; set; } = 15;
        // Retain the serialized bool so existing scenes keep their decoder preference.
        /// <summary>Stores the preferred hardware decoding setting for existing scenes.</summary>
        [SerializeField, FormerlySerializedAs("PreferHardwareDecoding"), HideInInspector]
        private bool _preferHardwareDecoding = true;
        /// <summary>Controls whether native GPU texture sharing is preferred.</summary>
        [SerializeField, FormerlySerializedAs("PreferNativeTextures"), Tooltip("Prefer native GPU texture sharing. If unavailable, retain hardware decoding with CPU upload when supported. Applies when opening media.")]
        private bool _preferNativeTextures = true;
        /// <summary>Requires hardware frames that remain on the GPU.</summary>
        [SerializeField, FormerlySerializedAs("RequireHardwareDecoding"), Tooltip("Require native GPU frames. Unsupported codecs/devices report an error instead of uploading CPU pixels. Applies when opening media.")]
        private bool _requireHardwareDecoding;
        /// <summary>Stores the renderer that receives the video texture.</summary>
        [SerializeField, FormerlySerializedAs("TargetRenderer")]
        private Renderer? _targetRenderer;
        /// <summary>Stores the target material's texture property name.</summary>
        [SerializeField, FormerlySerializedAs("TextureProperty")]
        private string _textureProperty = "_MainTex";
        /// <summary>Stores the optional destination render texture.</summary>
        [SerializeField, FormerlySerializedAs("TargetTexture"), Tooltip("Optional output; otherwise use Texture or TextureChanged.")]
        private RenderTexture? _targetTexture;
        /// <summary>Owns the current background decoder, or null while closed.</summary>
        private VideoDecodeSession? _session;
        /// <summary>Caches the latest immutable media information snapshot.</summary>
        private VideoInfo? _info;
        /// <summary>Owns the reusable texture for CPU RGBA uploads.</summary>
        private Texture2D? _uploadTexture;
        /// <summary>References the current output texture, or null before presentation.</summary>
        private Texture? _texture;
        /// <summary>Reuses renderer property storage when assigning video textures.</summary>
        private MaterialPropertyBlock? _materialProperties;
        /// <summary>Complete the pending preparation and seek operations, respectively.</summary>
        private TaskCompletionSource<bool>? _prepareCompletion, _seekCompletion;
        /// <summary>Carries cancellation for the pending preparation operation.</summary>
        private CancellationToken _prepareCancellation;
        /// <summary>Track deferred playback, buffering, preparation, hardware selection, GPU requirements, attempted CPU fallback, and pending frame stepping, respectively.</summary>
        private bool _playWhenReady, _waitingForFrame, _prepared, _hardwareActive, _hardwareRequired, _hardwareCpuUploadAttempted, _stepRequested;
        /// <summary>Prevents retrying the preferred native backend after falling back to the platform backend.</summary>
        private bool _platformBackendOnly;
        /// <summary>Caches the last logged transfer mode to avoid per-frame log messages.</summary>
        private string? _reportedTransferMode;
        /// <summary>Stores the playback state to restore after seeking.</summary>
        private VideoPlaybackState _afterSeek;
        /// <summary>Store the requested seek position and the end of the last presented frame, in seconds.</summary>
        private double _seekTarget, _lastFrameEnd;
        /// <summary>Track the presentation counter, last reported millisecond position, and control revision used to reject stale callbacks.</summary>
        private long _frameNumber, _lastReportedTime = -1, _controlRevision;
        /// <summary>Stores the monotonic Stopwatch timestamp of the last frame presented during playback.</summary>
        private long _lastPlaybackPresentTimestamp;
        /// <summary>Identifies the Unity thread allowed to control this player.</summary>
        private int _mainThread;
        /// <summary>Occurs when the first frame is presented; the argument is this player.</summary>
        public event Action<FFmpegVideoPlayer>? Prepared;
        /// <summary>Occurs when playback starts or resumes through <see cref="Play()"/>; the argument is this player.</summary>
        public event Action<FFmpegVideoPlayer>? Started;
        /// <summary>Occurs when prepared playback is paused; the argument is this player.</summary>
        public event Action<FFmpegVideoPlayer>? Paused;
        /// <summary>Occurs when stopping is requested; the argument is this player.</summary>
        /// <remarks>A seek back to the beginning may still be pending when the event is raised.</remarks>
        public event Action<FFmpegVideoPlayer>? Stopped;
        /// <summary>Occurs at the end of playback, before a possible loop restart; the argument is this player.</summary>
        public event Action<FFmpegVideoPlayer>? EndReached;
        /// <summary>Occurs when the latest seek finishes; the argument is this player.</summary>
        public event Action<FFmpegVideoPlayer>? SeekCompleted;
        /// <summary>Occurs when playback fails; arguments are this player and the error message.</summary>
        public event Action<FFmpegVideoPlayer, string>? ErrorReceived;
        /// <summary>Occurs when the output texture changes; arguments are this player and the new texture, or <see langword="null"/>.</summary>
        /// <remarks>The texture belongs to the player or the caller that supplied <see cref="TargetTexture"/>; listeners must not destroy it.</remarks>
        public event Action<FFmpegVideoPlayer, Texture?>? TextureChanged;
        /// <summary>Occurs after presenting a frame; arguments are this player and a one-based presentation counter.</summary>
        /// <remarks>The counter resets on <see cref="Close"/> and is not the source video's frame index.</remarks>
        public event Action<FFmpegVideoPlayer, long>? FrameReady;
        /// <summary>Occurs when the reported position changes; arguments are this player and the position in milliseconds.</summary>
        public event Action<FFmpegVideoPlayer, long>? TimeChanged;
        /// <summary>Gets the current playback state.</summary>
        public VideoPlaybackState State { get; private set; }
        /// <summary>Gets the last playback error message, or <see langword="null"/> if none has been reported since preparation began.</summary>
        public string? LastError { get; private set; }

        /// <summary>Gets or sets the local path or FFmpeg-supported URL to open.</summary>
        /// <remarks>Changing the value closes the current media. A null value clears the source.</remarks>
        [AllowNull]
        public string Url
        {
            get => _source;
            set
            {
                CheckThread();
                if (_source != value)
                {
                    Close();
                    _source = value ?? "";
                }
            }
        }

        /// <summary>Gets the current output texture, or <see langword="null"/> when no frame is available.</summary>
        /// <remarks>Do not destroy this texture. Subscribe to <see cref="TextureChanged"/> to track replacements.</remarks>
        public Texture? Texture => _texture;
        /// <summary>Gets whether media has been prepared and remains open.</summary>
        public bool IsPrepared => _prepared;
        /// <summary>Gets whether playback is active, including temporary buffering.</summary>
        public bool IsPlaying => State == VideoPlaybackState.Playing;
        /// <summary>Gets whether the prepared input supports seeking.</summary>
        public bool IsSeekable => _prepared && _info != null && _info.CanSeek;
        /// <summary>Gets whether the player is preparing, seeking, or waiting for a decoded frame.</summary>
        public bool IsBuffering => State == VideoPlaybackState.Preparing || State == VideoPlaybackState.Seeking || _waitingForFrame;
        /// <summary>Gets the number of decoded frames queued for presentation.</summary>
        public int BufferedFrames => _session?.BufferedFrames ?? 0;
        /// <summary>Gets the output texture width in pixels, or the source width before a texture is available; zero if unknown.</summary>
        public uint Width => (uint)(_texture != null ? _texture.width : _info?.Width ?? 0);
        /// <summary>Gets the output texture height in pixels, or the source height before a texture is available; zero if unknown.</summary>
        public uint Height => (uint)(_texture != null ? _texture.height : _info?.Height ?? 0);
        /// <summary>Gets the estimated source frame rate in frames per second, or zero if unavailable.</summary>
        public double FrameRate => _info?.FrameRate ?? 0;
        /// <summary>Gets the average video stream bit rate in bits per second, or zero if unavailable.</summary>
        public long BitRate => _info?.BitRate ?? 0;
        /// <summary>Gets the estimated video bit rate near the currently displayed frame, in bits per second; zero if unavailable.</summary>
        /// <remarks>Uses compressed video packets over up to one second of media time. Pausing retains the value; seeking and closing clear it.</remarks>
        public long CurrentBitRate { get; private set; }
        /// <summary>Gets the video encoding name, such as h264, or an empty string if unavailable.</summary>
        public string CodecName => _info?.Codec ?? "";
        /// <summary>Gets the selected FFmpeg decoder name, or an empty string if unavailable.</summary>
        public string DecoderName => _info?.DecoderName ?? "";
        /// <summary>Gets the decoder device description, or an empty string if unavailable.</summary>
        public string DecoderDevice => _info?.DecoderDevice ?? "";
        /// <summary>Gets the active decoder backend type; read this value after preparation.</summary>
        /// <remarks>Returns <see cref="VideoDecoderType.Software"/> when decoder information is unavailable.</remarks>
        public VideoDecoderType DecoderType => _info?.HardwareDecoding == true ? VideoDecoderType.Hardware : VideoDecoderType.Software;
        /// <summary>Gets the video duration in seconds, or zero if unknown.</summary>
        public double LengthSeconds => _info?.Duration ?? 0;
        /// <summary>Gets the video duration in whole milliseconds, or zero if unknown.</summary>
        public long Length => (long)(LengthSeconds * 1000);
        /// <summary>Gets or seeks to the playback position in whole milliseconds.</summary>
        /// <remarks>Setting the position requires prepared, seekable media and completes asynchronously.</remarks>
        public long Time { get => (long)(TimeSeconds * 1000); set => SeekTo(TimeSpan.FromMilliseconds(value)); }
        /// <summary>Gets or seeks to the playback position in seconds.</summary>
        /// <remarks>Setting the position clamps it to the known timeline and completes asynchronously.</remarks>
        /// <exception cref="InvalidOperationException">The input is not prepared or seekable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
        public double TimeSeconds { get => ClampTime(_session?.PlaybackPosition ?? 0); set => BeginSeek(value, ContinueState()); }

        /// <summary>Gets or seeks to the normalized playback position between zero and one.</summary>
        /// <remarks>The getter returns zero when duration is unknown. The setter clamps finite values to the valid range.</remarks>
        /// <exception cref="InvalidOperationException">Duration is unknown, or the input is not prepared or seekable.</exception>
        public float Position
        {
            get => LengthSeconds > 0 ? (float)(TimeSeconds / LengthSeconds) : 0;
            set
            {
                if (LengthSeconds <= 0)
                {
                    throw new InvalidOperationException("This input has no known duration.");
                }

                TimeSeconds = Mathf.Clamp01(value) * LengthSeconds;
            }
        }

        /// <summary>Gets or sets the playback speed multiplier without changing the current position.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is outside the inclusive range 0.0625 to 16.</exception>
        public float PlaybackRate
        {
            get => _playbackRate;
            set
            {
                CheckThread();
                PlaybackClock.ValidateRate(value);
                _session?.SetPlayback(rate: value);
                if (_playbackRate != value)
                {
                    MajDebug.LogDebug("FFmpeg", "[Player] Playback rate=" + value + ".");
                }

                _playbackRate = value;
            }
        }


        /// <summary>Gets or sets the preferred decoder backend for the next media open.</summary>
        /// <remarks>Hardware preference permits CPU upload or software fallback unless <see cref="RequireHardwareDecoding"/> is enabled.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined decoder type.</exception>
        public VideoDecoderType PreferredDecoderType
        {
            get => _preferHardwareDecoding ? VideoDecoderType.Hardware : VideoDecoderType.Software;
            set
            {
                if (value != VideoDecoderType.Hardware && value != VideoDecoderType.Software)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _preferHardwareDecoding = value == VideoDecoderType.Hardware;
            }
        }

        /// <summary>Gets or sets whether hardware decoding should prefer native GPU texture sharing on the next media open.</summary>
        /// <remarks>False requests CPU upload when hardware decoding is selected; <see cref="RequireHardwareDecoding"/> overrides this setting.</remarks>
        public bool PreferNativeTextures { get => _preferNativeTextures; set => _preferNativeTextures = value; }
        /// <summary>Gets or sets whether the next media open requires hardware decoding with a native GPU presentation path.</summary>
        /// <remarks>Overrides decoder and texture preferences. CPU upload and software fallback are prohibited; unsupported paths report an error.</remarks>
        public bool RequireHardwareDecoding { get => _requireHardwareDecoding; set => _requireHardwareDecoding = value; }
        /// <summary>Gets or sets an optional caller-owned render texture to receive presented frames.</summary>
        /// <remarks>Changes take effect on the next presented frame. The player does not destroy this texture.</remarks>
        public RenderTexture? TargetTexture { get => _targetTexture; set => _targetTexture = value; }
        /// <summary>Gets the current frame transfer description or a pending recovery description.</summary>
        /// <remarks>The last description remains available after closing media; read it after preparation to identify the active path.</remarks>
        public string TransferMode { get; private set; } = "Software RGBA upload";
        /// <summary>Gets the reason hardware decoding or native texture sharing fell back, or <see langword="null"/> if none was reported.</summary>
        public string? HardwareFallbackReason { get; private set; }

        private void Awake()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            _playbackRate = float.IsNaN(_playbackRate) || float.IsInfinity(_playbackRate) ? 1 : Math.Max(0.0625f, Math.Min(16, _playbackRate));
        }

        private void Start()
        {
            if (PlayOnAwake && !string.IsNullOrWhiteSpace(_source) && _session == null)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            if (IsPlaying)
            {
                Pause();
            }
        }

        private void OnDestroy()
        {
            Close();
        }

        /// <summary>Opens the configured source and presents its first frame without starting playback.</summary>
        /// <param name="cancellationToken">Cancels preparation and closes the pending session.</param>
        /// <returns>A task that completes when the first frame is presented; repeated calls share a pending preparation.</returns>
        /// <exception cref="InvalidOperationException">No source is configured, or the caller is not on the Unity main thread.</exception>
        /// <exception cref="OperationCanceledException">The token is already canceled.</exception>
        /// <remarks>Closing or replacing the media cancels the task. Playback failures fault it and raise <see cref="ErrorReceived"/>.</remarks>
        /// <exception cref="NotSupportedException">The requested dimensions, codec, platform, or native transport cannot be supported.</exception>
        public Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            CheckThread();
            if (_prepareCompletion != null)
            {
                return _prepareCompletion.Task;
            }

            if (IsPrepared)
            {
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(_source))
            {
                throw new InvalidOperationException("Set Url before preparing video.");
            }

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
                _platformBackendOnly = false;
                var options = new DecoderOptions
                {
                    IOTimeoutMilliseconds = Math.Max(1, IOTimeoutSeconds) * 1000,
                    RequireHardwareDecoding = _hardwareRequired,
                    AllowHardwareCpuUpload = !_hardwareRequired
                };
                MajDebug.LogInfo("FFmpeg", "[Player] Preparing video; preferred decoder="
                    + PreferredDecoderType + ", prefer native textures=" + _preferNativeTextures + ", require GPU-only="
                    + _hardwareRequired + ".");
                if (_preferHardwareDecoding || _hardwareRequired)
                {
                    ConfigureHardware(options, _preferNativeTextures || _hardwareRequired);
                }

                if (_hardwareRequired && !options.KeepNativeFrames)
                {
                    throw new NotSupportedException(HardwareFallbackReason ?? "Native GPU video playback is unavailable.");
                }

                _hardwareActive = options.HardwareDeviceType != global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                _hardwareCpuUploadAttempted = !options.KeepNativeFrames;
                _session = new VideoDecodeSession(NormalizeSource(_source), options, BufferedFrameLimit, _playbackRate);
            }
            catch (Exception error)
            {
                Fail(error);
            }

            return task;
        }

        /// <summary>Sets the source and prepares its first frame without starting playback.</summary>
        /// <param name="path">A local path or FFmpeg-supported URL.</param>
        /// <param name="cancellationToken">Cancels preparation and closes the pending session.</param>
        /// <returns>A task that completes when the first frame is presented.</returns>
        /// <remarks>Uses the same completion and error behavior as <see cref="PrepareAsync"/>.</remarks>
        public Task PreloadAsync(string path, CancellationToken cancellationToken = default)
        {
            Url = path;
            return PrepareAsync(cancellationToken);
        }

        /// <summary>Begins preparing the configured source without starting playback.</summary>
        /// <remarks>Use <see cref="Prepared"/> and <see cref="ErrorReceived"/> to observe asynchronous results.</remarks>
        public void Prepare()
        {
            Observe(PrepareAsync());
        }

        /// <summary>Sets the source and begins preparing it without starting playback.</summary>
        /// <param name="path">A local path or FFmpeg-supported URL.</param>
        /// <remarks>Use <see cref="Prepared"/> and <see cref="ErrorReceived"/> to observe asynchronous results.</remarks>
        public void Preload(string path)
        {
            Observe(PreloadAsync(path));
        }

        /// <summary>Sets the source and starts playback after preparation.</summary>
        /// <param name="path">A local path or FFmpeg-supported URL.</param>
        public void Play(string path)
        {
            Url = path;
            Play();
        }

        /// <summary>Starts or resumes playback, preparing the configured source first if necessary.</summary>
        /// <remarks>During a seek, playback starts when the seek finishes. Ended media is first rewound.</remarks>
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

            if (State == VideoPlaybackState.Seeking)
            {
                _afterSeek = VideoPlaybackState.Playing;
                return;
            }

            if (State == VideoPlaybackState.Ended)
            {
                BeginSeek(0, VideoPlaybackState.Playing);
                return;
            }

            if (IsPlaying)
            {
                return;
            }

            State = VideoPlaybackState.Playing;
            _session!.SetPlayback(playing: true);
            MajDebug.LogDebug("FFmpeg", "[Player] Play at " + TimeSeconds.ToString("F3") + " s.");
            Started?.Invoke(this);
        }

        /// <summary>Pauses playback or prevents a pending preparation or seek from starting playback.</summary>
        public void Pause()
        {
            CheckThread();
            _controlRevision++;
            _playWhenReady = false;
            if (State == VideoPlaybackState.Seeking)
            {
                _afterSeek = VideoPlaybackState.Paused;
                return;
            }

            if (!IsPrepared)
            {
                return;
            }

            _session!.SetPlayback(playing: false);
            _waitingForFrame = false;
            State = VideoPlaybackState.Paused;
            MajDebug.LogDebug("FFmpeg", "[Player] Pause at " + TimeSeconds.ToString("F3") + " s.");
            Paused?.Invoke(this);
        }

        /// <summary>Pauses or resumes playback.</summary>
        /// <param name="pause">True to pause; false to start or resume playback.</param>
        public void SetPause(bool pause)
        {
            if (pause)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>Pauses and requests presentation of the next decoded frame during a subsequent Unity update.</summary>
        /// <remarks>Has no effect before preparation. If no frames remain, playback stays paused and no new frame is presented.</remarks>
        public void NextFrame()
        {
            CheckThread();
            if (!IsPrepared)
            {
                return;
            }

            Pause();
            _stepRequested = true;
        }

        /// <summary>Stops playback and asynchronously seeks to the beginning while retaining prepared media.</summary>
        /// <remarks>Closes unprepared or nonseekable media. <see cref="Stopped"/> is raised before a pending rewind finishes.</remarks>
        public void Stop()
        {
            CheckThread();
            _playWhenReady = false;
            MajDebug.LogDebug("FFmpeg", "[Player] Stop requested.");
            if (!IsPrepared || !IsSeekable)
            {
                var closeRevision = _controlRevision + 1;
                Close();
                if (_controlRevision != closeRevision)
                {
                    return;
                }

                State = VideoPlaybackState.Stopped;
            }
            else
            {
                BeginSeek(0, VideoPlaybackState.Stopped);
            }

            Stopped?.Invoke(this);
        }

        /// <summary>Begins seeking to a position while preserving whether playback should resume.</summary>
        /// <param name="position">The requested position, clamped to the known timeline.</param>
        /// <exception cref="InvalidOperationException">The input is not prepared or seekable.</exception>
        public void SeekTo(TimeSpan position)
        {
            BeginSeek(position.TotalSeconds, ContinueState());
        }

        /// <summary>Seeks to a position while preserving whether playback should resume.</summary>
        /// <param name="seconds">The requested position in seconds, clamped to the known timeline.</param>
        /// <returns>A task that completes when the target frame is presented or decoding reaches the end of the input.</returns>
        /// <exception cref="InvalidOperationException">The input is not prepared or seekable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
        /// <remarks>A newer seek, source change, or close cancels the task. Playback failures fault it.</remarks>
        public Task SeekAsync(double seconds)
        {
            BeginSeek(seconds, ContinueState());
            // BeginSeek always installs the completion source before returning.
            return _seekCompletion!.Task;
        }

        /// <summary>Cancels pending operations, closes the media, releases presentation resources, and returns to the idle state.</summary>
        /// <remarks>Does not wait for blocking input on the decoding worker. The configured source and caller-owned target texture are retained.</remarks>
        public void Close()
        {
            CheckThread();
            if (_session != null)
            {
                MajDebug.LogDebug("FFmpeg", "[Player] Closing decoder session.");
            }

            _controlRevision++;
            _session?.Dispose();
            _session = null;
            _prepareCompletion?.TrySetCanceled();
            _prepareCompletion = null;
            _prepareCancellation = default;
            _seekCompletion?.TrySetCanceled();
            _seekCompletion = null;
            _info = null;
            _prepared = false;
            _playWhenReady = false;
            _waitingForFrame = false;
            _stepRequested = false;
            _hardwareActive = false;
            _lastFrameEnd = 0;
            _lastReportedTime = -1;
            _frameNumber = 0;
            CurrentBitRate = 0;
            State = VideoPlaybackState.Idle;
            ReleasePresentation();
            if (_uploadTexture != null)
            {
                Destroy(_uploadTexture);
            }

            _uploadTexture = null;
            SetTexture(null);
        }

        /// <summary>Cancels the previous seek and schedules decoding toward a new playback position.</summary>
        /// <param name="seconds">The media timeline position in seconds.</param>
        /// <param name="afterSeek">The playback state to restore after the target frame is available.</param>
        /// <exception cref="ArgumentOutOfRangeException">The requested position is NaN or infinity.</exception>
        /// <exception cref="InvalidOperationException">The caller is not on the Unity main thread, or media is not prepared and seekable.</exception>
        private void BeginSeek(double seconds, VideoPlaybackState afterSeek)
        {
            CheckThread();
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            if (!IsSeekable)
            {
                throw new InvalidOperationException("The input is not prepared or seekable.");
            }

            _controlRevision++;
            _seekCompletion?.TrySetCanceled();
            _seekCompletion = NewCompletion();
            // Observe failures for fire-and-forget VLC-style setters too.
            Observe(_seekCompletion.Task);
            _seekTarget = ClampTime(seconds);
            MajDebug.LogDebug("FFmpeg", "[Player] Seek to " + _seekTarget.ToString("F3") + " s; resume=" + afterSeek + ".");
            _afterSeek = afterSeek;
            _waitingForFrame = false;
            _stepRequested = false;
            State = VideoPlaybackState.Seeking;
            CurrentBitRate = 0;
            // IsSeekable above requires an active prepared session.
            _session!.Seek(_seekTarget);
        }

        /// <summary>Presents queued frames, completes pending controls, and reports playback state on the main thread.</summary>
        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            using var profile = UnityProfiler.Create("FFmpeg.Player.Update");
            var session = _session;
            var revision = _controlRevision;
            try
            {
                CheckHardwareErrors();
                if (State == VideoPlaybackState.Preparing && _prepareCancellation.IsCancellationRequested)
                {
                    Close();
                    return;
                }

                if (_session.Error != null)
                {
                    if (_hardwareActive && _session.Info?.HardwareDecoding != false)
                    {
                        RecoverHardwarePlayback(_session.Error);
                    }
                    else
                    {
                        Fail(_session.Error);
                    }

                    return;
                }

                _info = _session.Info ?? _info;
                if (_info != null && !string.IsNullOrEmpty(_info.HardwareFallbackReason) && string.IsNullOrEmpty(HardwareFallbackReason))
                {
                    HardwareFallbackReason = _info.HardwareFallbackReason;
                }

                if (State == VideoPlaybackState.Preparing || State == VideoPlaybackState.Seeking)
                {
                    var frame = _session.TakeFrame();
                    if (frame != null)
                    {
                        using (frame)
                        {
                            if (!Present(frame))
                            {
                                return;
                            }
                        }

                        if (State == VideoPlaybackState.Preparing)
                        {
                            // A first-texture/frame listener may Pause or Play the same
                            // preparing session. Its frame still completes preparation;
                            // only a replacement session invalidates it.
                            revision = _controlRevision;
                            _session!.SetPlayback(position: 0);
                            _prepared = true;
                            State = VideoPlaybackState.Prepared;
                            MajDebug.LogInfo("FFmpeg", "[Player] Prepared; encoding=" + CodecName
                                + ", decoder=" + DecoderName + ", type=" + DecoderType + ", device=" + DecoderDevice + ".");
                            var completion = _prepareCompletion;
                            _prepareCompletion = null;
                            _prepareCancellation = default;
                            completion?.TrySetResult(true);
                            Prepared?.Invoke(this);
                            if (!IsCurrent(session, revision))
                            {
                                return;
                            }

                            if (_playWhenReady && _session != null)
                            {
                                Play();
                            }
                        }
                        else
                        {
                            FinishSeek();
                        }
                    }
                    else if (_session.EndOfStream)
                    {
                        if (State == VideoPlaybackState.Preparing)
                        {
                            throw new InvalidDataException("The input contains no decodable video frames.");
                        }

                        FinishSeek();
                    }
                }

                if (!IsCurrent(session, revision))
                {
                    return;
                }

                if (_session != null && IsPlaying)
                {
                    AdvancePlayback();
                }

                if (!IsCurrent(session, revision))
                {
                    return;
                }

                if (_session != null && _stepRequested && State == VideoPlaybackState.Paused)
                {
                    var frame = _session.TakeFrame();
                    if (frame != null)
                    {
                        using (frame)
                        {
                            if (!Present(frame))
                            {
                                return;
                            }

                            _session!.SetPlayback(position: frame.PresentationTime);
                        }

                        _stepRequested = false;
                    }
                    else if (_session.EndOfStream)
                    {
                        _stepRequested = false;
                    }
                }

                var position = Time;
                if (position != _lastReportedTime)
                {
                    _lastReportedTime = position;
                    TimeChanged?.Invoke(this, position);
                }
            }
            catch (NotSupportedException error) when (_hardwareActive)
            {
                RecoverHardwarePlayback(error);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>Restores the requested playback state and completes the current seek operation.</summary>
        private void FinishSeek()
        {
            State = _afterSeek;
            _session!.SetPlayback(position: _seekTarget, playing: IsPlaying);
            var completion = _seekCompletion;
            _seekCompletion = null;
            completion?.TrySetResult(true);
            MajDebug.LogDebug("FFmpeg", "[Player] Seek completed at " + _seekTarget.ToString("F3") + " s.");
            SeekCompleted?.Invoke(this);
        }

        /// <summary>Consumes a bounded number of due frames and handles buffering, end-of-input, and looping.</summary>
        private void AdvancePlayback()
        {
            // Update calls this only for an active session; Present rejects session replacement.
            var session = _session!;
            var now = session.PlaybackPosition;
            // Select under one lock so worker-side replacement cannot race the due
            // check or temporarily require a second presenter-owned frame container.
            // While throttled, due frames stay queued and the worker replaces them.
            var newest = IsPresentationThrottled() ? null : session.TakeLatestFrame(now + 0.001);

            if (newest != null)
            {
                using (newest)
                {
                    if (!Present(newest))
                    {
                        return;
                    }
                }

                _lastPlaybackPresentTimestamp = Stopwatch.GetTimestamp();

                if (_waitingForFrame)
                {
                    _waitingForFrame = false;
                    session.SetPlayback(playing: true);
                }
            }

            if (session.EndOfStream && session.PlaybackPosition >= _lastFrameEnd)
            {
                session.SetPlayback(position: LengthSeconds > 0 ? LengthSeconds : _lastFrameEnd, playing: false);
                _waitingForFrame = false;
                State = VideoPlaybackState.Ended;
                MajDebug.LogDebug("FFmpeg", "[Player] End reached; loop=" + Loop + ".");
                EndReached?.Invoke(this);
                if (Loop && IsSeekable && State == VideoPlaybackState.Ended)
                {
                    BeginSeek(0, VideoPlaybackState.Playing);
                }
            }
            // A just-drained queue is refilled asynchronously; do not pause the
            // clock until an update actually fails to obtain a due frame.
            else if (newest == null && session.BufferedFrames == 0 && !session.EndOfStream && now > _lastFrameEnd)
            {
                session.SetPlayback(playing: false);
                _waitingForFrame = true;
            }
            else if (_waitingForFrame && session.BufferedFrames > 0)
            {
                _waitingForFrame = false;
                session.SetPlayback(playing: true);
            }
        }

        /// <summary>Checks whether presenting another frame now would exceed the video's nominal frame rate in real time.</summary>
        /// <returns>True if the next due frame should wait for a later update; otherwise false.</returns>
        /// <remarks>
        /// Above 1x speed, or after a stall, many frames can become due between updates. Each presentation costs a texture
        /// upload or GPU conversion on Unity's render thread, so the rate is bounded to what 1x playback needs; frames
        /// skipped by the bound are superseded on the decoding worker. The slack keeps 1x playback jitter unaffected.
        /// </remarks>
        private bool IsPresentationThrottled()
        {
            if (_waitingForFrame)
            {
                return false;
            }

            var frameRate = _info?.FrameRate ?? 0;
            var nominalFrameRate = double.IsNaN(frameRate) || frameRate <= 0 ? 60 : Math.Max(24, Math.Min(120, frameRate));
            var elapsed = (Stopwatch.GetTimestamp() - _lastPlaybackPresentTimestamp) / (double)Stopwatch.Frequency;
            return elapsed < 0.75 / nominalFrameRate;
        }

        /// <summary>Uploads or shares a frame and publishes its texture while guarding against reentrant controls.</summary>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <returns>True if presentation remains current after listeners run; otherwise false.</returns>
        /// <exception cref="NotSupportedException">The requested dimensions, codec, platform, or native transport cannot be supported.</exception>
        private bool Present(DecodedVideoFrame frame)
        {
            using var profile = UnityProfiler.Create("FFmpeg.Player.Present");
            var session = _session;
            var revision = _controlRevision;
            Texture? output = null;
            PresentHardware(frame, ref output);
            if (output == null)
            {
                if (frame.Data == IntPtr.Zero)
                {
                    throw new NotSupportedException("The graphics bridge could not present this hardware frame.");
                }

                if (_uploadTexture == null || _uploadTexture.width != frame.Width || _uploadTexture.height != frame.Height)
                {
                    if (_uploadTexture != null)
                    {
                        Destroy(_uploadTexture);
                    }

                    _uploadTexture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
                    {
                        name = "FFmpeg video",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };
                }

                using (UnityProfiler.Create("FFmpeg.Player.CpuUpload"))
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

            if (_targetTexture != null)
            {
                Graphics.Blit(output, _targetTexture);
                output = _targetTexture;
            }

            CurrentBitRate = frame.CurrentBitRate;
            SetTexture(output);
            if (!IsPresentationCurrent(session, revision))
            {
                return false;
            }

            _lastFrameEnd = frame.PresentationTime + Math.Max(0.001, frame.Duration);
            FrameReady?.Invoke(this, ++_frameNumber);
            return IsPresentationCurrent(session, revision);
        }

        /// <summary>Checks whether presentation still belongs to the active session after a listener runs.</summary>
        /// <param name="session">The captured decoding session used to detect replacement by an event listener.</param>
        /// <param name="revision">The captured control revision used to detect reentrant playback changes.</param>
        /// <returns>True if the same session still owns this presentation.</returns>
        private bool IsPresentationCurrent(VideoDecodeSession? session, long revision) => IsCurrent(session,
            revision) || (_session == session && State == VideoPlaybackState.Preparing);
        /// <summary>Checks whether a captured session and control revision still identify the current operation.</summary>
        /// <param name="session">The captured decoding session used to detect replacement by an event listener.</param>
        /// <param name="revision">The captured control revision used to detect reentrant playback changes.</param>
        /// <returns>True if both the session and control revision still match.</returns>
        private bool IsCurrent(VideoDecodeSession? session, long revision) => _session == session && _controlRevision == revision;
        /// <summary>Updates renderer output and notifies listeners when the displayed texture changes.</summary>
        /// <param name="value">The value to validate or assign.</param>
        private void SetTexture(Texture? value)
        {
            if (_texture == value)
            {
                return;
            }

            _texture = value;
            if (_targetRenderer != null)
            {
                if (_materialProperties == null)
                {
                    _materialProperties = new MaterialPropertyBlock();
                }

                _targetRenderer.GetPropertyBlock(_materialProperties);
                _materialProperties.SetTexture(_textureProperty, value);
                _targetRenderer.SetPropertyBlock(_materialProperties);
            }

            TextureChanged?.Invoke(this, value);
        }

        /// <summary>Closes failed playback, faults pending tasks, and reports the error to listeners.</summary>
        /// <param name="error">The playback exception to publish and use to fault pending operations.</param>
        private void Fail(Exception error)
        {
            var prepare = _prepareCompletion;
            var seek = _seekCompletion;
            _prepareCompletion = null;
            _seekCompletion = null;
            var closeRevision = _controlRevision + 1;
            Close();
            prepare?.TrySetException(error);
            seek?.TrySetException(error);
            // TextureChanged(null) may have already started a replacement media.
            // Complete the failed operation without overwriting that new session.
            if (_controlRevision == closeRevision)
            {
                State = VideoPlaybackState.Error;
                LastError = error.Message;
                ErrorReceived?.Invoke(this, LastError);
            }

            MajDebug.LogError("FFmpeg", "[Player] Playback failed: " + error);
        }

        /// <summary>Selects the next permitted hardware or software transport after a presentation failure.</summary>
        /// <param name="reason">The failure that triggered transport recovery or backend fallback.</param>
        private void RecoverHardwarePlayback(Exception reason)
        {
            HardwareFallbackReason = reason.Message;
            var device = (_session?.Info ?? _info)?.HardwareDeviceType;
            if (!_platformBackendOnly && (device == global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA
                || device == global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN))
            {
                _platformBackendOnly = true;
                MajDebug.LogWarning("FFmpeg", "[Player] Native video decoder failed; retrying the platform hardware backend. " + reason.Message);
                bool native = _preferNativeTextures || _hardwareRequired;
                RecoverPlayback(reason, !native, native);
                return;
            }

            if (_hardwareRequired)
            {
                Fail(reason);
                return;
            }

            if (_hardwareCpuUploadAttempted)
            {
                RecoverInSoftware(reason);
                return;
            }

            _hardwareCpuUploadAttempted = true;
            MajDebug.LogWarning("FFmpeg", "[Player] Hardware playback path failed; retrying hardware decoding with CPU upload. " + reason.Message);
            RecoverPlayback(reason, true);
        }

        /// <summary>Reopens playback with software decoding after a recoverable hardware failure.</summary>
        /// <param name="reason">The failure that triggered transport recovery or backend fallback.</param>
        private void RecoverInSoftware(Exception reason)
        {
            HardwareFallbackReason = reason.Message;
            if (_hardwareRequired)
            {
                Fail(reason);
                return;
            }

            MajDebug.LogWarning("FFmpeg", "[Player] Hardware playback unavailable; retrying software decoding with CPU upload. " + reason.Message);
            RecoverPlayback(reason, false);
        }

        /// <summary>Reopens media with a fallback transport while preserving the playback position and requested state.</summary>
        /// <param name="reason">The failure that triggered transport recovery or backend fallback.</param>
        /// <param name="hardwareCpuUpload">Whether recovery should retain hardware decoding with CPU pixel upload.</param>
        /// <param name="platformNative">Whether recovery should retry the platform native backend before CPU fallback.</param>
        private void RecoverPlayback(Exception reason, bool hardwareCpuUpload, bool platformNative = false)
        {
            _hardwareActive = hardwareCpuUpload || platformNative;
            var resume = State == VideoPlaybackState.Seeking ? _afterSeek : State;
            double position = TimeSeconds;
            _session?.SetPlayback(playing: false);
            _waitingForFrame = false;
            try
            {
                _session?.Dispose();
                _session = null;
                ReleasePresentation();
                TransferMode = platformNative ? "Reopening platform hardware decoder" : hardwareCpuUpload
                    ? "Reopening hardware decoder for CPU upload" : "Reopening software decoder";
                _reportedTransferMode = null;
                var options = new DecoderOptions
                {
                    IOTimeoutMilliseconds = Math.Max(1, IOTimeoutSeconds) * 1000,
                    RequireHardwareDecoding = _hardwareRequired,
                    AllowHardwareCpuUpload = !_hardwareRequired
                };
                if (hardwareCpuUpload || platformNative)
                {
                    ConfigureHardware(options, platformNative);
                }

                _session = new VideoDecodeSession(NormalizeSource(_source), options, BufferedFrameLimit, _playbackRate);
                if (_prepared && _info != null && _info.CanSeek)
                {
                    _seekTarget = position;
                    _afterSeek = resume;
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
                CurrentBitRate = 0;
                SetTexture(null);
            }
            catch (Exception recoveryError)
            {
                if (platformNative && !_hardwareRequired)
                {
                    _hardwareCpuUploadAttempted = true;
                    MajDebug.LogWarning("FFmpeg", "[Player] Platform GPU transport failed; retrying hardware decoding with CPU upload. " + recoveryError.Message);
                    RecoverPlayback(recoveryError, true);
                }
                else if (hardwareCpuUpload)
                {
                    RecoverInSoftware(recoveryError);
                }
                else
                {
                    Fail(recoveryError);
                }
            }
            // The original preload completion remains pending while the replacement opens.
        }

        /// <summary>Clamps a position to zero and the known media duration.</summary>
        /// <param name="seconds">The media timeline position in seconds.</param>
        /// <returns>The position restricted to the known media timeline.</returns>
        private double ClampTime(double seconds) => Math.Max(0, LengthSeconds > 0 ? Math.Min(seconds, LengthSeconds) : seconds);
        /// <summary>Determines whether a seek should resume playback or remain paused.</summary>
        /// <returns>Playing when playback should resume; otherwise Paused.</returns>
        private VideoPlaybackState ContinueState() => IsPlaying || (State == VideoPlaybackState.Seeking
            && _afterSeek == VideoPlaybackState.Playing) ? VideoPlaybackState.Playing : VideoPlaybackState.Paused;
        /// <summary>Rejects component access from a thread other than Unity's main thread.</summary>
        /// <exception cref="InvalidOperationException">The caller is not on the Unity main thread.</exception>
        private void CheckThread()
        {
            if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                throw new InvalidOperationException("FFmpegVideoPlayer must be controlled on Unity's main thread.");
            }
        }

        /// <summary>Creates a completion source whose continuations cannot run inline during player callbacks.</summary>
        /// <returns>A completion source with asynchronous continuations.</returns>
        private static TaskCompletionSource<bool> NewCompletion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Observes a task's failure for synchronous control APIs whose errors are reported by the player.</summary>
        /// <param name="task">The operation whose failure is already surfaced through player error reporting.</param>
        private static async void Observe(Task task)
        {
            try
            {
                await task;
            }
            catch
            { /* Update reports errors through ErrorReceived. */
            }
        }

        /// <summary>Converts local file URIs to paths while preserving other FFmpeg input URLs.</summary>
        /// <param name="path">The media input path or FFmpeg-supported URL.</param>
        /// <returns>A local filesystem path for file URIs, or the original input string.</returns>
        private static string NormalizeSource(string path)
        {
            path = path.Trim().Trim('"');
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : path;
        }

        /// <summary>Configures the preferred native decoding backend and its platform fallback.</summary>
        /// <param name="options">The resource limits and hardware configuration to use.</param>
        /// <param name="preferNative">Whether to request native GPU frame sharing instead of CPU pixel upload.</param>
        private partial void ConfigureHardware(DecoderOptions options, bool preferNative);
        /// <summary>Presents a native frame and updates the reported GPU transfer mode.</summary>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <param name="output">Receives the presented texture when the frame uses native GPU resources.</param>
        private partial void PresentHardware(DecodedVideoFrame frame, ref Texture? output);
        /// <summary>Checks the active presenter for asynchronous graphics failures.</summary>
        private partial void CheckHardwareErrors();
        /// <summary>Disposes the hardware presenter and clears its reference.</summary>
        private partial void ReleasePresentation();
    }
}
