using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.Video.Internal
{
    /// <summary>
    /// Single-worker, synchronous decoder. Open, ReadFrame, Seek and Dispose must execute on
    /// the same worker, never Unity's main thread. Cancellation may be signalled from any thread.
    /// Returned frames own their buffers and can outlive the decoder. Audio packets are skipped.
    /// </summary>
    public sealed unsafe class FFmpegVideoDecoder : IDisposable
    {
        private static readonly AVIOInterruptCB_callback InterruptCallback = Interrupt;
        private static readonly AVCodecContext_get_format FormatCallback = SelectPixelFormat;
        private readonly DecoderOptions _options;
        private readonly VideoFrameConverter _converter = new VideoFrameConverter();
        private AVFormatContext* _format;
        private AVCodecContext* _codec;
        private AVPacket* _packet;
        private AVFrame* _frame;
        private AVFrame* _seekCandidate;
        private GCHandle _selfHandle;
        private CancellationToken _cancellation;
        private long _ioDeadline;
        private int _ownerThread;
        private int _videoStreamIndex;
        private AVRational _timeBase;
        private AVPixelFormat _hardwarePixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
        private IHardwareDecodeSession _hardwareSession;
        private bool _cpuTransport;
        private bool _mediaCodecHardware;
        private string _lastReportedTransport;
        private string _hardwareDeviceDescription;
        private bool _packetPending;
        private bool _inputEnded;
        private bool _draining;
        private bool _ended;
        private bool _disposed;
        private double _origin;
        private bool _originKnown;
        private double _nextTimestamp;
        private double _seekTarget = double.NegativeInfinity;
        private double _seekCandidateTime, _seekCandidateDuration;

        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>Seconds, or zero when the demuxer cannot report a duration.</summary>
        public double Duration { get; private set; }
        public double FrameRate { get; private set; }
        /// <summary>Clockwise display rotation. Software output has already applied it.</summary>
        public double RotationDegrees { get; private set; }
        public bool HasAudio { get; private set; }
        public bool CanSeek { get; private set; }
        public string CodecName { get; private set; }
        public string DecoderName { get; private set; }
        public string DecoderDevice { get; private set; } = "Software";
        public string TransferMode { get; private set; } = "Software RGBA upload";
        public bool HardwareDecoding { get; private set; }
        public string HardwareFallbackReason { get; private set; }

        public FFmpegVideoDecoder(DecoderOptions options = null)
        {
            _options = options ?? new DecoderOptions();
            if (_options.IOTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "I/O timeout must be positive.");
            if (_options.MaximumPixelCount <= 0 || _options.MaximumPixelCount > int.MaxValue / 4)
                throw new ArgumentOutOfRangeException(nameof(options), "Pixel limit must fit one RGBA allocation.");
        }

        public void Open(string path, CancellationToken cancellationToken)
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.Open");
#endif
            if (_disposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));
            if (_format != null) throw new InvalidOperationException("Decoder is already open.");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A media path or URL is required.", nameof(path));
            _ownerThread = Thread.CurrentThread.ManagedThreadId;
            _cancellation = cancellationToken;
            _cancellation.ThrowIfCancellationRequested();
            _cpuTransport = !_options.KeepNativeFrames;
            MajDebug.LogDebug("FFmpeg", "[Decoder] Opening media; requested device=" + _options.HardwareDeviceType +
                ", native frames=" + _options.KeepNativeFrames + ", strict GPU=" + _options.RequireHardwareDecoding + ".");

            try
            {
                CheckVersion("avutil", ffmpeg.avutil_version());
                CheckVersion("avcodec", ffmpeg.avcodec_version());
                CheckVersion("avformat", ffmpeg.avformat_version());
                CheckVersion("swscale", ffmpeg.swscale_version());
                _selfHandle = GCHandle.Alloc(this);
                _format = ffmpeg.avformat_alloc_context();
                if (_format == null) throw new OutOfMemoryException("Cannot allocate FFmpeg input context.");
                _format->interrupt_callback = new AVIOInterruptCB
                {
                    callback = InterruptCallback,
                    opaque = (void*)GCHandle.ToIntPtr(_selfHandle)
                };
                // Bound probing on malformed/unbounded sources independently of the interrupt.
                _format->probesize = 8 * 1024 * 1024;
                _format->max_analyze_duration = 10 * ffmpeg.AV_TIME_BASE;
                var input = _format;
                BeginIO();
                int openResult;
                try { openResult = ffmpeg.avformat_open_input(&input, path, null, null); }
                finally { _format = input; }
                CheckIO(openResult, "Open media");
                BeginIO();
                CheckIO(ffmpeg.avformat_find_stream_info(_format, null), "Read media information");

                AVCodec* codec = null;
                _videoStreamIndex = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
                Check(_videoStreamIndex, "Find video stream");
                if (codec == null) throw new NotSupportedException("This FFmpeg build has no decoder for the video codec.");
                var stream = _format->streams[_videoStreamIndex];
                CodecName = ffmpeg.avcodec_get_name(stream->codecpar->codec_id);
                _timeBase = stream->time_base;
                if (_timeBase.num <= 0 || _timeBase.den <= 0)
                    throw new InvalidOperationException("The video stream has an invalid time base.");
                for (uint i = 0; i < _format->nb_streams; i++)
                    if (_format->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) HasAudio = true;
                var rate = ffmpeg.av_guess_frame_rate(_format, stream, null);
                FrameRate = rate.num > 0 && rate.den > 0 ? ffmpeg.av_q2d(rate) : 30.0;
                if (FrameRate < 0.001 || FrameRate > 1000) FrameRate = 30;
                Duration = stream->duration != ffmpeg.AV_NOPTS_VALUE && stream->duration > 0
                    ? stream->duration * ffmpeg.av_q2d(_timeBase)
                    : _format->duration != ffmpeg.AV_NOPTS_VALUE && _format->duration > 0
                        ? (double)_format->duration / ffmpeg.AV_TIME_BASE : 0;
                if (stream->start_time != ffmpeg.AV_NOPTS_VALUE)
                {
                    _origin = stream->start_time * ffmpeg.av_q2d(_timeBase);
                    _originKnown = true;
                }
                else if (_format->start_time != ffmpeg.AV_NOPTS_VALUE)
                {
                    _origin = (double)_format->start_time / ffmpeg.AV_TIME_BASE;
                    _originKnown = true;
                }
                CanSeek = _format->pb == null || (_format->pb->seekable & ffmpeg.AVIO_SEEKABLE_NORMAL) != 0;
                RotationDegrees = ReadStreamRotation(stream);
                UpdateDimensions(stream->codecpar->width, stream->codecpar->height, RotationDegrees);
                var softwareCodec = codec;
                bool mediaCodec = _options.HardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC;
                if (mediaCodec)
                {
                    // MediaCodec is a distinct decoder, unlike VAAPI/D3D11 hwaccels.
                    var name = stream->codecpar->codec_id == AVCodecID.AV_CODEC_ID_MPEG2VIDEO
                        ? "mpeg2_mediacodec" : ffmpeg.avcodec_get_name(stream->codecpar->codec_id) + "_mediacodec";
                    var androidCodec = ffmpeg.avcodec_find_decoder_by_name(name);
                    if (androidCodec != null) codec = androidCodec;
                    else HardwareFallbackReason = "This FFmpeg build has no " + name + " decoder.";
                }
                AllocateCodec(codec, stream);
                bool selectedMediaCodec = mediaCodec && ffmpeg.PtrToStringUTF8(codec->name).EndsWith("_mediacodec", StringComparison.Ordinal);
                if (!mediaCodec || (selectedMediaCodec && !_cpuTransport))
                {
                    try { ConfigureHardware(codec); }
                    catch (Exception error) when (!_options.RequireHardwareDecoding && !(error is OperationCanceledException) && !(error is OutOfMemoryException))
                    {
                        HardwareFallbackReason = "Hardware device initialization failed: " + error.Message;
                        MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason);
                    }
                }
                if (_options.RequireHardwareDecoding && _codec->hw_device_ctx == null)
                    throw new NotSupportedException(HardwareFallbackReason ?? "A hardware decoding device is required.");
                bool mediaCodecCpu = selectedMediaCodec && _codec->hw_device_ctx == null &&
                    _options.AllowHardwareCpuUpload && !_options.RequireHardwareDecoding;
                if (mediaCodecCpu) _cpuTransport = true;
                if (mediaCodec && _codec->hw_device_ctx == null && !mediaCodecCpu)
                {
                    ReleaseCodec();
                    codec = softwareCodec;
                    AllocateCodec(codec, stream);
                }
                AVDictionary* codecOptions = null;
                int codecResult;
                try
                {
                    // The JNI path explicitly filters software codecs. NDK create-by-MIME
                    // may silently select a software decoder when no hardware codec exists.
                    if (selectedMediaCodec && (_codec->hw_device_ctx != null || mediaCodecCpu))
                        Check(ffmpeg.av_dict_set(&codecOptions, "ndk_codec", "0", 0), "Select hardware MediaCodec");
                    codecResult = ffmpeg.avcodec_open2(_codec, codec, &codecOptions);
                }
                finally { ffmpeg.av_dict_free(&codecOptions); }
                if (codecResult < 0 && selectedMediaCodec && _codec->hw_device_ctx != null &&
                    _options.AllowHardwareCpuUpload && !_options.RequireHardwareDecoding)
                {
                    HardwareFallbackReason = "MediaCodec Surface open failed: " + ErrorText(codecResult);
                    MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason + "; retrying hardware byte-buffer output.");
                    ReleaseCodec();
                    AllocateCodec(codec, stream);
                    _cpuTransport = true;
                    mediaCodecCpu = true;
                    try
                    {
                        Check(ffmpeg.av_dict_set(&codecOptions, "ndk_codec", "0", 0), "Select hardware MediaCodec");
                        codecResult = ffmpeg.avcodec_open2(_codec, codec, &codecOptions);
                    }
                    finally { ffmpeg.av_dict_free(&codecOptions); }
                }
                if (codecResult < 0 && (_codec->hw_device_ctx != null || mediaCodecCpu) && !_options.RequireHardwareDecoding)
                {
                    // Some codecs reject a hardware configuration at open instead of get_format.
                    HardwareFallbackReason = "Hardware decoder open failed: " + ErrorText(codecResult);
                    MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason + "; opening software decoder.");
                    ReleaseCodec();
                    codec = softwareCodec;
                    AllocateCodec(codec, stream);
                    HardwareDecoding = false;
                    selectedMediaCodec = false;
                    mediaCodecCpu = false;
                    codecResult = ffmpeg.avcodec_open2(_codec, codec, null);
                }
                Check(codecResult, "Open video decoder");
                DecoderName = ffmpeg.PtrToStringUTF8(codec->name);
                _mediaCodecHardware = selectedMediaCodec && (mediaCodecCpu || _codec->hw_device_ctx != null);
                HardwareDecoding = _mediaCodecHardware || _codec->hw_device_ctx != null;
                if (_mediaCodecHardware)
                    DecoderDevice = DescribeHardwareDevice(mediaCodecCpu ? "MediaCodec hardware byte-buffer output" : "MediaCodec hardware Surface output");
                if (!HardwareDecoding) DecoderDevice = "Software";
                else _hardwareDeviceDescription = DecoderDevice;
                TransferMode = HardwareDecoding ? (_cpuTransport ? "Hardware decode + CPU RGBA upload" : "Native GPU frames") : "Software RGBA upload";
                MajDebug.LogInfo("FFmpeg", "[Decoder] Opened codec=" + CodecName + ", decoder=" + DecoderName +
                    ", device=" + DecoderDevice + ", transport=" + TransferMode + ", size=" + Width + "x" + Height + ".");
                _packet = ffmpeg.av_packet_alloc();
                _frame = ffmpeg.av_frame_alloc();
                if (_packet == null || _frame == null) throw new OutOfMemoryException("Cannot allocate decoder packet/frame.");
                Interlocked.Exchange(ref _ioDeadline, 0);
            }
            catch (Exception error)
            {
                if (!(error is OperationCanceledException)) MajDebug.LogError("FFmpeg", "[Decoder] Open failed: " + error.Message);
                Dispose();
                throw;
            }
        }

        /// <summary>Returns null only after every delayed frame has been drained at end of input.</summary>
        public DecodedVideoFrame ReadFrame()
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.ReadFrame");
#endif
            EnsureOwner();
            if (_codec == null) throw new InvalidOperationException("Open the media before reading.");
            if (_ended) return null;
            var again = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = ffmpeg.avcodec_receive_frame(_codec, _frame);
                if (result == 0)
                {
                    try
                    {
                        var duration = _frame->duration > 0 ? _frame->duration * ffmpeg.av_q2d(_timeBase) : 1.0 / FrameRate;
                        var timestamp = _frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE ? _frame->best_effort_timestamp : _frame->pts;
                        var seconds = timestamp == ffmpeg.AV_NOPTS_VALUE ? _nextTimestamp : timestamp * ffmpeg.av_q2d(_timeBase);
                        if (!_originKnown && timestamp != ffmpeg.AV_NOPTS_VALUE) { _origin = seconds; _originKnown = true; }
                        if (timestamp != ffmpeg.AV_NOPTS_VALUE) seconds -= _origin;
                        _nextTimestamp = seconds + duration;
                        // Decode preroll after backward seeking, including inter-frame references.
                        if (seconds + duration <= _seekTarget + 0.000001)
                        {
                            // Keep exactly one reference while decoding preroll. A seek to
                            // 100% (or a slightly overreported duration) must display the
                            // closest final frame instead of leaving the previous texture.
                            if (_seekCandidate == null) _seekCandidate = ffmpeg.av_frame_alloc();
                            if (_seekCandidate == null) throw new OutOfMemoryException("Cannot retain seek preroll frame.");
                            ffmpeg.av_frame_unref(_seekCandidate);
                            ffmpeg.av_frame_move_ref(_seekCandidate, _frame);
                            _seekCandidateTime = seconds;
                            _seekCandidateDuration = duration;
                            continue;
                        }
                        _seekTarget = double.NegativeInfinity;
                        ReleaseSeekCandidate();
                        return CreatePresentationFrame(_frame, seconds, duration);
                    }
                    finally { ffmpeg.av_frame_unref(_frame); }
                }
                if (result == ffmpeg.AVERROR_EOF) return FinishInput();
                if (result != again) Check(result, "Decode video frame");
                if (_draining)
                    throw new InvalidOperationException("Video decoder requested input after accepting its drain packet.");

                if (_packetPending)
                {
                    result = ffmpeg.avcodec_send_packet(_codec, _packet);
                    if (result == again)
                        throw new InvalidOperationException("Video decoder returned EAGAIN from both send and receive.");
                    Check(result, "Send video packet");
                    ffmpeg.av_packet_unref(_packet);
                    _packetPending = false;
                    continue;
                }
                if (_inputEnded)
                {
                    result = ffmpeg.avcodec_send_packet(_codec, null);
                    if (result == ffmpeg.AVERROR_EOF) return FinishInput();
                    Check(result, "Drain video decoder");
                    _draining = true;
                    continue;
                }

                // Preserve the deadline across nonblocking EAGAIN returns so a stalled
                // source cannot reset its own timeout forever without producing a packet.
                if (Interlocked.Read(ref _ioDeadline) == 0) BeginIO();
                result = ffmpeg.av_read_frame(_format, _packet);
                if (result == ffmpeg.AVERROR_EOF)
                {
                    Interlocked.Exchange(ref _ioDeadline, 0);
                    _inputEnded = true;
                    continue;
                }
                if (result == again)
                {
                    if (Stopwatch.GetTimestamp() >= Interlocked.Read(ref _ioDeadline))
                        CheckIO(result, "Read video packet");
                    // Nonblocking demuxers may return EAGAIN. Wait cancellably without a hot loop.
                    if (_cancellation.WaitHandle.WaitOne(5)) _cancellation.ThrowIfCancellationRequested();
                    continue;
                }
                CheckIO(result, "Read video packet");
                if (_packet->stream_index != _videoStreamIndex) { ffmpeg.av_packet_unref(_packet); continue; }
                _packetPending = true;
            }
        }

        public void Seek(double seconds)
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.Seek");
#endif
            MajDebug.LogDebug("FFmpeg", "[Decoder] Seeking to " + seconds.ToString("F3", CultureInfo.InvariantCulture) + " seconds.");
            EnsureOwner();
            if (_format == null) throw new InvalidOperationException("Open the media before seeking.");
            if (!CanSeek) throw new NotSupportedException("This media input is not seekable.");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            _cancellation.ThrowIfCancellationRequested();
            seconds = Math.Max(0, Duration > 0 ? Math.Min(seconds, Duration) : seconds);
            var timestamp = checked((long)Math.Round((seconds + _origin) / ffmpeg.av_q2d(_timeBase)));
            // The demuxer's end timestamp is exclusive. Seek one stream tick inside
            // the input, while retaining the caller's exact endpoint presentation time.
            if (Duration > 0 && seconds >= Duration)
                timestamp = Math.Max(checked((long)Math.Round(_origin / ffmpeg.av_q2d(_timeBase))), timestamp - 1);
            BeginIO();
            CheckIO(ffmpeg.avformat_seek_file(_format, _videoStreamIndex, long.MinValue, timestamp, timestamp, 0), "Seek media");
            ReleaseSeekCandidate();
            ffmpeg.avcodec_flush_buffers(_codec);
            _hardwareSession?.Flush();
            ffmpeg.av_packet_unref(_packet);
            ffmpeg.av_frame_unref(_frame);
            ReleaseSeekCandidate();
            _packetPending = false;
            _inputEnded = false;
            _draining = false;
            _ended = false;
            _nextTimestamp = seconds;
            _seekTarget = seconds;
        }

        private DecodedVideoFrame CreatePresentationFrame(AVFrame* frame, double seconds, double duration)
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.PreparePresentationFrame");
#endif
            var rotation = ReadFrameRotation(frame, RotationDegrees);
            UpdateDimensions(frame->width, frame->height, rotation);
            var hardware = frame->hw_frames_ctx != null ||
                ((AVPixelFormat)frame->format == AVPixelFormat.AV_PIX_FMT_MEDIACODEC && frame->data[3] != null);
            HardwareDecoding = hardware || _mediaCodecHardware;
            DecoderDevice = HardwareDecoding ? _hardwareDeviceDescription ?? DecoderDevice : "Software";
            if (_options.RequireHardwareDecoding && (!hardware || !_options.KeepNativeFrames))
                throw new NotSupportedException(HardwareFallbackReason ?? "The decoder did not produce a shareable hardware frame.");
            if (hardware && _options.KeepNativeFrames && !_cpuTransport)
            {
                var aspect = frame->sample_aspect_ratio;
                DecodedVideoFrame result;
                if (_hardwareSession != null && (AVPixelFormat)frame->format == AVPixelFormat.AV_PIX_FMT_MEDIACODEC)
                {
                    var image = _hardwareSession.CaptureFrame((IntPtr)frame);
                    if (image == IntPtr.Zero) throw new NotSupportedException("MediaCodec did not return a shareable Android hardware image.");
                    result = new DecodedVideoFrame(image, _hardwareSession.ReleaseImage);
                }
                else
                {
                    AVFrame* clone;
                    try
                    {
                        clone = _options.MapHardwareFrame != null
                            ? (AVFrame*)_options.MapHardwareFrame((IntPtr)frame)
                            : ffmpeg.av_frame_clone(frame);
                        if (clone == null && _options.MapHardwareFrame != null)
                            throw new NotSupportedException("The native graphics mapper returned no imported frame.");
                    }
                    catch (Exception error) when (_options.AllowHardwareCpuUpload && !_options.RequireHardwareDecoding &&
                        frame->hw_frames_ctx != null && !(error is OperationCanceledException) && !(error is OutOfMemoryException))
                    {
                        // Download the original VAAPI/D3D11/VideoToolbox frame, never a
                        // mapped DRM_PRIME frame whose transfer implementation may differ.
                        _cpuTransport = true;
                        HardwareFallbackReason = "Native frame mapping failed: " + error.Message;
                        MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason + "; keeping hardware decode with CPU upload.");
                        return ConvertForCpu(frame, seconds, duration, rotation);
                    }
                    if (clone == null) throw new OutOfMemoryException("Cannot reference hardware video frame.");
                    result = new DecodedVideoFrame(IntPtr.Zero, (IntPtr)clone);
                }
                result.Width = frame->width;
                result.Height = frame->height;
                result.PresentationTime = seconds;
                result.Duration = duration;
                result.RotationDegrees = rotation;
                result.PixelFormat = result.NativeFrame != IntPtr.Zero
                    ? (AVPixelFormat)((AVFrame*)result.NativeFrame)->format : (AVPixelFormat)frame->format;
                result.PixelAspectRatio = aspect.num > 0 && aspect.den > 0 ? ffmpeg.av_q2d(aspect) : 1;
                result.HardwareDecoded = true;
                result.TransferMode = "Native GPU frames";
                ReportTransport(result.TransferMode);
                return result;
            }
            return ConvertForCpu(frame, seconds, duration, rotation);
        }

        private DecodedVideoFrame ConvertForCpu(AVFrame* frame, double seconds, double duration, double rotation)
        {
            if (HardwareDecoding && (!_options.AllowHardwareCpuUpload || _options.RequireHardwareDecoding))
                throw new NotSupportedException("Hardware decoding is available, but CPU texture transport was disabled.");
            var result = _converter.Convert(frame, seconds, duration, rotation, _options.MaximumPixelCount);
            result.HardwareDecoded = HardwareDecoding;
            result.TransferMode = HardwareDecoding ? "Hardware decode + CPU RGBA upload" : "Software RGBA upload";
            if (!HardwareDecoding) DecoderDevice = "Software";
            ReportTransport(result.TransferMode);
            return result;
        }

        private void ReportTransport(string transport)
        {
            TransferMode = transport;
            if (_lastReportedTransport == transport) return;
            _lastReportedTransport = transport;
            MajDebug.LogInfo("FFmpeg", "[Decoder] Active decoder=" + DecoderName + ", device=" + DecoderDevice + ", transport=" + transport + ".");
        }

        private string DescribeHardwareDevice(string kind) => string.IsNullOrWhiteSpace(_options.HardwareDeviceDescription)
            ? kind : kind + "; " + _options.HardwareDeviceDescription;

        private DecodedVideoFrame FinishInput()
        {
            _ended = true;
            _seekTarget = double.NegativeInfinity;
            if (_seekCandidate == null) return null;
            try { return CreatePresentationFrame(_seekCandidate, _seekCandidateTime, _seekCandidateDuration); }
            finally { ReleaseSeekCandidate(); }
        }

        private void ReleaseSeekCandidate()
        {
            var candidate = _seekCandidate;
            _seekCandidate = null;
            if (candidate != null) ffmpeg.av_frame_free(&candidate);
        }

        private void ConfigureHardware(AVCodec* codec)
        {
            if (_options.HardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE) return;
            AVCodecHWConfig* configuration = null;
            for (var i = 0; ; i++)
            {
                var candidate = ffmpeg.avcodec_get_hw_config(codec, i);
                if (candidate == null) break;
                if (candidate->device_type == _options.HardwareDeviceType && (candidate->methods & 1) != 0)
                { configuration = candidate; break; }
            }
            if (configuration == null)
            {
                HardwareFallbackReason = "This codec/build has no hardware configuration for " + _options.HardwareDeviceType + ".";
                MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason);
                return;
            }
            AVBufferRef* device = null;
            try
            {
                int result = -1;
                bool suppliedDevice = false;
                bool genericDevice = false;
                try
                {
                    if (_options.CreateHardwareSession != null && !_cpuTransport)
                    {
                        suppliedDevice = true;
                        _hardwareSession = _options.CreateHardwareSession(_codec->width, _codec->height);
                        device = _hardwareSession == null ? null : (AVBufferRef*)_hardwareSession.AcquireDevice();
                        if (device != null) result = 0;
                        else HardwareFallbackReason = "The native hardware decoder surface is unavailable.";
                    }
                    else if (_options.AcquireHardwareDevice != null)
                    {
                        suppliedDevice = true;
                        device = (AVBufferRef*)_options.AcquireHardwareDevice();
                        if (device != null) result = 0;
                        else HardwareFallbackReason = "No hardware decode device matches Unity's graphics device.";
                    }
                    else if (_options.HardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA && _options.AcquireD3D11Device != null)
                    {
                        suppliedDevice = true;
                        device = ffmpeg.av_hwdevice_ctx_alloc(_options.HardwareDeviceType);
                        if (device == null) throw new OutOfMemoryException("Cannot allocate D3D11 video device.");
                        var deviceContext = (AVHWDeviceContext*)device->data;
                        var d3d11 = (AVD3D11VADeviceContext*)deviceContext->hwctx;
                        d3d11->device = (ID3D11Device*)_options.AcquireD3D11Device();
                        if (d3d11->device != null) result = ffmpeg.av_hwdevice_ctx_init(device);
                        else HardwareFallbackReason = "The Unity D3D11 device is unavailable.";
                    }
                }
                catch (Exception error) when (_options.AllowHardwareCpuUpload && !_options.RequireHardwareDecoding &&
                    !(error is OperationCanceledException) && !(error is OutOfMemoryException))
                {
                    HardwareFallbackReason = "Unity-compatible hardware device unavailable: " + error.Message;
                }
                if (result < 0)
                {
                    if (device != null) ffmpeg.av_buffer_unref(&device);
                    bool canCreateNativeDevice = !suppliedDevice &&
                        _options.HardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
                    if (!canCreateNativeDevice && (_options.RequireHardwareDecoding || !_options.AllowHardwareCpuUpload ||
                        _options.HardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC))
                    {
                        HardwareFallbackReason ??= "A compatible native hardware device was not supplied.";
                        MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason);
                        return;
                    }
                    if (suppliedDevice)
                    {
                        HardwareFallbackReason ??= "Unity-compatible hardware device initialization failed: " + ErrorText(result);
                        MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason + "; trying an independent hardware device for CPU upload.");
                    }
                    result = ffmpeg.av_hwdevice_ctx_create(&device, _options.HardwareDeviceType, null, null, 0);
                    genericDevice = true;
                    if (!canCreateNativeDevice) _cpuTransport = true;
                }
                if (result < 0)
                {
                    HardwareFallbackReason = "Hardware device creation failed: " + ErrorText(result);
                    MajDebug.LogWarning("FFmpeg", "[Decoder] " + HardwareFallbackReason);
                    return;
                }
                _hardwarePixelFormat = configuration->pix_fmt;
                _codec->hw_device_ctx = device;
                device = null;
                _codec->get_format = FormatCallback;
                HardwareDecoding = true;
                var kind = _options.HardwareDeviceType.ToString().Replace("AV_HWDEVICE_TYPE_", "");
                DecoderDevice = genericDevice && suppliedDevice ? kind + " (FFmpeg default hardware device)" :
                    DescribeHardwareDevice(genericDevice ? kind + " (FFmpeg default hardware device)" : kind);
            }
            finally { if (device != null) ffmpeg.av_buffer_unref(&device); }
        }

        private void AllocateCodec(AVCodec* codec, AVStream* stream)
        {
            _codec = ffmpeg.avcodec_alloc_context3(codec);
            if (_codec == null) throw new OutOfMemoryException("Cannot allocate video codec.");
            Check(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "Configure video decoder");
            _codec->pkt_timebase = stream->time_base;
            _codec->thread_count = Math.Max(1, Math.Min(_options.ThreadCount, 16));
            _codec->max_pixels = _options.MaximumPixelCount;
            _codec->opaque = (void*)GCHandle.ToIntPtr(_selfHandle);
        }

        private void ReleaseCodec()
        {
            var codec = _codec;
            _codec = null;
            if (codec != null) ffmpeg.avcodec_free_context(&codec);
            _hardwareSession?.Dispose();
            _hardwareSession = null;
        }

        [MonoPInvokeCallback(typeof(AVCodecContext_get_format))]
        private static AVPixelFormat SelectPixelFormat(AVCodecContext* context, AVPixelFormat* formats)
        {
            try
            {
                var self = (FFmpegVideoDecoder)GCHandle.FromIntPtr((IntPtr)context->opaque).Target;
                for (var cursor = formats; *cursor != AVPixelFormat.AV_PIX_FMT_NONE; cursor++)
                    if (*cursor == self._hardwarePixelFormat) return *cursor;
                self.HardwareDecoding = false;
                self.HardwareFallbackReason = "The decoder rejected the requested hardware pixel format.";
                MajDebug.LogWarning("FFmpeg", "[Decoder] " + self.HardwareFallbackReason);
                if (self._options.RequireHardwareDecoding) return AVPixelFormat.AV_PIX_FMT_NONE;
                for (var cursor = formats; *cursor != AVPixelFormat.AV_PIX_FMT_NONE; cursor++)
                {
                    var descriptor = ffmpeg.av_pix_fmt_desc_get(*cursor);
                    if (descriptor != null && (descriptor->flags & ffmpeg.AV_PIX_FMT_FLAG_HWACCEL) == 0) return *cursor;
                }
            }
            catch { /* Exceptions must never cross a native callback boundary. */ }
            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        private void BeginIO()
        {
            _cancellation.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _ioDeadline, Stopwatch.GetTimestamp() +
                (long)(_options.IOTimeoutMilliseconds * (double)Stopwatch.Frequency / 1000));
        }

        private void CheckIO(int error, string operation)
        {
            var deadline = Interlocked.Exchange(ref _ioDeadline, 0);
            _cancellation.ThrowIfCancellationRequested();
            if (error < 0 && deadline != 0 && Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException(operation + " exceeded " + _options.IOTimeoutMilliseconds + " ms.");
            Check(error, operation);
        }

        [MonoPInvokeCallback(typeof(AVIOInterruptCB_callback))]
        private static int Interrupt(void* opaque)
        {
            try
            {
                var self = (FFmpegVideoDecoder)GCHandle.FromIntPtr((IntPtr)opaque).Target;
                var deadline = Interlocked.Read(ref self._ioDeadline);
                return self._cancellation.IsCancellationRequested ||
                    (deadline != 0 && Stopwatch.GetTimestamp() >= deadline) ? 1 : 0;
            }
            catch { return 1; }
        }

        private static double ReadStreamRotation(AVStream* stream)
        {
            var data = ffmpeg.av_packet_side_data_get(stream->codecpar->coded_side_data,
                stream->codecpar->nb_coded_side_data, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX);
            if (data != null && data->size >= 9 * sizeof(int))
                return ReadRotation(data->data);
            var tag = ffmpeg.av_dict_get(stream->metadata, "rotate", null, 0);
            if (tag != null && double.TryParse(ffmpeg.PtrToStringUTF8(tag->value), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var rotation)) return NormalizeRotation(rotation);
            return 0;
        }

        private static double ReadFrameRotation(AVFrame* frame, double fallback)
        {
            var data = ffmpeg.av_frame_get_side_data(frame, AVFrameSideDataType.AV_FRAME_DATA_DISPLAYMATRIX);
            return data != null && data->size >= 9 * sizeof(int) ? ReadRotation(data->data) : fallback;
        }

        private static double ReadRotation(byte* data)
        {
            var matrix = *(int_array9*)data;
            return NormalizeRotation(-ffmpeg.av_display_rotation_get(in matrix));
        }

        private static double NormalizeRotation(double angle)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle)) return 0;
            // Phone/camera display matrices normally contain exact quarter turns.
            return ((Math.Round(angle / 90.0) * 90.0) % 360 + 360) % 360;
        }

        private void UpdateDimensions(int width, int height, double rotation)
        {
            // Some streams only disclose dimensions when their first frame is decoded.
            if (width == 0 && height == 0) return;
            ValidateDimensions(width, height, _options.MaximumPixelCount);
            var rotated = ((int)Math.Round(rotation / 90.0) & 1) != 0;
            Width = rotated ? height : width;
            Height = rotated ? width : height;
        }

        internal static void ValidateDimensions(int width, int height, int maximumPixels)
        {
            if (width <= 0 || height <= 0 || (long)width * height > maximumPixels || (long)width * height * 4 > int.MaxValue)
                throw new NotSupportedException("Video dimensions exceed the configured RGBA allocation limit: " + width + " x " + height + ".");
        }

        internal static void Check(int error, string operation)
        {
            if (error < 0) throw new InvalidOperationException(operation + ": " + ErrorText(error) + " (" + error + ").");
        }

        private static string ErrorText(int error)
        {
            byte* buffer = stackalloc byte[256];
            ffmpeg.av_strerror(error, buffer, 256);
            return ffmpeg.PtrToStringUTF8(buffer);
        }

        private static void CheckVersion(string library, uint version)
        {
            var expected = ffmpeg.LibraryVersionMap[library];
            if ((version >> 16) != expected)
                throw new NotSupportedException(library + " ABI mismatch: expected " + expected + ", loaded " + (version >> 16) + ".");
        }

        private void EnsureOwner()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));
            if (_ownerThread != 0 && _ownerThread != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("FFmpeg decoder operations must remain on their owning worker thread.");
        }

        public void Dispose()
        {
#if (UNITY_EDITOR || DEBUG) && ENABLE_PROFILER
            using var profile = UnityProfiler.Create("FFmpeg.Decoder.Dispose");
#endif
            if (_disposed) return;
            EnsureOwner();
            _disposed = true;
            _converter.Dispose();
            var packet = _packet;
            if (packet != null) ffmpeg.av_packet_free(&packet);
            _packet = null;
            var frame = _frame;
            if (frame != null) ffmpeg.av_frame_free(&frame);
            _frame = null;
            ReleaseSeekCandidate();
            ReleaseCodec();
            var format = _format;
            if (format != null) ffmpeg.avformat_close_input(&format);
            _format = null;
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            MajDebug.LogDebug("FFmpeg", "[Decoder] Decoder and input resources released.");
        }
    }
}
