#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Publishes immutable decoder metadata across the worker and presentation threads.</summary>
    internal sealed class VideoInfo
    {
        /// <summary>Store the video's displayed dimensions in pixels.</summary>
        public readonly int Width, Height;
        /// <summary>Store the duration in seconds and estimated frame rate in frames per second.</summary>
        public readonly double Duration, FrameRate;
        /// <summary>Stores the average compressed video bit rate in bits per second, or zero when unknown.</summary>
        public readonly long BitRate;
        /// <summary>Report seek support, the presence of an ignored audio stream, and active hardware decoding, respectively.</summary>
        public readonly bool CanSeek, HasAudio, HardwareDecoding;
        /// <summary>Store the codec identity, optional hardware fallback reason, and selected decoder name.</summary>
        public readonly string? Codec, HardwareFallbackReason, DecoderName;
        /// <summary>Describe the actual decoding device and frame transfer path.</summary>
        public readonly string DecoderDevice, TransferMode;
        /// <summary>Identifies the hardware backend active when this snapshot was captured.</summary>
        public readonly global::FFmpeg.AutoGen.AVHWDeviceType HardwareDeviceType;
        /// <summary>Captures immutable media and transport information from an open decoder.</summary>
        /// <param name="decoder">The open worker-owned decoder whose metadata is captured.</param>
        public VideoInfo(FFmpegVideoDecoder decoder)
        {
            Width = decoder.Width;
            Height = decoder.Height;
            Duration = decoder.Duration;
            FrameRate = decoder.FrameRate;
            BitRate = decoder.BitRate;
            CanSeek = decoder.CanSeek;
            HasAudio = decoder.HasAudio;
            Codec = decoder.CodecName;
            HardwareFallbackReason = decoder.HardwareFallbackReason;
            DecoderName = decoder.DecoderName;
            DecoderDevice = decoder.DecoderDevice;
            HardwareDecoding = decoder.HardwareDecoding;
            TransferMode = decoder.TransferMode;
            HardwareDeviceType = decoder.ActiveHardwareDeviceType;
        }
    }

    /// <summary>One owner thread per demuxer. Cancellation never joins the Unity thread.</summary>
    internal sealed class VideoDecodeSession : IDisposable
    {
        /// <summary>The wall-clock lag, in seconds, after which the worker skips to a later keyframe instead of decoding the gap.</summary>
        private const double CatchUpLagSeconds = 0.25;
        /// <summary>Protects the playback timeline, queued frames, control revisions, and worker status across threads.</summary>
        private readonly object _gate = new object();
        /// <summary>Queues owned frames awaiting main-thread presentation.</summary>
        private readonly Queue<DecodedVideoFrame> _frames;
        /// <summary>Preallocates containers for queued, worker-held, and presenter-held frames.</summary>
        private readonly DecodedVideoFramePool _framePool;
        /// <summary>Interrupts input and decoding when the session closes.</summary>
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        /// <summary>Stores the normalized media input path or URL.</summary>
        private readonly string _path;
        /// <summary>Stores immutable decoding limits and hardware preferences for this session.</summary>
        private readonly DecoderOptions _options;
        /// <summary>Limits the number of queued presentation frames.</summary>
        private readonly int _capacity;
        /// <summary>Owns the shared presentation and frame-expiration timeline, accessed under the session lock.</summary>
        private readonly PlaybackClock _playbackClock = new PlaybackClock();
        /// <summary>Track requested closure, worker completion, and fully drained input, respectively.</summary>
        private bool _disposed, _finished, _eof;
        /// <summary>Identifies the latest seek request so stale decoded frames can be discarded.</summary>
        private long _revision;
        /// <summary>Identifies the seek revision the worker is currently decoding for, accessed under the session lock.</summary>
        private long _decodingRevision;
        /// <summary>Counts decoded frames released before conversion because the playback clock had passed them.</summary>
        private long _discardedFrames;
        /// <summary>Counts forward keyframe seeks performed to catch up with the playback clock.</summary>
        private long _catchUpSeeks;
        /// <summary>Stores the latest requested seek position in seconds.</summary>
        private double _seek;
        /// <summary>Publishes the latest immutable decoder information under the lock.</summary>
        private VideoInfo? _info;
        /// <summary>Stores the worker failure, or null when no failure has been reported.</summary>
        private Exception? _error;
        /// <summary>Starts a background decoder with a bounded frame queue and private frame pool.</summary>
        /// <param name="path">The media input path or FFmpeg-supported URL.</param>
        /// <param name="options">The resource limits and hardware configuration to use.</param>
        /// <param name="capacity">The number of frame containers or queued frames to retain.</param>
        /// <param name="playbackRate">The initial playback multiplier, applied before the decoding worker starts.</param>
        /// <exception cref="ArgumentOutOfRangeException">The playback rate is not finite or outside the supported range.</exception>
        public VideoDecodeSession(string path, DecoderOptions options, int capacity, double playbackRate = 1)
        {
            _playbackClock.Rate = playbackRate;
            _path = path;
            _options = options;
            _capacity = Math.Max(1, Math.Min(8, capacity));
            _frames = new Queue<DecodedVideoFrame>(_capacity);
            // The worker and presenter can each hold one frame outside the queue.
            _framePool = new DecodedVideoFramePool(_capacity + 2);
            new Thread(Run)
            {
                IsBackground = true,
                Name = "FFmpeg video decoder"
            }.Start();
        }

        /// <summary>Gets the current media snapshot, or null until the input is opened.</summary>
        public VideoInfo? Info
        {
            get
            {
                lock (_gate)
                {
                    return _info;
                }
            }
        }

        /// <summary>Gets the worker failure, or null if no failure has been reported.</summary>
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

        /// <summary>Gets whether decoding ended and all queued frames have been consumed.</summary>
        public bool EndOfStream
        {
            get
            {
                lock (_gate)
                {
                    return _eof && _frames.Count == 0;
                }
            }
        }

        /// <summary>Gets the number of frames waiting in the presentation queue.</summary>
        public int BufferedFrames
        {
            get
            {
                lock (_gate)
                {
                    return _frames.Count;
                }
            }
        }

        /// <summary>Gets the next queued timestamp in seconds, or NaN if the queue is empty.</summary>
        public double NextPresentationTime
        {
            get
            {
                lock (_gate)
                {
                    return _frames.Count == 0 ? double.NaN : _frames.Peek().PresentationTime;
                }
            }
        }

        /// <summary>Gets the number of decoded frames discarded before conversion because they had already expired.</summary>
        public long DiscardedFrames
        {
            get
            {
                lock (_gate)
                {
                    return _discardedFrames;
                }
            }
        }

        /// <summary>Gets the number of forward keyframe seeks performed to catch up with the playback clock.</summary>
        public long CatchUpSeeks
        {
            get
            {
                lock (_gate)
                {
                    return _catchUpSeeks;
                }
            }
        }

        /// <summary>Gets the shared playback position in seconds under the session lock.</summary>
        public double PlaybackPosition
        {
            get
            {
                lock (_gate)
                {
                    return _playbackClock.Position;
                }
            }
        }

        /// <summary>Updates the shared playback timeline atomically and wakes the decoding worker.</summary>
        /// <param name="position">An optional new media position in seconds; null preserves the current position.</param>
        /// <param name="rate">An optional playback multiplier from 0.0625 through 16; null preserves the current rate.</param>
        /// <param name="playing">True starts the clock, false freezes it, and null preserves its running state.</param>
        /// <exception cref="ArgumentOutOfRangeException">The supplied rate is not finite or outside the supported range.</exception>
        public void SetPlayback(double? position = null, double? rate = null, bool? playing = null)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (rate.HasValue)
                {
                    _playbackClock.Rate = rate.Value;
                }

                if (playing == false)
                {
                    _playbackClock.Pause();
                }

                if (position.HasValue)
                {
                    _playbackClock.Set(position.Value);
                }

                if (playing == true)
                {
                    _playbackClock.Start();
                }

                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Transfers ownership of the next queued frame to the caller and wakes the worker.</summary>
        /// <returns>The next owned frame, or null if the queue is empty; the caller must dispose the frame.</returns>
        public DecodedVideoFrame? TakeFrame()
        {
            lock (_gate)
            {
                if (_frames.Count == 0)
                {
                    return null;
                }

                var frame = _frames.Dequeue();
                Monitor.PulseAll(_gate);
                return frame;
            }
        }

        /// <summary>Transfers the newest due frame and releases older due frames atomically.</summary>
        /// <param name="maximumPresentationTime">The latest eligible timestamp in seconds.</param>
        /// <returns>The newest eligible owned frame, or null; the caller must dispose the returned frame.</returns>
        /// <remarks>The bounded queue cannot refill during selection, and only one frame leaves its ownership.</remarks>
        public DecodedVideoFrame? TakeLatestFrame(double maximumPresentationTime)
        {
            lock (_gate)
            {
                DecodedVideoFrame? newest = null;
                while (_frames.Count != 0 && _frames.Peek().PresentationTime <= maximumPresentationTime)
                {
                    newest?.Dispose();
                    newest = _frames.Dequeue();
                }

                Monitor.PulseAll(_gate);
                return newest;
            }
        }

        /// <summary>Replaces pending seeks, discards buffered frames, and wakes the decoder worker.</summary>
        /// <param name="seconds">The media timeline position in seconds.</param>
        /// <exception cref="ObjectDisposedException">The session has already been closed.</exception>
        public void Seek(double seconds)
        {
            MajDebug.LogDebug("FFmpeg", "[Session] Scheduling seek to " + seconds + " seconds.");
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(VideoDecodeSession));
                }

                // Preparation of the seek frame must not use the previous playback cutoff.
                _playbackClock.Pause();
                _playbackClock.Set(seconds);
                _seek = seconds;
                _revision++;
                _eof = false;
                ClearFrames();
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Owns the decoder lifecycle, executes seeks, and fills the bounded presentation queue.</summary>
        private void Run()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("FFmpeg", "Decoder");
            MajDebug.LogDebug("FFmpeg", "[Session] Decode worker started; queue capacity=" + _capacity + ".");
            try
            {
                using (var decoder = new FFmpegVideoDecoder(_options, _framePool))
                {
                    decoder.FrameDeadlineProvider = ReadFrameDeadline;
                    decoder.Open(_path, _cancel.Token);
                    lock (_gate)
                    {
                        _info = new VideoInfo(decoder);
                    }

                    MajDebug.LogInfo("FFmpeg", "[Session] Media ready; codec=" + decoder.CodecName
                        + ", decoder=" + decoder.DecoderName + ", device=" + decoder.DecoderDevice + ".");
                    long decodedRevision = 0;
                    while (true)
                    {
                        long revision;
                        double seek;
                        lock (_gate)
                        {
                            while (!_disposed && decodedRevision == _revision && (_eof || _frames.Count >= _capacity))
                            {
                                if (!_eof && _playbackClock.Running && _frames.Peek().PresentationTime <= _playbackClock.Position)
                                {
                                    // Keep the queued frame available until a newer due candidate
                                    // replaces it, including when the queue has only one slot.
                                    break;
                                }

                                Monitor.Wait(_gate, _eof ? Timeout.Infinite : FrameWaitMilliseconds(_frames.Peek().PresentationTime));
                            }

                            if (_disposed)
                            {
                                break;
                            }

                            revision = _revision;
                            _decodingRevision = revision;
                            seek = _seek;
                        }

                        if (revision != decodedRevision)
                        {
                            decoder.Seek(seek);
                            decodedRevision = revision;
                        }

                        DecodedVideoFrame? frame;
                        using (var profile = UnityProfiler.Create("FFmpeg.Session.DecodeAndQueue"))
                        {
                            frame = decoder.ReadFrame();
                        }
                        try
                        {
                            lock (_gate)
                            {
                                _discardedFrames = decoder.DiscardedFrames;
                                _catchUpSeeks = decoder.CatchUpSeeks;
                                if (_info.Width != decoder.Width || _info.Height != decoder.Height
                                    || _info.HardwareFallbackReason != decoder.HardwareFallbackReason || _info.HardwareDecoding != decoder.HardwareDecoding
                                    || _info.DecoderName != decoder.DecoderName || _info.DecoderDevice != decoder.DecoderDevice
                                    || _info.TransferMode != decoder.TransferMode)
                                {
                                    _info = new VideoInfo(decoder);
                                }

                                if (!_disposed && revision == _revision)
                                {
                                    if (frame == null)
                                    {
                                        _eof = true;
                                        MajDebug.LogDebug("FFmpeg", "[Session] Decoder drained at end of input.");
                                    }
                                    else
                                    {
                                        while (!_disposed && revision == _revision)
                                        {
                                            DiscardSupersededFrames(frame);
                                            if (_frames.Count < _capacity)
                                            {
                                                _frames.Enqueue(frame);
                                                frame = null;
                                                break;
                                            }

                                            // One decoded candidate may wait outside the queue.
                                            // A future candidate must not displace the last due frame.
                                            Monitor.Wait(_gate, FrameWaitMilliseconds(frame.PresentationTime));
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            // Seek, close, and failures retain ownership of an unqueued candidate here.
                            frame?.Dispose();
                        }
                    }
                }
            }
            catch (Exception error)
            {
                bool report;
                lock (_gate)
                {
                    report = !_disposed;
                    if (report)
                    {
                        _error = error;
                    }
                }

                if (report)
                {
                    MajDebug.LogError("FFmpeg", "[Session] Decode worker failed: " + error.Message);
                }
                else
                {
                    MajDebug.LogDebug("FFmpeg", "[Session] Worker stopped after cancellation.");
                }
            }
            finally
            {
                lock (_gate)
                {
                    _finished = true;
                    if (_disposed)
                    {
                        ClearFrames();
                    }

                    _cancel.Dispose();
                }

                MajDebug.LogDebug("FFmpeg", "[Session] Decode worker exited.");
                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }

        /// <summary>Snapshots the playback deadlines used by the decoder to discard expired frames before conversion.</summary>
        /// <returns>The current deadlines, or <see cref="FrameDeadline.None"/> while playback is frozen or a seek is pending.</returns>
        /// <remarks>Runs on the decoding worker for each decoded frame. One expired frame is still delivered whenever the
        /// queue is empty, so the presenter always has the newest available picture while decoding falls behind.</remarks>
        private FrameDeadline ReadFrameDeadline()
        {
            lock (_gate)
            {
                if (_disposed || _decodingRevision != _revision || !_playbackClock.Running)
                {
                    return FrameDeadline.None;
                }

                var position = _playbackClock.Position;
                var discardBefore = _frames.Count == 0 ? double.NegativeInfinity : position;
                return new FrameDeadline(position, discardBefore, position - CatchUpLagSeconds * _playbackClock.Rate);
            }
        }

        /// <summary>Releases older due frames only when a newer due candidate can replace them.</summary>
        /// <param name="candidate">The worker-owned decoded frame that will replace obsolete queued frames.</param>
        /// <remarks>The caller holds the session lock; future frames and paused timelines are preserved.</remarks>
        private void DiscardSupersededFrames(DecodedVideoFrame candidate)
        {
            if (!_playbackClock.Running || candidate.PresentationTime > _playbackClock.Position)
            {
                return;
            }

            while (_frames.Count != 0 && _frames.Peek().PresentationTime <= candidate.PresentationTime)
            {
                _frames.Dequeue().Dispose();
            }
        }

        /// <summary>Calculates a worker wait until a frame becomes due on the shared playback timeline.</summary>
        /// <param name="presentationTime">The queued or pending frame timestamp in seconds.</param>
        /// <returns>A positive wait in milliseconds, or an infinite wait while playback is frozen.</returns>
        /// <remarks>The caller holds the session lock; control changes and dequeues interrupt this wait.</remarks>
        private int FrameWaitMilliseconds(double presentationTime)
        {
            if (!_playbackClock.Running)
            {
                return Timeout.Infinite;
            }

            var milliseconds = Math.Ceiling((presentationTime - _playbackClock.Position) / _playbackClock.Rate * 1000);
            return (int)Math.Max(1, Math.Min(int.MaxValue, milliseconds));
        }

        /// <summary>Releases all queued frames while the caller holds the session lock.</summary>
        private void ClearFrames()
        {
            while (_frames.Count != 0)
            {
                _frames.Dequeue().Dispose();
            }
        }

        /// <summary>Requests worker cancellation and releases queued frames without joining the worker thread.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _playbackClock.Pause();
                _disposed = true;
                if (!_finished)
                {
                    _cancel.Cancel();
                }

                ClearFrames();
                Monitor.PulseAll(_gate);
            }
        }
    }
}
