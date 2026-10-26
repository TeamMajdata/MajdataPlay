#nullable enable
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    /// MRP display output uses MajCameraCaptureBridge; other SRPs use CameraCaptureBridge.
    /// Explicit targets are copied after end-context rendering.
    /// Built-in uses the camera's post-render callback. Custom pipelines must execute bridge actions or
    /// raise end-camera and end-context callbacks for explicit targets. Recording includes camera output,
    /// excluding audio and Screen Space Overlay canvases. Hardware encoding still uses CPU pixel readback.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Video/FFmpeg Camera Capturer")]
    [DefaultExecutionOrder(FrameStartExecutionOrder)]
    public sealed class FFmpegCameraCapturer : MonoBehaviour
    {
        /// <summary>Runs <see cref="Update"/> before other scripts so that it samples the recording clock at the start of each frame.</summary>
        private const int FrameStartExecutionOrder = -32000;
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
        private int _bufferedFrameLimit = 6;
        /// <summary>Controls conversion from native GPU row order to top-first video.</summary>
        [SerializeField]
        [FormerlySerializedAs("ReadbackOrientation")]
        private CameraReadbackOrientation _readbackOrientation;
        /// <summary>Requests RGBA to NV12 conversion on the GPU before readback when the device supports it.</summary>
        [SerializeField]
        [FormerlySerializedAs("PreferGpuColorConversion")]
        private bool _preferGpuColorConversion = true;
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
        /// <summary>Assigns strictly increasing frame timestamps to renders and counts frame periods without a render.</summary>
        private readonly CaptureFrameTimeline _timeline = new CaptureFrameTimeline();
        /// <summary>Stores the recording clock in seconds when <see cref="Update"/> began the current Unity frame.</summary>
        private double _frameStartSeconds;
        /// <summary>Stores the Unity frame in which <see cref="_frameStartSeconds"/> was sampled, or -1 before any.</summary>
        private int _frameStartFrame = -1;
        /// <summary>Stores the Unity frame of the latest capture attempt so that each frame is recorded at most once.</summary>
        private int _lastCaptureFrame = -1;
        /// <summary>Counts rendered frames discarded because every capture buffer was reserved.</summary>
        private long _bufferOverflowFrames;
        /// <summary>Tracks whether render callbacks are attached.</summary>
        private bool _hooked;
        /// <summary>Tracks the single bridge supplying the current SRP display output.</summary>
        private CaptureBridgeRoute _captureBridge;
        /// <summary>Identifies the provider of a renderer-owned display color target.</summary>
        private enum CaptureBridgeRoute
        {
            /// <summary>No display callback is registered.</summary>
            None,
            /// <summary>The active SRP executes Core RP capture actions.</summary>
            Core,
            /// <summary>The active MRP executes its engine-only capture actions.</summary>
            Maj
        }
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
                if (value < 1 || value > 8)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _bufferedFrameLimit = value;
            }
        }
        /// <summary>Gets or sets the readback row interpretation used for the next recording.</summary>
        public CameraReadbackOrientation ReadbackOrientation { get => _readbackOrientation; set => _readbackOrientation = value; }
        /// <summary>Gets or sets whether the next recording converts RGBA to NV12 on the GPU before readback when the device supports it.</summary>
        /// <remarks>
        /// GPU conversion reads back 1.5 instead of 4 bytes per pixel and needs no CPU converter threads, with samples equal
        /// to the CPU conversion. Without support for the conversion shader or R8 render and readback targets the
        /// recording converts on the CPU; see <see cref="UsesGpuColorConversion"/>.
        /// </remarks>
        public bool PreferGpuColorConversion { get => _preferGpuColorConversion; set => _preferGpuColorConversion = value; }
        /// <summary>Gets whether the active or last recording converts RGBA to NV12 on the GPU, or false before recording starts.</summary>
        public bool UsesGpuColorConversion => _session?.PixelLayout == CapturePixelLayout.Nv12;
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
        /// <summary>Gets how the active software codec parallelizes encoding: <c>frame</c>, <c>slice</c>, <c>codec</c>, or <c>none</c> for one thread, hardware, or before initialization.</summary>
        public string SoftwareThreadingMode => _session?.SoftwareThreadingMode ?? "none";
        /// <summary>Gets the active RGBA color-converter worker count, or zero before initialization or with GPU color conversion.</summary>
        public int ConverterThreadCount => _session?.ConverterThreadCount ?? 0;
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
        /// <remarks>It is the sum of <see cref="SourceGapFrames"/> and <see cref="BufferOverflowFrames"/>.</remarks>
        public long DroppedFrames => _timeline.SourceGapFrames + _bufferOverflowFrames;
        /// <summary>Gets the number of frame-rate intervals in which the camera rendered no frame, because it rendered slower than <see cref="FrameRate"/>.</summary>
        public long SourceGapFrames => _timeline.SourceGapFrames;
        /// <summary>Gets the number of rendered frames discarded because GPU readback and encoding held every capture buffer.</summary>
        /// <remarks>A nonzero value during steady rendering means <see cref="BufferedFrameLimit"/> is too small or the encoder is too slow.</remarks>
        public long BufferOverflowFrames => _bufferOverflowFrames;
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
            if (camera == null)
            {
                throw new InvalidOperationException("Assign TargetCamera or attach the capturer to a Camera.");
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                throw new NotSupportedException("This graphics device does not support asynchronous GPU readback.");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Provide a local output file.", nameof(outputPath));
            }

            if (!Path.IsPathRooted(outputPath) && Uri.TryCreate(outputPath, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Recording requires a local filename rather than a URL.", nameof(outputPath));
            }

            var path = Path.GetFullPath(outputPath);
            var options = _options.ValidateAndClone();

            if (_bufferedFrameLimit < 1 || _bufferedFrameLimit > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(BufferedFrameLimit));
            }

            if (!Enum.IsDefined(typeof(CameraReadbackOrientation), _readbackOrientation))
            {
                throw new ArgumentOutOfRangeException(nameof(ReadbackOrientation));
            }

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
            _timeline.Reset();
            _lastCaptureFrame = -1;
            _bufferOverflowFrames = 0;
            _completionReported = false;
            _clock.Reset();
            State = CameraCaptureState.Preparing;
            VideoEncodeSession? session = null;
            try
            {
                var resources = new CaptureResources(options, _bufferedFrameLimit,
                    _readbackOrientation != CameraReadbackOrientation.TopFirst, _preferGpuColorConversion);
                try
                {
                    session = new VideoEncodeSession(path, options, _bufferedFrameLimit, resources.PixelLayout, cancellationToken);
                    _session = session;
                    resources.Session = session;
                }
                catch
                {
                    resources.Close();
                    throw;
                }

                _resources = resources;
                await session.Ready;
                if (session != _session || State != CameraCaptureState.Preparing)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (session.Error != null)
                {
                    throw session.Error;
                }

                if (!isActiveAndEnabled || camera == null)
                {
                    StopRecording();
                    return;
                }

                if (_captureAction == null)
                {
                    var reference = new WeakReference<FFmpegCameraCapturer>(this);
                    _captureAction = (source, commands) =>
                    {
                        if (reference.TryGetTarget(out var capturer) && capturer != null)
                        {
                            capturer.Capture(source, commands);
                        }
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
                    if (!(error is OperationCanceledException))
                    {
                        ReportError(error);
                    }
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
            if (session == null)
            {
                return;
            }

            try
            {
                var released = _resources?.Released ?? Task.CompletedTask;
                await Task.WhenAll(session.Completion, released);
            }
            finally
            {
                if (session == _session)
                {
                    ReportCompletion();
                }
            }
        }

        private void Awake()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnDisable()
        {
            StopRecording();
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        private void Update()
        {
            _frameStartSeconds = _clock.Elapsed.TotalSeconds;
            _frameStartFrame = Time.frameCount;
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
            if (!IsRecording || GraphicsSettings.currentRenderPipeline != null || camera != _activeCamera || _resources == null)
            {
                return;
            }

            var commands = _resources.Commands;
            SetCaptureBridge(false);
            commands.Clear();
            Capture(BuiltinRenderTextureType.CameraTarget, commands);
            Graphics.ExecuteCommandBuffer(commands);
        }

        /// <summary>Selects the SRP capture path before the pipeline reads camera capture actions.</summary>
        /// <param name="context">The pipeline's rendering context.</param>
        /// <param name="camera">The camera beginning a render, including temporary offscreen requests.</param>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _activeCamera && IsRecording)
            {
                SetCaptureBridge(camera.targetTexture == null);
            }
        }

        /// <summary>Retains an explicit SRP target until overlays have rendered.</summary>
        /// <remarks>URP 17.3's capture pass may reference an unallocated intermediate when rendering directly to an offscreen target.</remarks>
        /// <param name="context">The pipeline's rendering context.</param>
        /// <param name="camera">The camera whose rendering has completed.</param>
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsRecording || camera != _activeCamera || camera.targetTexture == null || _resources == null
                || _captureBridge != CaptureBridgeRoute.None)
            {
                return;
            }

            _pendingSrpTexture = camera.targetTexture;
        }

        /// <summary>Copies the final explicit SRP target after the complete rendering context.</summary>
        /// <param name="context">The completed pipeline rendering context.</param>
        /// <param name="cameras">The cameras rendered in this context, including stack overlays.</param>
        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            var texture = _pendingSrpTexture;
            _pendingSrpTexture = null;
            if (!IsRecording || texture == null || _resources == null)
            {
                return;
            }

            var commands = _resources.Commands;
            commands.Clear();
            Capture(texture, commands);
            Graphics.ExecuteCommandBuffer(commands);
        }

        /// <summary>Routes display capture to exactly one bridge as the pipeline or destination changes.</summary>
        /// <param name="enabled">Whether the camera needs the renderer-provided display color target.</param>
        private void SetCaptureBridge(bool enabled)
        {
            var camera = _activeCamera;
            var action = _captureAction;
            var route = CaptureBridgeRoute.None;
            if (enabled && camera != null && action != null)
            {
                var pipeline = GraphicsSettings.currentRenderPipeline;
                route = pipeline is MajRenderPipelineAsset ? CaptureBridgeRoute.Maj
                    : pipeline != null ? CaptureBridgeRoute.Core : CaptureBridgeRoute.None;
            }

            if (_captureBridge == route)
            {
                return;
            }

            // MRP can unregister a destroyed Unity camera through its retained managed reference.
            // Core RP's own null guard remains responsible for its existing destroyed-camera behavior.
            if (!ReferenceEquals(camera, null) && action != null)
            {
                if (_captureBridge == CaptureBridgeRoute.Maj)
                {
                    MajCameraCaptureBridge.RemoveCaptureAction(camera, action);
                }
                else if (_captureBridge == CaptureBridgeRoute.Core)
                {
                    CameraCaptureBridge.RemoveCaptureAction(camera, action);
                }
            }

            _captureBridge = CaptureBridgeRoute.None;
            if (camera == null || action == null)
            {
                return;
            }

            if (route == CaptureBridgeRoute.Maj)
            {
                MajCameraCaptureBridge.AddCaptureAction(camera, action);
            }
            else if (route == CaptureBridgeRoute.Core)
            {
                CameraCaptureBridge.AddCaptureAction(camera, action);
            }

            _captureBridge = route;
        }

        /// <summary>Records a scaled RGBA copy and asynchronous readback into the renderer's command stream.</summary>
        /// <param name="source">The renderer-provided final camera color target.</param>
        /// <param name="commands">The command buffer that executes after the camera renders.</param>
        private void Capture(RenderTargetIdentifier source, CommandBuffer commands)
        {
            if (!IsRecording || _resources == null || _session == null || _activeOptions == null)
            {
                return;
            }

            // Covers reservation and command recording; the GPU copy, conversion and readback run later on the render thread.
            using var profile = UnityProfiler.Create("FFmpeg.Capturer.Capture");
            var unityFrame = Time.frameCount;
            if (unityFrame == _lastCaptureFrame)
            {
                return;
            }

            _lastCaptureFrame = unityFrame;
            // The render shows the state computed at the start of its frame; timing it there also excludes the varying
            // script and culling time before rendering, which would otherwise jitter the timestamps. A sample taken before
            // this recording's clock was reset is newer than the clock and is ignored.
            var seconds = _clock.Elapsed.TotalSeconds;
            if (_frameStartFrame == unityFrame && _frameStartSeconds <= seconds)
            {
                seconds = _frameStartSeconds;
            }
            if (!_timeline.TryAssign(seconds * _activeOptions.FrameRate, out var frameIndex))
            {
                return;
            }

            var flip = _resources.FlipVertically;
            var frame = _session.TryReserve(frameIndex, flip);
            if (frame == null)
            {
                _bufferOverflowFrames++;
                return;
            }

            var viewport = GraphicsSettings.currentRenderPipeline == null && _activeCamera != null && _activeCamera.targetTexture == null
                ? _activeCamera.rect : new Rect(0, 0, 1, 1);
            _resources.Record(source, commands, frame, viewport);
        }

        /// <summary>Detaches render hooks without changing the camera's target or viewport.</summary>
        private void Unhook()
        {
            if (!_hooked)
            {
                return;
            }

            Camera.onPostRender -= OnCameraRendered;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            _pendingSrpTexture = null;
            SetCaptureBridge(false);
            _captureBridge = CaptureBridgeRoute.None;
            _hooked = false;
        }

        /// <summary>Publishes completion once and retains the final statistics for callers.</summary>
        private void ReportCompletion()
        {
            if (_session == null || !_session.Finished || _completionReported)
            {
                return;
            }

            StopRecording();
            if (_resources != null && !_resources.Released.IsCompleted)
            {
                return;
            }

            _completionReported = true;
            MajDebug.LogInfo("FFmpeg", "[Capturer] Recorded " + EncodedFrames + " frames at " + FrameRate + " fps; " + _timeline.SourceGapFrames
                + " frame periods had no camera render and " + _bufferOverflowFrames + " renders found all " + _session.Capacity
                + " capture buffers busy (peak " + _session.PeakReservedFrames + " reserved).");
            var error = _session.Error;
            if (error != null && !(error is OperationCanceledException))
            {
                ReportError(error);
            }
            else if (State != CameraCaptureState.Error)
            {
                State = CameraCaptureState.Stopped;
            }

            Stopped?.Invoke(this);
        }

        /// <summary>Stores a failure and publishes it once on the Unity main thread.</summary>
        /// <param name="error">The capture or encoding failure.</param>
        private void ReportError(Exception error)
        {
            State = CameraCaptureState.Error;
            if (LastError != null)
            {
                return;
            }

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
            /// <summary>Converts captures to NV12 before readback, or null when slots read back RGBA for CPU conversion.</summary>
            private readonly CaptureGpuConverter? _gpuConverter;
            /// <summary>Stores the texture format requested from every readback.</summary>
            private readonly TextureFormat _readbackFormat;
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
            /// <summary>Gets the fixed row conversion for this recording.</summary>
            public bool FlipVertically { get; }
            /// <summary>Gets the layout that the readbacks deliver to the encoding session.</summary>
            public CapturePixelLayout PixelLayout => _gpuConverter != null ? CapturePixelLayout.Nv12 : CapturePixelLayout.Rgba32;
            /// <summary>Gets the task reporting that all owned GPU resources have been released.</summary>
            public Task Released => _releasedCompletion.Task;

            /// <summary>Allocates fixed GPU copies without redirecting the source camera.</summary>
            /// <param name="options">The fixed recording resolution.</param>
            /// <param name="capacity">The simultaneous capture limit.</param>
            /// <param name="flipVertically">Whether the captured rows are bottom-first.</param>
            /// <param name="preferGpuConversion">Whether to convert to NV12 on the GPU when the device supports it.</param>
            /// <exception cref="NotSupportedException">The graphics device cannot create a capture target.</exception>
            public CaptureResources(EncoderOptions options, int capacity, bool flipVertically, bool preferGpuConversion)
            {
                FlipVertically = flipVertically;
                _slots = new ReadbackSlot[capacity];
                try
                {
                    if (preferGpuConversion)
                    {
                        _gpuConverter = CaptureGpuConverter.TryCreate(options.Width, options.Height, flipVertically, out var reason);
                        if (_gpuConverter == null)
                        {
                            MajDebug.LogInfo("FFmpeg", "[Capturer] Converting captures on the CPU because " + reason + ".");
                        }
                    }

                    _readbackFormat = _gpuConverter != null ? CaptureGpuConverter.ReadbackFormat : TextureFormat.RGBA32;
                    for (var i = 0; i < capacity; i++)
                    {
                        var texture = _gpuConverter != null ? _gpuConverter.CreateTarget()
                            : CaptureGpuConverter.CreateRgbaTarget(options.Width, options.Height)
                                ?? throw new NotSupportedException("Cannot create the camera's RGBA8 capture target.");
                        _slots[i] = new ReadbackSlot(this, texture);
                    }
                }
                catch
                {
                    Close();
                    throw;
                }
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
                    var scale = new Vector2(viewport.width, viewport.height);
                    var offset = new Vector2(viewport.x, viewport.y);
                    if (_gpuConverter != null)
                    {
                        _gpuConverter.Record(commands, source, scale, offset, slot.Texture);
                    }
                    else
                    {
                        commands.Blit(source, slot.Texture, scale, offset);
                    }

                    commands.RequestAsyncReadback(slot.Texture, 0, _readbackFormat, slot.Callback);
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
                if (_pending == 0)
                {
                    Release();
                }
            }

            /// <summary>Destroys all idle owned GPU resources on the Unity thread.</summary>
            private void Release()
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                foreach (var slot in _slots)
                {
                    if (slot == null)
                    {
                        continue;
                    }

                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }

                _gpuConverter?.Dispose();
                Commands.Dispose();
                _releasedCompletion.TrySetResult(true);
            }

            /// <summary>Owns a reusable GPU copy and its single cached completion callback.</summary>
            private sealed class ReadbackSlot
            {
                /// <summary>Retains the resource owner until the GPU completion runs.</summary>
                private readonly CaptureResources _owner;
                /// <summary>Gets the single-sample RGBA or NV12 readback texture owned by this slot.</summary>
                public RenderTexture Texture { get; }
                /// <summary>Gets the cached callback, avoiding a delegate allocation per frame.</summary>
                public Action<AsyncGPUReadbackRequest> Callback { get; }
                /// <summary>Gets or sets the reservation associated with the current GPU request.</summary>
                public VideoEncodeSession.CaptureFrame? Frame { get; set; }

                /// <summary>Takes ownership of one fixed-resolution GPU capture target.</summary>
                /// <param name="owner">The resource lifetime and encoding owner.</param>
                /// <param name="texture">The created readback target, destroyed when the owner releases its resources.</param>
                public ReadbackSlot(CaptureResources owner, RenderTexture texture)
                {
                    _owner = owner;
                    Texture = texture;
                    Callback = Complete;
                }

                /// <summary>Copies successful GPU pixels to fixed storage and returns the readback reservation.</summary>
                /// <param name="request">The completed asynchronous GPU request.</param>
                private void Complete(AsyncGPUReadbackRequest request)
                {
                    using var profile = UnityProfiler.Create("FFmpeg.Capturer.CompleteReadback");
                    var frame = Frame;
                    var success = false;
                    try
                    {
                        if (frame == null)
                        {
                            throw new InvalidOperationException("The readback has no frame reservation.");
                        }

                        if (request.hasError)
                        {
                            throw new IOException("Asynchronous camera GPU readback failed.");
                        }

                        using (UnityProfiler.Create("FFmpeg.Capturer.CopyReadback"))
                        {
                            request.GetData<byte>().CopyTo(frame.Pixels);
                        }

                        success = true;
                    }
                    catch (Exception error)
                    {
                        _owner.Session?.Fail(error);
                    }
                    finally
                    {
                        if (frame != null)
                        {
                            _owner.Session?.CompleteReadback(frame, success);
                        }

                        Frame = null;
                        _owner._pending--;
                        if (_owner._closing && _owner._pending == 0)
                        {
                            _owner.Release();
                        }
                    }
                }
            }
        }
    }
}
