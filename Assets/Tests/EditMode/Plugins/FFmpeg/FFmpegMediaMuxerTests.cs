using System;
using System.IO;
using System.Threading;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies audio and video muxing, PCM conversion and argument validation of the media muxer.</summary>
    public class FFmpegMediaMuxerTests
    {
        /// <summary>Owns the fixture media.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores a one-second MPEG-4 clip used as the video input.</summary>
        private string _video = "";

        /// <summary>Encodes the shared video input.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
            _video = FFmpegTestMedia.CreateVideo(_directory, "video.mkv");
        }

        /// <summary>Deletes the fixture media.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Creates interleaved float PCM bytes containing a quiet sine wave.</summary>
        /// <param name="frames">The number of sample frames.</param>
        /// <param name="channels">The channel count.</param>
        /// <param name="extraBytes">Additional trailing bytes forming an incomplete frame.</param>
        /// <returns>A readable PCM stream.</returns>
        private static MemoryStream Pcm(int frames, int channels, int extraBytes = 0)
        {
            var bytes = new byte[(frames * channels * 4) + extraBytes];
            for (var i = 0; i < frames * channels; i++)
            {
                BitConverter.GetBytes((float)(0.25 * Math.Sin(i * 0.05))).CopyTo(bytes, i * 4);
            }

            return new MemoryStream(bytes);
        }

        /// <summary>Writes muxed bytes to a file and opens it with the decoder.</summary>
        /// <param name="output">The muxed output.</param>
        /// <param name="name">The file name.</param>
        /// <param name="hasAudio">Receives whether the file contains an audio stream.</param>
        /// <returns>The number of decoded video frames.</returns>
        private int Inspect(MemoryStream output, string name, out bool hasAudio)
        {
            var file = _directory.Directory.CreateFile(name);
            file.WriteAllBytes(output.ToArray());
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(file.Location, CancellationToken.None);
            hasAudio = decoder.HasAudio;
            var count = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    return count;
                }

                count++;
            }
        }

        /// <summary>Checks float to 16-bit PCM conversion, rounding and clamping.</summary>
        [Test]
        public void ConvertToPcm16_RoundsAndClamps()
        {
            var source = new[] { 0f, 1f, -1f, 2f, -2f, float.NaN, 0.5f, float.PositiveInfinity, float.NegativeInfinity, -0.5f };
            var destination = new short[source.Length];

            FFmpegMediaMuxer.ConvertToPcm16(source, destination);

            Assert.That(destination, Is.EqualTo(new short[] { 0, 32767, -32767, 32767, -32768, 0, 16384, 32767, -32768, -16384 }));
        }

        /// <summary>Checks null argument validation and the reported parameter names.</summary>
        [Test]
        public void Mux_RejectsNullArguments()
        {
            using var pcm = new MemoryStream();
            using var output = new MemoryStream();

            Assert.That(Assert.Throws<ArgumentNullException>(() => FFmpegMediaMuxer.Mux(null!, pcm, 48000, 2, output, MediaContainerFormat.MP4))!.ParamName, Is.EqualTo("videoPath"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => FFmpegMediaMuxer.Mux(_video, null!, 48000, 2, output, MediaContainerFormat.MP4))!.ParamName, Is.EqualTo("pcm"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, null!, MediaContainerFormat.MP4))!.ParamName, Is.EqualTo("output"));
        }

        /// <summary>Checks validation of the remaining scalar and stream arguments.</summary>
        [Test]
        public void Mux_RejectsInvalidArguments()
        {
            using var pcm = new MemoryStream();
            using var output = new MemoryStream();
            using var readOnly = new MemoryStream(new byte[4], false);
            using var nonSeekable = new NonSeekableStream();

            Assert.Throws<ArgumentException>(() => FFmpegMediaMuxer.Mux(" ", pcm, 48000, 2, output, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, readOnly, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, nonSeekable, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 7999, 2, output, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 384001, 2, output, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 0, output, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 9, output, MediaContainerFormat.MP4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, (MediaContainerFormat)7));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, MediaContainerFormat.MP4, TimeSpan.FromMilliseconds(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, MediaContainerFormat.MP4, TimeSpan.FromSeconds(61)));
            Assert.That(output.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that empty PCM produces a video-only file in both containers.</summary>
        /// <param name="container">The output container.</param>
        [TestCase(MediaContainerFormat.MP4)]
        [TestCase(MediaContainerFormat.Matroska)]
        public void Mux_EmptyPcm_WritesVideoOnly(MediaContainerFormat container)
        {
            using var pcm = new MemoryStream();
            using var output = new MemoryStream();

            var codec = FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, container);

            Assert.That(codec, Is.Null);
            Assert.That(Inspect(output, "video-only-" + container + (container == MediaContainerFormat.MP4 ? ".mp4" : ".mkv"), out var hasAudio), Is.EqualTo(30));
            Assert.That(hasAudio, Is.False);
        }

        /// <summary>Checks AAC muxing with different channel layouts, sample rates, delays and a trailing partial frame.</summary>
        /// <param name="container">The output container.</param>
        /// <param name="sampleRate">The PCM sample rate.</param>
        /// <param name="channels">The PCM channel count.</param>
        /// <param name="delayMilliseconds">The audio delay.</param>
        [TestCase(MediaContainerFormat.MP4, 48000, 2, 0)]
        [TestCase(MediaContainerFormat.Matroska, 44100, 1, 250)]
        [TestCase(MediaContainerFormat.MP4, 8000, 6, 2000)]
        public void Mux_WithPcm_EncodesAac(MediaContainerFormat container, int sampleRate, int channels, int delayMilliseconds)
        {
            using var pcm = Pcm(sampleRate / 2, channels, 3);
            using var output = new MemoryStream();

            var codec = FFmpegMediaMuxer.Mux(_video, pcm, sampleRate, channels, output, container, TimeSpan.FromMilliseconds(delayMilliseconds));

            Assert.That(codec, Is.EqualTo("aac"));
            Assert.That(Inspect(output, "aac-" + sampleRate + (container == MediaContainerFormat.MP4 ? ".mp4" : ".mkv"), out var hasAudio), Is.EqualTo(30));
            Assert.That(hasAudio, Is.True);
        }

        /// <summary>Checks that a sample rate rejected by AAC falls back to 16-bit PCM in Matroska.</summary>
        [Test]
        public void Mux_UnsupportedAacRate_FallsBackToPcmInMatroska()
        {
            using var pcm = Pcm(3000, 2);
            using var output = new MemoryStream();

            var codec = FFmpegMediaMuxer.Mux(_video, pcm, 384000, 2, output, MediaContainerFormat.Matroska, TimeSpan.FromMilliseconds(5));

            Assert.That(codec, Is.EqualTo("pcm_s16le"));
            Assert.That(Inspect(output, "pcm.mkv", out var hasAudio), Is.EqualTo(30));
            Assert.That(hasAudio, Is.True);
        }

        /// <summary>Checks that MP4 uses PCM only when the loaded MP4 muxer can store it and otherwise rejects the audio.</summary>
        [Test]
        public unsafe void Mux_UnsupportedAacRate_FollowsMp4PcmSupport()
        {
            using var pcm = Pcm(3000, 2);
            using var output = new MemoryStream();
            var mp4 = global::FFmpeg.AutoGen.ffmpeg.av_guess_format("mp4", null, null);
            var storesPcm = global::FFmpeg.AutoGen.ffmpeg.avformat_query_codec(mp4, global::FFmpeg.AutoGen.AVCodecID.AV_CODEC_ID_PCM_S16LE,
                global::FFmpeg.AutoGen.ffmpeg.FF_COMPLIANCE_NORMAL) != 0;

            if (storesPcm)
            {
                Assert.That(FFmpegMediaMuxer.Mux(_video, pcm, 384000, 2, output, MediaContainerFormat.MP4), Is.EqualTo("pcm_s16le"));
            }
            else
            {
                var error = Assert.Throws<NotSupportedException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 384000, 2, output, MediaContainerFormat.MP4));
                Assert.That(error!.Message, Does.Contain("cannot store PCM"));
            }
        }

        /// <summary>Checks that a non-seekable PCM input still produces an audio stream.</summary>
        [Test]
        public void Mux_NonSeekablePcm_IsAccepted()
        {
            using var pcm = new NonSeekableStream(Pcm(4800, 2).ToArray());
            using var output = new MemoryStream();

            Assert.That(FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, MediaContainerFormat.Matroska), Is.EqualTo("aac"));
        }

        /// <summary>Checks that a missing video input is reported.</summary>
        [Test]
        public void Mux_MissingVideo_Throws()
        {
            using var pcm = new MemoryStream();
            using var output = new MemoryStream();

            Assert.Throws<InvalidOperationException>(() => FFmpegMediaMuxer.Mux(FFmpegTestMedia.PathIn(_directory, "missing.mkv"), pcm, 48000, 2, output, MediaContainerFormat.MP4));
        }

        /// <summary>Checks that cancellation is observed before muxing.</summary>
        [Test]
        public void Mux_Canceled_Throws()
        {
            using var pcm = Pcm(100, 2);
            using var output = new MemoryStream();
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, MediaContainerFormat.Matroska, default, source.Token));
        }

        /// <summary>Checks that output stream failures surface as I/O errors.</summary>
        [Test]
        public void Mux_OutputFailure_IsReportedAsIOException()
        {
            using var pcm = Pcm(4800, 2);
            using var output = new FailingStream();

            var error = Assert.Throws<IOException>(() => FFmpegMediaMuxer.Mux(_video, pcm, 48000, 2, output, MediaContainerFormat.Matroska));

            Assert.That(error!.InnerException, Is.InstanceOf<IOException>());
        }

        /// <summary>A memory stream that reports itself as non-seekable.</summary>
        private sealed class NonSeekableStream : MemoryStream
        {
            /// <summary>Creates an empty writable stream.</summary>
            public NonSeekableStream()
            {
            }

            /// <summary>Creates a readable stream over existing bytes.</summary>
            /// <param name="bytes">The stream contents.</param>
            public NonSeekableStream(byte[] bytes) : base(bytes)
            {
            }

            /// <summary>Gets false so callers treat the stream as sequential.</summary>
            public override bool CanSeek => false;
        }

        /// <summary>A writable, seekable stream whose writes always fail.</summary>
        private sealed class FailingStream : MemoryStream
        {
            /// <summary>Always fails the write.</summary>
            /// <param name="buffer">The unused source buffer.</param>
            /// <param name="offset">The unused offset.</param>
            /// <param name="count">The unused count.</param>
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new IOException("disk full");
            }

            /// <summary>Always fails the write.</summary>
            /// <param name="buffer">The unused source span.</param>
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                throw new IOException("disk full");
            }
        }
    }
}
