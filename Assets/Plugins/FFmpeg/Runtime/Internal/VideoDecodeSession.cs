using System;
using System.Collections.Generic;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.Video.Internal
{
    internal sealed class VideoInfo
    {
        public readonly int Width, Height;
        public readonly double Duration, FrameRate;
        public readonly bool CanSeek, HasAudio, HardwareDecoding;
        public readonly string Codec, HardwareFallbackReason, DecoderName, DecoderDevice, TransferMode;
        public VideoInfo(FFmpegVideoDecoder decoder)
        {
            Width = decoder.Width; Height = decoder.Height;
            Duration = decoder.Duration; FrameRate = decoder.FrameRate;
            CanSeek = decoder.CanSeek; HasAudio = decoder.HasAudio; Codec = decoder.CodecName;
            HardwareFallbackReason = decoder.HardwareFallbackReason;
            DecoderName = decoder.DecoderName; DecoderDevice = decoder.DecoderDevice;
            HardwareDecoding = decoder.HardwareDecoding; TransferMode = decoder.TransferMode;
        }
    }

    /// <summary>One owner thread per demuxer. Cancellation never joins the Unity thread.</summary>
    internal sealed class VideoDecodeSession : IDisposable
    {
        readonly object _gate = new object();
        readonly Queue<DecodedVideoFrame> _frames = new Queue<DecodedVideoFrame>();
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        readonly string _path;
        readonly DecoderOptions _options;
        readonly int _capacity;
        bool _disposed, _finished, _eof;
        long _revision;
        double _seek;
        VideoInfo _info;
        Exception _error;

        public VideoDecodeSession(string path, DecoderOptions options, int capacity)
        {
            _path = path; _options = options; _capacity = Math.Max(1, Math.Min(8, capacity));
            new Thread(Run) { IsBackground = true, Name = "FFmpeg video decoder" }.Start();
        }
        public VideoInfo Info { get { lock (_gate) return _info; } }
        public Exception Error { get { lock (_gate) return _error; } }
        public bool EndOfStream { get { lock (_gate) return _eof && _frames.Count == 0; } }
        public int BufferedFrames { get { lock (_gate) return _frames.Count; } }
        public double NextPresentationTime { get { lock (_gate) return _frames.Count == 0 ? double.NaN : _frames.Peek().PresentationTime; } }

        public DecodedVideoFrame TakeFrame()
        {
            lock (_gate)
            {
                if (_frames.Count == 0) return null;
                var frame = _frames.Dequeue();
                Monitor.PulseAll(_gate);
                return frame;
            }
        }
        public void Seek(double seconds)
        {
            MajDebug.LogDebug("FFmpeg", "[Session] Scheduling seek to " + seconds + " seconds.");
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(VideoDecodeSession));
                _seek = seconds; _revision++; _eof = false;
                ClearFrames();
                Monitor.PulseAll(_gate);
            }
        }
        void Run()
        {
            UnityEngine.Profiling.Profiler.BeginThreadProfiling("FFmpeg", "Decoder");
            MajDebug.LogDebug("FFmpeg", "[Session] Decode worker started; queue capacity=" + _capacity + ".");
            try
            {
                using (var decoder = new FFmpegVideoDecoder(_options))
                {
                    decoder.Open(_path, _cancel.Token);
                    lock (_gate) _info = new VideoInfo(decoder);
                    MajDebug.LogInfo("FFmpeg", "[Session] Media ready; codec=" + decoder.CodecName +
                        ", decoder=" + decoder.DecoderName + ", device=" + decoder.DecoderDevice + ".");
                    long decodedRevision = 0;
                    while (true)
                    {
                        long revision;
                        double seek;
                        lock (_gate)
                        {
                            while (!_disposed && decodedRevision == _revision && (_eof || _frames.Count >= _capacity))
                                Monitor.Wait(_gate);
                            if (_disposed) break;
                            revision = _revision; seek = _seek;
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
                            if (_info.Width != decoder.Width || _info.Height != decoder.Height ||
                                _info.HardwareFallbackReason != decoder.HardwareFallbackReason ||
                                _info.HardwareDecoding != decoder.HardwareDecoding || _info.DecoderName != decoder.DecoderName ||
                                _info.DecoderDevice != decoder.DecoderDevice || _info.TransferMode != decoder.TransferMode)
                                _info = new VideoInfo(decoder);
                            if (_disposed || revision != _revision) frame?.Dispose();
                            else if (frame == null)
                            {
                                _eof = true;
                                MajDebug.LogDebug("FFmpeg", "[Session] Decoder drained at end of input.");
                            }
                            else _frames.Enqueue(frame);
                        }
                    }
                }
            }
            catch (Exception error)
            {
                bool report;
                lock (_gate) { report = !_disposed; if (report) _error = error; }
                if (report) MajDebug.LogError("FFmpeg", "[Session] Decode worker failed: " + error.Message);
                else MajDebug.LogDebug("FFmpeg", "[Session] Worker stopped after cancellation.");
            }
            finally
            {
                lock (_gate)
                {
                    _finished = true;
                    if (_disposed) ClearFrames();
                    _cancel.Dispose();
                }
                MajDebug.LogDebug("FFmpeg", "[Session] Decode worker exited.");
                UnityEngine.Profiling.Profiler.EndThreadProfiling();
            }
        }
        void ClearFrames() { while (_frames.Count != 0) _frames.Dequeue().Dispose(); }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (!_finished) _cancel.Cancel();
                ClearFrames();
                Monitor.PulseAll(_gate);
            }
        }
    }
}
