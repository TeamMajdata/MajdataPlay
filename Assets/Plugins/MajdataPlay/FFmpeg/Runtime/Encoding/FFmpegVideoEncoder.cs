#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AOT;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Encodes packed sRGB RGBA8 frames and muxes a video-only local file on one worker.</summary>
    /// <remarks>Open, Encode, Complete, and Dispose must run on the same thread. Dispose releases resources; Complete explicitly drains and finalizes the file.</remarks>
    public sealed unsafe class FFmpegVideoEncoder : IDisposable
    {
        /// <summary>Identifies the reviewed AMF property checks by the checked-in source patch's SHA256 prefix.</summary>
        private const string CheckedAmfConfigurationOption = "--extra-version=MajdataPlay-AMF-RC-v1-c0604b924b6a";
        /// <summary>Identifies the native wrapper that rejects ignored x265 parameters.</summary>
        private const string CheckedX265ConfigurationMarker = "MajdataPlay-X265-Params-v1-5b457330f94a";
        /// <summary>Roots the managed local-file write callback for IL2CPP and Mono.</summary>
        private static readonly avio_alloc_context_write_packet s_writeCallback = WriteOutput;
        /// <summary>Roots the managed local-file seek callback for IL2CPP and Mono.</summary>
        private static readonly avio_alloc_context_seek s_seekCallback = SeekOutput;
        /// <summary>Stores immutable settings for this encoder instance.</summary>
        private readonly EncoderOptions _options;
        /// <summary>Reuses the existing bounded one-second compressed bitrate estimator.</summary>
        private readonly VideoBitRateTracker _bitRateTracker = new VideoBitRateTracker();
        /// <summary>Reuses input plane pointers for swscale.</summary>
        private readonly byte*[] _source = new byte*[8];
        /// <summary>Reuses input row strides for swscale.</summary>
        private readonly int[] _sourceStride = new int[8];
        /// <summary>Reuses output plane pointers for swscale.</summary>
        private readonly byte*[] _destination = new byte*[4];
        /// <summary>Reuses output row strides for swscale.</summary>
        private readonly int[] _destinationStride = new int[4];
        /// <summary>Owns the output container context.</summary>
        private AVFormatContext* _format;
        /// <summary>Owns the selected codec context.</summary>
        private AVCodecContext* _codec;
        /// <summary>Borrows the output video stream from the container.</summary>
        private AVStream* _stream;
        /// <summary>Owns reusable compressed packet storage.</summary>
        private AVPacket* _packet;
        /// <summary>Owns aligned RGBA input staging and padding required by SIMD converters.</summary>
        private AVFrame* _rgba;
        /// <summary>Owns reusable CPU encoder input frame storage.</summary>
        private AVFrame* _frame;
        /// <summary>Owns reusable hardware upload frame storage when VAAPI is selected.</summary>
        private AVFrame* _hardwareFrame;
        /// <summary>Owns an optional VAAPI device reference.</summary>
        private AVBufferRef* _hardwareDevice;
        /// <summary>Owns an optional VAAPI frame pool reference.</summary>
        private AVBufferRef* _hardwareFrames;
        /// <summary>Owns the RGBA to encoder-pixel-format converter.</summary>
        private SwsContext* _scale;
        /// <summary>Owns the custom seekable local-file AVIO context.</summary>
        private AVIOContext* _io;
        /// <summary>Owns the output file opened with CreateNew to prevent overwrite races.</summary>
        private FileStream? _output;
        /// <summary>Keeps this instance reachable by native file callbacks.</summary>
        private GCHandle _selfHandle;
        /// <summary>Stores cancellation for encoding and local I/O.</summary>
        private CancellationToken _cancellation;
        /// <summary>Stores the first managed local I/O failure to rethrow outside native callbacks.</summary>
        private Exception? _ioError;
        /// <summary>Identifies the worker that owns the native contexts.</summary>
        private int _ownerThread;
        /// <summary>Stores the last accepted frame timestamp, or -1 before the first frame.</summary>
        private long _lastFrameIndex = -1;
        /// <summary>Stores the latest encoded packet end on the recording timeline.</summary>
        private double _latestPacketEnd;
        /// <summary>Stores the current compressed bitrate for thread-safe readers.</summary>
        private long _currentBitRate;
        /// <summary>Counts submitted video frames accepted by the encoder.</summary>
        private long _encodedFrames;
        /// <summary>Counts compressed video payload bytes written to the muxer.</summary>
        private long _bytesWritten;
        /// <summary>Indicates that the encoder and file header opened successfully.</summary>
        private bool _opened;
        /// <summary>Indicates that delayed packets and the output trailer were written successfully.</summary>
        private bool _completed;
        /// <summary>Prevents access after native resources have been released.</summary>
        private bool _disposed;

        /// <summary>Gets the selected FFmpeg codec implementation name, or null before opening.</summary>
        public string? EncoderName { get; private set; }
        /// <summary>Gets the actual compression backend after opening successfully.</summary>
        public VideoEncoderType EncoderType { get; private set; }
        /// <summary>Gets the actual configured rate control mode after opening successfully.</summary>
        public VideoRateControlMode RateControlMode => _options.RateControlMode;
        /// <summary>Gets the fixed compressed video format selected for this recording.</summary>
        public VideoEncodingFormat EncodingFormat => _options.Format;
        /// <summary>Gets the configured target bitrate in bits per second.</summary>
        public long BitRate => _options.BitRate;
        /// <summary>Gets the effective encoder maximum bitrate in bits per second; CBR uses its constant target.</summary>
        /// <remarks>VBV encoders use a buffered limit; libvpx/libaom use rate-control budgets that can overshoot in either mode. Neither limits individual packet sizes.</remarks>
        public long MaximumBitRate => RateControlMode == VideoRateControlMode.CBR ? _options.BitRate : _options.MaximumBitRate;
        /// <summary>Gets the reason hardware preference fell back to software, or null when no fallback occurred.</summary>
        public string? HardwareFallbackReason { get; private set; }
        /// <summary>Gets the current compressed video bitrate estimated over up to one recording second, in bits per second.</summary>
        public long CurrentBitRate => Interlocked.Read(ref _currentBitRate);
        /// <summary>Gets the number of frames accepted by the encoder; delayed packets are finalized by Complete.</summary>
        public long EncodedFrames => Interlocked.Read(ref _encodedFrames);
        /// <summary>Gets compressed video payload bytes written to the muxer, excluding container overhead.</summary>
        public long BytesWritten => Interlocked.Read(ref _bytesWritten);
        /// <summary>Gets the fixed output width in pixels.</summary>
        public int Width => _options.Width;
        /// <summary>Gets the fixed output height in pixels.</summary>
        public int Height => _options.Height;
        /// <summary>Gets the fixed output frame rate.</summary>
        public int FrameRate => _options.FrameRate;
        /// <summary>Gets the effective FFmpeg software codec worker count, or zero for hardware encoding.</summary>
        public int SoftwareThreadCount { get; private set; }

        /// <summary>Creates an encoder with an independent validated options snapshot.</summary>
        /// <param name="options">The settings to copy for this recording.</param>
        /// <exception cref="ArgumentNullException">Options are null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A setting is outside its supported range.</exception>
        public FFmpegVideoEncoder(EncoderOptions options)
        {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndClone();
        }

        /// <summary>Selects an encoder and creates a new local output file using the extension's container format.</summary>
        /// <param name="localOutputPath">The local output filename; existing files are never overwritten.</param>
        /// <param name="cancellationToken">Cancellation for opening, subsequent encoding, and finalization.</param>
        /// <exception cref="ArgumentException">The output path is empty or is a URL.</exception>
        /// <exception cref="IOException">The output already exists or local file I/O fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The file cannot be created with the current permissions.</exception>
        /// <exception cref="InvalidOperationException">The encoder is already open, accessed by another thread, or a native operation fails.</exception>
        /// <exception cref="NotSupportedException">The native ABI, container, requested codec, or rate control combination is unavailable.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="OutOfMemoryException">A native allocation fails.</exception>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        public void Open(string localOutputPath, CancellationToken cancellationToken)
        {
            EnsureOwner();
            if (_format != null || _opened)
            {
                throw new InvalidOperationException("Encoder is already open.");
            }

            if (string.IsNullOrWhiteSpace(localOutputPath)
                || (!Path.IsPathRooted(localOutputPath) && Uri.TryCreate(localOutputPath, UriKind.Absolute, out _)))
            {
                throw new ArgumentException("A local output filename is required.", nameof(localOutputPath));
            }

            var path = Path.GetFullPath(localOutputPath);
            _ownerThread = Thread.CurrentThread.ManagedThreadId;
            _cancellation = cancellationToken;
            _cancellation.ThrowIfCancellationRequested();
            try
            {
                CheckVersion("avutil", ffmpeg.avutil_version());
                CheckVersion("avcodec", ffmpeg.avcodec_version());
                CheckVersion("avformat", ffmpeg.avformat_version());
                CheckVersion("swscale", ffmpeg.swscale_version());
                AVFormatContext* format = null;
                var result = ffmpeg.avformat_alloc_output_context2(&format, null, null!, path);
                _format = format;
                if (result < 0 || format == null)
                {
                    throw new NotSupportedException("This FFmpeg build has no muxer for the output extension. Rebuild native libraries with recording encoders and muxers enabled.");
                }

                if ((format->oformat->flags & ffmpeg.AVFMT_NOFILE) != 0)
                {
                    throw new NotSupportedException("The selected muxer does not produce an ordinary local file.");
                }

                SelectEncoder();
                _stream = ffmpeg.avformat_new_stream(_format, null);
                if (_stream == null)
                {
                    throw new OutOfMemoryException("Cannot allocate the output video stream.");
                }

                _stream->time_base = _codec->time_base;
                _stream->avg_frame_rate = _codec->framerate;
                Check(ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _codec), "Copy encoder stream parameters");
                AllocateFrames();
                OpenLocalFile(path);
                CheckOutput(ffmpeg.avformat_write_header(_format, null), "Write video header");
                _opened = true;
                MajDebug.LogInfo("FFmpeg", "[Encoder] Opened encoder=" + EncoderName + ", backend=" + EncoderType
                    + ", mode=" + RateControlMode + ", target=" + _options.BitRate + ", maximum=" + _codec->rc_max_rate
                    + ", software workers=" + SoftwareThreadCount + ".");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Converts a tightly packed sRGB RGBA8 frame to the encoder's pixel format and drains available packets.</summary>
        /// <param name="packedRgba">Exactly Width times Height times four sRGB bytes, borrowed only until this call returns.</param>
        /// <param name="frameIndex">A nonnegative, strictly increasing timestamp in units of one output frame; gaps preserve dropped-frame timing.</param>
        /// <param name="flipVertically">Whether the first input row is the bottom row and must be reversed.</param>
        /// <exception cref="ArgumentException">The input buffer has the wrong length.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The frame timestamp is negative or not strictly increasing.</exception>
        /// <exception cref="InvalidOperationException">The encoder is closed, finalized, accessed by another thread, or native conversion/encoding fails.</exception>
        /// <exception cref="IOException">Writing the local output fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        public void Encode(ReadOnlySpan<byte> packedRgba, long frameIndex, bool flipVertically)
        {
            EnsureOpen();
            _cancellation.ThrowIfCancellationRequested();
            if (packedRgba.Length != checked(Width * Height * 4))
            {
                throw new ArgumentException("The input must contain exactly one tightly packed RGBA8 frame.", nameof(packedRgba));
            }

            if (frameIndex < 0 || frameIndex <= _lastFrameIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex), "Frame indices must be nonnegative and strictly increasing.");
            }

            Check(ffmpeg.av_frame_make_writable(_rgba), "Make RGBA staging writable");
            Check(ffmpeg.av_frame_make_writable(_frame), "Make encoder frame writable");
            var rowBytes = Width * 4;
            fixed (byte* input = packedRgba)
            {
                for (var y = 0; y < Height; y++)
                {
                    var sourceRow = flipVertically ? Height - 1 - y : y;
                    Buffer.MemoryCopy(input + sourceRow * rowBytes, _rgba->data[0] + y * _rgba->linesize[0], rowBytes, rowBytes);
                }
            }

            _source[0] = _rgba->data[0];
            _sourceStride[0] = _rgba->linesize[0];
            for (var plane = 0; plane < 4; plane++)
            {
                _destination[plane] = _frame->data[(uint)plane];
                _destinationStride[plane] = _frame->linesize[(uint)plane];
            }

            var rows = ffmpeg.sws_scale(_scale, _source, _sourceStride, 0, Height, _destination, _destinationStride);
            Check(rows, "Convert capture pixels");
            if (rows != Height)
            {
                throw new InvalidOperationException("FFmpeg produced an incomplete encoder input frame.");
            }

            _frame->pts = frameIndex;
            _frame->duration = 1;
            var submitted = _frame;
            if (_hardwareFrames != null)
            {
                ffmpeg.av_frame_unref(_hardwareFrame);
                Check(ffmpeg.av_hwframe_get_buffer(_hardwareFrames, _hardwareFrame, 0), "Acquire encoder hardware frame");
                Check(ffmpeg.av_hwframe_transfer_data(_hardwareFrame, _frame, 0), "Upload encoder hardware frame");
                Check(ffmpeg.av_frame_copy_props(_hardwareFrame, _frame), "Copy hardware frame metadata");
                submitted = _hardwareFrame;
            }

            SendFrame(submitted);
            _lastFrameIndex = frameIndex;
            Interlocked.Increment(ref _encodedFrames);
            ReceivePackets(false);
        }

        /// <summary>Drains delayed encoder packets, writes the container trailer, and flushes the local output.</summary>
        /// <remarks>Repeated successful calls have no effect. Cancellation or failure leaves a partial file for the caller to handle.</remarks>
        /// <exception cref="InvalidOperationException">The encoder is not open, accessed by another thread, or draining/finalization fails.</exception>
        /// <exception cref="IOException">Writing or flushing the local output fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        public void Complete()
        {
            EnsureOwner();
            if (_completed)
            {
                return;
            }

            EnsureOpen();
            _cancellation.ThrowIfCancellationRequested();
            SendFrame(null);
            ReceivePackets(true);
            CheckOutput(ffmpeg.av_write_trailer(_format), "Write video trailer");
            ffmpeg.avio_flush(_io);
            CheckOutput(_io->error, "Flush video output");
            _output!.Flush();
            _completed = true;
        }

        /// <summary>Attempts hardware implementations first when requested, then known software implementations of the same format.</summary>
        /// <exception cref="NotSupportedException">No installed implementation satisfies the requested format, mode, and rate limit.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        private void SelectEncoder()
        {
            var failures = new StringBuilder();
            var codecId = GetCodecId(_options.Format);
            if (ffmpeg.avformat_query_codec(_format->oformat, codecId, ffmpeg.FF_COMPLIANCE_NORMAL) == 0)
            {
                throw new NotSupportedException("The selected output container cannot store " + _options.Format + ".");
            }

            if (_options.PreferredEncoderType == VideoEncoderType.Hardware)
            {
                var prefix = _options.Format.ToString().ToLowerInvariant();
                if (_options.Format != VideoEncodingFormat.MPEG4)
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    if (TryEncoder(prefix + "_nvenc", true, failures) || TryEncoder(prefix + "_amf", true, failures))
                    {
                        return;
                    }
                    failures.Append("MediaFoundation cannot verify that its driver accepted rate control and maximum bitrate; ");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    if (_options.Format == VideoEncodingFormat.H264 && TryEncoder("h264_videotoolbox", true, failures))
                    {
                        return;
                    }
                    if (_options.Format != VideoEncodingFormat.H264)
                    {
                        failures.Append("VideoToolbox does not expose a checked maximum-rate setting for this format; ");
                    }
#elif UNITY_IOS && !UNITY_EDITOR
                    failures.Append("VideoToolbox cannot verify hardware-only encoding through this iOS FFmpeg wrapper; ");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
                    if (TryEncoder(prefix + "_nvenc", true, failures) || TryEncoder(prefix + "_vaapi", true, failures))
                    {
                        return;
                    }
#elif UNITY_ANDROID
                    failures.Append("MediaCodec does not expose a verified maximum bitrate and hardware-only selection through this FFmpeg wrapper; ");
#endif
                }
                else
                {
                    failures.Append("No supported MPEG4 hardware encoder; ");
                }

                HardwareFallbackReason = failures.ToString().Trim();
                MajDebug.LogWarning("FFmpeg", "[Encoder] " + HardwareFallbackReason + " Trying software encoding.");
            }

            var softwareName = _options.Format switch
            {
                VideoEncodingFormat.H264 => "libx264",
                VideoEncodingFormat.HEVC => "libx265",
                VideoEncodingFormat.VP9 => "libvpx-vp9",
                VideoEncodingFormat.AV1 => "libaom-av1",
                _ => "mpeg4"
            };
            if (!TryEncoder(softwareName, false, failures))
            {
                throw new NotSupportedException("No encoder supports " + _options.Format + " with " + _options.RateControlMode
                    + " and the requested rate limit. Rebuild FFmpeg with the required encoder and muxer; formats are not substituted. " + failures);
            }
        }

        /// <summary>Opens one known encoder and frees all candidate resources if it cannot satisfy the requested configuration.</summary>
        /// <param name="name">The exact FFmpeg encoder name.</param>
        /// <param name="hardware">Whether this candidate requires hardware compression.</param>
        /// <param name="failures">The startup diagnostics receiving rejected candidate reasons.</param>
        /// <returns>True when the candidate opened and became the selected encoder.</returns>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="OutOfMemoryException">The codec context cannot be allocated.</exception>
        private bool TryEncoder(string name, bool hardware, StringBuilder failures)
        {
            _cancellation.ThrowIfCancellationRequested();
            var codec = ffmpeg.avcodec_find_encoder_by_name(name);
            if (codec == null)
            {
                failures.Append(name).Append(" is not compiled in; ");
                return false;
            }

            _codec = ffmpeg.avcodec_alloc_context3(codec);
            if (_codec == null)
            {
                throw new OutOfMemoryException("Cannot allocate encoder context.");
            }

            try
            {
                _codec->width = Width;
                _codec->height = Height;
                _codec->time_base = new AVRational { num = 1, den = FrameRate };
                _codec->framerate = new AVRational { num = FrameRate, den = 1 };
                _codec->sample_aspect_ratio = new AVRational { num = 1, den = 1 };
                _codec->gop_size = FrameRate * 2;
                _codec->max_b_frames = 0;
                var maximumCodecThreads = name switch
                {
                    "libx265" => 16,
                    "libvpx-vp9" => 64,
                    "libaom-av1" => 64,
                    _ => 128
                };
                _codec->thread_count = hardware ? 1 : Math.Min(_options.MaximumSoftwareThreads, maximumCodecThreads);
                _codec->bit_rate = _options.BitRate;
                _codec->rc_max_rate = MaximumBitRate;
                _codec->rc_min_rate = _options.RateControlMode == VideoRateControlMode.CBR ? _options.BitRate : 0;
                _codec->rc_buffer_size = checked((int)_codec->rc_max_rate);
                _codec->color_range = AVColorRange.AVCOL_RANGE_MPEG;
                _codec->colorspace = AVColorSpace.AVCOL_SPC_BT709;
                _codec->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
                _codec->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
                if ((_format->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    _codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }

                ConfigureRateControl(name);
                if (name.EndsWith("_vaapi", StringComparison.Ordinal))
                {
                    ConfigureVaapi();
                }
                else
                {
                    _codec->pix_fmt = ChooseSoftwarePixelFormat(codec);
                }

                Check(ffmpeg.avcodec_open2(_codec, codec, null), "Open " + name);
                if (!hardware && _codec->thread_count > _options.MaximumSoftwareThreads)
                {
                    throw new NotSupportedException("The encoder exceeds the requested software worker count.");
                }

                EncoderName = name;
                EncoderType = hardware ? VideoEncoderType.Hardware : VideoEncoderType.Software;
                SoftwareThreadCount = hardware ? 0 : Math.Max(1, _codec->thread_count);
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException || error is NotSupportedException)
            {
                failures.Append(name).Append(": ").Append(error.Message).Append("; ");
                ReleaseCodec();
                return false;
            }
        }

        /// <summary>Configures explicit rate control options supported by a known implementation.</summary>
        /// <param name="name">The exact implementation name.</param>
        /// <exception cref="NotSupportedException">The implementation cannot provide this mode or checked rate limit.</exception>
        /// <exception cref="InvalidOperationException">An explicit private encoder option is rejected.</exception>
        private void ConfigureRateControl(string name)
        {
            var cbr = _options.RateControlMode == VideoRateControlMode.CBR;
            if (name == "mpeg4")
            {
                // FFmpeg's MPEG4 encoder applies the configured VBV minimum/maximum
                // and inserts MPEG4 stuffing when constant-rate output needs padding.
            }
            else if (name.EndsWith("_nvenc", StringComparison.Ordinal))
            {
                SetOption("rc", cbr ? "cbr" : "vbr");
                if (cbr)
                {
                    SetOption("cbr_padding", "1");
                }
            }
            else if (name.EndsWith("_amf", StringComparison.Ordinal))
            {
                if (ffmpeg.avcodec_configuration().IndexOf(CheckedAmfConfigurationOption, StringComparison.Ordinal) < 0)
                {
                    throw new NotSupportedException("This AMF build lacks the reviewed driver rate-control property checks. Rebuild native libraries with the current AMF source patch and "
                        + CheckedAmfConfigurationOption + ".");
                }

                SetOption("rc", cbr ? "cbr" : "vbr_peak");
                SetOption("enforce_hrd", "1");
                if (cbr)
                {
                    SetOption("filler_data", "1");
                }
            }
            else if (name.EndsWith("_vaapi", StringComparison.Ordinal))
            {
                SetOption("rc_mode", cbr ? "CBR" : "VBR");
            }
            else if (name == "h264_videotoolbox")
            {
                SetOption("allow_sw", "0");
                SetOption("require_sw", "0");
                SetOption("constant_bit_rate", cbr ? "1" : "0");
            }
            else if (name == "libx264")
            {
                SetOption("preset", "veryfast");
                // VBR HRD signaling is compatible with MP4. Equal VBV target/maximum
                // and independent filler still select x264's actual CBR rate control.
                SetOption("nal-hrd", "vbr");
                // Unlike x264-params, this parser returns an error for rejected keys.
                SetOption("x264opts", "sync-lookahead=0:rc-lookahead=0:lookahead-threads=1:filler=" + (cbr ? "1" : "0"));
            }
            else if (name == "libx265")
            {
                if (ffmpeg.avcodec_configuration().IndexOf(CheckedX265ConfigurationMarker, StringComparison.Ordinal) < 0)
                {
                    throw new NotSupportedException("This x265 wrapper does not reject ignored encoder parameters. Rebuild native libraries with "
                        + CheckedX265ConfigurationMarker + ".");
                }

                SetOption("preset", "veryfast");
                SetOption("x265-params", "pools=none:frame-threads=" + _codec->thread_count.ToString(CultureInfo.InvariantCulture)
                    + ":wpp=0:strict-cbr=" + (cbr ? "1" : "0"));
            }
            else if (name == "libvpx-vp9" || name == "libaom-av1")
            {
                // With CRF unset, equal min/max/target selects CBR; otherwise the
                // good-quality defaults select one-pass VBR. The rate limit is a
                // native budget, rather than a hard bound on compressed payload.
                SetOption("crf", "-1");
                SetOption("cpu-used", "6");
                SetOption("lag-in-frames", "0");
                SetOption("drop-threshold", "0");
                SetOption("undershoot-pct", "0");
                SetOption("overshoot-pct", "0");
                if (name == "libvpx-vp9")
                {
                    SetOption("deadline", "good");
                }
                else
                {
                    SetOption("usage", "good");
                    var percentage = (MaximumBitRate * 100 / _options.BitRate).ToString(CultureInfo.InvariantCulture);
                    SetOption("aom-params", "max-intra-rate=" + percentage + ":max-inter-rate=" + percentage);
                }
            }
            else
            {
                throw new NotSupportedException("The encoder's rate control contract is unknown.");
            }
        }

        /// <summary>Assigns one checked encoder-private option before opening the codec.</summary>
        /// <param name="name">The private option name.</param>
        /// <param name="value">The option value or named constant.</param>
        /// <exception cref="InvalidOperationException">FFmpeg rejects the option or its value.</exception>
        private void SetOption(string name, string value)
        {
            Check(ffmpeg.av_opt_set(_codec->priv_data, name, value, 0), "Set encoder " + name);
        }

        /// <summary>Selects a supported CPU input format that swscale can produce.</summary>
        /// <param name="codec">The borrowed candidate codec.</param>
        /// <returns>A supported YUV420P or NV12 input format.</returns>
        /// <exception cref="NotSupportedException">The codec provides no compatible 8-bit CPU input format.</exception>
        /// <exception cref="InvalidOperationException">FFmpeg cannot report supported configurations.</exception>
        private AVPixelFormat ChooseSoftwarePixelFormat(AVCodec* codec)
        {
            void* configurations = null;
            var count = 0;
            Check(ffmpeg.avcodec_get_supported_config(_codec, codec, AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT,
                0, &configurations, &count), "Query encoder pixel formats");
            if (configurations == null)
            {
                return AVPixelFormat.AV_PIX_FMT_YUV420P;
            }

            var formats = (AVPixelFormat*)configurations;
            for (var i = 0; i < count; i++)
            {
                if (formats[i] == AVPixelFormat.AV_PIX_FMT_YUV420P || formats[i] == AVPixelFormat.AV_PIX_FMT_NV12)
                {
                    return formats[i];
                }
            }

            throw new NotSupportedException("The encoder has no supported YUV420P or NV12 CPU input.");
        }

        /// <summary>Creates a VAAPI device and an NV12 hardware frame pool for explicit CPU-to-GPU upload.</summary>
        /// <exception cref="InvalidOperationException">VAAPI device or pool initialization fails.</exception>
        /// <exception cref="OutOfMemoryException">A device frame pool reference cannot be allocated.</exception>
        private void ConfigureVaapi()
        {
            AVBufferRef* device = null;
            var result = ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
                "/dev/dri/renderD128", null, 0);
            _hardwareDevice = device;
            Check(result, "Create VAAPI encoder device");
            _hardwareFrames = ffmpeg.av_hwframe_ctx_alloc(_hardwareDevice);
            if (_hardwareFrames == null)
            {
                throw new OutOfMemoryException("Cannot allocate VAAPI encoder frame pool.");
            }

            var frames = (AVHWFramesContext*)_hardwareFrames->data;
            frames->format = AVPixelFormat.AV_PIX_FMT_VAAPI;
            frames->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
            frames->width = Width;
            frames->height = Height;
            frames->initial_pool_size = 8;
            Check(ffmpeg.av_hwframe_ctx_init(_hardwareFrames), "Initialize VAAPI encoder frame pool");
            _codec->hw_frames_ctx = ffmpeg.av_buffer_ref(_hardwareFrames);
            if (_codec->hw_frames_ctx == null)
            {
                throw new OutOfMemoryException("Cannot retain the VAAPI encoder frame pool.");
            }

            _codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_VAAPI;
        }

        /// <summary>Allocates padded reusable pixel buffers, packet storage, and the color converter.</summary>
        /// <exception cref="OutOfMemoryException">Native frame, packet, or pixel storage cannot be allocated.</exception>
        /// <exception cref="InvalidOperationException">The converter cannot be configured.</exception>
        private void AllocateFrames()
        {
            _rgba = AllocateFrame(AVPixelFormat.AV_PIX_FMT_RGBA);
            var cpuFormat = _hardwareFrames != null ? AVPixelFormat.AV_PIX_FMT_NV12 : _codec->pix_fmt;
            _frame = AllocateFrame(cpuFormat);
            _hardwareFrame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            if (_hardwareFrame == null || _packet == null)
            {
                throw new OutOfMemoryException("Cannot allocate encoder frame or packet storage.");
            }

            _scale = ffmpeg.sws_getContext(Width, Height, AVPixelFormat.AV_PIX_FMT_RGBA, Width, Height,
                cpuFormat, (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (_scale == null)
            {
                throw new InvalidOperationException("Cannot create capture color converter.");
            }

            var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(ffmpeg.SWS_CS_ITU709);
            Check(ffmpeg.sws_setColorspaceDetails(_scale, in coefficients, 1, in coefficients, 0,
                0, 1 << 16, 1 << 16), "Configure BT.709 capture colors");
        }

        /// <summary>Allocates an aligned pixel frame with SDR BT.709 primaries/matrix and an sRGB transfer curve.</summary>
        /// <param name="pixelFormat">The CPU pixel format to allocate.</param>
        /// <returns>A frame owned by the caller.</returns>
        /// <exception cref="OutOfMemoryException">The native frame or pixel storage cannot be allocated.</exception>
        private AVFrame* AllocateFrame(AVPixelFormat pixelFormat)
        {
            var frame = ffmpeg.av_frame_alloc();
            if (frame == null)
            {
                throw new OutOfMemoryException("Cannot allocate capture pixel frame.");
            }

            frame->format = (int)pixelFormat;
            frame->width = Width;
            frame->height = Height;
            frame->color_range = pixelFormat == AVPixelFormat.AV_PIX_FMT_RGBA ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
            frame->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            frame->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
            frame->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
            frame->sample_aspect_ratio = _codec->sample_aspect_ratio;
            if (ffmpeg.av_frame_get_buffer(frame, 32) < 0)
            {
                ffmpeg.av_frame_free(&frame);
                throw new OutOfMemoryException("Cannot allocate aligned capture pixels.");
            }

            return frame;
        }

        /// <summary>Opens a new file atomically and attaches custom seekable I/O to the muxer.</summary>
        /// <param name="path">The full local output path.</param>
        /// <exception cref="IOException">The file exists or cannot be opened.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating the file is not permitted.</exception>
        /// <exception cref="OutOfMemoryException">The AVIO buffer or context cannot be allocated.</exception>
        private void OpenLocalFile(string path)
        {
            _cancellation.ThrowIfCancellationRequested();
            _output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536);
            _selfHandle = GCHandle.Alloc(this);
            var buffer = (byte*)ffmpeg.av_malloc(65536);
            if (buffer == null)
            {
                throw new OutOfMemoryException("Cannot allocate encoder output buffer.");
            }

            _io = ffmpeg.avio_alloc_context(buffer, 65536, 1, (void*)GCHandle.ToIntPtr(_selfHandle),
                null, s_writeCallback, s_seekCallback);
            if (_io == null)
            {
                ffmpeg.av_free(buffer);
                throw new OutOfMemoryException("Cannot allocate encoder output I/O context.");
            }

            _io->seekable = ffmpeg.AVIO_SEEKABLE_NORMAL;
            _format->pb = _io;
            _format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
        }

        /// <summary>Passes a frame or drain marker to FFmpeg, resolving backpressure by receiving packets before retrying.</summary>
        /// <param name="frame">The borrowed input frame, or null to begin draining.</param>
        /// <exception cref="InvalidOperationException">FFmpeg cannot accept the frame or violates its send/receive progress contract.</exception>
        /// <exception cref="IOException">Writing compressed packets fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        private void SendFrame(AVFrame* frame)
        {
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = ffmpeg.avcodec_send_frame(_codec, frame);
                if (result != -ffmpeg.EAGAIN)
                {
                    Check(result, frame == null ? "Drain video encoder" : "Send captured frame");
                    return;
                }

                if (ReceivePackets(false) == 0)
                {
                    throw new InvalidOperationException("The encoder cannot make progress while handling frame backpressure.");
                }
            }
        }

        /// <summary>Receives and muxes available packets, preserving timestamps and updating video-only bitrate diagnostics.</summary>
        /// <param name="draining">Whether FFmpeg must reach EOF after accepting its drain marker.</param>
        /// <returns>The number of packets received during this call.</returns>
        /// <exception cref="InvalidOperationException">Encoding or muxing fails, or draining returns EAGAIN instead of EOF.</exception>
        /// <exception cref="IOException">The managed output callback reported a write or seek failure.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        private int ReceivePackets(bool draining)
        {
            var count = 0;
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = ffmpeg.avcodec_receive_packet(_codec, _packet);
                if (result == ffmpeg.AVERROR_EOF)
                {
                    return count;
                }

                if (result == -ffmpeg.EAGAIN)
                {
                    if (draining)
                    {
                        throw new InvalidOperationException("The encoder requested input after accepting its drain marker.");
                    }

                    return count;
                }

                Check(result, "Receive encoded video packet");
                try
                {
                    var bytes = _packet->size;
                    var timestamp = _packet->pts != ffmpeg.AV_NOPTS_VALUE ? _packet->pts : _packet->dts;
                    var start = timestamp == ffmpeg.AV_NOPTS_VALUE ? _latestPacketEnd : timestamp / (double)FrameRate;
                    var duration = Math.Max(1L, _packet->duration) / (double)FrameRate;
                    _bitRateTracker.Add(start, duration, bytes);
                    _latestPacketEnd = Math.Max(_latestPacketEnd, start + duration);
                    ffmpeg.av_packet_rescale_ts(_packet, _codec->time_base, _stream->time_base);
                    if (_packet->duration <= 0)
                    {
                        _packet->duration = ffmpeg.av_rescale_q(1, _codec->time_base, _stream->time_base);
                    }

                    _packet->stream_index = _stream->index;
                    CheckOutput(ffmpeg.av_interleaved_write_frame(_format, _packet), "Mux encoded video packet");
                    Interlocked.Add(ref _bytesWritten, bytes);
                    Interlocked.Exchange(ref _currentBitRate, _bitRateTracker.Measure(_latestPacketEnd));
                    count++;
                }
                finally
                {
                    ffmpeg.av_packet_unref(_packet);
                }
            }
        }

        /// <summary>Writes borrowed FFmpeg bytes to the local file without throwing across the native callback boundary.</summary>
        /// <param name="opaque">The handle identifying this encoder.</param>
        /// <param name="buffer">The borrowed bytes to write.</param>
        /// <param name="length">The number of bytes to write.</param>
        /// <returns>The number of bytes written, or a negative FFmpeg error.</returns>
        [MonoPInvokeCallback(typeof(avio_alloc_context_write_packet))]
        private static int WriteOutput(void* opaque, byte* buffer, int length)
        {
            FFmpegVideoEncoder? self = null;
            try
            {
                self = (FFmpegVideoEncoder)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
                if (self._cancellation.IsCancellationRequested)
                {
                    return ffmpeg.AVERROR_EXIT;
                }

                self._output!.Write(new ReadOnlySpan<byte>(buffer, length));
                return length;
            }
            catch (Exception error)
            {
                if (self != null)
                {
                    self._ioError ??= error;
                }

                return -5;
            }
        }

        /// <summary>Seeks the local output for seekable muxers without throwing across the native callback boundary.</summary>
        /// <param name="opaque">The handle identifying this encoder.</param>
        /// <param name="offset">The requested byte offset.</param>
        /// <param name="whence">The seek origin or AVSEEK_SIZE query flag.</param>
        /// <returns>The file position or length, or a negative FFmpeg error.</returns>
        [MonoPInvokeCallback(typeof(avio_alloc_context_seek))]
        private static long SeekOutput(void* opaque, long offset, int whence)
        {
            FFmpegVideoEncoder? self = null;
            try
            {
                self = (FFmpegVideoEncoder)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
                if (self._cancellation.IsCancellationRequested)
                {
                    return ffmpeg.AVERROR_EXIT;
                }

                if ((whence & ffmpeg.AVSEEK_SIZE) != 0)
                {
                    return self._output!.Length;
                }

                whence &= ~ffmpeg.AVSEEK_FORCE;
                if (whence < 0 || whence > 2)
                {
                    return -22;
                }

                return self._output!.Seek(offset, (SeekOrigin)whence);
            }
            catch (Exception error)
            {
                if (self != null)
                {
                    self._ioError ??= error;
                }

                return -5;
            }
        }

        /// <summary>Checks cancellation, managed callback errors, native return values, and deferred AVIO errors.</summary>
        /// <param name="result">The native status code.</param>
        /// <param name="operation">The diagnostic operation name.</param>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="IOException">A local file callback failed.</exception>
        /// <exception cref="InvalidOperationException">FFmpeg reported failure.</exception>
        private void CheckOutput(int result, string operation)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_ioError != null)
            {
                throw new IOException(operation + " failed while accessing the local output.", _ioError);
            }

            Check(result, operation);
            if (_io != null)
            {
                Check(_io->error, operation + " I/O");
            }
        }

        /// <summary>Maps the requested public format to its FFmpeg codec identifier.</summary>
        /// <param name="format">The validated public video format.</param>
        /// <returns>The matching native codec identifier.</returns>
        private static AVCodecID GetCodecId(VideoEncodingFormat format) => format switch
        {
            VideoEncodingFormat.H264 => AVCodecID.AV_CODEC_ID_H264,
            VideoEncodingFormat.HEVC => AVCodecID.AV_CODEC_ID_HEVC,
            VideoEncodingFormat.VP9 => AVCodecID.AV_CODEC_ID_VP9,
            VideoEncodingFormat.AV1 => AVCodecID.AV_CODEC_ID_AV1,
            _ => AVCodecID.AV_CODEC_ID_MPEG4
        };

        /// <summary>Rejects native library ABI versions that do not match the repository's bindings.</summary>
        /// <param name="library">The native library short name.</param>
        /// <param name="version">The library's packed version.</param>
        /// <exception cref="NotSupportedException">The loaded ABI major differs from the bindings.</exception>
        private static void CheckVersion(string library, uint version)
        {
            var expected = ffmpeg.LibraryVersionMap[library];
            if ((version >> 16) != expected)
            {
                throw new NotSupportedException(library + " ABI mismatch: expected " + expected + ", loaded " + (version >> 16) + ".");
            }
        }

        /// <summary>Translates a negative native result through the existing FFmpeg diagnostic helper.</summary>
        /// <param name="result">The native status code.</param>
        /// <param name="operation">The operation name included in failures.</param>
        /// <exception cref="InvalidOperationException">The native result is negative.</exception>
        private static void Check(int result, string operation) => FFmpegVideoDecoder.Check(result, operation);

        /// <summary>Checks lifetime and ensures all native operations remain on their owning worker.</summary>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The caller is on a different thread from the owner.</exception>
        private void EnsureOwner()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FFmpegVideoEncoder));
            }

            if (_ownerThread != 0 && _ownerThread != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("FFmpeg encoder operations must remain on their owning worker thread.");
            }
        }

        /// <summary>Checks that the encoder is open and has not been finalized.</summary>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The encoder is closed, finalized, or called from another thread.</exception>
        private void EnsureOpen()
        {
            EnsureOwner();
            if (!_opened || _completed)
            {
                throw new InvalidOperationException("The encoder must be open and not finalized.");
            }
        }

        /// <summary>Releases one codec candidate and its optional hardware device and frame pool.</summary>
        private void ReleaseCodec()
        {
            var codec = _codec;
            _codec = null;
            if (codec != null)
            {
                ffmpeg.avcodec_free_context(&codec);
            }

            var frames = _hardwareFrames;
            _hardwareFrames = null;
            if (frames != null)
            {
                ffmpeg.av_buffer_unref(&frames);
            }

            var device = _hardwareDevice;
            _hardwareDevice = null;
            if (device != null)
            {
                ffmpeg.av_buffer_unref(&device);
            }
        }

        /// <summary>Releases all native and local-file resources without implicitly completing a canceled or failed recording.</summary>
        /// <remarks>Call Complete first for a playable finalized file. Repeated disposal has no effect.</remarks>
        /// <exception cref="InvalidOperationException">The caller is not the owning worker.</exception>
        /// <exception cref="IOException">Closing the local file fails.</exception>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureOwner();
            _disposed = true;
            var rgba = _rgba;
            _rgba = null;
            if (rgba != null)
            {
                ffmpeg.av_frame_free(&rgba);
            }
            var frame = _frame;
            _frame = null;
            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }
            var hardwareFrame = _hardwareFrame;
            _hardwareFrame = null;
            if (hardwareFrame != null)
            {
                ffmpeg.av_frame_free(&hardwareFrame);
            }
            var packet = _packet;
            _packet = null;
            if (packet != null)
            {
                ffmpeg.av_packet_free(&packet);
            }
            if (_scale != null)
            {
                ffmpeg.sws_freeContext(_scale);
                _scale = null;
            }

            ReleaseCodec();
            if (_format != null)
            {
                _format->pb = null;
                ffmpeg.avformat_free_context(_format);
                _format = null;
                _stream = null;
            }

            var io = _io;
            _io = null;
            if (io != null)
            {
                ffmpeg.av_free(io->buffer);
                io->buffer = null;
                ffmpeg.avio_context_free(&io);
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            var output = _output;
            _output = null;
            output?.Dispose();
        }
    }
}
