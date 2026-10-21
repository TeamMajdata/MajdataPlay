#nullable enable
using AOT;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Identifies the container written by <see cref="FFmpegMediaMuxer"/>.</summary>
    public enum MediaContainerFormat
    {
        /// <summary>Writes an ISO base media file, normally named with the <c>.mp4</c> extension.</summary>
        MP4,
        /// <summary>Writes a Matroska file, normally named with the <c>.mkv</c> extension.</summary>
        Matroska
    }

    /// <summary>Combines an encoded video file and interleaved 32-bit float PCM into one MP4 or Matroska output.</summary>
    /// <remarks>
    /// Video packets are copied without re-encoding. Audio uses FFmpeg's AAC encoder when the loaded libraries provide it,
    /// otherwise uncompressed signed 16-bit PCM when the container accepts it. Audio starts at the video timeline origin,
    /// preceded by silence when the first PCM frame was played later than that origin. All work runs synchronously on the
    /// calling thread.
    /// </remarks>
    public sealed unsafe class FFmpegMediaMuxer : IDisposable
    {
        /// <summary>Sets the AAC bit rate per channel in bits per second.</summary>
        private const int AacBitRatePerChannel = 96000;
        /// <summary>Limits the leading silence that an audio delay can insert.</summary>
        private const int MaximumAudioDelaySeconds = 60;
        /// <summary>Sets the number of frames per uncompressed PCM packet, and per AAC frame when the encoder accepts any size.</summary>
        private const int AudioPacketFrames = 1024;
        /// <summary>Sets the size of the native output buffer.</summary>
        private const int OutputBufferSize = 65536;
        /// <summary>Scales nominal float samples to signed 16-bit PCM.</summary>
        private const float Pcm16Scale = 32767f;
        /// <summary>Roots the managed output write callback for IL2CPP and Mono.</summary>
        private static readonly avio_alloc_context_write_packet s_writeCallback = WriteOutput;
        /// <summary>Roots the managed output seek callback for IL2CPP and Mono.</summary>
        private static readonly avio_alloc_context_seek s_seekCallback = SeekOutput;
        /// <summary>Stores the captured video file to copy.</summary>
        private readonly string _videoPath;
        /// <summary>Stores the interleaved float PCM input.</summary>
        private readonly Stream _pcm;
        /// <summary>Stores the sample rate of the PCM input.</summary>
        private readonly int _sampleRate;
        /// <summary>Stores the interleaved channel count of the PCM input.</summary>
        private readonly int _channels;
        /// <summary>Stores the destination of the muxed output.</summary>
        private readonly Stream _output;
        /// <summary>Stores the requested output container.</summary>
        private readonly MediaContainerFormat _container;
        /// <summary>Stores cancellation for reading, encoding, and writing.</summary>
        private readonly CancellationToken _cancellation;
        /// <summary>Stores the timestamp unit of produced audio packets, one sample.</summary>
        private readonly AVRational _audioTimeBase;
        /// <summary>Reuses raw PCM bytes read from the input.</summary>
        private readonly byte[] _pcmBytes;
        /// <summary>Owns the captured video input context.</summary>
        private AVFormatContext* _input;
        /// <summary>Owns the output container context.</summary>
        private AVFormatContext* _format;
        /// <summary>Owns the custom output I/O context.</summary>
        private AVIOContext* _io;
        /// <summary>Borrows the copied output video stream.</summary>
        private AVStream* _videoStream;
        /// <summary>Borrows the output audio stream, or null when the PCM input is empty.</summary>
        private AVStream* _audioStream;
        /// <summary>Owns the AAC encoder, or null when audio is written as uncompressed PCM.</summary>
        private AVCodecContext* _audioCodec;
        /// <summary>Owns reusable encoder input samples.</summary>
        private AVFrame* _audioFrame;
        /// <summary>Owns the pending video packet.</summary>
        private AVPacket* _videoPacket;
        /// <summary>Owns the pending audio packet.</summary>
        private AVPacket* _audioPacket;
        /// <summary>Keeps this instance reachable by native output callbacks.</summary>
        private GCHandle _selfHandle;
        /// <summary>Stores the first managed output failure to rethrow outside native callbacks.</summary>
        private Exception? _ioError;
        /// <summary>Stores the input video stream index.</summary>
        private int _inputVideoIndex;
        /// <summary>Stores the number of frames per AAC encoder frame.</summary>
        private int _audioFrameSize;
        /// <summary>Counts audio frames produced for the audio stream, including leading silence.</summary>
        private long _audioFramesRead;
        /// <summary>Stores the number of silent frames that delay the first PCM frame.</summary>
        private readonly long _leadingSilenceFrames;
        /// <summary>Counts the silent frames still to be produced before the first PCM frame.</summary>
        private long _remainingSilenceFrames;
        /// <summary>Indicates that the PCM input has ended and the encoder has received its drain marker.</summary>
        private bool _audioInputEnded;
        /// <summary>Prevents access after native resources have been released.</summary>
        private bool _disposed;

        /// <summary>Stores the inputs of one muxing operation.</summary>
        /// <param name="videoPath">The local captured video file.</param>
        /// <param name="pcm">The interleaved float PCM input.</param>
        /// <param name="sampleRate">The PCM sample rate.</param>
        /// <param name="channels">The PCM channel count.</param>
        /// <param name="output">The muxed output destination.</param>
        /// <param name="container">The output container.</param>
        /// <param name="audioDelay">The time on the video timeline at which the first PCM frame plays.</param>
        /// <param name="cancellationToken">Cancellation for the operation.</param>
        private FFmpegMediaMuxer(string videoPath, Stream pcm, int sampleRate, int channels, Stream output,
            MediaContainerFormat container, TimeSpan audioDelay, CancellationToken cancellationToken)
        {
            _videoPath = videoPath;
            _pcm = pcm;
            _sampleRate = sampleRate;
            _channels = channels;
            _output = output;
            _container = container;
            _cancellation = cancellationToken;
            _audioTimeBase = new AVRational { num = 1, den = sampleRate };
            _pcmBytes = new byte[AudioPacketFrames * channels * sizeof(float)];
            _leadingSilenceFrames = (long)Math.Round(audioDelay.TotalSeconds * sampleRate);
            _remainingSilenceFrames = _leadingSilenceFrames;
        }

        /// <summary>Copies the first video stream of a file and adds the PCM input as an audio stream in a new container.</summary>
        /// <param name="videoPath">The local video file to copy, such as a Matroska file written by <see cref="FFmpegCameraCapturer"/>.</param>
        /// <param name="pcm">Interleaved little-endian 32-bit float samples, read to the end; an empty input produces a video-only output.</param>
        /// <param name="sampleRate">The PCM sample rate in hertz, between 8000 and 384000.</param>
        /// <param name="channels">The PCM channel count, between one and eight.</param>
        /// <param name="output">A writable, seekable destination that remains owned by the caller.</param>
        /// <param name="container">The output container format.</param>
        /// <param name="audioDelay">
        /// The time after the video timeline origin at which the first PCM frame was played, from zero to one minute; the audio
        /// stream still starts at the origin, with this much leading silence.
        /// </param>
        /// <param name="cancellationToken">Cancellation for reading, encoding, and writing.</param>
        /// <returns>The written audio codec name, such as <c>aac</c> or <c>pcm_s16le</c>, or null when the PCM input was empty.</returns>
        /// <exception cref="ArgumentNullException">A stream or path is null.</exception>
        /// <exception cref="ArgumentException">The video path is empty, or the output is not writable and seekable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The sample rate, channel count, container, or audio delay is invalid.</exception>
        /// <exception cref="NotSupportedException">The libraries lack the container's muxer, or the container cannot store the video or any available audio codec.</exception>
        /// <exception cref="InvalidOperationException">FFmpeg cannot read, encode, or mux the media.</exception>
        /// <exception cref="IOException">Reading the PCM input or writing the output fails.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="OutOfMemoryException">A native allocation fails.</exception>
        public static string? Mux(string videoPath, Stream pcm, int sampleRate, int channels, Stream output,
            MediaContainerFormat container, TimeSpan audioDelay = default, CancellationToken cancellationToken = default)
        {
            if (videoPath is null || pcm is null || output is null)
            {
                throw new ArgumentNullException(videoPath is null ? nameof(videoPath) : pcm is null ? nameof(pcm) : nameof(output));
            }
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                throw new ArgumentException("A local video file is required.", nameof(videoPath));
            }
            if (!output.CanWrite || !output.CanSeek)
            {
                throw new ArgumentException("The output must be writable and seekable.", nameof(output));
            }
            if (sampleRate < 8000 || sampleRate > 384000)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }
            if (channels < 1 || channels > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }
            if (!Enum.IsDefined(typeof(MediaContainerFormat), container))
            {
                throw new ArgumentOutOfRangeException(nameof(container));
            }
            if (audioDelay < TimeSpan.Zero || audioDelay > TimeSpan.FromSeconds(MaximumAudioDelaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(audioDelay));
            }

            using (var muxer = new FFmpegMediaMuxer(videoPath, pcm, sampleRate, channels, output, container, audioDelay, cancellationToken))
            {
                return muxer.Run();
            }
        }

        /// <summary>Converts float samples to signed 16-bit PCM, clamping values outside the nominal range.</summary>
        /// <param name="source">The float samples.</param>
        /// <param name="destination">The 16-bit samples, at least as long as the source.</param>
        internal static void ConvertToPcm16(ReadOnlySpan<float> source, Span<short> destination)
        {
            for (var i = 0; i < source.Length; i++)
            {
                var scaled = source[i] * Pcm16Scale;
                if (float.IsNaN(scaled))
                {
                    destination[i] = 0;
                }
                else if (scaled >= short.MaxValue)
                {
                    destination[i] = short.MaxValue;
                }
                else if (scaled <= short.MinValue)
                {
                    destination[i] = short.MinValue;
                }
                else
                {
                    destination[i] = (short)MathF.Round(scaled);
                }
            }
        }

        /// <summary>Opens both inputs, writes the interleaved output, and finalizes the container.</summary>
        /// <returns>The written audio codec name, or null for a video-only output.</returns>
        private string? Run()
        {
            CheckVersion("avutil", ffmpeg.avutil_version());
            CheckVersion("avcodec", ffmpeg.avcodec_version());
            CheckVersion("avformat", ffmpeg.avformat_version());
            _cancellation.ThrowIfCancellationRequested();
            OpenInput();
            CreateOutput();
            AddVideoStream();
            var audioCodecName = AddAudioStream();
            AllocatePackets();
            OpenOutputIO();
            CheckOutput(ffmpeg.avformat_write_header(_format, null), "Write muxed header");
            var videoPackets = WriteInterleavedPackets();
            CheckOutput(ffmpeg.av_write_trailer(_format), "Write muxed trailer");
            ffmpeg.avio_flush(_io);
            CheckOutput(_io->error, "Flush muxed output");
            _output.Flush();
            MajDebug.LogInfo("FFmpeg", "[Muxer] Wrote " + _container + " with " + videoPackets + " video packets and "
                + _audioFramesRead + " audio frames as " + (audioCodecName ?? "no audio") + ", starting with "
                + (_leadingSilenceFrames - _remainingSilenceFrames) + " silent frames.");
            return audioCodecName;
        }

        /// <summary>Opens the captured video and selects its video stream.</summary>
        /// <exception cref="InvalidOperationException">The file cannot be opened or contains no video stream.</exception>
        private void OpenInput()
        {
            AVFormatContext* input = null;
            var result = ffmpeg.avformat_open_input(&input, _videoPath, null, null);
            _input = input;
            Check(result, "Open captured video");
            Check(ffmpeg.avformat_find_stream_info(_input, null), "Read captured video information");
            _inputVideoIndex = ffmpeg.av_find_best_stream(_input, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            Check(_inputVideoIndex, "Find captured video stream");
        }

        /// <summary>Allocates the output context for the requested container.</summary>
        /// <exception cref="NotSupportedException">The libraries contain no muxer for the container.</exception>
        private void CreateOutput()
        {
            var name = _container == MediaContainerFormat.MP4 ? "mp4" : "matroska";
            AVFormatContext* format = null;
            var result = ffmpeg.avformat_alloc_output_context2(&format, null, name, null!);
            _format = format;
            if (result < 0 || format == null)
            {
                throw new NotSupportedException("This FFmpeg build has no " + name + " muxer.");
            }
        }

        /// <summary>Adds an output stream with the captured video's codec parameters.</summary>
        /// <exception cref="NotSupportedException">The container cannot store the captured video codec.</exception>
        /// <exception cref="OutOfMemoryException">The stream cannot be allocated.</exception>
        private void AddVideoStream()
        {
            var inputStream = _input->streams[_inputVideoIndex];
            var codecId = inputStream->codecpar->codec_id;
            if (ffmpeg.avformat_query_codec(_format->oformat, codecId, ffmpeg.FF_COMPLIANCE_NORMAL) == 0)
            {
                throw new NotSupportedException(_container + " cannot store " + ffmpeg.avcodec_get_name(codecId) + " video.");
            }

            _videoStream = ffmpeg.avformat_new_stream(_format, null);
            if (_videoStream == null)
            {
                throw new OutOfMemoryException("Cannot allocate the output video stream.");
            }
            Check(ffmpeg.avcodec_parameters_copy(_videoStream->codecpar, inputStream->codecpar), "Copy video stream parameters");
            // The input container's codec tag is meaningless in another container.
            _videoStream->codecpar->codec_tag = 0;
            _videoStream->time_base = inputStream->time_base;
            _videoStream->avg_frame_rate = inputStream->avg_frame_rate;
        }

        /// <summary>Adds an AAC or uncompressed PCM audio stream unless the PCM input is empty.</summary>
        /// <returns>The audio codec name, or null when no audio stream was added.</returns>
        /// <exception cref="NotSupportedException">No available audio codec can be stored in the container.</exception>
        private string? AddAudioStream()
        {
            if (_pcm.CanSeek && _pcm.Length - _pcm.Position < _channels * sizeof(float))
            {
                return null;
            }
            if (TryOpenAacEncoder())
            {
                return ffmpeg.avcodec_get_name(AVCodecID.AV_CODEC_ID_AAC);
            }
            if (ffmpeg.avformat_query_codec(_format->oformat, AVCodecID.AV_CODEC_ID_PCM_S16LE, ffmpeg.FF_COMPLIANCE_NORMAL) == 0)
            {
                throw new NotSupportedException("The loaded FFmpeg libraries have no AAC encoder and " + _container
                    + " cannot store PCM audio. Rebuild the native libraries with the aac encoder or record to Matroska.");
            }

            _audioStream = NewAudioStream();
            var parameters = _audioStream->codecpar;
            parameters->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            parameters->codec_id = AVCodecID.AV_CODEC_ID_PCM_S16LE;
            parameters->format = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            parameters->sample_rate = _sampleRate;
            ffmpeg.av_channel_layout_default(&parameters->ch_layout, _channels);
            parameters->bits_per_coded_sample = 16;
            parameters->block_align = _channels * sizeof(short);
            parameters->bit_rate = (long)_sampleRate * _channels * 16;
            return ffmpeg.avcodec_get_name(AVCodecID.AV_CODEC_ID_PCM_S16LE);
        }

        /// <summary>Opens FFmpeg's AAC encoder and its output stream when the libraries and container support them.</summary>
        /// <returns>True when audio will be encoded as AAC.</returns>
        /// <exception cref="OutOfMemoryException">A native allocation fails.</exception>
        private bool TryOpenAacEncoder()
        {
            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (codec == null || ffmpeg.avformat_query_codec(_format->oformat, AVCodecID.AV_CODEC_ID_AAC, ffmpeg.FF_COMPLIANCE_NORMAL) == 0)
            {
                MajDebug.LogWarning("FFmpeg", "[Muxer] The loaded libraries have no AAC encoder; writing uncompressed PCM audio.");
                return false;
            }

            _audioCodec = ffmpeg.avcodec_alloc_context3(codec);
            if (_audioCodec == null)
            {
                throw new OutOfMemoryException("Cannot allocate the AAC encoder context.");
            }
            try
            {
                _audioCodec->sample_rate = _sampleRate;
                ffmpeg.av_channel_layout_default(&_audioCodec->ch_layout, _channels);
                _audioCodec->sample_fmt = ChooseAacSampleFormat(codec);
                _audioCodec->bit_rate = (long)AacBitRatePerChannel * _channels;
                _audioCodec->time_base = _audioTimeBase;
                if ((_format->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    _audioCodec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }
                Check(ffmpeg.avcodec_open2(_audioCodec, codec, null), "Open AAC encoder");
            }
            catch (Exception error) when (error is InvalidOperationException || error is NotSupportedException)
            {
                MajDebug.LogWarning("FFmpeg", "[Muxer] AAC is unavailable for " + _sampleRate + " Hz, " + _channels
                    + " channels (" + error.Message + "); writing uncompressed PCM audio.");
                var rejected = _audioCodec;
                _audioCodec = null;
                ffmpeg.avcodec_free_context(&rejected);
                return false;
            }

            _audioFrameSize = _audioCodec->frame_size > 0 ? _audioCodec->frame_size : AudioPacketFrames;
            _audioFrame = ffmpeg.av_frame_alloc();
            if (_audioFrame == null)
            {
                throw new OutOfMemoryException("Cannot allocate the AAC input frame.");
            }
            _audioFrame->format = (int)_audioCodec->sample_fmt;
            _audioFrame->sample_rate = _sampleRate;
            _audioFrame->nb_samples = _audioFrameSize;
            Check(ffmpeg.av_channel_layout_copy(&_audioFrame->ch_layout, &_audioCodec->ch_layout), "Copy AAC channel layout");
            Check(ffmpeg.av_frame_get_buffer(_audioFrame, 0), "Allocate AAC input samples");

            _audioStream = NewAudioStream();
            Check(ffmpeg.avcodec_parameters_from_context(_audioStream->codecpar, _audioCodec), "Copy AAC stream parameters");
            return true;
        }

        /// <summary>Selects a float input format accepted by the AAC encoder.</summary>
        /// <param name="codec">The AAC encoder.</param>
        /// <returns>Planar float when accepted, otherwise interleaved float.</returns>
        /// <exception cref="NotSupportedException">The encoder accepts neither float format.</exception>
        private AVSampleFormat ChooseAacSampleFormat(AVCodec* codec)
        {
            void* configurations = null;
            var count = 0;
            Check(ffmpeg.avcodec_get_supported_config(_audioCodec, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT,
                0, &configurations, &count), "Query AAC sample formats");
            if (configurations == null)
            {
                return AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            }
            var formats = (AVSampleFormat*)configurations;
            var hasInterleaved = false;
            for (var i = 0; i < count; i++)
            {
                if (formats[i] == AVSampleFormat.AV_SAMPLE_FMT_FLTP)
                {
                    return AVSampleFormat.AV_SAMPLE_FMT_FLTP;
                }
                hasInterleaved |= formats[i] == AVSampleFormat.AV_SAMPLE_FMT_FLT;
            }
            if (hasInterleaved)
            {
                return AVSampleFormat.AV_SAMPLE_FMT_FLT;
            }
            throw new NotSupportedException("The AAC encoder accepts no float sample format.");
        }

        /// <summary>Adds an audio stream whose timestamps count samples.</summary>
        /// <returns>The borrowed stream.</returns>
        /// <exception cref="OutOfMemoryException">The stream cannot be allocated.</exception>
        private AVStream* NewAudioStream()
        {
            var stream = ffmpeg.avformat_new_stream(_format, null);
            if (stream == null)
            {
                throw new OutOfMemoryException("Cannot allocate the output audio stream.");
            }
            stream->time_base = _audioTimeBase;
            return stream;
        }

        /// <summary>Allocates the reusable video and audio packets.</summary>
        /// <exception cref="OutOfMemoryException">A packet cannot be allocated.</exception>
        private void AllocatePackets()
        {
            _videoPacket = ffmpeg.av_packet_alloc();
            _audioPacket = ffmpeg.av_packet_alloc();
            if (_videoPacket == null || _audioPacket == null)
            {
                throw new OutOfMemoryException("Cannot allocate muxer packets.");
            }
        }

        /// <summary>Attaches seekable custom I/O that writes to the caller's stream.</summary>
        /// <exception cref="OutOfMemoryException">The I/O buffer or context cannot be allocated.</exception>
        private void OpenOutputIO()
        {
            _selfHandle = GCHandle.Alloc(this);
            var buffer = (byte*)ffmpeg.av_malloc(OutputBufferSize);
            if (buffer == null)
            {
                throw new OutOfMemoryException("Cannot allocate the muxer output buffer.");
            }
            _io = ffmpeg.avio_alloc_context(buffer, OutputBufferSize, 1, (void*)GCHandle.ToIntPtr(_selfHandle),
                null, s_writeCallback, s_seekCallback);
            if (_io == null)
            {
                ffmpeg.av_free(buffer);
                throw new OutOfMemoryException("Cannot allocate the muxer output I/O context.");
            }
            _io->seekable = ffmpeg.AVIO_SEEKABLE_NORMAL;
            _format->pb = _io;
            _format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
        }

        /// <summary>Writes video and audio packets in presentation order until both inputs end.</summary>
        /// <returns>The number of video packets written.</returns>
        private long WriteInterleavedPackets()
        {
            var videoTimeBase = _input->streams[_inputVideoIndex]->time_base;
            var videoPackets = 0L;
            var hasVideo = ReadVideoPacket();
            var hasAudio = _audioStream != null && ReadAudioPacket();
            while (hasVideo || hasAudio)
            {
                _cancellation.ThrowIfCancellationRequested();
                var videoTime = _videoPacket->dts != ffmpeg.AV_NOPTS_VALUE ? _videoPacket->dts : _videoPacket->pts;
                var writeVideo = hasVideo && (!hasAudio || videoTime == ffmpeg.AV_NOPTS_VALUE
                    || ffmpeg.av_compare_ts(videoTime, videoTimeBase, _audioPacket->pts, _audioTimeBase) <= 0);
                if (writeVideo)
                {
                    WritePacket(_videoPacket, videoTimeBase, _videoStream, "Mux video packet");
                    videoPackets++;
                    hasVideo = ReadVideoPacket();
                }
                else
                {
                    WritePacket(_audioPacket, _audioTimeBase, _audioStream, "Mux audio packet");
                    hasAudio = ReadAudioPacket();
                }
            }
            if (videoPackets == 0)
            {
                throw new InvalidOperationException("The captured video contains no packets.");
            }
            return videoPackets;
        }

        /// <summary>Reads the next packet of the selected input video stream.</summary>
        /// <returns>True when a packet is pending; false at the end of the input.</returns>
        private bool ReadVideoPacket()
        {
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = ffmpeg.av_read_frame(_input, _videoPacket);
                if (result == ffmpeg.AVERROR_EOF)
                {
                    return false;
                }
                Check(result, "Read captured video packet");
                if (_videoPacket->stream_index == _inputVideoIndex)
                {
                    return true;
                }
                ffmpeg.av_packet_unref(_videoPacket);
            }
        }

        /// <summary>Produces the next audio packet from the PCM input.</summary>
        /// <returns>True when a packet is pending; false after all audio has been written.</returns>
        private bool ReadAudioPacket()
        {
            return _audioCodec == null ? ReadPcmPacket() : ReadAacPacket();
        }

        /// <summary>Packs the next block of PCM input as an uncompressed 16-bit packet.</summary>
        /// <returns>True when a packet is pending; false at the end of the input.</returns>
        private bool ReadPcmPacket()
        {
            var samples = ReadPcm(AudioPacketFrames);
            if (samples.IsEmpty)
            {
                return false;
            }
            Check(ffmpeg.av_new_packet(_audioPacket, samples.Length * sizeof(short)), "Allocate PCM packet");
            ConvertToPcm16(samples, new Span<short>(_audioPacket->data, samples.Length));
            var frames = samples.Length / _channels;
            _audioPacket->pts = _audioFramesRead - frames;
            _audioPacket->dts = _audioPacket->pts;
            _audioPacket->duration = frames;
            return true;
        }

        /// <summary>Feeds PCM input to the AAC encoder until it produces a packet or finishes draining.</summary>
        /// <returns>True when a packet is pending; false after the encoder has drained.</returns>
        private bool ReadAacPacket()
        {
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = ffmpeg.avcodec_receive_packet(_audioCodec, _audioPacket);
                if (result == 0)
                {
                    return true;
                }
                if (result == ffmpeg.AVERROR_EOF)
                {
                    return false;
                }
                if (result != -ffmpeg.EAGAIN)
                {
                    Check(result, "Receive encoded audio packet");
                }
                if (_audioInputEnded)
                {
                    throw new InvalidOperationException("The AAC encoder requested input after accepting its drain marker.");
                }

                var samples = ReadPcm(_audioFrameSize);
                if (samples.IsEmpty)
                {
                    _audioInputEnded = true;
                    Check(ffmpeg.avcodec_send_frame(_audioCodec, null), "Drain AAC encoder");
                    continue;
                }
                var frames = samples.Length / _channels;
                Check(ffmpeg.av_frame_make_writable(_audioFrame), "Make AAC input writable");
                // Only the final frame may be shorter than the encoder frame size.
                _audioFrame->nb_samples = frames;
                _audioFrame->pts = _audioFramesRead - frames;
                FillAacFrame(samples, frames);
                Check(ffmpeg.avcodec_send_frame(_audioCodec, _audioFrame), "Send audio samples");
            }
        }

        /// <summary>Copies interleaved float samples into the encoder frame's planar or interleaved layout.</summary>
        /// <param name="samples">The interleaved input samples.</param>
        /// <param name="frames">The number of frames in the input.</param>
        private void FillAacFrame(ReadOnlySpan<float> samples, int frames)
        {
            if (_audioCodec->sample_fmt == AVSampleFormat.AV_SAMPLE_FMT_FLT)
            {
                samples.CopyTo(new Span<float>(_audioFrame->data[0], samples.Length));
                return;
            }
            for (uint channel = 0; channel < _channels; channel++)
            {
                var plane = new Span<float>(_audioFrame->data[channel], frames);
                for (var frame = 0; frame < frames; frame++)
                {
                    plane[frame] = samples[frame * _channels + (int)channel];
                }
            }
        }

        /// <summary>Reads up to the requested number of whole frames of leading silence followed by the PCM input.</summary>
        /// <param name="maximumFrames">The maximum number of frames to read.</param>
        /// <returns>The interleaved samples read, empty at the end of the input; valid until the next read.</returns>
        /// <exception cref="IOException">Reading the PCM input fails.</exception>
        private ReadOnlySpan<float> ReadPcm(int maximumFrames)
        {
            var frameBytes = _channels * sizeof(float);
            var requested = maximumFrames * frameBytes;
            // Silence and input share one read so that only the final AAC frame can be shorter than the encoder frame size.
            var silentFrames = (int)Math.Min(maximumFrames, _remainingSilenceFrames);
            _remainingSilenceFrames -= silentFrames;
            var filled = silentFrames * frameBytes;
            _pcmBytes.AsSpan(0, filled).Clear();
            while (filled < requested)
            {
                var read = _pcm.Read(_pcmBytes, filled, requested - filled);
                if (read == 0)
                {
                    break;
                }
                filled += read;
            }
            // A trailing partial frame cannot be represented and is discarded.
            var frames = filled / frameBytes;
            _audioFramesRead += frames;
            return MemoryMarshal.Cast<byte, float>(_pcmBytes.AsSpan(0, frames * frameBytes));
        }

        /// <summary>Rescales a packet to its output stream and passes it to the interleaving muxer.</summary>
        /// <param name="packet">The packet, which the muxer takes and resets.</param>
        /// <param name="sourceTimeBase">The packet's current timestamp unit.</param>
        /// <param name="stream">The output stream receiving the packet.</param>
        /// <param name="operation">The diagnostic operation name.</param>
        private void WritePacket(AVPacket* packet, AVRational sourceTimeBase, AVStream* stream, string operation)
        {
            ffmpeg.av_packet_rescale_ts(packet, sourceTimeBase, stream->time_base);
            packet->stream_index = stream->index;
            packet->pos = -1;
            CheckOutput(ffmpeg.av_interleaved_write_frame(_format, packet), operation);
        }

        /// <summary>Writes borrowed FFmpeg bytes to the output stream without throwing across the native callback boundary.</summary>
        /// <param name="opaque">The handle identifying this muxer.</param>
        /// <param name="buffer">The borrowed bytes to write.</param>
        /// <param name="length">The number of bytes to write.</param>
        /// <returns>The number of bytes written, or a negative FFmpeg error.</returns>
        [MonoPInvokeCallback(typeof(avio_alloc_context_write_packet))]
        private static int WriteOutput(void* opaque, byte* buffer, int length)
        {
            FFmpegMediaMuxer? self = null;
            try
            {
                self = (FFmpegMediaMuxer)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
                if (self._cancellation.IsCancellationRequested)
                {
                    return ffmpeg.AVERROR_EXIT;
                }
                self._output.Write(new ReadOnlySpan<byte>(buffer, length));
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

        /// <summary>Seeks the output stream for seekable muxers without throwing across the native callback boundary.</summary>
        /// <param name="opaque">The handle identifying this muxer.</param>
        /// <param name="offset">The requested byte offset.</param>
        /// <param name="whence">The seek origin or AVSEEK_SIZE query flag.</param>
        /// <returns>The stream position or length, or a negative FFmpeg error.</returns>
        [MonoPInvokeCallback(typeof(avio_alloc_context_seek))]
        private static long SeekOutput(void* opaque, long offset, int whence)
        {
            FFmpegMediaMuxer? self = null;
            try
            {
                self = (FFmpegMediaMuxer)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
                if (self._cancellation.IsCancellationRequested)
                {
                    return ffmpeg.AVERROR_EXIT;
                }
                if ((whence & ffmpeg.AVSEEK_SIZE) != 0)
                {
                    return self._output.Length;
                }
                whence &= ~ffmpeg.AVSEEK_FORCE;
                if (whence < 0 || whence > 2)
                {
                    return -22;
                }
                return self._output.Seek(offset, (SeekOrigin)whence);
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

        /// <summary>Checks cancellation, managed output errors, the native result, and deferred AVIO errors.</summary>
        /// <param name="result">The native status code.</param>
        /// <param name="operation">The diagnostic operation name.</param>
        /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
        /// <exception cref="IOException">Writing the output stream failed.</exception>
        /// <exception cref="InvalidOperationException">FFmpeg reported failure.</exception>
        private void CheckOutput(int result, string operation)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_ioError != null)
            {
                throw new IOException(operation + " failed while writing the output.", _ioError);
            }
            Check(result, operation);
            if (_io != null)
            {
                Check(_io->error, operation + " I/O");
            }
        }

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

        /// <summary>Releases all native resources without closing the caller's streams.</summary>
        /// <remarks>Repeated disposal has no effect. A failed or canceled operation leaves a partial output for the caller to handle.</remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            var videoPacket = _videoPacket;
            _videoPacket = null;
            if (videoPacket != null)
            {
                ffmpeg.av_packet_free(&videoPacket);
            }
            var audioPacket = _audioPacket;
            _audioPacket = null;
            if (audioPacket != null)
            {
                ffmpeg.av_packet_free(&audioPacket);
            }
            var audioFrame = _audioFrame;
            _audioFrame = null;
            if (audioFrame != null)
            {
                ffmpeg.av_frame_free(&audioFrame);
            }
            var audioCodec = _audioCodec;
            _audioCodec = null;
            if (audioCodec != null)
            {
                ffmpeg.avcodec_free_context(&audioCodec);
            }
            var input = _input;
            _input = null;
            if (input != null)
            {
                ffmpeg.avformat_close_input(&input);
            }
            if (_format != null)
            {
                _format->pb = null;
                ffmpeg.avformat_free_context(_format);
                _format = null;
                _videoStream = null;
                _audioStream = null;
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
        }
    }
}
