#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks encoder configuration, native output, timestamps, and resource ownership.</summary>
    internal static class EncodingChecks
    {
        /// <summary>Counts the assertions in the current validation run.</summary>
        private static int s_checks;

        /// <summary>Validates options without loading FFmpeg or calling Unity native code.</summary>
        /// <returns>The number of managed assertions that passed.</returns>
        public static int RunManaged()
        {
            s_checks = 0;
            TestOptions();
            return s_checks;
        }

        /// <summary>Encodes real fixtures and checks their decoded pixels and presentation timestamps.</summary>
        /// <param name="workDirectory">An isolated directory for generated test media.</param>
        /// <param name="expectUnavailable">Whether the supplied native build intentionally has no encoders.</param>
        /// <param name="requireHardware">Whether to additionally require actual H.264 hardware CBR and VBR encoding.</param>
        /// <returns>The number of native assertions that passed.</returns>
        public static int RunNative(string workDirectory, bool expectUnavailable, bool requireHardware)
        {
            s_checks = 0;
            var encoders = GetVideoEncoders();
            Console.WriteLine("Native video encoders: " + (encoders.Count == 0 ? "none" : string.Join(", ", encoders)));
            var directory = Path.Combine(Path.GetFullPath(workDirectory), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            if (expectUnavailable)
            {
                Check(encoders.Count == 0, "unavailable mode requires an encoder-free native build");
                TestMissingRecordingSupport(directory);
                return s_checks;
            }

            Check(encoders.Contains("mpeg4"), "recording build supplies the built-in MPEG4 software encoder");
            TestRoundTrip(directory, false);
            TestRoundTrip(directory, true);
            TestRoundTrip(directory, false, 78);
            TestHardwareFallback(directory);
            TestCancellationAndFailureCleanup(directory);
            TestSessionOwnershipAndDrain(directory);
            TestSessionCancellation(directory);
            TestConstantBitRateSupport(directory, encoders);
            if (requireHardware)
            {
                TestHardwareRoundTrip(directory, VideoRateControlMode.VBR);
                TestHardwareRoundTrip(directory, VideoRateControlMode.CBR);
            }
            return s_checks;
        }

        /// <summary>Requires all four external software encoders and checks both recording rate-control modes.</summary>
        /// <param name="workDirectory">The isolated parent directory for generated recordings.</param>
        /// <returns>The number of software configuration, ownership, and native roundtrip assertions.</returns>
        /// <exception cref="Exception">An encoder is missing or an actual recording fails validation.</exception>
        public static int RunSoftware(string workDirectory)
        {
            s_checks = 0;
            var names = new[] { "libx264", "libx265", "libaom-av1", "libvpx-vp9" };
            var formats = new[] { VideoEncodingFormat.H264, VideoEncodingFormat.HEVC, VideoEncodingFormat.AV1, VideoEncodingFormat.VP9 };
            var available = GetVideoEncoders();
            Console.WriteLine("Native video encoders: " + string.Join(", ", available));
            var directory = Path.Combine(Path.GetFullPath(workDirectory), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            for (var codec = 0; codec < names.Length; codec++)
            {
                Check(available.Contains(names[codec]), "full software profile requires " + names[codec]);
                foreach (var mode in new[] { VideoRateControlMode.CBR, VideoRateControlMode.VBR })
                {
                    TestSoftwareRoundTrip(directory, formats[codec], names[codec], mode, false);
                    TestSoftwareRoundTrip(directory, formats[codec], names[codec], mode, true);
                }
            }
            return s_checks;
        }

        /// <summary>Checks the actual software configuration, cross-thread guards, statistics, and decoded media.</summary>
        /// <param name="directory">The isolated directory for one unique output file.</param>
        /// <param name="format">The requested compressed video format.</param>
        /// <param name="name">The required software implementation name.</param>
        /// <param name="mode">The requested native rate-control algorithm.</param>
        /// <param name="complex">Whether the source includes a noise burst, flat recovery, and sustained motion.</param>
        /// <exception cref="Exception">Configuration, encoding, output ownership, or decoding is incorrect.</exception>
        private static unsafe void TestSoftwareRoundTrip(string directory, VideoEncodingFormat format, string name,
            VideoRateControlMode mode, bool complex)
        {
            var path = Path.Combine(directory, name + "-" + mode + "-" + (complex ? "complex" : "flat")
                + (format == VideoEncodingFormat.VP9 ? ".webm" : ".mp4"));
            var options = CreateOptions();
            options.Width = 128;
            options.Height = 64;
            options.MaximumSoftwareThreads = 2;
            options.Format = format;
            options.RateControlMode = mode;
            var pixels = CreatePixels(options.Width, options.Height);
            long bytes;
            long rate;
            using (var encoder = new FFmpegVideoEncoder(options))
            {
                options.Width = 256;
                options.MaximumSoftwareThreads = 8;
                options.Format = VideoEncodingFormat.MPEG4;
                options.RateControlMode = mode == VideoRateControlMode.CBR ? VideoRateControlMode.VBR : VideoRateControlMode.CBR;
                options.BitRate = 900_000;
                options.MaximumBitRate = 1_000_000;
                encoder.Open(path, CancellationToken.None);
                Check(encoder.EncoderName == name && encoder.EncoderType == VideoEncoderType.Software,
                    "software request selects the exact implementation without format substitution");
                Check(encoder.EncodingFormat == format && encoder.RateControlMode == mode && encoder.Width == 128 && encoder.Height == 64,
                    "later options edits preserve actual format, dimensions, and mode");
                var maximum = mode == VideoRateControlMode.CBR ? 200_000 : 400_000;
                Check(encoder.BitRate == 200_000 && encoder.MaximumBitRate == maximum,
                    "software diagnostics preserve the snapshotted target and effective maximum");
                Check(encoder.SoftwareThreadCount > 0 && encoder.SoftwareThreadCount <= 2,
                    "software codec workers respect the construction-time maximum");
                var context = (AVCodecContext*)Pointer.Unbox(typeof(FFmpegVideoEncoder)
                    .GetField("_codec", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(encoder)!);
                Check(context->bit_rate == 200_000 && context->rc_max_rate == maximum && context->rc_buffer_size == maximum
                    && context->rc_min_rate == (mode == VideoRateControlMode.CBR ? 200_000 : 0),
                    "opened native codec retains the requested rate-control inputs");
                Check((context->flags & (ffmpeg.AV_CODEC_FLAG_PASS1 | ffmpeg.AV_CODEC_FLAG_PASS2)) == 0,
                    "live software recording does not require a two-pass input");
                var rejected = Task.Run(() => Expect<InvalidOperationException>(() => encoder.Encode(pixels, 0, false),
                    "native encoder access from a different thread is rejected"));
                rejected.GetAwaiter().GetResult();
                using var cancelReader = new CancellationTokenSource();
                using var readerStarted = new ManualResetEventSlim();
                var reader = Task.Run(() =>
                {
                    long previousFrames = 0;
                    long previousBytes = 0;
                    var observations = 0;
                    while (!cancelReader.IsCancellationRequested)
                    {
                        var frames = encoder.EncodedFrames;
                        var written = encoder.BytesWritten;
                        if (frames < previousFrames || written < previousBytes || encoder.CurrentBitRate < 0
                            || encoder.EncoderName != name || encoder.EncoderType != VideoEncoderType.Software
                            || encoder.RateControlMode != mode || encoder.EncodingFormat != format)
                        {
                            throw new Exception("Concurrent encoder diagnostics changed identity or regressed.");
                        }
                        previousFrames = frames;
                        previousBytes = written;
                        observations++;
                        readerStarted.Set();
                        Thread.Sleep(1);
                    }
                    return observations;
                });
                try
                {
                    Check(readerStarted.Wait(TimeSpan.FromSeconds(5)), "concurrent diagnostics reader starts before encoding");
                    for (var index = 0; index < 60; index++)
                    {
                        if (complex)
                        {
                            AddComplexPattern(pixels, index);
                        }
                        encoder.Encode(pixels, index + (index >= 30 ? 1 : 0), false);
                    }
                    encoder.Complete();
                    encoder.Complete();
                }
                finally
                {
                    cancelReader.Cancel();
                }
                Check(reader.GetAwaiter().GetResult() > 0, "statistics remain readable concurrently with real native encoding");
                Check(encoder.EncodedFrames == 60 && encoder.BytesWritten > 0 && encoder.CurrentBitRate > 0,
                    "software flush retains all accepted frames and publishes real payload statistics");
                bytes = encoder.BytesWritten;
                rate = encoder.CurrentBitRate;
                if (name == "libx264" && mode == VideoRateControlMode.CBR && !complex)
                {
                    Check(Math.Abs(rate - 200_000) <= 40_000, "MP4 H264 CBR uses actual flat-scene filler");
                }
                if (!complex || (format != VideoEncodingFormat.VP9 && format != VideoEncodingFormat.AV1))
                {
                    Check(rate <= maximum + context->rc_buffer_size + 8192,
                        "fixture payload fits the rate plus configured VBV burst capacity and header allowance");
                }
                else
                {
                    Console.WriteLine("NOTE: " + name + " complex payload is measured against a native rate budget; a hard payload boundary is not asserted.");
                }
                Expect<InvalidOperationException>(() => encoder.Encode(pixels, 62, false), "completed software output refuses further input");
            }
            using (var decoder = new FFmpegVideoDecoder())
            {
                decoder.Open(path, CancellationToken.None);
                var expectedName = format == VideoEncodingFormat.H264 ? "h264" : format == VideoEncodingFormat.HEVC ? "hevc"
                    : format == VideoEncodingFormat.AV1 ? "av1" : "vp9";
                Check(decoder.CodecName == expectedName && decoder.Width == 128 && decoder.Height == 64,
                    "completed container identifies the actual requested software format");
                var count = 0;
                while (true)
                {
                    using var frame = decoder.ReadFrame();
                    if (frame == null)
                    {
                        break;
                    }
                    Check(count < 60, "software decoder does not emit extra delayed frames");
                    Check(Math.Abs(frame.PresentationTime - (count + (count >= 30 ? 1 : 0)) / 30.0) < 0.001,
                        "software PTS retains the deliberate source-frame gap");
                    CheckColor(frame, 8, false, "software decoded blue bottom");
                    CheckColor(frame, 56, true, "software decoded red top");
                    count++;
                }
                Check(count == 60 && decoder.ReadFrame() == null, "software decoder drains exactly sixty frames and stable EOF");
            }
            var original = File.ReadAllBytes(path);
            var originalOptions = CreateOptions();
            originalOptions.Format = format;
            originalOptions.RateControlMode = mode;
            originalOptions.Width = 128;
            originalOptions.Height = 64;
            using (var repeated = new FFmpegVideoEncoder(originalOptions))
            {
                Expect<IOException>(() => repeated.Open(path, CancellationToken.None), "software recording refuses an existing output file");
            }
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "failed software overwrite preserves every output byte");
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.Length > 0, "software native encoder and decoder release output handles");
            Console.WriteLine("Encoded " + name + " " + mode + " " + (complex ? "complex" : "flat")
                + ": frames=60; payload=" + bytes + "; lastWindow=" + rate + " bit/s; path=" + path);
        }

        /// <summary>Adds a noise burst, quiet recovery, and sustained motion while preserving orientation markers.</summary>
        /// <param name="pixels">The 128 by 64 packed RGBA source buffer.</param>
        /// <param name="index">The source frame number used as the deterministic seed.</param>
        private static void AddComplexPattern(byte[] pixels, int index)
        {
            var state = 0xA17F542Du ^ (uint)index;
            for (var y = 0; y < 64; y++)
            {
                for (var x = 64; x < 128; x++)
                {
                    var offset = (y * 128 + x) * 4;
                    if (index >= 15 && index < 30)
                    {
                        pixels[offset] = y < 32 ? (byte)255 : (byte)0;
                        pixels[offset + 1] = 0;
                        pixels[offset + 2] = y < 32 ? (byte)0 : (byte)255;
                    }
                    else
                    {
                        state = unchecked(state * 1664525u + 1013904223u);
                        pixels[offset] = (byte)(state >> 24);
                        pixels[offset + 1] = (byte)(state >> 16);
                        pixels[offset + 2] = (byte)(state >> 8);
                    }
                }
            }
        }

        /// <summary>Checks that an older AMF wrapper cannot advertise unchecked rate-control properties.</summary>
        /// <param name="workDirectory">The isolated directory for creation and overwrite checks.</param>
        /// <returns>The number of native rejection assertions that passed.</returns>
        public static unsafe int RunUncheckedAmf(string workDirectory)
        {
            s_checks = 0;
            var version = ffmpeg.av_version_info();
            Console.WriteLine("Unchecked AMF fixture version: " + version);
            Check(!version.Contains("MajdataPlay-AMF-RC-v1-", StringComparison.Ordinal),
                "negative AMF fixture must precede the checked rate-control patch");
            Check(GetVideoEncoders().Contains("h264_amf"), "negative fixture actually contains the old AMF H264 wrapper");
            var directory = Path.Combine(Path.GetFullPath(workDirectory), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            foreach (var mode in new[] { VideoRateControlMode.CBR, VideoRateControlMode.VBR })
            {
                var options = CreateOptions();
                options.Width = 256;
                options.Height = 128;
                options.Format = VideoEncodingFormat.H264;
                options.PreferredEncoderType = VideoEncoderType.Hardware;
                options.RateControlMode = mode;
                var path = Path.Combine(directory, "unchecked-" + mode + ".mp4");
                using var encoder = new FFmpegVideoEncoder(options);
                var error = Expect<NotSupportedException>(() => encoder.Open(path, CancellationToken.None),
                    "unchecked AMF H264 request must fail when no alternative H264 implementation is usable");
                Check(error.Message.Contains("AMF", StringComparison.OrdinalIgnoreCase)
                    && error.Message.Contains("MajdataPlay-AMF-RC-v1-", StringComparison.Ordinal),
                    "unchecked AMF diagnostic identifies the required native patch marker");
                Check(encoder.EncodingFormat == VideoEncodingFormat.H264 && encoder.EncoderName != "mpeg4"
                    && encoder.EncodedFrames == 0 && encoder.BytesWritten == 0,
                    "rejection retains the requested format and does not silently encode MPEG4");
                Check(!File.Exists(path), "unchecked AMF rejection creates no output file");
                var original = new byte[] { 9, 8, 7, 6 };
                File.WriteAllBytes(path, original);
                using var repeated = new FFmpegVideoEncoder(options);
                Expect<NotSupportedException>(() => repeated.Open(path, CancellationToken.None),
                    "unchecked AMF cannot overwrite an existing output");
                Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "unchecked rejection preserves original file bytes");
                Console.WriteLine("Rejected unchecked AMF " + mode + ": " + error.Message);
            }
            return s_checks;
        }

        /// <summary>Checks option boundaries and configuration snapshot ownership.</summary>
        private static void TestOptions()
        {
            var source = CreateOptions();
            var copy = source.ValidateAndClone();
            Check(!ReferenceEquals(source, copy), "validated options have independent ownership");
            source.Width = 128;
            Check(copy.Width == 64, "later configuration edits do not change a validated snapshot");
            ExpectInvalid(options => options.Width = 0, "zero width");
            ExpectInvalid(options => options.Width = 63, "odd width");
            ExpectInvalid(options => options.Width = 16386, "width allocation limit");
            ExpectInvalid(options => options.Height = -2, "negative height");
            ExpectInvalid(options => options.Height = 47, "odd height");
            ExpectInvalid(options => options.Height = 16386, "height allocation limit");
            ExpectInvalid(options => options.FrameRate = 0, "zero frame rate");
            ExpectInvalid(options => options.FrameRate = 241, "frame rate upper limit");
            ExpectInvalid(options => options.MaximumSoftwareThreads = 0, "automatic thread count is not a maximum");
            ExpectInvalid(options => options.MaximumSoftwareThreads = 129, "thread count upper limit");
            ExpectInvalid(options => options.BitRate = 999, "target bit rate lower limit");
            ExpectInvalid(options => options.MaximumBitRate = 999, "maximum bit rate lower limit");
            ExpectInvalid(options => options.MaximumBitRate = options.BitRate - 1, "maximum below target");
            ExpectInvalid(options => options.MaximumBitRate = (long)int.MaxValue + 1, "native VBV integer limit");
            ExpectInvalid(options => options.Format = (VideoEncodingFormat)999, "undefined encoding format");
            ExpectInvalid(options => options.PreferredEncoderType = (VideoEncoderType)999, "undefined encoder preference");
            ExpectInvalid(options => options.RateControlMode = (VideoRateControlMode)999, "undefined rate control mode");
            var constant = CreateOptions();
            constant.RateControlMode = VideoRateControlMode.CBR;
            Check(constant.ValidateAndClone().MaximumBitRate > constant.BitRate,
                "CBR accepts a maximum above its configured constant target");
        }

        /// <summary>Checks a single malformed configuration at the public encoder boundary.</summary>
        /// <param name="configure">The change that makes the baseline options invalid.</param>
        /// <param name="message">A description included if the assertion fails.</param>
        private static void ExpectInvalid(Action<EncoderOptions> configure, string message)
        {
            var options = CreateOptions();
            configure(options);
            Expect<ArgumentOutOfRangeException>(() =>
            {
                using var encoder = new FFmpegVideoEncoder(options);
            }, "invalid options rejected before native allocation: " + message);
        }

        /// <summary>Creates compact settings that are suitable for repeatable software encoding.</summary>
        /// <returns>A fresh 64 by 48 MPEG4 VBR configuration with one software thread.</returns>
        private static EncoderOptions CreateOptions()
        {
            return new EncoderOptions
            {
                Width = 64,
                Height = 48,
                FrameRate = 30,
                PreferredEncoderType = VideoEncoderType.Software,
                MaximumSoftwareThreads = 1,
                Format = VideoEncodingFormat.MPEG4,
                BitRate = 200_000,
                MaximumBitRate = 400_000,
                RateControlMode = VideoRateControlMode.VBR
            };
        }

        /// <summary>Lists the actual video encoders supplied by the loaded FFmpeg build.</summary>
        /// <returns>AVCodec names for all enabled native video encoders.</returns>
        private static unsafe List<string> GetVideoEncoders()
        {
            var encoders = new List<string>();
            void* opaque = null;
            while (true)
            {
                var codec = ffmpeg.av_codec_iterate(&opaque);
                if (codec == null)
                {
                    break;
                }
                if (codec->type == AVMediaType.AVMEDIA_TYPE_VIDEO && ffmpeg.av_codec_is_encoder(codec) != 0)
                {
                    encoders.Add(Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown");
                }
            }
            return encoders;
        }

        /// <summary>Checks pixels, optional vertical inversion, timestamp gaps, statistics, and drain completion.</summary>
        /// <param name="directory">The isolated output directory.</param>
        /// <param name="flipVertically">Whether input rows must be inverted before native encoding.</param>
        /// <param name="width">The fixture width, including widths that require SIMD row padding.</param>
        private static void TestRoundTrip(string directory, bool flipVertically, int width = 64)
        {
            var path = Path.Combine(directory, (flipVertically ? "flipped-" : "upright-") + width + ".mp4");
            var options = CreateOptions();
            options.Width = width;
            var pixels = CreatePixels(options.Width, options.Height);
            var frameIndices = new long[] { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12 };
            using (var encoder = new FFmpegVideoEncoder(options))
            {
                options.Width = 128;
                options.MaximumSoftwareThreads = 8;
                options.RateControlMode = VideoRateControlMode.CBR;
                encoder.Open(path, CancellationToken.None);
                Check(encoder.EncoderName == "mpeg4" && encoder.EncoderType == VideoEncoderType.Software,
                    "software preference reports the actual MPEG4 encoder");
                Check(encoder.RateControlMode == VideoRateControlMode.VBR,
                    "configuration edits do not relabel the active rate-control mode");
                Check(encoder.EncodingFormat == VideoEncodingFormat.MPEG4 && encoder.BitRate == 200_000
                    && encoder.MaximumBitRate == 400_000, "VBR reports its applied format, target, and maximum rate");
                Check(encoder.SoftwareThreadCount > 0 && encoder.SoftwareThreadCount <= 1,
                    "actual software thread count respects the construction snapshot");
                Expect<ArgumentException>(() => encoder.Encode(pixels.AsSpan(0, pixels.Length - 1), 0, flipVertically),
                    "truncated RGBA input is rejected");
                foreach (var frameIndex in frameIndices)
                {
                    encoder.Encode(pixels, frameIndex, flipVertically);
                }
                Expect<ArgumentOutOfRangeException>(() => encoder.Encode(pixels, frameIndices[frameIndices.Length - 1], flipVertically),
                    "duplicate presentation indices are rejected");
                encoder.Complete();
                encoder.Complete();
                Check(encoder.EncodedFrames == frameIndices.Length, "complete drains exactly the accepted input frames");
                Check(encoder.BytesWritten > 0 && encoder.CurrentBitRate > 0, "real packets populate bytes and current bit rate");
                Expect<InvalidOperationException>(() => encoder.Encode(pixels, 13, flipVertically),
                    "completed output refuses additional frames");
                Console.WriteLine("Encoded " + encoder.EncoderName + "; " + encoder.EncoderType + "; " +
                    encoder.RateControlMode + "; threads=" + encoder.SoftwareThreadCount + "; bit/s=" + encoder.CurrentBitRate);
            }
            using (var decoder = new FFmpegVideoDecoder())
            {
                decoder.Open(path, CancellationToken.None);
                Check(decoder.Width == width && decoder.Height == 48 && decoder.CodecName == "mpeg4",
                    "container retains the selected format and snapshotted dimensions");
                var count = 0;
                while (true)
                {
                    using var frame = decoder.ReadFrame();
                    if (frame == null)
                    {
                        break;
                    }
                    Check(count < frameIndices.Length, "decode never emits excess delayed frames");
                    Check(Math.Abs(frame.PresentationTime - frameIndices[count] / 30.0) < 0.001,
                        "encoded PTS preserves frame index gaps");
                    // Decoder output is bottom-up for Unity; test the encoder's native top/bottom convention accordingly.
                    CheckColor(frame, 8, flipVertically, "decoded bottom half");
                    CheckColor(frame, 40, !flipVertically, "decoded top half");
                    count++;
                }
                Check(count == frameIndices.Length, "trailer and drain retain every accepted frame");
                Check(decoder.ReadFrame() == null, "EOF remains stable after the encoder is drained");
            }
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.Length > 0, "encoder and decoder release the output file handle");
        }

        /// <summary>Builds an opaque red top half and blue bottom half in top-down RGBA order.</summary>
        /// <param name="width">The fixture width in pixels.</param>
        /// <param name="height">The fixture height in pixels.</param>
        /// <returns>The tightly packed RGBA fixture.</returns>
        private static byte[] CreatePixels(int width, int height)
        {
            var pixels = new byte[checked(width * height * 4)];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    pixels[offset] = y < height / 2 ? (byte)255 : (byte)0;
                    pixels[offset + 2] = y < height / 2 ? (byte)0 : (byte)255;
                    pixels[offset + 3] = 255;
                }
            }
            return pixels;
        }

        /// <summary>Checks a pixel well inside one half while allowing lossy YUV conversion.</summary>
        /// <param name="frame">The independently owned decoded RGBA frame.</param>
        /// <param name="row">The bottom-up presentation row to sample.</param>
        /// <param name="expectRed">Whether the sample should be red rather than blue.</param>
        /// <param name="message">The assertion context.</param>
        private static void CheckColor(DecodedVideoFrame frame, int row, bool expectRed, string message)
        {
            var offset = (row * frame.Width + 16) * 4;
            var red = Marshal.ReadByte(frame.Data, offset);
            var blue = Marshal.ReadByte(frame.Data, offset + 2);
            Check(expectRed ? red > 180 && blue < 60 : blue > 180 && red < 60, message);
        }

        /// <summary>Checks that a preference fallback reports the backend that actually opened.</summary>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestHardwareFallback(string directory)
        {
            var options = CreateOptions();
            options.PreferredEncoderType = VideoEncoderType.Hardware;
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(Path.Combine(directory, "fallback.mp4"), CancellationToken.None);
            Check(encoder.EncoderType == VideoEncoderType.Software && encoder.EncoderName == "mpeg4",
                "unavailable MPEG4 hardware preference selects the actual software encoder");
            Check(!string.IsNullOrWhiteSpace(encoder.HardwareFallbackReason), "hardware fallback remains observable");
            encoder.Encode(CreatePixels(options.Width, options.Height), 0, false);
            encoder.Complete();
        }

        /// <summary>Requires real H.264 hardware encoding and checks its output separately from software fallback.</summary>
        /// <param name="directory">The isolated output directory.</param>
        /// <param name="mode">The hardware rate-control algorithm to validate.</param>
        private static void TestHardwareRoundTrip(string directory, VideoRateControlMode mode)
        {
            var options = CreateOptions();
            options.Width = 256;
            options.Height = 128;
            options.Format = VideoEncodingFormat.H264;
            options.PreferredEncoderType = VideoEncoderType.Hardware;
            options.RateControlMode = mode;
            var path = Path.Combine(directory, "hardware-" + mode + ".mp4");
            var pixels = CreatePixels(options.Width, options.Height);
            using (var encoder = new FFmpegVideoEncoder(options))
            {
                encoder.Open(path, CancellationToken.None);
                Check(encoder.EncoderType == VideoEncoderType.Hardware,
                    "hardware verification requires an actual hardware encoder: " + encoder.HardwareFallbackReason);
                Check(encoder.RateControlMode == mode && !string.IsNullOrWhiteSpace(encoder.EncoderName),
                    "hardware session reports its actual encoder and selected rate control");
                for (var index = 0; index < 60; index++)
                {
                    encoder.Encode(pixels, index, false);
                }
                encoder.Complete();
                Check(encoder.EncodedFrames == 60 && encoder.CurrentBitRate > 0 && encoder.BytesWritten > 0,
                    "hardware encoder drains every frame and reports real packet statistics");
                Console.WriteLine("Hardware encoded " + encoder.EncoderName + "; " + mode + "; bit/s=" + encoder.CurrentBitRate);
            }
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            Check(decoder.CodecName == "h264" && decoder.Width == options.Width && decoder.Height == options.Height,
                "hardware output retains requested format and resolution");
            var count = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }
                Check(Math.Abs(frame.PresentationTime - count / 30.0) < 0.001, "hardware output has monotonic presentation timing");
                CheckColor(frame, 8, false, "hardware output bottom half");
                CheckColor(frame, frame.Height - 8, true, "hardware output top half");
                count++;
            }
            Check(count == 60, "actual hardware output decodes every submitted frame");
        }

        /// <summary>Checks canceled open, canceled encoding, failed I/O, and exclusive file ownership after disposal.</summary>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestCancellationAndFailureCleanup(string directory)
        {
            var canceledPath = Path.Combine(directory, "pre-canceled.mp4");
            using (var cancellation = new CancellationTokenSource())
            using (var encoder = new FFmpegVideoEncoder(CreateOptions()))
            {
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => encoder.Open(canceledPath, cancellation.Token),
                    "pre-canceled open stops before touching output");
            }
            Check(!File.Exists(canceledPath), "pre-canceled open creates no partial file");
            using (var encoder = new FFmpegVideoEncoder(CreateOptions()))
            {
                Expect<DirectoryNotFoundException>(() => encoder.Open(Path.Combine(directory, "missing-parent", "failure.mp4"), CancellationToken.None),
                    "missing output parent fails with the original local I/O error");
            }
            var existingPath = Path.Combine(directory, "existing.mp4");
            var originalBytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(existingPath, originalBytes);
            using (var encoder = new FFmpegVideoEncoder(CreateOptions()))
            {
                Expect<IOException>(() => encoder.Open(existingPath, CancellationToken.None),
                    "encoder never overwrites an existing output file");
            }
            Check(File.ReadAllBytes(existingPath).AsSpan().SequenceEqual(originalBytes),
                "failed creation preserves the complete original file");
            var interruptedPath = Path.Combine(directory, "interrupted.mp4");
            using (var cancellation = new CancellationTokenSource())
            using (var encoder = new FFmpegVideoEncoder(CreateOptions()))
            {
                encoder.Open(interruptedPath, cancellation.Token);
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => encoder.Encode(CreatePixels(64, 48), 0, false),
                    "active cancellation is observed before submitting another frame");
            }
            using var exclusive = File.Open(interruptedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.CanWrite, "canceled encoder releases its native I/O handle");
        }

        /// <summary>Checks a fixed capture pool, failed-readback reuse, readback ordering, and graceful stop.</summary>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestSessionOwnershipAndDrain(string directory)
        {
            var options = CreateOptions();
            var path = Path.Combine(directory, "session.mp4");
            var session = new VideoEncodeSession(path, options, 2, CancellationToken.None);
            try
            {
                WaitFor(session.Ready);
                var dropped = session.TryReserve(0, false);
                Check(dropped != null, "session provides its first fixed capture reservation");
                session.CompleteReadback(dropped!, false);
                var first = session.TryReserve(0, false);
                var second = session.TryReserve(2, false);
                Check(first != null && second != null && !ReferenceEquals(first, second),
                    "simultaneous capture reservations never alias");
                Check(ReferenceEquals(first, dropped), "failed GPU readback returns the same preallocated buffer");
                Check(session.TryReserve(3, false) == null, "reading reservations consume the fixed pool capacity");
                var pixels = CreatePixels(options.Width, options.Height);
                Array.Copy(pixels, first!.Pixels, pixels.Length);
                Array.Copy(pixels, second!.Pixels, pixels.Length);
                session.CompleteReadback(second, true);
                Check(session.TryReserve(3, false) == null, "queued pixels consume pool capacity alongside outstanding readback");
                session.Stop();
                Check(session.TryReserve(3, false) == null, "graceful stop immediately refuses new captures");
                Check(!session.Completion.Wait(TimeSpan.FromMilliseconds(100)),
                    "graceful stop waits for the earlier outstanding GPU reservation");
                Check(session.EncodedFrames == 0, "a later completed readback cannot overtake an earlier capture");
                session.CompleteReadback(first, true);
                WaitFor(session.Completion);
                Check(session.Finished && session.Error == null && session.EncodedFrames == 2,
                    "graceful stop drains queued and outstanding captures before releasing FFmpeg");
                Check(session.EncoderName == "mpeg4" && session.EncoderType == VideoEncoderType.Software
                    && session.RateControlMode == VideoRateControlMode.VBR && session.CurrentBitRate > 0,
                    "session publishes the actual initialized encoder and final packet statistics");
            }
            finally
            {
                if (!session.Finished)
                {
                    session.Fail(new InvalidOperationException("Validation cleanup after an interrupted session test."));
                    try
                    {
                        WaitFor(session.Completion);
                    }
                    catch (InvalidOperationException)
                    {
                        // The deliberate cleanup failure must still release worker-owned resources.
                    }
                }
            }
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            using (var first = decoder.ReadFrame())
            {
                Check(first != null && Math.Abs(first.PresentationTime) < 0.001, "session keeps the first capture timestamp");
            }
            using (var second = decoder.ReadFrame())
            {
                Check(second != null && Math.Abs(second.PresentationTime - 2.0 / 30) < 0.001,
                    "session keeps capture order and skipped-frame timestamp gap");
            }
            Check(decoder.ReadFrame() == null, "session stop writes a decodable complete trailer");
        }

        /// <summary>Checks cancellation releases native ownership without waiting for a GPU callback.</summary>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestSessionCancellation(string directory)
        {
            var path = Path.Combine(directory, "session-canceled.mp4");
            using var cancellation = new CancellationTokenSource();
            var session = new VideoEncodeSession(path, CreateOptions(), 1, cancellation.Token);
            try
            {
                WaitFor(session.Ready);
                var reading = session.TryReserve(0, false);
                Check(reading != null && session.TryReserve(1, false) == null,
                    "single-buffer session remains bounded while GPU readback is pending");
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => WaitFor(session.Completion),
                    "session cancellation completes even with an outstanding GPU reservation");
                Check(session.Finished && session.Error is OperationCanceledException,
                    "session publishes cancellation after releasing its worker resources");
                session.CompleteReadback(reading!, false);
                Check(session.TryReserve(1, false) == null, "late GPU callback cannot restart a canceled session");
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Check(exclusive.CanWrite, "session cancellation releases the output file handle");
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    WaitFor(session.Completion);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the expected worker completion state for this test.
                }
            }
        }

        /// <summary>Waits for a worker task while preserving its original exception type.</summary>
        /// <param name="task">The initialization or completion task to wait for.</param>
        /// <exception cref="TimeoutException">The worker does not complete within 20 seconds.</exception>
        /// <exception cref="OperationCanceledException">The recording task was canceled.</exception>
        private static void WaitFor(Task task)
        {
            task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }

        /// <summary>Checks real MPEG4 CBR filler and optional libx264 constant-rate selection.</summary>
        /// <param name="directory">The isolated output directory.</param>
        /// <param name="encoders">The actual native encoder inventory.</param>
        private static void TestConstantBitRateSupport(string directory, List<string> encoders)
        {
            var options = CreateOptions();
            options.RateControlMode = VideoRateControlMode.CBR;
            var mpeg4Path = Path.Combine(directory, "mpeg4-cbr.mp4");
            using (var encoder = new FFmpegVideoEncoder(options))
            {
                encoder.Open(mpeg4Path, CancellationToken.None);
                Check(encoder.EncoderName == "mpeg4" && encoder.EncoderType == VideoEncoderType.Software
                    && encoder.RateControlMode == VideoRateControlMode.CBR, "MPEG4 reports the actual software CBR session");
                Check(encoder.BitRate == options.BitRate && encoder.MaximumBitRate == options.BitRate,
                    "CBR reports its effective maximum as the constant target");
                var mpeg4Pixels = CreatePixels(options.Width, options.Height);
                for (var index = 0; index < 60; index++)
                {
                    encoder.Encode(mpeg4Pixels, index, false);
                }
                encoder.Complete();
                Check(encoder.EncodedFrames == 60 && encoder.BytesWritten > 0, "software CBR drains every accepted frame");
                Check(Math.Abs(encoder.CurrentBitRate - options.BitRate) < options.BitRate * 0.15,
                    "MPEG4 CBR adds real filler for a flat scene instead of reporting a VBR packet rate");
                Console.WriteLine("Software CBR encoded " + encoder.EncoderName + "; bit/s=" + encoder.CurrentBitRate);
            }
            using (var decoder = new FFmpegVideoDecoder())
            {
                decoder.Open(mpeg4Path, CancellationToken.None);
                var count = 0;
                while (true)
                {
                    using var frame = decoder.ReadFrame();
                    if (frame == null)
                    {
                        break;
                    }
                    Check(Math.Abs(frame.PresentationTime - count / 30.0) < 0.001, "software CBR keeps frame presentation timing");
                    CheckColor(frame, 8, false, "software CBR bottom half");
                    CheckColor(frame, 40, true, "software CBR top half");
                    count++;
                }
                Check(count == 60, "software CBR output decodes all frames including padded packets");
            }
            options.Format = VideoEncodingFormat.H264;
            using var h264 = new FFmpegVideoEncoder(options);
            var path = Path.Combine(directory, "h264-cbr.mkv");
            if (!encoders.Contains("libx264"))
            {
                Expect<NotSupportedException>(() => h264.Open(path, CancellationToken.None),
                    "missing software H264 CBR support is rejected explicitly");
                return;
            }
            h264.Open(path, CancellationToken.None);
            Check(h264.EncoderType == VideoEncoderType.Software && h264.RateControlMode == VideoRateControlMode.CBR,
                "actual H264 software session reports constant bit rate");
            var pixels = CreatePixels(64, 48);
            for (var index = 0; index < 60; index++)
            {
                h264.Encode(pixels, index, false);
            }
            h264.Complete();
            Check(h264.CurrentBitRate > 0 && h264.EncodedFrames == 60, "constant-rate encoder drains real video packets");
        }

        /// <summary>Checks old playback-only builds fail clearly when recording encoders or muxers are unavailable.</summary>
        /// <param name="directory">The isolated output directory.</param>
        private static void TestMissingRecordingSupport(string directory)
        {
            foreach (var preference in new[] { VideoEncoderType.Software, VideoEncoderType.Hardware })
            {
                var options = CreateOptions();
                options.PreferredEncoderType = preference;
                using var encoder = new FFmpegVideoEncoder(options);
                var path = Path.Combine(directory, preference + "-unavailable.mp4");
                var error = Expect<NotSupportedException>(() => encoder.Open(path, CancellationToken.None),
                    "playback-only native build rejects missing recording support: " + preference);
                Check(error.Message.Contains("Rebuild", StringComparison.OrdinalIgnoreCase),
                    "missing native recording support explains how to obtain compatible libraries");
                Check(encoder.EncodedFrames == 0 && encoder.BytesWritten == 0,
                    "failed encoder selection reports no encoded output");
            }
        }

        /// <summary>Checks that an operation fails with the documented exception type.</summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="operation">The operation under test.</param>
        /// <param name="message">The assertion context.</param>
        /// <returns>The exception raised by the operation.</returns>
        /// <exception cref="InvalidOperationException">The operation does not raise the expected exception type.</exception>
        private static TException Expect<TException>(Action operation, string message) where TException : Exception
        {
            try
            {
                operation();
            }
            catch (TException error)
            {
                Check(true, message);
                return error;
            }
            throw new InvalidOperationException("Encoding: " + message);
        }

        /// <summary>Records an assertion and reports its encoder-specific context on failure.</summary>
        /// <param name="condition">Whether the expected behavior occurred.</param>
        /// <param name="message">The assertion context.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_checks++;
            if (!condition)
            {
                throw new InvalidOperationException("Encoding: " + message);
            }
        }
    }
}
