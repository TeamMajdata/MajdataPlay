#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Describes the current camera recording lifecycle.</summary>
    public enum CameraCaptureState
    {
        /// <summary>No recording has started.</summary>
        Idle,
        /// <summary>The background worker is opening the encoder and output.</summary>
        Preparing,
        /// <summary>The component is capturing rendered camera frames.</summary>
        Recording,
        /// <summary>Pending GPU readbacks and compressed packets are draining.</summary>
        Stopping,
        /// <summary>The worker has released its output resources.</summary>
        Stopped,
        /// <summary>Capture, encoding, or output failed.</summary>
        Error,
        /// <summary>Capture and its clock are paused while the encoding session remains open.</summary>
        Paused
    }

    /// <summary>Specifies the native row order of GPU readback pixels.</summary>
    public enum CameraReadbackOrientation
    {
        /// <summary>Uses the bottom-first row order of the Unity blit capture texture.</summary>
        Automatic,
        /// <summary>The first readback row is the top of the rendered image.</summary>
        TopFirst,
        /// <summary>The first readback row is the bottom of the rendered image.</summary>
        BottomFirst
    }

    /// <summary>Records a Camera through bounded GPU readbacks and a background FFmpeg encoder.</summary>
    /// <remarks>
    /// Call component APIs on the Unity main thread. Events are raised on that thread.
    /// SRP display output uses CameraCaptureBridge; explicit targets are copied after end-context rendering.
    /// Built-in uses the camera's post-render callback. Custom pipelines must execute bridge actions or
    /// raise end-camera and end-context callbacks for explicit targets. Recording includes camera output,
    /// excluding audio and Screen Space Overlay canvases. Hardware encoding still uses CPU pixel readback.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Video/FFmpeg Camera Capturer")]
    public sealed class FFmpegCameraCapturer : MonoBehaviour
    {
        /// <summary>Stores the camera to record, or null to use this GameObject's camera.</summary>
        [SerializeField]
        [FormerlySerializedAs("TargetCamera")]
        private Camera? _targetCamera;
        /// <summary>Stores encoding preferences applied to the next recording.</summary>
        [SerializeField]
        [FormerlySerializedAs("Options")]
        private EncoderOptions _options = new EncoderOptions();
        /// <summary>Limits all simultaneous GPU, queued, and encoder frame reservations.</summary>
        [SerializeField]
        [FormerlySerializedAs("BufferedFrameLimit")]
        [Range(1, 8)]
        private int _bufferedFrameLimit = 3;
        /// <summary>Controls conversion from native GPU row order to top-first video.</summary>
        [SerializeField]
        [FormerlySerializedAs("ReadbackOrientation")]
        private CameraReadbackOrientation _readbackOrientation;
        /// <summary>Tracks elapsed recording time independently of the Unity time scale.</summary>
        private readonly Stopwatch _clock = new Stopwatch();
        /// <summary>Owns the active background encoding session.</summary>
        private VideoEncodeSession? _session;
        /// <summary>Retains GPU textures until their outstanding requests finish.</summary>
        private CaptureResources? _resources;
        /// <summary>Retains the fixed camera selected when recording starts.</summary>
        private Camera? _activeCamera;
        /// <summary>Retains the explicit SRP output until the complete camera stack has rendered.</summary>
        private RenderTexture? _pendingSrpTexture;
        /// <summary>Retains the immutable encoder settings for this recording.</summary>
        private EncoderOptions? _activeOptions;
        /// <summary>Reuses the SRP capture action between sessions.</summary>
        private Action<RenderTargetIdentifier, CommandBuffer>? _captureAction;
        /// <summary>Identifies the Unity thread allowed to control this component.</summary>
        private int _mainThread;
        /// <summary>Stores the last attempted capture timestamp to prevent duplicate frames.</summary>
        private long _lastFrameIndex = -1;
        /// <summary>Counts frames omitted because rendering or encoding could not keep pace.</summary>
        private long _droppedFrames;
        /// <summary>Tracks whether render callbacks are attached.</summary>
        private bool _hooked;
        /// <summary>Tracks whether the current SRP camera uses the pipeline capture bridge.</summary>
        private bool _bridgeEnabled;
        /// <summary>Prevents duplicate completion events for the same session.</summary>
        private bool _completionReported;
        /// <summary>Occurs after FFmpeg opens the output and camera capture begins.</summary>
        public event Action<FFmpegCameraCapturer>? Started;
        /// <summary>Occurs after all GPU requests and native encoding resources finish.</summary>
        public event Action<FFmpegCameraCapturer>? Stopped;
        /// <summary>Occurs when recording fails; arguments are this component and the error message.</summary>
        public event Action<FFmpegCameraCapturer, string>? ErrorReceived;

        /// <summary>Gets or sets the camera used for the next recording.</summary>
        public Camera? TargetCamera { get => _targetCamera; set => _targetCamera = value; }
        /// <summary>Gets or sets preferences applied when the next recording starts.</summary>
        /// <remarks>Changing preferences never relabels or reconfigures an active encoder.</remarks>
        public EncoderOptions Options { get => _options; set => _options = value ?? throw new ArgumentNullException(nameof(value)); }
        /// <summary>Gets or sets the capture capacity, from one to eight frames, for the next recording.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The capacity is outside one through eight.</exception>
        public int BufferedFrameLimit
        {
            get => _bufferedFrameLimit;
            set
            {
                if (value < 1 || value > 8) { throw new ArgumentOutOfRangeException(nameof(value)); }
                _bufferedFrameLimit = value;
            }
        }
        /// <summary>Gets or sets the readback row interpretation used for the next recording.</summary>
        public CameraReadbackOrientation ReadbackOrientation { get => _readbackOrientation; set => _readbackOrientation = value; }
        /// <summary>Gets the current recording state.</summary>
        public CameraCaptureState State { get; private set; }
        /// <summary>Gets whether new rendered camera frames are being captured.</summary>
        public bool IsRecording => State == CameraCaptureState.Recording;
        /// <summary>Gets whether capture is paused while retaining the current output and encoder.</summary>
        public bool IsPaused
        {
            get => State == CameraCaptureState.Paused;
        }
        /// <summary>Gets whether the enabled component has finished releasing its previous recording resources.</summary>
        /// <remarks>Read on the Unity main thread. This does not validate the camera, output path, or encoder settings.</remarks>
        public bool CanStartRecording
        {
            get => isActiveAndEnabled && (_session == null || _session.Finished)
                && (_resources == null || _resources.Released.IsCompleted);
        }
        /// <summary>Gets the active or last recording path, or null before recording starts.</summary>
        public string? OutputPath { get; private set; }
        /// <summary>Gets the last capture or encoding failure, or null after a successful start.</summary>
        public string? LastError { get; private set; }
        /// <summary>Gets the actual initialized AVCodec name, or null before initialization.</summary>
        public string? EncoderName => _session?.EncoderName;
        /// <summary>Gets the actual backend, or null before encoder initialization.</summary>
        public VideoEncoderType? EncoderType => EncoderName != null ? _session?.EncoderType : null;
        /// <summary>Gets the actual rate-control mode, or null before encoder initialization.</summary>
        public VideoRateControlMode? RateControlMode => EncoderName != null ? _session?.RateControlMode : null;
        /// <summary>Gets the hardware fallback diagnostic, or null when no fallback occurred.</summary>
        public string? HardwareFallbackReason => _session?.HardwareFallbackReason;
        /// <summary>Gets the recent compressed-video bit rate in bits per second, excluding container overhead.</summary>
        /// <remarks>Uses about one second of media time. Pausing retains the value while accepted frames may still finish encoding.</remarks>
        public long CurrentBitRate => _session?.CurrentBitRate ?? 0;
        /// <summary>Gets the configured maximum bit rate of the active or last recording, or zero before starting.</summary>
        public long MaximumBitRate => _activeOptions == null ? 0 : _activeOptions.RateControlMode == VideoRateControlMode.CBR
            ? _activeOptions.BitRate : _activeOptions.MaximumBitRate;
        /// <summary>Gets the configured target bit rate of the active or last recording, or zero before starting.</summary>
        public long BitRate => _activeOptions?.BitRate ?? 0;
        /// <summary>Gets the number of submitted video frames.</summary>
        public long EncodedFrames => _session?.EncodedFrames ?? 0;
        /// <summary>Gets total compressed-video bytes, excluding container overhead.</summary>
        public long BytesWritten => _session?.BytesWritten ?? 0;
        /// <summary>Gets the active software codec worker count, or zero for hardware or before initialization.</summary>
        public int SoftwareThreadCount => _session?.SoftwareThreadCount ?? 0;
        /// <summary>Gets the active recording format, or null before recording starts.</summary>
        public VideoEncodingFormat? EncodingFormat => _activeOptions?.Format;
        /// <summary>Gets the fixed recording width in pixels, or zero before recording starts.</summary>
        public int Width => _activeOptions?.Width ?? 0;
        /// <summary>Gets the fixed recording height in pixels, or zero before recording starts.</summary>
        public int Height => _activeOptions?.Height ?? 0;
        /// <summary>Gets the capture limit and timestamp time base in frames per second, or zero before recording starts.</summary>
        /// <remarks>Actual submissions can be fewer when camera rendering or encoding cannot keep pace. Frames are not duplicated.</remarks>
        public int FrameRate => _activeOptions?.FrameRate ?? 0;
        /// <summary>Gets the number of frame-rate intervals omitted during capture.</summary>
        public long DroppedFrames => _droppedFrames;
        /// <summary>Gets elapsed recording time in seconds, excluding initialization, recording pauses, and finalization.</summary>
        public double TimeSeconds => _clock.Elapsed.TotalSeconds;

        /// <summary>Opens a local output and begins capturing after encoder initialization.</summary>
        /// <param name="outputPath">The new local output file, whose extension selects the container.</param>
        /// <param name="cancellationToken">Cancellation for the entire recording, including encoding and finalization.</param>
        /// <returns>A task completing when capture begins; failures also populate LastError.</returns>
        /// <exception cref="InvalidOperationException">Called off the main thread, already active, disabled, or without a camera.</exception>
        /// <exception cref="NotSupportedException">Asynchronous GPU readback or the requested encoder/container is unavailable.</exception>
        /// <exception cref="ArgumentException">The path or encoding settings are invalid.</exception>
        /// <exception cref="IOException">The local output cannot be opened or written.</exception>
        /// <exception cref="OperationCanceledException">The recording lifetime is canceled.</exception>
        public async Task StartRecordingAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            CheckThread();
            if (!isActiveAndEnabled || (_session != null && !_session.Finished))
            {
                throw new InvalidOperationException("The capturer must be enabled and its previous recording fully stopped.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var camera = _targetCamera != null ? _targetCamera : GetComponent<Camera>();
            if (camera == null) { throw new InvalidOperationException("Assign TargetCamera or attach the capturer to a Camera."); }
            if (!SystemInfo.supportsAsyncGPUReadback) { throw new NotSupportedException("This graphics device does not support asynchronous GPU readback."); }
            if (string.IsNullOrWhiteSpace(outputPath)) { throw new ArgumentException("Provide a local output file.", nameof(outputPath)); }
            if (!Path.IsPathRooted(outputPath) && Uri.TryCreate(outputPath, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Recording requires a local filename rather than a URL.", nameof(outputPath));
            }
            var path = Path.GetFullPath(outputPath);
            var options = _options.ValidateAndClone();
            if (_bufferedFrameLimit < 1 || _bufferedFrameLimit > 8) { throw new ArgumentOutOfRangeException(nameof(BufferedFrameLimit)); }
            if (!Enum.IsDefined(typeof(CameraReadbackOrientation), _readbackOrientation)) { throw new ArgumentOutOfRangeException(nameof(ReadbackOrientation)); }
            Unhook();
            _resources?.Close();
            if (_resources != null && !_resources.Released.IsCompleted)
            {
                throw new InvalidOperationException("Await StopRecordingAsync before restarting while GPU readbacks are pending.");
            }
            _session = null;
            _activeCamera = camera;
            _activeOptions = options;
            OutputPath = path;
            LastError = null;
            _droppedFrames = 0;
            _lastFrameIndex = -1;
            _completionReported = false;
            _clock.Reset();
            State = CameraCaptureState.Preparing;
            VideoEncodeSession? session = null;
            try
            {
                var resources = new CaptureResources(options, _bufferedFrameLimit);
                resources.FlipVertically = _readbackOrientation != CameraReadbackOrientation.TopFirst;
                try
                {
                    session = new VideoEncodeSession(path, options, _bufferedFrameLimit, cancellationToken);
                    _session = session;
                    resources.Session = session;
                }
                catch { resources.Close(); throw; }
                _resources = resources;
                await session.Ready;
                if (session != _session || State != CameraCaptureState.Preparing) { return; }
                cancellationToken.ThrowIfCancellationRequested();
                if (session.Error != null) { throw session.Error; }
                if (!isActiveAndEnabled || camera == null) { StopRecording(); return; }
                if (_captureAction == null)
                {
                    var reference = new WeakReference<FFmpegCameraCapturer>(this);
                    _captureAction = (source, commands) =>
                    {
                        if (reference.TryGetTarget(out var capturer) && capturer != null) { capturer.Capture(source, commands); }
                    };
                }
                Camera.onPostRender += OnCameraRendered;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
                RenderPipelineManager.endContextRendering += OnEndContextRendering;
                _hooked = true;
                SetCaptureBridge(GraphicsSettings.currentRenderPipeline != null && camera.targetTexture == null);
                _clock.Start();
                State = CameraCaptureState.Recording;
                Started?.Invoke(this);
            }
            catch (Exception error)
            {
                if (session == _session)
                {
                    StopRecording();
                    if (!(error is OperationCanceledException)) { ReportError(error); }
                }
                throw;
            }
        }

        /// <summary>Pauses new captures and the recording clock while retaining the output and encoder.</summary>
        /// <remarks>Already accepted GPU readbacks and frames may finish encoding. Calls outside Recording do nothing.</remarks>
        /// <exception cref="InvalidOperationException">Called off the Unity main thread.</exception>
        public void PauseRecording()
        {
            CheckThread();
            if (!IsRecording)
            {
                return;
            }
            _clock.Stop();
            _pendingSrpTexture = null;
            SetCaptureBridge(false);
            State = CameraCaptureState.Paused;
        }

        /// <summary>Resumes the same recording without adding the paused interval to video timestamps.</summary>
        /// <remarks>Calls outside Paused do nothing. An invalid camera or ended session is stopped instead of resumed.</remarks>
        /// <exception cref="InvalidOperationException">Called off the Unity main thread.</exception>
        public void ResumeRecording()
        {
            CheckThread();
            if (!IsPaused)
            {
                return;
            }
            if (!isActiveAndEnabled || _activeCamera == null || _session == null || !_session.CanAcceptFrames)
            {
                StopRecording();
                return;
            }
            SetCaptureBridge(GraphicsSettings.currentRenderPipeline != null && _activeCamera.targetTexture == null);
            _clock.Start();
            State = CameraCaptureState.Recording;
        }

        /// <summary>Stops new captures without waiting on the Unity main thread.</summary>
        /// <remarks>Use StopRecordingAsync to wait until the output trailer is written. Pending GPU readbacks remain alive.</remarks>
        /// <exception cref="InvalidOperationException">Called off the Unity main thread.</exception>
        public void StopRecording()
        {
            CheckThread();
            Unhook();
            _clock.Stop();
            _resources?.Close();
            _session?.Stop();
            if (_session != null && !_completionReported && State != CameraCaptureState.Error)
            {
                State = CameraCaptureState.Stopping;
            }
        }

        /// <summary>Stops capture and waits for all readbacks, delayed packets, and the output trailer.</summary>
        /// <returns>A task completing after native resources are released, or faulting with the recording error.</returns>
        /// <exception cref="InvalidOperationException">Called off the Unity main thread.</exception>
        /// <exception cref="IOException">Capture, encoding, or output finalization fails.</exception>
        /// <exception cref="OperationCanceledException">The recording lifetime was canceled.</exception>
        public async Task StopRecordingAsync()
        {
            StopRecording();
            var session = _session;
            if (session == null) { return; }
            try
            {
                var released = _resources?.Released ?? Task.CompletedTask;
                await Task.WhenAll(session.Completion, released);
            }
            finally { if (session == _session) { ReportCompletion(); } }
        }

        private void Awake() => _mainThread = Thread.CurrentThread.ManagedThreadId;
        private void OnDisable() => StopRecording();
        private void OnDestroy() => StopRecording();
        private void Update()
        {
            if (_session == null)
            {
                return;
            }
            if (_activeCamera == null && (IsRecording || IsPaused))
            {
                StopRecording();
            }
            if (_session.Finished)
            {
                ReportCompletion();
            }
        }

        /// <summary>Captures the active Built-in camera while its final output is available.</summary>
        /// <param name="camera">The camera whose render has just completed.</param>
        private void OnCameraRendered(Camera camera)
        {
            if (!IsRecording || GraphicsSettings.currentRenderPipeline != null || camera != _activeCamera || _resources == null) { return; }
            var commands = _resources.Commands;
            commands.Clear();
            Capture(BuiltinRenderTextureType.CameraTarget, commands);
            Graphics.ExecuteCommandBuffer(commands);
        }

        /// <summary>Selects the SRP capture path before the pipeline reads camera capture actions.</summary>
        /// <param name="context">The pipeline's rendering context.</param>
        /// <param name="camera">The camera beginning a render, including temporary offscreen requests.</param>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _activeCamera && IsRecording) { SetCaptureBridge(camera.targetTexture == null); }
        }

        /// <summary>Retains an explicit SRP target until overlays have rendered.</summary>
        /// <remarks>URP 17.3's capture pass may reference an unallocated intermediate when rendering directly to an offscreen target.</remarks>
        /// <param name="context">The pipeline's rendering context.</param>
        /// <param name="camera">The camera whose rendering has completed.</param>
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsRecording || camera != _activeCamera || camera.targetTexture == null || _resources == null || _bridgeEnabled) { return; }
            _pendingSrpTexture = camera.targetTexture;
        }

        /// <summary>Copies the final explicit SRP target after the complete rendering context.</summary>
        /// <param name="context">The completed pipeline rendering context.</param>
        /// <param name="cameras">The cameras rendered in this context, including stack overlays.</param>
        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            var texture = _pendingSrpTexture;
            _pendingSrpTexture = null;
            if (!IsRecording || texture == null || _resources == null) { return; }
            var commands = _resources.Commands;
            commands.Clear();
            Capture(texture, commands);
            Graphics.ExecuteCommandBuffer(commands);
        }

        /// <summary>Changes bridge registration only when the camera's render destination changes.</summary>
        /// <param name="enabled">Whether the camera needs the renderer-provided display color target.</param>
        private void SetCaptureBridge(bool enabled)
        {
            if (_bridgeEnabled == enabled || _activeCamera == null || _captureAction == null) { return; }
            if (enabled) { CameraCaptureBridge.AddCaptureAction(_activeCamera, _captureAction); }
            else { CameraCaptureBridge.RemoveCaptureAction(_activeCamera, _captureAction); }
            _bridgeEnabled = enabled;
        }

        /// <summary>Records a scaled RGBA copy and asynchronous readback into the renderer's command stream.</summary>
        /// <param name="source">The renderer-provided final camera color target.</param>
        /// <param name="commands">The command buffer that executes after the camera renders.</param>
        private void Capture(RenderTargetIdentifier source, CommandBuffer commands)
        {
            if (!IsRecording || _resources == null || _session == null || _activeOptions == null) { return; }
            var frameIndex = (long)(_clock.Elapsed.TotalSeconds * _activeOptions.FrameRate);
            if (frameIndex <= _lastFrameIndex) { return; }
            _droppedFrames += Math.Max(0, frameIndex - _lastFrameIndex - 1);
            _lastFrameIndex = frameIndex;
            var flip = _resources.FlipVertically;
            var frame = _session.TryReserve(frameIndex, flip);
            if (frame == null) { _droppedFrames++; return; }
            var viewport = GraphicsSettings.currentRenderPipeline == null && _activeCamera != null && _activeCamera.targetTexture == null
                ? _activeCamera.rect : new Rect(0, 0, 1, 1);
            _resources.Record(source, commands, frame, viewport);
        }

        /// <summary>Detaches render hooks without changing the camera's target or viewport.</summary>
        private void Unhook()
        {
            if (!_hooked) { return; }
            Camera.onPostRender -= OnCameraRendered;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            _pendingSrpTexture = null;
            SetCaptureBridge(false);
            _bridgeEnabled = false;
            _hooked = false;
        }

        /// <summary>Publishes completion once and retains the final statistics for callers.</summary>
        private void ReportCompletion()
        {
            if (_session == null || !_session.Finished || _completionReported) { return; }
            StopRecording();
            if (_resources != null && !_resources.Released.IsCompleted) { return; }
            _completionReported = true;
            var error = _session.Error;
            if (error != null && !(error is OperationCanceledException)) { ReportError(error); }
            else if (State != CameraCaptureState.Error) { State = CameraCaptureState.Stopped; }
            Stopped?.Invoke(this);
        }

        /// <summary>Stores a failure and publishes it once on the Unity main thread.</summary>
        /// <param name="error">The capture or encoding failure.</param>
        private void ReportError(Exception error)
        {
            State = CameraCaptureState.Error;
            if (LastError != null) { return; }
            LastError = error.Message;
            ErrorReceived?.Invoke(this, error.Message);
        }

        /// <summary>Enforces the component's Unity main-thread requirement.</summary>
        /// <exception cref="InvalidOperationException">The caller is not the thread that created this component.</exception>
        private void CheckThread()
        {
            if (_mainThread != 0 && Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                throw new InvalidOperationException("Control FFmpegCameraCapturer on the Unity main thread.");
            }
        }

        /// <summary>Retains GPU objects and callbacks independently of the component's lifetime.</summary>
        private sealed class CaptureResources
        {
            /// <summary>Owns one GPU copy and cached callback per bounded frame slot.</summary>
            private readonly ReadbackSlot[] _slots;
            /// <summary>Counts outstanding GPU readbacks that must finish before texture destruction.</summary>
            private int _pending;
            /// <summary>Requests texture destruction as soon as pending readbacks finish.</summary>
            private bool _closing;
            /// <summary>Prevents duplicate native texture and command buffer destruction.</summary>
            private bool _released;
            /// <summary>Completes after the final outstanding GPU request and resource release.</summary>
            private readonly TaskCompletionSource<bool> _releasedCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            /// <summary>Gets or sets the worker receiving the completed packed pixels.</summary>
            public VideoEncodeSession? Session { get; set; }
            /// <summary>Gets the reusable command buffer for Built-in rendering.</summary>
            public CommandBuffer Commands { get; } = new CommandBuffer { name = "FFmpeg Camera Capture" };
            /// <summary>Gets or sets the fixed row conversion for this recording.</summary>
            public bool FlipVertically { get; set; }
            /// <summary>Gets the task reporting that all owned GPU resources have been released.</summary>
            public Task Released => _releasedCompletion.Task;

            /// <summary>Allocates fixed GPU copies without redirecting the source camera.</summary>
            /// <param name="options">The fixed recording resolution.</param>
            /// <param name="capacity">The simultaneous capture limit.</param>
            public CaptureResources(EncoderOptions options, int capacity)
            {
                _slots = new ReadbackSlot[capacity];
                try
                {
                    for (var i = 0; i < capacity; i++) { _slots[i] = new ReadbackSlot(this, options.Width, options.Height); }
                }
                catch { Close(); throw; }
            }

            /// <summary>Adds GPU commands for a reserved buffer.</summary>
            /// <param name="source">The final camera color texture.</param>
            /// <param name="commands">The renderer's execution buffer.</param>
            /// <param name="frame">The corresponding session reservation.</param>
            /// <param name="viewport">The normalized camera region within the source render target.</param>
            public void Record(RenderTargetIdentifier source, CommandBuffer commands, VideoEncodeSession.CaptureFrame frame, Rect viewport)
            {
                var slot = _slots[frame.Slot];
                slot.Frame = frame;
                _pending++;
                try
                {
                    commands.Blit(source, slot.Texture, new Vector2(viewport.width, viewport.height), new Vector2(viewport.x, viewport.y));
                    commands.RequestAsyncReadback(slot.Texture, 0, TextureFormat.RGBA32, slot.Callback);
                }
                catch (Exception error)
                {
                    _pending--;
                    Session?.CompleteReadback(frame, false);
                    Session?.Fail(error);
                }
            }

            /// <summary>Releases GPU resources after their outstanding readbacks finish.</summary>
            public void Close()
            {
                _closing = true;
                if (_pending == 0) { Release(); }
            }

            /// <summary>Destroys all idle owned GPU resources on the Unity thread.</summary>
            private void Release()
            {
                if (_released) { return; }
                _released = true;
                foreach (var slot in _slots)
                {
                    if (slot == null) { continue; }
                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }
                Commands.Dispose();
                _releasedCompletion.TrySetResult(true);
            }

            /// <summary>Owns a reusable GPU copy and its single cached completion callback.</summary>
            private sealed class ReadbackSlot
            {
                /// <summary>Retains the resource owner until the GPU completion runs.</summary>
                private readonly CaptureResources _owner;
                /// <summary>Gets the single-sample RGBA texture owned by this slot.</summary>
                public RenderTexture Texture { get; }
                /// <summary>Gets the cached callback, avoiding a delegate allocation per frame.</summary>
                public Action<AsyncGPUReadbackRequest> Callback { get; }
                /// <summary>Gets or sets the reservation associated with the current GPU request.</summary>
                public VideoEncodeSession.CaptureFrame? Frame { get; set; }

                /// <summary>Allocates one fixed-resolution GPU capture target.</summary>
                /// <param name="owner">The resource lifetime and encoding owner.</param>
                /// <param name="width">The recording width in pixels.</param>
                /// <param name="height">The recording height in pixels.</param>
                /// <exception cref="NotSupportedException">The graphics device cannot create an RGBA8 target.</exception>
                public ReadbackSlot(CaptureResources owner, int width, int height)
                {
                    _owner = owner;
                    Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    {
                        name = "FFmpeg camera readback", antiAliasing = 1, useMipMap = false, autoGenerateMips = false
                    };
                    if (!Texture.Create()) { Destroy(Texture); throw new NotSupportedException("Cannot create the camera's RGBA8 capture target."); }
                    Callback = Complete;
                }

                /// <summary>Copies successful GPU pixels to fixed storage and returns the readback reservation.</summary>
                /// <param name="request">The completed asynchronous GPU request.</param>
                private void Complete(AsyncGPUReadbackRequest request)
                {
                    var frame = Frame;
                    var success = false;
                    try
                    {
                        if (frame == null) { throw new InvalidOperationException("The readback has no frame reservation."); }
                        if (request.hasError) { throw new IOException("Asynchronous camera GPU readback failed."); }
                        request.GetData<byte>().CopyTo(frame.Pixels);
                        success = true;
                    }
                    catch (Exception error) { _owner.Session?.Fail(error); }
                    finally
                    {
                        if (frame != null) { _owner.Session?.CompleteReadback(frame, success); }
                        Frame = null;
                        _owner._pending--;
                        if (_owner._closing && _owner._pending == 0) { _owner.Release(); }
                    }
                }
            }
        }
    }
}
