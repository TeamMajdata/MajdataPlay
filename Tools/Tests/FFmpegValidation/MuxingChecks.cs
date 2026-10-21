#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks that captured video and float PCM are muxed into playable MP4 and Matroska outputs.</summary>
    internal static class MuxingChecks
    {
        /// <summary>Sets the fixture audio sample rate.</summary>
        private const int SampleRate = 48000;
        /// <summary>Sets the fixture audio channel count.</summary>
        private const int Channels = 2;
        /// <summary>Sets the fixture video frame rate.</summary>
        private const int FrameRate = 30;
        /// <summary>Sets the number of fixture video frames, two seconds at the fixture frame rate.</summary>
        private const int VideoFrames = 60;
        /// <summary>Sets the number of fixture audio frames, two seconds at the fixture sample rate.</summary>
        private const int AudioFrames = SampleRate * 2;
        /// <summary>Sets the audio delay used by the leading-silence checks, in milliseconds.</summary>
        private const int AudioDelayMilliseconds = 250;
        /// <summary>Sets the decoded magnitude above which the fixture sine counts as audible.</summary>
        private const float AudibleThreshold = 0.05f;
        /// <summary>Sets the allowed onset error, which covers AAC priming and pre-echo of up to one encoder frame.</summary>
        private const double OnsetToleranceSeconds = 0.025;
        /// <summary>Counts the assertions in the current validation run.</summary>
        private static int s_checks;

        /// <summary>Validates PCM conversion and argument checks without loading FFmpeg.</summary>
        /// <returns>The number of managed assertions that passed.</returns>
        public static int RunManaged()
        {
            s_checks = 0;
            TestPcm16Conversion();
            TestArguments();
            return s_checks;
        }

        /// <summary>Muxes a real encoded video with generated audio and inspects the written containers.</summary>
        /// <param name="workDirectory">An isolated parent directory for generated media.</param>
        /// <returns>The number of native assertions that passed.</returns>
        public static int RunNative(string workDirectory)
        {
            s_checks = 0;
            var directory = Path.Combine(Path.GetFullPath(workDirectory), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var videoPath = Path.Combine(directory, "capture.mkv");
            EncodeVideo(videoPath);
            var pcm = CreateSineBytes(AudioFrames);
            var hasAac = HasAacEncoder();
            Console.WriteLine("AAC encoder available: " + hasAac);

            var expected = hasAac ? "aac" : "pcm_s16le";
            var matroska = Path.Combine(directory, "muxed.mkv");
            var codec = MuxToFile(videoPath, pcm, matroska, MediaContainerFormat.Matroska);
            Check(codec == expected, "Matroska audio uses AAC when available and PCM otherwise, got " + codec);
            CheckOutput(matroska, codec!, pcm);

            var mp4 = Path.Combine(directory, "muxed.mp4");
            if (hasAac)
            {
                codec = MuxToFile(videoPath, pcm, mp4, MediaContainerFormat.MP4);
                Check(codec == "aac", "MP4 audio uses AAC");
                CheckOutput(mp4, codec!, pcm);
            }
            else
            {
                TestMp4WithoutAac(videoPath, pcm, mp4);
            }

            TestAudioDelay(videoPath, pcm, directory, hasAac);
            TestVideoOnly(videoPath, Path.Combine(directory, "video-only.mkv"));
            TestPartialTrailingFrame(videoPath, pcm, Path.Combine(directory, "partial.mkv"), hasAac);
            TestFailures(videoPath, pcm, directory);
            return s_checks;
        }

        /// <summary>Checks rounding, clamping, and NaN handling of float to 16-bit conversion.</summary>
        private static void TestPcm16Conversion()
        {
            var source = new[] { 0f, 1f, -1f, 2f, -2f, 0.5f, float.NaN, 1f / 32767f };
            var target = new short[source.Length];
            FFmpegMediaMuxer.ConvertToPcm16(source, target);
            Check(target[0] == 0 && target[1] == short.MaxValue && target[2] == -short.MaxValue,
                "nominal float samples map to the symmetric 16-bit range");
            Check(target[3] == short.MaxValue && target[4] == short.MinValue, "out-of-range samples clamp");
            Check(target[5] == 16384 && target[6] == 0 && target[7] == 1, "conversion rounds and maps NaN to silence");
        }

        /// <summary>Checks argument validation that runs before any native call.</summary>
        private static void TestArguments()
        {
            using var pcm = new MemoryStream();
            using var output = new MemoryStream();
            Expect<ArgumentException>(() => FFmpegMediaMuxer.Mux(" ", pcm, SampleRate, Channels, output, MediaContainerFormat.MP4),
                "an empty video path is rejected");
            Expect<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, 7999, Channels, output, MediaContainerFormat.MP4),
                "a sample rate below 8000 Hz is rejected");
            Expect<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, SampleRate, 9, output, MediaContainerFormat.MP4),
                "more than eight channels are rejected");
            Expect<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, SampleRate, Channels, output, (MediaContainerFormat)7),
                "an undefined container is rejected");
            using var readOnly = new MemoryStream(new byte[1], false);
            Expect<ArgumentException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, SampleRate, Channels, readOnly, MediaContainerFormat.MP4),
                "a non-writable output is rejected");
            Expect<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, SampleRate, Channels, output, MediaContainerFormat.MP4,
                TimeSpan.FromTicks(-1)), "a negative audio delay is rejected");
            Expect<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux("a.mkv", pcm, SampleRate, Channels, output, MediaContainerFormat.MP4,
                TimeSpan.FromSeconds(60) + TimeSpan.FromTicks(1)), "an audio delay over one minute is rejected");
        }

        /// <summary>Accepts either PCM in MP4 or an explicit request to rebuild with AAC when the libraries lack AAC.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        /// <param name="path">The output file.</param>
        private static void TestMp4WithoutAac(string videoPath, byte[] pcm, string path)
        {
            try
            {
                var codec = MuxToFile(videoPath, pcm, path, MediaContainerFormat.MP4);
                Check(codec == "pcm_s16le", "MP4 without AAC stores uncompressed PCM");
                CheckOutput(path, codec!, pcm);
                Console.WriteLine("MP4 without AAC: PCM audio accepted by the mp4 muxer.");
            }
            catch (NotSupportedException error)
            {
                Check(error.Message.Contains("aac"), "MP4 rejection names the missing AAC encoder");
                Console.WriteLine("MP4 without AAC: rejected (" + error.Message + ")");
            }
        }

        /// <summary>Checks that an audio delay becomes leading silence while both streams keep the video timeline origin.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        /// <param name="directory">The isolated output directory.</param>
        /// <param name="hasAac">Whether audio is encoded as AAC; without it only Matroska is guaranteed to accept the audio.</param>
        private static void TestAudioDelay(string videoPath, byte[] pcm, string directory, bool hasAac)
        {
            var delay = TimeSpan.FromMilliseconds(AudioDelayMilliseconds);
            var containers = hasAac
                ? new[] { MediaContainerFormat.Matroska, MediaContainerFormat.MP4 }
                : new[] { MediaContainerFormat.Matroska };
            foreach (var container in containers)
            {
                var extension = container == MediaContainerFormat.MP4 ? ".mp4" : ".mkv";
                var undelayedPath = Path.Combine(directory, "delay-0ms" + extension);
                var delayedPath = Path.Combine(directory, "delay-" + AudioDelayMilliseconds + "ms" + extension);
                MuxToFile(videoPath, pcm, undelayedPath, container);
                var codec = MuxToFile(videoPath, pcm, delayedPath, container, delay);

                var probe = Probe(delayedPath);
                var video = probe.Streams.Find(stream => stream.Type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    ?? throw new Exception(delayedPath + " has no video stream");
                var audio = probe.Streams.Find(stream => stream.Type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    ?? throw new Exception(delayedPath + " has no audio stream");
                Check(Math.Abs(audio.StartSeconds) < 0.05 && Math.Abs(video.StartSeconds) < 0.05,
                    container + " delayed audio still starts at the video timeline origin");
                Check(Math.Abs(audio.EndSeconds - (2.0 + delay.TotalSeconds)) < 0.1 && Math.Abs(video.EndSeconds - 2.0) < 0.1,
                    container + " delayed audio is extended by the delay while video keeps its length");
                if (codec == "pcm_s16le")
                {
                    CheckDelayedPcmPayload(delayedPath, pcm);
                }

                // Onsets are measured against the video start so that a container-wide timestamp shift cancels out.
                var undelayedOnset = FindAudioOnsetSeconds(undelayedPath) - video.StartSeconds;
                var delayedOnset = FindAudioOnsetSeconds(delayedPath) - video.StartSeconds;
                Console.WriteLine(container + " " + codec + " audio onset: " + (undelayedOnset * 1000).ToString("0.00") + " ms without delay, "
                    + (delayedOnset * 1000).ToString("0.00") + " ms with " + AudioDelayMilliseconds + " ms delay.");
                Check(Math.Abs(undelayedOnset) < OnsetToleranceSeconds, container + " undelayed audio is audible at the origin, got " + undelayedOnset);
                Check(Math.Abs(delayedOnset - delay.TotalSeconds) < OnsetToleranceSeconds,
                    container + " delayed audio is first audible after the delay, got " + delayedOnset);
            }
        }

        /// <summary>Checks that delayed PCM output begins with exact silence followed by the unchanged input samples.</summary>
        /// <param name="path">The muxed file with uncompressed 16-bit audio.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        private static void CheckDelayedPcmPayload(string path, byte[] pcm)
        {
            var silentBytes = SampleRate * AudioDelayMilliseconds / 1000 * Channels * sizeof(short);
            var payload = ReadAudioPayload(path);
            Check(payload.Length == silentBytes + (AudioFrames * Channels * sizeof(short)), "delayed PCM adds exactly the silent frames");
            Check(payload.AsSpan(0, silentBytes).IndexOfAnyExcept((byte)0) < 0, "delayed PCM begins with exact silence");
            var expected = new short[16];
            FFmpegMediaMuxer.ConvertToPcm16(MemoryMarshal.Cast<byte, float>(pcm.AsSpan(0, expected.Length * sizeof(float))), expected);
            Check(payload.AsSpan(silentBytes, expected.Length * sizeof(short)).SequenceEqual(MemoryMarshal.AsBytes(expected.AsSpan())),
                "delayed PCM continues with the unchanged input samples");
        }

        /// <summary>Checks that empty PCM produces a playable video-only file.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="path">The output file.</param>
        private static void TestVideoOnly(string videoPath, string path)
        {
            var codec = MuxToFile(videoPath, Array.Empty<byte>(), path, MediaContainerFormat.Matroska);
            Check(codec == null, "empty PCM reports no audio codec");
            var probe = Probe(path);
            Check(probe.Streams.Count == 1 && probe.Streams[0].Type == AVMediaType.AVMEDIA_TYPE_VIDEO, "empty PCM writes only the video stream");
            Check(probe.Streams[0].Packets == VideoFrames, "video-only output keeps every video packet");
        }

        /// <summary>Checks that a trailing partial frame is discarded rather than corrupting channel order.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        /// <param name="path">The output file.</param>
        /// <param name="hasAac">Whether audio is encoded as AAC, which hides raw byte counts.</param>
        private static void TestPartialTrailingFrame(string videoPath, byte[] pcm, string path, bool hasAac)
        {
            var truncated = pcm.AsSpan(0, pcm.Length - sizeof(float)).ToArray();
            MuxToFile(videoPath, truncated, path, MediaContainerFormat.Matroska);
            var audio = Probe(path).Streams.Find(stream => stream.Type == AVMediaType.AVMEDIA_TYPE_AUDIO);
            Check(audio != null, "truncated PCM still produces an audio stream");
            if (!hasAac)
            {
                Check(audio!.Bytes == (AudioFrames - 1) * Channels * sizeof(short), "the trailing partial frame is discarded");
            }
        }

        /// <summary>Checks cancellation, a missing video, and a non-seekable output.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestFailures(string videoPath, byte[] pcm, string directory)
        {
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                using var output = new MemoryStream();
                Expect<OperationCanceledException>(() => FFmpegMediaMuxer.Mux(videoPath, new MemoryStream(pcm), SampleRate, Channels,
                    output, MediaContainerFormat.Matroska, cancellationToken: canceled.Token), "a canceled token stops muxing");
            }
            using (var output = new MemoryStream())
            {
                Expect<InvalidOperationException>(() => FFmpegMediaMuxer.Mux(Path.Combine(directory, "missing.mkv"), new MemoryStream(pcm),
                    SampleRate, Channels, output, MediaContainerFormat.Matroska), "a missing video file is reported");
            }
            using (var output = new NonSeekableStream())
            {
                Expect<ArgumentException>(() => FFmpegMediaMuxer.Mux(videoPath, new MemoryStream(pcm), SampleRate, Channels,
                    output, MediaContainerFormat.Matroska), "a non-seekable output is rejected");
            }
        }

        /// <summary>Checks stream layout, packet counts, audio payload, timeline origin, and interleaving of a muxed file.</summary>
        /// <param name="path">The muxed file.</param>
        /// <param name="audioCodec">The expected audio codec name.</param>
        /// <param name="pcm">The fixture audio bytes.</param>
        private static void CheckOutput(string path, string audioCodec, byte[] pcm)
        {
            var probe = Probe(path);
            Check(probe.Streams.Count == 2, path + " contains one video and one audio stream");
            var video = probe.Streams.Find(stream => stream.Type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                ?? throw new Exception(path + " has no video stream");
            var audio = probe.Streams.Find(stream => stream.Type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                ?? throw new Exception(path + " has no audio stream");
            Check(video.Codec == "mpeg4" && video.Packets == VideoFrames, "video packets are copied without re-encoding");
            Check(audio.Codec == audioCodec && audio.SampleRate == SampleRate && audio.Channels == Channels,
                "audio stream keeps the codec, sample rate, and channel count");
            Check(Math.Abs(audio.StartSeconds) < 0.05 && Math.Abs(video.StartSeconds) < 0.05,
                "audio and video share the timeline origin");
            Check(Math.Abs(audio.EndSeconds - 2.0) < 0.1 && Math.Abs(video.EndSeconds - 2.0) < 0.1,
                "both streams last the two-second fixture");
            Check(probe.MaximumInterleaveGapSeconds < 1.0, "packets are interleaved by time, gap " + probe.MaximumInterleaveGapSeconds);
            if (audioCodec == "pcm_s16le")
            {
                Check(audio.Bytes == AudioFrames * Channels * sizeof(short), "PCM keeps every captured sample");
                var expected = new short[16];
                FFmpegMediaMuxer.ConvertToPcm16(MemoryMarshal.Cast<byte, float>(pcm.AsSpan(0, expected.Length * sizeof(float))), expected);
                Check(audio.FirstPayload.AsSpan(0, expected.Length * sizeof(short)).SequenceEqual(MemoryMarshal.AsBytes(expected.AsSpan())),
                    "PCM payload preserves sample values and channel order");
            }
            else
            {
                Check(audio.Packets >= AudioFrames / 1024, "AAC writes at least one packet per encoder frame");
            }
            Console.WriteLine("Muxed " + Path.GetFileName(path) + ": video " + video.Packets + " packets, audio " + audio.Codec + " "
                + audio.Packets + " packets, " + audio.EndSeconds.ToString("0.000") + " s.");
        }

        /// <summary>Muxes byte PCM into a new local file.</summary>
        /// <param name="videoPath">The captured video.</param>
        /// <param name="pcm">The interleaved float PCM bytes.</param>
        /// <param name="path">The output file.</param>
        /// <param name="container">The output container.</param>
        /// <param name="audioDelay">The time on the video timeline at which the first PCM frame plays.</param>
        /// <returns>The audio codec name reported by the muxer.</returns>
        private static string? MuxToFile(string videoPath, byte[] pcm, string path, MediaContainerFormat container, TimeSpan audioDelay = default)
        {
            using var input = new MemoryStream(pcm, false);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
            return FFmpegMediaMuxer.Mux(videoPath, input, SampleRate, Channels, output, container, audioDelay);
        }

        /// <summary>Decodes the audio stream, honoring container edit lists, and finds the first audible sample of the left channel.</summary>
        /// <param name="path">The muxed file.</param>
        /// <returns>The presentation time of the first sample whose magnitude exceeds <see cref="AudibleThreshold"/>, in seconds.</returns>
        private static unsafe double FindAudioOnsetSeconds(string path)
        {
            AVFormatContext* input = null;
            Check(ffmpeg.avformat_open_input(&input, path, null, null) >= 0, "muxed output opens for decoding: " + path);
            AVCodecContext* decoder = null;
            var packet = ffmpeg.av_packet_alloc();
            var frame = ffmpeg.av_frame_alloc();
            try
            {
                Check(ffmpeg.avformat_find_stream_info(input, null) >= 0, "muxed output stream information is readable for decoding");
                var index = ffmpeg.av_find_best_stream(input, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
                Check(index >= 0, path + " has a decodable audio stream");
                var stream = input->streams[index];
                var codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
                Check(codec != null, "the libraries decode " + ffmpeg.avcodec_get_name(stream->codecpar->codec_id));
                decoder = ffmpeg.avcodec_alloc_context3(codec);
                Check(ffmpeg.avcodec_parameters_to_context(decoder, stream->codecpar) >= 0, "audio decoder parameters are accepted");
                // The packet time base lets the decoder shift timestamps when it discards edit-list priming samples.
                decoder->pkt_timebase = stream->time_base;
                Check(ffmpeg.avcodec_open2(decoder, codec, null) >= 0, "the audio decoder opens");
                var timeBase = ffmpeg.av_q2d(stream->time_base);
                while (ffmpeg.av_read_frame(input, packet) >= 0)
                {
                    try
                    {
                        if (packet->stream_index == index)
                        {
                            Check(ffmpeg.avcodec_send_packet(decoder, packet) >= 0, "audio packets decode");
                            if (TryReceiveOnset(decoder, frame, timeBase, out var onset))
                            {
                                return onset;
                            }
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(packet);
                    }
                }
                Check(ffmpeg.avcodec_send_packet(decoder, null) >= 0, "the audio decoder drains");
                if (TryReceiveOnset(decoder, frame, timeBase, out var drainedOnset))
                {
                    return drainedOnset;
                }
                throw new Exception(path + " contains no audible audio");
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avcodec_free_context(&decoder);
                ffmpeg.avformat_close_input(&input);
            }
        }

        /// <summary>Receives every available decoded frame and searches it for the first audible left-channel sample.</summary>
        /// <param name="decoder">The open audio decoder.</param>
        /// <param name="frame">The reusable decoded frame.</param>
        /// <param name="timeBase">The audio stream time base in seconds.</param>
        /// <param name="onset">Receives the presentation time of the first audible sample, in seconds.</param>
        /// <returns>True when an audible sample was found.</returns>
        private static unsafe bool TryReceiveOnset(AVCodecContext* decoder, AVFrame* frame, double timeBase, out double onset)
        {
            while (ffmpeg.avcodec_receive_frame(decoder, frame) >= 0)
            {
                try
                {
                    var timestamp = frame->pts != ffmpeg.AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                    for (var i = 0; i < frame->nb_samples; i++)
                    {
                        if (Math.Abs(ReadLeftSample(frame, i)) > AudibleThreshold)
                        {
                            onset = (timestamp * timeBase) + (i / (double)frame->sample_rate);
                            return true;
                        }
                    }
                }
                finally
                {
                    ffmpeg.av_frame_unref(frame);
                }
            }
            onset = 0;
            return false;
        }

        /// <summary>Reads one left-channel sample of a decoded frame as a nominal float.</summary>
        /// <param name="frame">The decoded audio frame.</param>
        /// <param name="index">The sample index within the frame.</param>
        /// <returns>The sample value, where full scale is one.</returns>
        private static unsafe float ReadLeftSample(AVFrame* frame, int index)
        {
            var channels = frame->ch_layout.nb_channels;
            return (AVSampleFormat)frame->format switch
            {
                AVSampleFormat.AV_SAMPLE_FMT_FLTP => ((float*)frame->data[0])[index],
                AVSampleFormat.AV_SAMPLE_FMT_FLT => ((float*)frame->data[0])[index * channels],
                AVSampleFormat.AV_SAMPLE_FMT_S16P => ((short*)frame->data[0])[index] / 32768f,
                AVSampleFormat.AV_SAMPLE_FMT_S16 => ((short*)frame->data[0])[index * channels] / 32768f,
                _ => throw new Exception("unexpected decoded sample format " + ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format))
            };
        }

        /// <summary>Concatenates the payloads of every audio packet in file order.</summary>
        /// <param name="path">The muxed file.</param>
        /// <returns>The raw audio payload bytes.</returns>
        private static unsafe byte[] ReadAudioPayload(string path)
        {
            AVFormatContext* input = null;
            Check(ffmpeg.avformat_open_input(&input, path, null, null) >= 0, "muxed output opens for payload reading: " + path);
            var packet = ffmpeg.av_packet_alloc();
            try
            {
                Check(ffmpeg.avformat_find_stream_info(input, null) >= 0, "muxed output stream information is readable for payload reading");
                var index = ffmpeg.av_find_best_stream(input, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
                Check(index >= 0, path + " has an audio stream");
                using var payload = new MemoryStream();
                while (ffmpeg.av_read_frame(input, packet) >= 0)
                {
                    if (packet->stream_index == index)
                    {
                        payload.Write(new ReadOnlySpan<byte>(packet->data, packet->size));
                    }
                    ffmpeg.av_packet_unref(packet);
                }
                return payload.ToArray();
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avformat_close_input(&input);
            }
        }

        /// <summary>Encodes a two-second MPEG4 fixture into a Matroska file like the camera capturer's temporary output.</summary>
        /// <param name="path">The output file.</param>
        private static void EncodeVideo(string path)
        {
            var options = new EncoderOptions
            {
                Width = 64,
                Height = 48,
                FrameRate = FrameRate,
                PreferredEncoderType = VideoEncoderType.Software,
                MaximumSoftwareThreads = 1,
                Format = VideoEncodingFormat.MPEG4,
                BitRate = 200_000,
                MaximumBitRate = 400_000,
                RateControlMode = VideoRateControlMode.VBR
            };
            var pixels = new byte[options.Width * options.Height * 4];
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(path, CancellationToken.None);
            for (var frame = 0; frame < VideoFrames; frame++)
            {
                pixels.AsSpan().Fill((byte)(frame * 4));
                encoder.Encode(pixels, frame, false);
            }
            encoder.Complete();
        }

        /// <summary>Creates two seconds of a stereo 440 Hz and 660 Hz sine pair as float bytes.</summary>
        /// <param name="frames">The number of frames.</param>
        /// <returns>Interleaved little-endian float PCM bytes.</returns>
        private static byte[] CreateSineBytes(int frames)
        {
            var samples = new float[frames * Channels];
            for (var frame = 0; frame < frames; frame++)
            {
                var time = frame / (double)SampleRate;
                samples[frame * Channels] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * time));
                samples[frame * Channels + 1] = (float)(0.25 * Math.Sin(2 * Math.PI * 660 * time));
            }
            return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        }

        /// <summary>Reports whether the loaded libraries contain an AAC encoder.</summary>
        /// <returns>True when an AAC encoder is available.</returns>
        private static unsafe bool HasAacEncoder()
        {
            return ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC) != null;
        }

        /// <summary>Reads every packet of a media file and summarizes its streams.</summary>
        /// <param name="path">The media file.</param>
        /// <returns>The stream summaries and the largest time jump between consecutive packets.</returns>
        private static unsafe ProbeResult Probe(string path)
        {
            AVFormatContext* input = null;
            Check(ffmpeg.avformat_open_input(&input, path, null, null) >= 0, "muxed output opens: " + path);
            var packet = ffmpeg.av_packet_alloc();
            try
            {
                Check(ffmpeg.avformat_find_stream_info(input, null) >= 0, "muxed output stream information is readable");
                var result = new ProbeResult();
                for (var i = 0; i < input->nb_streams; i++)
                {
                    var parameters = input->streams[i]->codecpar;
                    result.Streams.Add(new StreamSummary(parameters->codec_type, ffmpeg.avcodec_get_name(parameters->codec_id),
                        parameters->sample_rate, parameters->ch_layout.nb_channels));
                }
                var previousTime = double.NaN;
                while (ffmpeg.av_read_frame(input, packet) >= 0)
                {
                    var stream = input->streams[packet->stream_index];
                    var summary = result.Streams[packet->stream_index];
                    var timeBase = ffmpeg.av_q2d(stream->time_base);
                    var time = (packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts) * timeBase;
                    if (summary.Packets == 0)
                    {
                        summary.StartSeconds = time;
                        summary.FirstPayload = new ReadOnlySpan<byte>(packet->data, packet->size).ToArray();
                    }
                    summary.Packets++;
                    summary.Bytes += packet->size;
                    summary.EndSeconds = Math.Max(summary.EndSeconds, time + packet->duration * timeBase);
                    if (!double.IsNaN(previousTime))
                    {
                        result.MaximumInterleaveGapSeconds = Math.Max(result.MaximumInterleaveGapSeconds, Math.Abs(time - previousTime));
                    }
                    previousTime = time;
                    ffmpeg.av_packet_unref(packet);
                }
                return result;
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.avformat_close_input(&input);
            }
        }

        /// <summary>Throws when a validation condition fails.</summary>
        /// <param name="condition">The observed condition.</param>
        /// <param name="message">The failure description.</param>
        private static void Check(bool condition, string message)
        {
            s_checks++;
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        /// <summary>Requires an operation to fail with a specific exception type.</summary>
        /// <typeparam name="TException">The expected exception type, including derived types.</typeparam>
        /// <param name="operation">The operation to run.</param>
        /// <param name="message">The expectation description.</param>
        private static void Expect<TException>(Action operation, string message) where TException : Exception
        {
            try
            {
                operation();
            }
            catch (TException)
            {
                Check(true, message);
                return;
            }
            catch (Exception error)
            {
                throw new Exception(message + "; got " + error.GetType().Name + ": " + error.Message, error);
            }
            throw new Exception(message + "; no exception was thrown");
        }

        /// <summary>Describes the packets of one stream in a probed file.</summary>
        private sealed class StreamSummary
        {
            /// <summary>Creates a summary for one stream.</summary>
            /// <param name="type">The media type.</param>
            /// <param name="codec">The codec name.</param>
            /// <param name="sampleRate">The audio sample rate, or zero for video.</param>
            /// <param name="channels">The audio channel count, or zero for video.</param>
            public StreamSummary(AVMediaType type, string codec, int sampleRate, int channels)
            {
                Type = type;
                Codec = codec;
                SampleRate = sampleRate;
                Channels = channels;
            }

            /// <summary>Gets the media type.</summary>
            public AVMediaType Type { get; }
            /// <summary>Gets the codec name.</summary>
            public string Codec { get; }
            /// <summary>Gets the audio sample rate.</summary>
            public int SampleRate { get; }
            /// <summary>Gets the audio channel count.</summary>
            public int Channels { get; }
            /// <summary>Gets or sets the packet count.</summary>
            public int Packets { get; set; }
            /// <summary>Gets or sets the total payload bytes.</summary>
            public long Bytes { get; set; }
            /// <summary>Gets or sets the presentation time of the first packet in seconds.</summary>
            public double StartSeconds { get; set; }
            /// <summary>Gets or sets the latest packet end in seconds.</summary>
            public double EndSeconds { get; set; }
            /// <summary>Gets or sets the payload of the first packet.</summary>
            public byte[] FirstPayload { get; set; } = Array.Empty<byte>();
        }

        /// <summary>Holds the summaries of a probed file.</summary>
        private sealed class ProbeResult
        {
            /// <summary>Gets the stream summaries in stream order.</summary>
            public List<StreamSummary> Streams { get; } = new List<StreamSummary>();
            /// <summary>Gets or sets the largest time jump between consecutive packets in file order.</summary>
            public double MaximumInterleaveGapSeconds { get; set; }
        }

        /// <summary>Wraps a writable memory stream that refuses seeking.</summary>
        private sealed class NonSeekableStream : MemoryStream
        {
            /// <summary>Gets false so the muxer must reject this output.</summary>
            public override bool CanSeek => false;
        }
    }
}
