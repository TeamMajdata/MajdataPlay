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
        /// <summary>Protects queued frames, control revisions, and worker status across threads.</summary>
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
        /// <summary>Track requested closure, worker completion, and fully drained input, respectively.</summary>
        private bool _disposed, _finished, _eof;
        /// <summary>Identifies the latest seek request so stale decoded frames can be discarded.</summary>
        private long _revision;
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
        public VideoDecodeSession(string path, DecoderOptions options, int capacity)
        {
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
                                Monitor.Wait(_gate);
                            }

                            if (_disposed)
                            {
                                break;
                            }

                            revision = _revision;
                            seek = _seek;
                        }

                        if (revision != decodedRevision)
                        {
                            decoder.Seek(seek);
                            decodedRevision = revision;
                        }

                        using var profile = UnityProfiler.Create("FFmpeg.Session.DecodeAndQueue");
                        var frame = decoder.ReadFrame();
                        lock (_gate)
                        {
                            if (_info.Width != decoder.Width || _info.Height != decoder.Height
                                || _info.HardwareFallbackReason != decoder.HardwareFallbackReason || _info.HardwareDecoding != decoder.HardwareDecoding
                                || _info.DecoderName != decoder.DecoderName || _info.DecoderDevice != decoder.DecoderDevice
                                || _info.TransferMode != decoder.TransferMode)
                            {
                                _info = new VideoInfo(decoder);
                            }

                            if (_disposed || revision != _revision)
                            {
                                frame?.Dispose();
                            }
                            else if (frame == null)
                            {
                                _eof = true;
                                MajDebug.LogDebug("FFmpeg", "[Session] Decoder drained at end of input.");
                            }
                            else
                            {
                                _frames.Enqueue(frame);
                            }
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
