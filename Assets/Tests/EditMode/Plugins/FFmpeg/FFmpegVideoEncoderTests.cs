using System;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Collections.Generic;
#endif
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies RGBA video encoding, encoder selection and lifetime rules with the real FFmpeg encoders.</summary>
    public class FFmpegVideoEncoderTests
    {
        /// <summary>The test frame width.</summary>
        private const int Width = 64;
        /// <summary>The test frame height.</summary>
        private const int Height = 48;
        /// <summary>Owns the output files of one test.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Creates a temporary output directory.</summary>
        [SetUp]
        public void SetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary output directory.</summary>
        [TearDown]
        public void TearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Creates encoder options for a small test recording.</summary>
        /// <param name="format">The requested format.</param>
        /// <param name="mode">The rate control mode.</param>
        /// <returns>Valid options.</returns>
        private static EncoderOptions Options(VideoEncodingFormat format = VideoEncodingFormat.MPEG4, VideoRateControlMode mode = VideoRateControlMode.VBR)
        {
            return new EncoderOptions
            {
                Width = Width,
                Height = Height,
                FrameRate = 30,
                Format = format,
                RateControlMode = mode,
                MaximumSoftwareThreads = 2,
                BitRate = 300000,
                MaximumBitRate = 600000
            };
        }

        /// <summary>Decodes a file and returns the frame count and codec name.</summary>
        /// <param name="path">The encoded file.</param>
        /// <param name="codec">Receives the codec name.</param>
        /// <returns>The number of decoded frames.</returns>
        private static int CountFrames(string path, out string? codec)
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            codec = decoder.CodecName;
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

        /// <summary>Checks constructor validation and the configuration reported before opening.</summary>
        [Test]
        public void Constructor_ValidatesAndSnapshotsOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new FFmpegVideoEncoder(null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegVideoEncoder(new EncoderOptions { Width = 3 }));
            var options = Options(VideoEncodingFormat.H264, VideoRateControlMode.CBR);
            using var encoder = new FFmpegVideoEncoder(options);
            options.Width = 1000;

            Assert.That(encoder.Width, Is.EqualTo(Width));
            Assert.That(encoder.Height, Is.EqualTo(Height));
            Assert.That(encoder.FrameRate, Is.EqualTo(30));
            Assert.That(encoder.EncodingFormat, Is.EqualTo(VideoEncodingFormat.H264));
            Assert.That(encoder.RateControlMode, Is.EqualTo(VideoRateControlMode.CBR));
            Assert.That(encoder.BitRate, Is.EqualTo(300000));
            Assert.That(encoder.MaximumBitRate, Is.EqualTo(300000));
            Assert.That(encoder.EncoderName, Is.Null);
            Assert.That(encoder.SoftwareThreadCount, Is.EqualTo(0));
            Assert.That(encoder.SoftwareThreadingMode, Is.EqualTo("none"));
            Assert.That(encoder.ConverterThreadCount, Is.EqualTo(0));
            Assert.That(encoder.EncodedFrames, Is.EqualTo(0));
            Assert.That(encoder.BytesWritten, Is.EqualTo(0));
            Assert.That(encoder.CurrentBitRate, Is.EqualTo(0));
        }

        /// <summary>Checks that VBR reports its separate maximum bitrate.</summary>
        [Test]
        public void MaximumBitRate_Vbr_UsesConfiguredMaximum()
        {
            using var encoder = new FFmpegVideoEncoder(Options());

            Assert.That(encoder.MaximumBitRate, Is.EqualTo(600000));
        }

        /// <summary>Checks path validation of Open.</summary>
        /// <param name="path">The invalid output location.</param>
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://example.invalid/out.mkv")]
        public void Open_RejectsNonLocalPaths(string path)
        {
            using var encoder = new FFmpegVideoEncoder(Options());

            Assert.Throws<ArgumentException>(() => encoder.Open(path, CancellationToken.None));
        }

        /// <summary>Checks that a canceled token prevents opening.</summary>
        [Test]
        public void Open_CanceledToken_Throws()
        {
            using var encoder = new FFmpegVideoEncoder(Options());
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => encoder.Open(FFmpegTestMedia.PathIn(_directory, "canceled.mkv"), source.Token));
        }

        /// <summary>Checks that an unknown extension is rejected and disposes the encoder.</summary>
        [Test]
        public void Open_UnknownExtension_IsNotSupported()
        {
            var encoder = new FFmpegVideoEncoder(Options());

            Assert.Throws<NotSupportedException>(() => encoder.Open(FFmpegTestMedia.PathIn(_directory, "out.unknown-extension"), CancellationToken.None));
            Assert.Throws<ObjectDisposedException>(() => encoder.Open(FFmpegTestMedia.PathIn(_directory, "out.mkv"), CancellationToken.None));
        }

        /// <summary>Checks that a container which cannot store the requested format is rejected without substitution.</summary>
        [Test]
        public void Open_ContainerWithoutFormat_IsNotSupported()
        {
            using var encoder = new FFmpegVideoEncoder(Options());

            var error = Assert.Throws<NotSupportedException>(() => encoder.Open(FFmpegTestMedia.PathIn(_directory, "out.wav"), CancellationToken.None));

            Assert.That(error!.Message, Does.Contain("cannot store"));
        }

        /// <summary>Checks that an existing output file is never overwritten.</summary>
        [Test]
        public void Open_ExistingFile_IsNotOverwritten()
        {
            var existing = _directory.WriteText("existing.mkv", "keep");
            using var encoder = new FFmpegVideoEncoder(Options());

            Assert.Throws<IOException>(() => encoder.Open(existing.Location, CancellationToken.None));
            Assert.That(existing.ReadAllText(), Is.EqualTo("keep"));
        }

        /// <summary>Checks Encode and Complete lifetime and argument validation.</summary>
        [Test]
        public void EncodeAndComplete_ValidateState()
        {
            using var encoder = new FFmpegVideoEncoder(Options());
            var frame = FFmpegTestMedia.CreateFrame(Width, Height, 0);

            Assert.Throws<InvalidOperationException>(() => encoder.Encode(frame, 0, false));
            Assert.Throws<InvalidOperationException>(() => encoder.Complete());

            encoder.Open(FFmpegTestMedia.PathIn(_directory, "state.mkv"), CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() => encoder.Open(FFmpegTestMedia.PathIn(_directory, "again.mkv"), CancellationToken.None));
            Assert.Throws<ArgumentException>(() => encoder.Encode(new byte[10], 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.Encode(frame, -1, false));
            encoder.Encode(frame, 3, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.Encode(frame, 3, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => encoder.Encode(frame, 2, false));

            encoder.Complete();
            encoder.Complete();

            Assert.Throws<InvalidOperationException>(() => encoder.Encode(frame, 4, false));
            Assert.That(encoder.EncodedFrames, Is.EqualTo(1));
        }

        /// <summary>Checks that NV12 input needs an NV12 encoder, which creates no converter threads and produces a decodable file.</summary>
        [Test]
        public void EncodeNv12_ValidatesLayoutAndEncodes()
        {
            var nv12 = new byte[Width * Height * 3 / 2];
            Array.Fill(nv12, (byte)128);
            using (var rgba = new FFmpegVideoEncoder(Options()))
            {
                rgba.Open(FFmpegTestMedia.PathIn(_directory, "rgba.mkv"), CancellationToken.None);
                Assert.Throws<InvalidOperationException>(() => rgba.EncodeNv12(nv12, 0));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegVideoEncoder(Options(), (CapturePixelLayout)99));
            var path = FFmpegTestMedia.PathIn(_directory, "nv12.mkv");
            using (var encoder = new FFmpegVideoEncoder(Options(), CapturePixelLayout.Nv12))
            {
                Assert.Throws<InvalidOperationException>(() => encoder.EncodeNv12(nv12, 0));
                encoder.Open(path, CancellationToken.None);
                Assert.That(encoder.ConverterThreadCount, Is.EqualTo(0));
                Assert.Throws<InvalidOperationException>(() => encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, 0), 0, false));
                Assert.Throws<ArgumentException>(() => encoder.EncodeNv12(new byte[10], 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => encoder.EncodeNv12(nv12, -1));
                for (var i = 0; i < 4; i++)
                {
                    encoder.EncodeNv12(nv12, i);
                }

                Assert.Throws<ArgumentOutOfRangeException>(() => encoder.EncodeNv12(nv12, 3));
                encoder.Complete();
                Assert.That(encoder.EncodedFrames, Is.EqualTo(4));
            }

            Assert.That(CountFrames(path, out var codec), Is.EqualTo(4));
            Assert.That(codec, Is.EqualTo("mpeg4"));
        }

        /// <summary>Checks that the encoder rejects use from another thread.</summary>
        [Test]
        public void Encode_FromAnotherThread_IsRejected()
        {
            using var encoder = new FFmpegVideoEncoder(Options());
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "thread.mkv"), CancellationToken.None);
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, 0), 0, false);
                }
                catch (Exception caught)
                {
                    error = caught;
                }
            });
            thread.Start();
            thread.Join();

            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
        }

        /// <summary>Checks that cancellation after opening stops encoding and leaves a partial file.</summary>
        [Test]
        public void Encode_AfterCancellation_Throws()
        {
            using var source = new CancellationTokenSource();
            var path = FFmpegTestMedia.PathIn(_directory, "cancel.mkv");
            var encoder = new FFmpegVideoEncoder(Options());
            encoder.Open(path, source.Token);
            encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, 0), 0, false);
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, 1), 1, false));
            Assert.Throws<OperationCanceledException>(() => encoder.Complete());
            encoder.Dispose();
            encoder.Dispose();

            Assert.That(_directory.Directory.FindFile("cancel.mkv"), Is.Not.Null);
        }

        /// <summary>Checks every software encoder with both rate control modes, including decoding the result.</summary>
        /// <param name="format">The requested format.</param>
        /// <param name="mode">The rate control mode.</param>
        /// <param name="extension">The output container extension.</param>
        /// <param name="encoderName">The expected FFmpeg encoder.</param>
        /// <param name="codecName">The expected decoded codec name.</param>
        [TestCase(VideoEncodingFormat.MPEG4, VideoRateControlMode.CBR, "mkv", "mpeg4", "mpeg4")]
        [TestCase(VideoEncodingFormat.MPEG4, VideoRateControlMode.VBR, "mp4", "mpeg4", "mpeg4")]
        [TestCase(VideoEncodingFormat.H264, VideoRateControlMode.CBR, "mp4", "libx264", "h264")]
        [TestCase(VideoEncodingFormat.H264, VideoRateControlMode.VBR, "mkv", "libx264", "h264")]
        [TestCase(VideoEncodingFormat.HEVC, VideoRateControlMode.CBR, "mkv", "libx265", "hevc")]
        [TestCase(VideoEncodingFormat.HEVC, VideoRateControlMode.VBR, "mp4", "libx265", "hevc")]
        [TestCase(VideoEncodingFormat.VP9, VideoRateControlMode.CBR, "webm", "libvpx-vp9", "vp9")]
        [TestCase(VideoEncodingFormat.VP9, VideoRateControlMode.VBR, "mkv", "libvpx-vp9", "vp9")]
        [TestCase(VideoEncodingFormat.AV1, VideoRateControlMode.CBR, "mkv", "libaom-av1", "av1")]
        [TestCase(VideoEncodingFormat.AV1, VideoRateControlMode.VBR, "mp4", "libaom-av1", "av1")]
        public void SoftwareEncoders_ProduceDecodableFiles(VideoEncodingFormat format, VideoRateControlMode mode,
            string extension, string encoderName, string codecName)
        {
            var path = FFmpegTestMedia.PathIn(_directory, "out." + extension);
            using (var encoder = new FFmpegVideoEncoder(Options(format, mode)))
            {
                encoder.Open(path, CancellationToken.None);

                Assert.That(encoder.EncoderName, Is.EqualTo(encoderName));
                Assert.That(encoder.EncoderType, Is.EqualTo(VideoEncoderType.Software));
                Assert.That(encoder.HardwareFallbackReason, Is.Null);
                Assert.That(encoder.IsMaximumBitRateEnforced, Is.True);
                Assert.That(encoder.SoftwareThreadCount, Is.InRange(1, 2));
                Assert.That(new[] { "frame", "slice", "codec", "none" }, Does.Contain(encoder.SoftwareThreadingMode));
                Assert.That(encoder.ConverterThreadCount, Is.GreaterThanOrEqualTo(1));
                for (var i = 0; i < 20; i++)
                {
                    encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, i), i, (i & 1) == 0);
                }

                encoder.Complete();

                Assert.That(encoder.EncodedFrames, Is.EqualTo(20));
                Assert.That(encoder.BytesWritten, Is.GreaterThan(0));
                Assert.That(encoder.CurrentBitRate, Is.GreaterThan(0));
            }

            Assert.That(CountFrames(path, out var codec), Is.EqualTo(20));
            Assert.That(codec, Is.EqualTo(codecName));
        }

        /// <summary>Checks that one software worker reports single-threaded encoding.</summary>
        [Test]
        public void SoftwareEncoder_SingleWorker_ReportsNoThreading()
        {
            var options = Options();
            options.MaximumSoftwareThreads = 1;
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "single.mkv"), CancellationToken.None);

            Assert.That(encoder.SoftwareThreadCount, Is.EqualTo(1));
            Assert.That(encoder.SoftwareThreadingMode, Is.EqualTo("none"));
            Assert.That(encoder.ConverterThreadCount, Is.EqualTo(1));
        }

        /// <summary>Checks that skipped timestamps are preserved in the output timeline.</summary>
        [Test]
        public void Encode_TimestampGaps_ArePreserved()
        {
            var path = FFmpegTestMedia.PathIn(_directory, "gaps.mkv");
            using (var encoder = new FFmpegVideoEncoder(Options()))
            {
                encoder.Open(path, CancellationToken.None);
                foreach (var index in new long[] { 0, 3, 9 })
                {
                    encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, (int)index), index, false);
                }

                encoder.Complete();
            }

            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            var times = new System.Collections.Generic.List<double>();
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }

                times.Add(frame.PresentationTime);
            }

            Assert.That(times, Is.EqualTo(new[] { 0, 0.1, 0.3 }).Within(0.002));
        }

        /// <summary>Checks that vertical flipping changes which input row becomes the top of the video.</summary>
        /// <param name="flip">Whether the input rows are bottom-first.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Encode_FlipVertically_ReversesRows(bool flip)
        {
            var path = FFmpegTestMedia.PathIn(_directory, "flip.mkv");
            var pixels = new byte[Width * Height * 4];
            for (var y = 0; y < Height / 2; y++)
            {
                for (var x = 0; x < Width * 4; x++)
                {
                    pixels[(y * Width * 4) + x] = 255;
                }
            }

            using (var encoder = new FFmpegVideoEncoder(Options()))
            {
                encoder.Open(path, CancellationToken.None);
                encoder.Encode(pixels, 0, flip);
                encoder.Complete();
            }

            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            using var decoded = decoder.ReadFrame();
            var topRow = Height - 1;
            var top = Marshal.ReadByte(decoded!.Data, topRow * Width * 4);
            var bottom = Marshal.ReadByte(decoded.Data, 0);

            if (flip)
            {
                Assert.That(top, Is.LessThan(40));
                Assert.That(bottom, Is.GreaterThan(215));
            }
            else
            {
                Assert.That(top, Is.GreaterThan(215));
                Assert.That(bottom, Is.LessThan(40));
            }
        }

        /// <summary>Checks that hardware preference for MPEG-4 always falls back to the same-format software encoder.</summary>
        [Test]
        public void HardwarePreference_Mpeg4_FallsBackToSoftware()
        {
            var options = Options();
            options.PreferredEncoderType = VideoEncoderType.Hardware;
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "hardware.mkv"), CancellationToken.None);

            Assert.That(encoder.EncoderName, Is.EqualTo("mpeg4"));
            Assert.That(encoder.EncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain("No supported MPEG4 hardware encoder"));
        }

        /// <summary>Checks that hardware preference for H.264 uses a verified hardware encoder or reports the software fallback.</summary>
        [Test]
        public void HardwarePreference_H264_UsesHardwareOrReportsFallback()
        {
            var options = Options(VideoEncodingFormat.H264, VideoRateControlMode.CBR);
            options.PreferredEncoderType = VideoEncoderType.Hardware;
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "hardware.mp4"), CancellationToken.None);

            if (encoder.EncoderType == VideoEncoderType.Hardware)
            {
                Assert.That(encoder.EncoderName, Does.EndWith("_nvenc").Or.EndWith("_amf"));
                Assert.That(encoder.SoftwareThreadCount, Is.EqualTo(0));
            }
            else
            {
                Assert.That(encoder.EncoderName, Is.EqualTo("libx264"));
                Assert.That(encoder.HardwareFallbackReason, Does.Contain("h264_nvenc"));
                Assert.That(encoder.HardwareFallbackReason, Does.Contain("MediaFoundation"));
            }

            encoder.Encode(FFmpegTestMedia.CreateFrame(Width, Height, 0), 0, false);
            encoder.Complete();
            Assert.That(encoder.EncodedFrames, Is.EqualTo(1));
            Assert.That(encoder.EncoderDevice, Is.Not.Null.And.Not.Empty);
        }

        /// <summary>Checks that software preference reports the software device and ignores GPUs.</summary>
        [Test]
        public void SoftwarePreference_ReportsSoftwareDevice()
        {
            var options = Options();
            options.HardwareGpus = new[] { new HardwareGpu("0", "Unused GPU", HardwareGpu.NvidiaVendorId, 0x2504) };
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "software.mkv"), CancellationToken.None);

            Assert.That(encoder.EncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(encoder.EncoderDevice, Is.EqualTo("Software"));
            Assert.That(encoder.HardwareFallbackReason, Is.Null);
            Assert.That(encoder.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.OtherGpus));
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>The width of hardware encoder tests, above common NVENC and AMF minimums.</summary>
        private const int HardwareWidth = 320;
        /// <summary>The height of hardware encoder tests.</summary>
        private const int HardwareHeight = 240;
        /// <summary>The PCI vendor ID of VMware virtual GPUs, a vendor without a checked hardware encoder.</summary>
        private const int VMwareVendorId = 0x15AD;

        /// <summary>Creates options for an H.264 hardware recording on a supplied GPU list.</summary>
        /// <param name="gpus">The ordered GPUs, Unity's renderer first.</param>
        /// <param name="route">The fallback route.</param>
        /// <returns>Valid options.</returns>
        private static EncoderOptions HardwareOptions(IReadOnlyList<HardwareGpu> gpus, HardwareFallbackRoute route)
        {
            var options = Options(VideoEncodingFormat.H264);
            options.Width = HardwareWidth;
            options.Height = HardwareHeight;
            options.PreferredEncoderType = VideoEncoderType.Hardware;
            options.HardwareFallbackRoute = route;
            options.HardwareGpus = gpus;
            return options;
        }

        /// <summary>Gets the first DXGI adapter with a checked hardware encoder vendor, or ignores the test.</summary>
        /// <returns>The NVIDIA or AMD adapter, listed as another GPU.</returns>
        private static HardwareGpu EncoderAdapter()
        {
            foreach (var adapter in DxgiAdapterQuery.GetAdapters(false))
            {
                if (adapter.VendorId == HardwareGpu.NvidiaVendorId || adapter.VendorId == HardwareGpu.AmdVendorId)
                {
                    return adapter.WithRole(HardwareGpuRole.Other);
                }
            }

            Assert.Ignore("This machine has no NVIDIA or AMD DXGI adapter.");
            return null!;
        }

        /// <summary>Gets the DXGI index of the Microsoft Basic Render Driver, on which D3D11 encoder devices cannot be created.</summary>
        /// <returns>The adapter's FFmpeg device string.</returns>
        /// <remarks>FFmpeg silently opens the default adapter for a nonexistent index, so a real adapter is needed to make device creation fail.</remarks>
        private static string UnopenableAdapterDevice()
        {
            foreach (var adapter in DxgiAdapterQuery.GetAdapters(true))
            {
                if (adapter.VendorId == 0x1414 && adapter.DeviceId == 0x8C)
                {
                    return adapter.Device;
                }
            }

            Assert.Ignore("DXGI does not list the Microsoft Basic Render Driver.");
            return null!;
        }

        /// <summary>Encodes three frames, finalizes the file, and checks that it decodes.</summary>
        /// <param name="encoder">The open encoder.</param>
        /// <param name="path">The output file.</param>
        private static void EncodeThreeFrames(FFmpegVideoEncoder encoder, string path)
        {
            for (var i = 0; i < 3; i++)
            {
                encoder.Encode(FFmpegTestMedia.CreateFrame(HardwareWidth, HardwareHeight, i), i, false);
            }

            encoder.Complete();
            Assert.That(CountFrames(path, out var codec), Is.EqualTo(3));
            Assert.That(codec, Is.EqualTo("h264"));
        }

        /// <summary>Checks that a renderer GPU without a checked encoder is followed by the encoder of another GPU.</summary>
        [Test]
        public void HardwarePreference_RendererWithoutEncoder_UsesOtherGpu()
        {
            var other = EncoderAdapter();
            var renderer = new HardwareGpu("99", "VMware SVGA renderer", VMwareVendorId, 0x0405).WithRole(HardwareGpuRole.Renderer);
            var path = FFmpegTestMedia.PathIn(_directory, "other-gpu.mp4");
            using var encoder = new FFmpegVideoEncoder(HardwareOptions(new[] { renderer, other }, HardwareFallbackRoute.OtherGpus));
            encoder.Open(path, CancellationToken.None);
            if (encoder.EncoderType != VideoEncoderType.Hardware)
            {
                Assert.Ignore("No checked H.264 hardware encoder opened on " + other + ": " + encoder.HardwareFallbackReason);
            }

            Assert.That(encoder.EncoderName, Is.EqualTo(other.VendorId == HardwareGpu.NvidiaVendorId ? "h264_nvenc" : "h264_amf"));
            Assert.That(encoder.EncoderDevice, Is.EqualTo(other.ToString()));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain(renderer + " has no checked hardware encoder"));
            Assert.That(encoder.HardwareFallbackReason, Does.EndWith("Encoding on " + other + "."));
            EncodeThreeFrames(encoder, path);
        }

        /// <summary>Checks that a renderer GPU whose encoder device cannot be selected is followed by another GPU of the same vendor.</summary>
        /// <remarks>
        /// The renderer has no LUID, so no CUDA device matches it for NVENC, and it names the Microsoft Basic Render Driver,
        /// on which AMF cannot create its D3D11 device.
        /// </remarks>
        [Test]
        public void HardwarePreference_RendererDeviceUnavailable_UsesOtherGpu()
        {
            var other = EncoderAdapter();
            var renderer = new HardwareGpu(UnopenableAdapterDevice(), "Missing renderer", other.VendorId, other.DeviceId).WithRole(HardwareGpuRole.Renderer);
            var path = FFmpegTestMedia.PathIn(_directory, "missing-renderer.mp4");
            using var encoder = new FFmpegVideoEncoder(HardwareOptions(new[] { renderer, other }, HardwareFallbackRoute.OtherGpus));
            encoder.Open(path, CancellationToken.None);
            if (encoder.EncoderType != VideoEncoderType.Hardware)
            {
                Assert.Ignore("No checked H.264 hardware encoder opened on " + other + ": " + encoder.HardwareFallbackReason);
            }

            Assert.That(encoder.EncoderDevice, Is.EqualTo(other.ToString()));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain(" on " + renderer + ": "));
            Assert.That(encoder.HardwareFallbackReason, Does.EndWith("Encoding on " + other + "."));
            EncodeThreeFrames(encoder, path);
        }

        /// <summary>Checks that an NVIDIA adapter maps to a CUDA ordinal by its LUID and that an unknown LUID matches nothing.</summary>
        [Test]
        public void CudaDevice_MapsNvidiaAdapterByLuid()
        {
            var adapter = EncoderAdapter();
            if (adapter.VendorId != HardwareGpu.NvidiaVendorId)
            {
                Assert.Ignore("The first encoder adapter is not an NVIDIA GPU.");
            }

            bool found;
            int ordinal;
            try
            {
                found = CudaDeviceQuery.TryGetOrdinal(adapter, out ordinal);
            }
            catch (NotSupportedException error)
            {
                Assert.Ignore("The CUDA driver is unavailable: " + error.Message);
                return;
            }

            Assert.That(found, Is.True);
            Assert.That(ordinal, Is.GreaterThanOrEqualTo(0));
            Assert.That(CudaDeviceQuery.TryGetOrdinal(new HardwareGpu("0", "No LUID", HardwareGpu.NvidiaVendorId, adapter.DeviceId), out ordinal), Is.False);
            Assert.That(ordinal, Is.EqualTo(-1));
        }

        /// <summary>Checks that the software codec route uses the same-format software encoder instead of another GPU.</summary>
        [Test]
        public void HardwarePreference_SoftwareCodecRoute_SkipsOtherGpus()
        {
            var renderer = new HardwareGpu("99", "VMware SVGA renderer", VMwareVendorId, 0x0405).WithRole(HardwareGpuRole.Renderer);
            var path = FFmpegTestMedia.PathIn(_directory, "software-route.mp4");
            using var encoder = new FFmpegVideoEncoder(HardwareOptions(new[] { renderer, EncoderAdapter() }, HardwareFallbackRoute.SoftwareCodec));
            encoder.Open(path, CancellationToken.None);

            Assert.That(encoder.EncoderName, Is.EqualTo("libx264"));
            Assert.That(encoder.EncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(encoder.EncoderDevice, Is.EqualTo("Software"));
            Assert.That(encoder.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.SoftwareCodec));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain(renderer + " has no checked hardware encoder"));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain("Other GPUs were not tried because the fallback route is SoftwareCodec"));
            Assert.That(encoder.HardwareFallbackReason, Does.Not.Contain("_nvenc").And.Not.Contain("_amf"));
            EncodeThreeFrames(encoder, path);
        }

        /// <summary>Ignores the test unless the loaded native libraries contain the QSV encoders.</summary>
        private static unsafe void RequireQsvBuild()
        {
            if (global::FFmpeg.AutoGen.ffmpeg.avcodec_find_encoder_by_name("h264_qsv") == null)
            {
                Assert.Ignore("The loaded FFmpeg libraries were built without QSV encoders.");
            }
        }

        /// <summary>Checks that every native build with QSV encoders carries the reviewed rate-control check.</summary>
        [Test]
        public void QsvBuild_CarriesRateControlCheckMarker()
        {
            RequireQsvBuild();

            Assert.That(global::FFmpeg.AutoGen.ffmpeg.avcodec_configuration(), Does.Contain(FFmpegVideoEncoder.CheckedQsvConfigurationMarker));
            Assert.That(global::FFmpeg.AutoGen.ffmpeg.avcodec_configuration(), Does.Contain("--enable-libvpl"));
        }

        /// <summary>Checks that an Intel renderer is tried with QSV before another GPU, and that QSV can run or fails cleanly.</summary>
        /// <remarks>
        /// The Intel entry names the Microsoft Basic Render Driver, so its D3D11 child device cannot be created
        /// on machines without an Intel GPU, and the recording continues on the machine's NVIDIA or AMD GPU.
        /// </remarks>
        [Test]
        public void HardwarePreference_IntelRenderer_TriesQsvBeforeOtherGpu()
        {
            RequireQsvBuild();
            var other = EncoderAdapter();
            var renderer = new HardwareGpu(UnopenableAdapterDevice(), "Intel renderer", HardwareGpu.IntelVendorId, 0x46A6).WithRole(HardwareGpuRole.Renderer);
            var path = FFmpegTestMedia.PathIn(_directory, "intel-renderer.mp4");
            using var encoder = new FFmpegVideoEncoder(HardwareOptions(new[] { renderer, other }, HardwareFallbackRoute.OtherGpus));
            encoder.Open(path, CancellationToken.None);
            if (encoder.EncoderType != VideoEncoderType.Hardware)
            {
                Assert.Ignore("No checked H.264 hardware encoder opened on " + other + ": " + encoder.HardwareFallbackReason);
            }

            Assert.That(encoder.HardwareFallbackReason, Does.Contain("h264_qsv on " + renderer + ": Create D3D11 device for QSV: "));
            Assert.That(encoder.EncoderDevice, Is.EqualTo(other.ToString()));
            EncodeThreeFrames(encoder, path);
        }

        /// <summary>Checks that a VBR request whose maximum equals its target is rejected for QSV, which would encode it as CBR.</summary>
        [Test]
        public void HardwarePreference_QsvVbrWithoutHeadroom_IsRejected()
        {
            RequireQsvBuild();
            var renderer = new HardwareGpu("0", "Intel renderer", HardwareGpu.IntelVendorId, 0x46A6).WithRole(HardwareGpuRole.Renderer);
            var options = HardwareOptions(new[] { renderer }, HardwareFallbackRoute.SoftwareCodec);
            options.RateControlMode = VideoRateControlMode.VBR;
            options.MaximumBitRate = options.BitRate;
            using var encoder = new FFmpegVideoEncoder(options);
            encoder.Open(FFmpegTestMedia.PathIn(_directory, "qsv-vbr.mp4"), CancellationToken.None);

            Assert.That(encoder.EncoderName, Is.EqualTo("libx264"));
            Assert.That(encoder.HardwareFallbackReason, Does.Contain("h264_qsv on " + renderer
                + ": QSV selects CBR when the VBR maximum bitrate equals the target."));
        }
#endif
    }
}
