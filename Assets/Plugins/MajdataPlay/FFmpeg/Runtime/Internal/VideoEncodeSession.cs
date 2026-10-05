#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;

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
            /// <summary>Stores packed RGBA8 pixels until encoding finishes.</summary>
            public readonly byte[] Pixels;
            /// <summary>Stores the capture timestamp in units of the recording frame rate.</summary>
            public long FrameIndex;
            /// <summary>Requests conversion of bottom-first readback rows to top-first video rows.</summary>
            public bool FlipVertically;
            /// <summary>Tracks the current buffer owner under the session lock.</summary>
            internal BufferState State;

            /// <summary>Creates one fixed-size capture buffer.</summary>
            /// <param name="slot">The corresponding GPU texture index.</param>
            /// <param name="size">The packed RGBA byte count.</param>
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
        /// <summary>Store the latest compressed-video rate, encoded count, and byte count.</summary>
        private long _currentBitRate, _encodedFrames, _bytesWritten;
        /// <summary>Stores the active software codec worker count, or zero for hardware.</summary>
        private int _softwareThreadCount;

        /// <summary>Starts a worker with bounded capture storage.</summary>
        /// <param name="path">The local output file to create.</param>
        /// <param name="options">The validated configuration snapshot.</param>
        /// <param name="capacity">The maximum simultaneous GPU, queued, and encoder reservations.</param>
        /// <param name="cancellationToken">Cancellation for the entire recording lifetime.</param>
        public VideoEncodeSession(string path, EncoderOptions options, int capacity, CancellationToken cancellationToken)
        {
            _path = path;
            _options = options.ValidateAndClone();
            if (capacity < 1 || capacity > 8) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
            _frames = new CaptureFrame[capacity];
            for (var i = 0; i < capacity; i++)
            {
                _frames[i] = new CaptureFrame(i, checked(_options.Width * _options.Height * 4));
            }
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            new Thread(Run) { IsBackground = true, Name = "FFmpeg camera encoder" }.Start();
        }

        /// <summary>Gets the task that reports encoder initialization.</summary>
        public Task Ready => _ready.Task;
        /// <summary>Gets the task that reports fully finalized output or the recording error.</summary>
        public Task Completion => _completion.Task;
        /// <summary>Gets whether all native encoder resources have been released.</summary>
        public bool Finished { get { lock (_gate) { return _finished; } } }
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
        public Exception? Error { get { lock (_gate) { return _error; } } }
        /// <summary>Gets the initialized FFmpeg encoder name, or null before initialization.</summary>
        public string? EncoderName { get { lock (_gate) { return _encoderName; } } }
        /// <summary>Gets the actual encoder backend after initialization.</summary>
        public VideoEncoderType EncoderType { get { lock (_gate) { return _encoderType; } } }
        /// <summary>Gets the actual rate-control mode after initialization.</summary>
        public VideoRateControlMode RateControlMode { get { lock (_gate) { return _rateControlMode; } } }
        /// <summary>Gets the hardware fallback diagnostic, or null.</summary>
        public string? HardwareFallbackReason { get { lock (_gate) { return _hardwareFallbackReason; } } }
        /// <summary>Gets the recent compressed-video rate in bits per second.</summary>
        public long CurrentBitRate { get { lock (_gate) { return _currentBitRate; } } }
        /// <summary>Gets the number of submitted video frames.</summary>
        public long EncodedFrames { get { lock (_gate) { return _encodedFrames; } } }
        /// <summary>Gets the total compressed-video bytes, excluding container overhead.</summary>
        public long BytesWritten { get { lock (_gate) { return _bytesWritten; } } }
        /// <summary>Gets the active software codec worker count, or zero for hardware.</summary>
        public int SoftwareThreadCount { get { lock (_gate) { return _softwareThreadCount; } } }

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
                foreach (var frame in _frames)
                {
                    if (frame.State == BufferState.Free)
                    {
                        frame.FrameIndex = frameIndex;
                        frame.FlipVertically = flipVertically;
                        frame.State = BufferState.Reading;
                        return frame;
                    }
                }
                return null;
            }
        }

        /// <summary>Returns a GPU reservation or makes its pixels available to the encoder.</summary>
        /// <param name="frame">The buffer previously returned by TryReserve.</param>
        /// <param name="hasPixels">Whether the GPU readback succeeded.</param>
        public void CompleteReadback(CaptureFrame frame, bool hasPixels)
        {
            lock (_gate)
            {
                frame.State = hasPixels && !_finished ? BufferState.Ready : BufferState.Free;
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
        private void Run()
        {
            try
            {
                using (var encoder = new FFmpegVideoEncoder(_options))
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
                            encoder.Encode(frame.Pixels, frame.FrameIndex, frame.FlipVertically);
                            Publish(encoder);
                        }
                        finally
                        {
                            lock (_gate) { frame.State = BufferState.Free; }
                        }
                    }
                    encoder.Complete();
                    Publish(encoder);
                }
            }
            catch (Exception error)
            {
                lock (_gate) { _error = error; }
                if (!(error is OperationCanceledException))
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
                        if (frame.State != BufferState.Free && (next == null || frame.FrameIndex < next.FrameIndex))
                        {
                            next = frame;
                        }
                    }
                    if (next == null && !_accepting)
                    {
                        return null;
                    }
                    if (next != null && next.State == BufferState.Ready)
                    {
                        next.State = BufferState.Encoding;
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
                _currentBitRate = encoder.CurrentBitRate;
                _encodedFrames = encoder.EncodedFrames;
                _bytesWritten = encoder.BytesWritten;
                _softwareThreadCount = encoder.SoftwareThreadCount;
            }
        }
    }
}
