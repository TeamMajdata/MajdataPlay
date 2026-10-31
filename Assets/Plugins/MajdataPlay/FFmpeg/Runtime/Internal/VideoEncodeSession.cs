#nullable enable
using MajdataPlay.Diagnostics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Owns a fixed pool of readback buffers and one background FFmpeg encoder.</summary>
    internal sealed class VideoEncodeSession
    {
        /// <summary>Describes ownership of a capture buffer.</summary>
        internal enum BufferState
        {
            /// <summary>The buffer is available for a new GPU readback.</summary>
            Free,
            /// <summary>The GPU owns the buffer reservation.</summary>
            Reading,
            /// <summary>The worker may encode the completed pixels.</summary>
            Ready,
            /// <summary>The worker owns the pixels.</summary>
            Encoding
        }

        /// <summary>Stores reusable packed pixels and their timestamp reservation.</summary>
        internal sealed class CaptureFrame
        {
            /// <summary>Identifies the matching reusable GPU texture.</summary>
            public readonly int Slot;
            /// <summary>Stores the tightly packed readback pixels in the session's layout until encoding finishes.</summary>
            public readonly byte[] Pixels;
            /// <summary>Stores the capture timestamp in units of the recording frame rate.</summary>
            public long FrameIndex;
            /// <summary>Requests conversion of bottom-first readback rows to top-first video rows; NV12 frames are already top-first.</summary>
            public bool FlipVertically;
            /// <summary>Tracks the current buffer owner under the session lock.</summary>
            internal BufferState _state;

            /// <summary>Creates one fixed-size capture buffer.</summary>
            /// <param name="slot">The corresponding GPU texture index.</param>
            /// <param name="size">The packed frame byte count.</param>
            public CaptureFrame(int slot, int size)
            {
                Slot = slot;
                Pixels = new byte[size];
            }
        }

        /// <summary>Protects buffer ownership and published encoder statistics.</summary>
        private readonly object _gate = new object();
        /// <summary>Retains all buffers, including GPU and encoder reservations.</summary>
        private readonly CaptureFrame[] _frames;
        /// <summary>Holds the immutable configuration for this session.</summary>
        private readonly EncoderOptions _options;
        /// <summary>Stores the pixel layout of every capture buffer.</summary>
        private readonly CapturePixelLayout _layout;
        /// <summary>Stores the local output path.</summary>
        private readonly string _path;
        /// <summary>Links recording cancellation to the caller's lifetime.</summary>
        private readonly CancellationTokenSource _cancel;
        /// <summary>Completes when FFmpeg has written the output header.</summary>
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Completes after draining, writing the trailer, and releasing FFmpeg resources.</summary>
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Prevents new reservations after stopping.</summary>
        private bool _accepting = true;
        /// <summary>Indicates that the worker has released its native resources.</summary>
        private bool _finished;
        /// <summary>Stores the worker failure, or null after a normal stop.</summary>
        private Exception? _error;
        /// <summary>Retains the actual initialized encoder name.</summary>
        private string? _encoderName;
        /// <summary>Retains the actual initialized encoder backend.</summary>
        private VideoEncoderType _encoderType;
        /// <summary>Retains the actual initialized rate-control mode.</summary>
        private VideoRateControlMode _rateControlMode;
        /// <summary>Retains the reason for selecting software after a hardware preference.</summary>
        private string? _hardwareFallbackReason;
        /// <summary>Retains the description of the device running the initialized encoder.</summary>
        private string? _encoderDevice;
        /// <summary>Store the latest compressed-video rate, encoded count, and byte count.</summary>
        private long _currentBitRate, _encodedFrames, _bytesWritten;
        /// <summary>Stores the active software codec worker count, or zero for hardware.</summary>
        private int _softwareThreadCount;
        /// <summary>Stores how the active software codec parallelizes encoding.</summary>
        private string _softwareThreadingMode = "none";
        /// <summary>Stores the active RGBA color-converter worker count.</summary>
        private int _converterThreadCount;
        /// <summary>Stores the largest number of buffers reserved at once.</summary>
        private int _peakReservedFrames;

        /// <summary>Starts a worker with bounded storage for RGBA8 captures.</summary>
        /// <param name="path">The local output file to create.</param>
        /// <param name="options">The validated configuration snapshot.</param>
        /// <param name="capacity">The maximum simultaneous GPU, queued, and encoder reservations.</param>
        /// <param name="cancellationToken">Cancellation for the entire recording lifetime.</param>
        /// <exception cref="ArgumentOutOfRangeException">The options or capacity are invalid.</exception>
        public VideoEncodeSession(string path, EncoderOptions options, int capacity, CancellationToken cancellationToken)
            : this(path, options, capacity, CapturePixelLayout.Rgba32, cancellationToken)
        {
        }

        /// <summary>Starts a worker with bounded capture storage in one pixel layout.</summary>
        /// <param name="path">The local output file to create.</param>
        /// <param name="options">The validated configuration snapshot.</param>
        /// <param name="capacity">The maximum simultaneous GPU, queued, and encoder reservations.</param>
        /// <param name="layout">The layout that every readback writes into the capture buffers.</param>
        /// <param name="cancellationToken">Cancellation for the entire recording lifetime.</param>
        /// <exception cref="ArgumentOutOfRangeException">The options, capacity, or layout are invalid.</exception>
        public VideoEncodeSession(string path, EncoderOptions options, int capacity, CapturePixelLayout layout,
            CancellationToken cancellationToken)
        {
            _path = path;
            _options = options.ValidateAndClone();
            if (capacity < 1 || capacity > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (!Enum.IsDefined(typeof(CapturePixelLayout), layout))
            {
                throw new ArgumentOutOfRangeException(nameof(layout));
            }
            _layout = layout;
            var frameSize = layout.GetFrameByteCount(_options.Width, _options.Height);
            _frames = new CaptureFrame[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _frames[i] = new CaptureFrame(i, frameSize);
            }
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            new Thread(Run) { IsBackground = true, Name = "FFmpeg camera encoder" }.Start();
        }

        /// <summary>Gets the task that reports encoder initialization.</summary>
        public Task Ready => _ready.Task;
        /// <summary>Gets the task that reports fully finalized output or the recording error.</summary>
        public Task Completion => _completion.Task;
        /// <summary>Gets whether all native encoder resources have been released.</summary>
        public bool Finished
        {
            get
            {
                lock (_gate)
                {
                    return _finished;
                }
            }
        }
        /// <summary>Gets whether the live session accepts frames and has not been stopped, failed, or canceled.</summary>
        public bool CanAcceptFrames
        {
            get
            {
                lock (_gate)
                {
                    return _accepting && !_finished && _error == null && !_cancel.IsCancellationRequested;
                }
            }
        }
        /// <summary>Gets the recording failure, including cancellation, or null.</summary>
        public Exception? Error
        {
            get
            {
                lock (_gate)
                {
                    return _error;
                }
            }
        }
        /// <summary>Gets the initialized FFmpeg encoder name, or null before initialization.</summary>
        public string? EncoderName
        {
            get
            {
                lock (_gate)
                {
                    return _encoderName;
                }
            }
        }
        /// <summary>Gets the actual encoder backend after initialization.</summary>
        public VideoEncoderType EncoderType
        {
            get
            {
                lock (_gate)
                {
                    return _encoderType;
                }
            }
        }
        /// <summary>Gets the actual rate-control mode after initialization.</summary>
        public VideoRateControlMode RateControlMode
        {
            get
            {
                lock (_gate)
                {
                    return _rateControlMode;
                }
            }
        }
        /// <summary>Gets the hardware fallback diagnostic, or null.</summary>
        public string? HardwareFallbackReason
        {
            get
            {
                lock (_gate)
                {
                    return _hardwareFallbackReason;
                }
            }
        }
        /// <summary>Gets the description of the GPU or other device running the encoder, or null before initialization.</summary>
        public string? EncoderDevice
        {
            get
            {
                lock (_gate)
                {
                    return _encoderDevice;
                }
            }
        }
        /// <summary>Gets the recent compressed-video rate in bits per second.</summary>
        public long CurrentBitRate
        {
            get
            {
                lock (_gate)
                {
                    return _currentBitRate;
                }
            }
        }
        /// <summary>Gets the number of submitted video frames.</summary>
        public long EncodedFrames
        {
            get
            {
                lock (_gate)
                {
                    return _encodedFrames;
                }
            }
        }
        /// <summary>Gets the total compressed-video bytes, excluding container overhead.</summary>
        public long BytesWritten
        {
            get
            {
                lock (_gate)
                {
                    return _bytesWritten;
                }
            }
        }
        /// <summary>Gets the active software codec worker count, or zero for hardware.</summary>
        public int SoftwareThreadCount
        {
            get
            {
                lock (_gate)
                {
                    return _softwareThreadCount;
                }
            }
        }
        /// <summary>Gets how the active software codec parallelizes encoding: <c>frame</c>, <c>slice</c>, <c>codec</c>, or <c>none</c>.</summary>
        public string SoftwareThreadingMode
        {
            get
            {
                lock (_gate)
                {
                    return _softwareThreadingMode;
                }
            }
        }
        /// <summary>Gets the active RGBA color-converter worker count, or zero before initialization.</summary>
        public int ConverterThreadCount
        {
            get
            {
                lock (_gate)
                {
                    return _converterThreadCount;
                }
            }
        }

        /// <summary>Gets the number of capture buffers, which bounds simultaneous GPU, queued, and encoder reservations.</summary>
        public int Capacity => _frames.Length;
        /// <summary>Gets the pixel layout that readbacks write into the capture buffers.</summary>
        public CapturePixelLayout PixelLayout => _layout;
        /// <summary>Gets the largest number of buffers reserved at once, including the reservation that found the pool full.</summary>
        /// <remarks>Reaching <see cref="Capacity"/> repeatedly means readback latency and encoding need more buffers.</remarks>
        public int PeakReservedFrames
        {
            get
            {
                lock (_gate)
                {
                    return _peakReservedFrames;
                }
            }
        }

        /// <summary>Reserves one free buffer without waiting for the encoder.</summary>
        /// <param name="frameIndex">The monotonic capture timestamp in frame-rate units.</param>
        /// <param name="flipVertically">Whether the incoming rows are bottom-first.</param>
        /// <returns>The reserved buffer, or null if the pool is full or recording is stopping.</returns>
        public CaptureFrame? TryReserve(long frameIndex, bool flipVertically)
        {
            lock (_gate)
            {
                if (!_accepting || _finished || _cancel.IsCancellationRequested)
                {
                    return null;
                }
                CaptureFrame? reserved = null;
                var reservedCount = 1;
                foreach (var frame in _frames)
                {
                    if (frame._state != BufferState.Free)
                    {
                        reservedCount++;
                    }
                    else if (reserved == null)
                    {
                        reserved = frame;
                    }
                }
                _peakReservedFrames = Math.Max(_peakReservedFrames, Math.Min(reservedCount, _frames.Length));
                if (reserved != null)
                {
                    reserved.FrameIndex = frameIndex;
                    reserved.FlipVertically = flipVertically;
                    reserved._state = BufferState.Reading;
                }
                return reserved;
            }
        }

        /// <summary>Returns a GPU reservation or makes its pixels available to the encoder.</summary>
        /// <param name="frame">The buffer previously returned by TryReserve.</param>
        /// <param name="hasPixels">Whether the GPU readback succeeded.</param>
        public void CompleteReadback(CaptureFrame frame, bool hasPixels)
        {
            lock (_gate)
            {
                frame._state = hasPixels && !_finished ? BufferState.Ready : BufferState.Free;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Stops accepting captures and drains outstanding GPU and encoder reservations.</summary>
        public void Stop()
        {
            lock (_gate)
            {
                _accepting = false;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Stops encoding after a capture failure.</summary>
        /// <param name="error">The GPU or capture error to report from the completion task.</param>
        public void Fail(Exception error)
        {
            lock (_gate)
            {
                _error ??= error;
                _accepting = false;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Encodes completed captures in reservation order on the owner thread.</summary>
        /// <remarks>The worker registers with the Unity Profiler as <c>FFmpeg / Encoder</c>, so its encoder samples appear in the Timeline.</remarks>
        private void Run()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("FFmpeg", "Encoder");
            try
            {
                using (var encoder = new FFmpegVideoEncoder(_options, _layout))
                {
                    encoder.Open(_path, _cancel.Token);
                    Publish(encoder);
                    _ready.TrySetResult(true);
                    while (true)
                    {
                        var frame = TakeNext();
                        if (frame == null)
                        {
                            break;
                        }
                        try
                        {
                            if (_layout == CapturePixelLayout.Nv12)
                            {
                                encoder.EncodeNv12(frame.Pixels, frame.FrameIndex);
                            }
                            else
                            {
                                encoder.Encode(frame.Pixels, frame.FrameIndex, frame.FlipVertically);
                            }
                            Publish(encoder);
                        }
                        finally
                        {
                            lock (_gate)
                            {
                                frame._state = BufferState.Free;
                            }
                        }
                    }
                    encoder.Complete();
                    Publish(encoder);
                }
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _error = error;
                }
                if (error is not OperationCanceledException)
                {
                    MajDebug.LogError("FFmpeg", "[Capturer] Encoding failed: " + error.Message);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _finished = true;
                    _accepting = false;
                }
                _cancel.Dispose();
                if (_error is OperationCanceledException)
                {
                    _ready.TrySetCanceled();
                    _completion.TrySetCanceled();
                }
                else if (_error != null)
                {
                    _ready.TrySetException(_error);
                    _completion.TrySetException(_error);
                    // The component can disappear before Update observes the worker.
                    _ = _ready.Task.Exception;
                    _ = _completion.Task.Exception;
                }
                else
                {
                    _completion.TrySetResult(true);
                }

                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }

        /// <summary>Waits for the earliest capture, preserving order when readbacks complete out of order.</summary>
        /// <returns>The next ready capture, or null when graceful draining finishes.</returns>
        /// <exception cref="OperationCanceledException">The recording lifetime was canceled.</exception>
        private CaptureFrame? TakeNext()
        {
            lock (_gate)
            {
                while (true)
                {
                    _cancel.Token.ThrowIfCancellationRequested();
                    if (_error != null)
                    {
                        throw _error;
                    }
                    CaptureFrame? next = null;
                    foreach (var frame in _frames)
                    {
                        if (frame._state != BufferState.Free && (next == null || frame.FrameIndex < next.FrameIndex))
                        {
                            next = frame;
                        }
                    }
                    if (next == null && !_accepting)
                    {
                        return null;
                    }
                    if (next != null && next._state == BufferState.Ready)
                    {
                        next._state = BufferState.Encoding;
                        return next;
                    }
                    Monitor.Wait(_gate, 50);
                }
            }
        }

        /// <summary>Publishes a consistent encoder snapshot without accessing Unity.</summary>
        /// <param name="encoder">The worker-owned initialized encoder.</param>
        private void Publish(FFmpegVideoEncoder encoder)
        {
            lock (_gate)
            {
                _encoderName = encoder.EncoderName;
                _encoderType = encoder.EncoderType;
                _rateControlMode = encoder.RateControlMode;
                _hardwareFallbackReason = encoder.HardwareFallbackReason;
                _encoderDevice = encoder.EncoderDevice;
                _currentBitRate = encoder.CurrentBitRate;
                _encodedFrames = encoder.EncodedFrames;
                _bytesWritten = encoder.BytesWritten;
                _softwareThreadCount = encoder.SoftwareThreadCount;
                _softwareThreadingMode = encoder.SoftwareThreadingMode;
                _converterThreadCount = encoder.ConverterThreadCount;
            }
        }
    }
}
