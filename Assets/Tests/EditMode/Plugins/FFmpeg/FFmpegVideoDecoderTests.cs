using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using MajdataPlay.FFmpeg.Interop;
#endif
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the worker-owned FFmpeg decoder against small clips encoded by the project's encoder.</summary>
    public class FFmpegVideoDecoderTests
    {
        /// <summary>The H.264 clip width, large enough for common hardware decoder minimums.</summary>
        private const int HardwareWidth = 256;
        /// <summary>The H.264 clip height, a whole number of macroblocks.</summary>
        private const int HardwareHeight = 144;
        /// <summary>Owns the encoded test media for the fixture.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores a one-second, 30 fps MPEG-4 clip.</summary>
        private string _clip = "";
        /// <summary>Stores a six-second, 10 fps MPEG-4 clip with keyframes every two seconds.</summary>
        private string _long = "";
        /// <summary>Stores a one-second H.264 clip used by hardware decoding tests.</summary>
        private string _h264 = "";
        /// <summary>Stores an MPEG-4 clip muxed with an AAC audio track.</summary>
        private string _withAudio = "";

        /// <summary>Encodes the fixture media once.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
            _clip = FFmpegTestMedia.CreateVideo(_directory, "clip.mkv");
            _long = FFmpegTestMedia.CreateVideo(_directory, "long.mkv", 60, 10);
            _h264 = FFmpegTestMedia.CreateVideo(_directory, "clip-h264.mp4", 30, 30, VideoEncodingFormat.H264, HardwareWidth, HardwareHeight);
            using var output = new MemoryStream();
            var pcm = new byte[48000 * 2 * 4 / 2];
            FFmpegMediaMuxer.Mux(_clip, new MemoryStream(pcm), 48000, 2, output, MediaContainerFormat.Matroska);
            var file = _directory.Directory.CreateFile("audio.mkv");
            file.WriteAllBytes(output.ToArray());
            _withAudio = file.Location;
        }

        /// <summary>Deletes the fixture media.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Reads every remaining frame and returns their presentation times.</summary>
        /// <param name="decoder">The open decoder.</param>
        /// <returns>The presentation times in decoding order.</returns>
        private static List<double> ReadAll(FFmpegVideoDecoder decoder)
        {
            var times = new List<double>();
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    return times;
                }

                times.Add(frame.PresentationTime);
            }
        }

        /// <summary>Creates a D3D11VA device or ignores the test when this machine has no compatible GPU.</summary>
        /// <returns>An owned FFmpeg device reference.</returns>
        private static unsafe IntPtr CreateD3D11Device()
        {
            AVBufferRef* device = null;
            var result = ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0);
            if (result < 0 || device == null)
            {
                Assert.Ignore("D3D11VA is unavailable on this machine: " + FFmpegVideoDecoder.ErrorText(result));
            }

            return (IntPtr)device;
        }

        /// <summary>Checks constructor validation of timeouts and pixel limits.</summary>
        [Test]
        public void Constructor_RejectsInvalidOptions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegVideoDecoder(new DecoderOptions { IOTimeoutMilliseconds = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegVideoDecoder(new DecoderOptions { MaximumPixelCount = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegVideoDecoder(new DecoderOptions { MaximumPixelCount = (int.MaxValue / 4) + 1 }));
            using var decoder = new FFmpegVideoDecoder(null);
            Assert.That(decoder.DecoderDevice, Is.EqualTo("Software"));
            Assert.That(decoder.TransferMode, Is.EqualTo("Software RGBA upload"));
            Assert.That(decoder.DecoderThreadingMode, Is.EqualTo("none"));
        }

        /// <summary>Checks argument and lifetime validation of Open.</summary>
        [Test]
        public void Open_ValidatesArgumentsAndLifetime()
        {
            var decoder = new FFmpegVideoDecoder();
            Assert.Throws<ArgumentException>(() => decoder.Open(" ", CancellationToken.None));
            Assert.Throws<ArgumentException>(() => decoder.Open(null!, CancellationToken.None));
            decoder.Dispose();
            decoder.Dispose();

            Assert.Throws<ObjectDisposedException>(() => decoder.Open(_clip, CancellationToken.None));
        }

        /// <summary>Checks that a canceled token is observed before any input is opened.</summary>
        [Test]
        public void Open_CanceledToken_Throws()
        {
            using var decoder = new FFmpegVideoDecoder();
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(() => decoder.Open(_clip, source.Token));
        }

        /// <summary>Checks that a failed open reports the failure and disposes the decoder.</summary>
        [Test]
        public void Open_MissingFile_FailsAndDisposes()
        {
            var decoder = new FFmpegVideoDecoder();

            Assert.Throws<InvalidOperationException>(() => decoder.Open(FFmpegTestMedia.PathIn(_directory, "missing.mkv"), CancellationToken.None));
            Assert.Throws<ObjectDisposedException>(() => decoder.ReadFrame());
        }

        /// <summary>Checks that content URIs are rejected when the loaded build lacks the Android content protocol.</summary>
        [Test]
        public void Open_ContentUriWithoutProtocol_IsNotSupported()
        {
            if (FFmpegVideoDecoder.SupportsInputProtocol("content"))
            {
                Assert.Ignore("This FFmpeg build includes the content protocol.");
            }

            var decoder = new FFmpegVideoDecoder();

            Assert.Throws<NotSupportedException>(() => decoder.Open("content://provider/document/1", CancellationToken.None));
        }

        /// <summary>Checks the metadata read when a software decoder opens the clip.</summary>
        [Test]
        public void Open_ReadsStreamMetadata()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);

            Assert.That(decoder.Width, Is.EqualTo(FFmpegTestMedia.DefaultWidth));
            Assert.That(decoder.Height, Is.EqualTo(FFmpegTestMedia.DefaultHeight));
            Assert.That(decoder.Duration, Is.EqualTo(1).Within(0.05));
            Assert.That(decoder.FrameRate, Is.EqualTo(30).Within(0.01));
            Assert.That(decoder.CodecName, Is.EqualTo("mpeg4"));
            Assert.That(decoder.DecoderName, Is.EqualTo("mpeg4"));
            Assert.That(decoder.CanSeek, Is.True);
            Assert.That(decoder.HasAudio, Is.False);
            Assert.That(decoder.RotationDegrees, Is.EqualTo(0));
            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.ActiveHardwareDeviceType, Is.EqualTo(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));
            Assert.That(decoder.HardwareFallbackReason, Is.Null);
            Assert.That(decoder.DecoderThreadCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(new[] { "frame", "slice", "codec", "none" }, Does.Contain(decoder.DecoderThreadingMode));
            Assert.Throws<InvalidOperationException>(() => decoder.Open(_clip, CancellationToken.None));
        }

        /// <summary>Checks that the immutable session snapshot copies the decoder metadata.</summary>
        [Test]
        public void VideoInfo_CopiesDecoderMetadata()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);

            var info = new VideoInfo(decoder);

            Assert.That(info.Width, Is.EqualTo(decoder.Width));
            Assert.That(info.Height, Is.EqualTo(decoder.Height));
            Assert.That(info.Duration, Is.EqualTo(decoder.Duration));
            Assert.That(info.FrameRate, Is.EqualTo(decoder.FrameRate));
            Assert.That(info.BitRate, Is.EqualTo(decoder.BitRate));
            Assert.That(info.CanSeek, Is.True);
            Assert.That(info.HasAudio, Is.False);
            Assert.That(info.Codec, Is.EqualTo("mpeg4"));
            Assert.That(info.DecoderName, Is.EqualTo("mpeg4"));
            Assert.That(info.DecoderDevice, Is.EqualTo("Software"));
            Assert.That(info.TransferMode, Is.EqualTo("Software RGBA upload"));
            Assert.That(info.HardwareDecoding, Is.False);
            Assert.That(info.HardwareDeviceType, Is.EqualTo(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));
            Assert.That(info.DecoderThreadCount, Is.EqualTo(decoder.DecoderThreadCount));
            Assert.That(info.DecoderThreadingMode, Is.EqualTo(decoder.DecoderThreadingMode));
        }

        /// <summary>Checks that every frame is decoded in order, followed by end of input.</summary>
        [Test]
        public void ReadFrame_DecodesEveryFrameThenEnds()
        {
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions { ConversionThreadCount = 1 });
            decoder.Open(_clip, CancellationToken.None);
            var times = new List<double>();
            long bitRate = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }

                Assert.That(frame.Width, Is.EqualTo(FFmpegTestMedia.DefaultWidth));
                Assert.That(frame.Height, Is.EqualTo(FFmpegTestMedia.DefaultHeight));
                Assert.That(frame.DataSize, Is.EqualTo(FFmpegTestMedia.DefaultWidth * FFmpegTestMedia.DefaultHeight * 4));
                Assert.That(frame.Duration, Is.EqualTo(1 / 30.0).Within(0.002));
                Assert.That(frame.TransferMode, Is.EqualTo("Software RGBA upload"));
                bitRate = Math.Max(bitRate, frame.CurrentBitRate);
                times.Add(frame.PresentationTime);
            }

            Assert.That(times.Count, Is.EqualTo(30));
            Assert.That(times[0], Is.EqualTo(0).Within(0.001));
            Assert.That(times, Is.Ordered);
            Assert.That(times[29], Is.EqualTo(29 / 30.0).Within(0.002));
            Assert.That(bitRate, Is.GreaterThan(0));
            Assert.That(decoder.ReadFrame(), Is.Null);
            Assert.That(decoder.ConversionScaleThreadCount, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>Checks that audio packets are skipped while the audio stream is reported.</summary>
        [Test]
        public void ReadFrame_SkipsAudioPackets()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_withAudio, CancellationToken.None);

            Assert.That(decoder.HasAudio, Is.True);
            Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
        }

        /// <summary>Checks that operations before opening are rejected.</summary>
        [Test]
        public void ReadAndSeek_BeforeOpen_Throw()
        {
            using var decoder = new FFmpegVideoDecoder();

            Assert.Throws<InvalidOperationException>(() => decoder.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => decoder.Seek(0));
        }

        /// <summary>Checks that the decoder rejects use from a thread other than its owning worker.</summary>
        [Test]
        public void Operations_FromAnotherThread_AreRejected()
        {
            var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);
            Exception? readError = null;
            Exception? disposeError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    decoder.ReadFrame();
                }
                catch (Exception error)
                {
                    readError = error;
                }

                try
                {
                    decoder.Dispose();
                }
                catch (Exception error)
                {
                    disposeError = error;
                }
            });
            thread.Start();
            thread.Join();

            Assert.That(readError, Is.InstanceOf<InvalidOperationException>());
            Assert.That(disposeError, Is.InstanceOf<InvalidOperationException>());
            decoder.Dispose();
        }

        /// <summary>Checks that seeking backwards decodes the preroll and resumes at the requested frame.</summary>
        [Test]
        public void Seek_Backwards_ResumesAtTarget()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);
            ReadAll(decoder);

            decoder.Seek(0.5);
            var times = ReadAll(decoder);

            Assert.That(times[0], Is.EqualTo(0.5).Within(0.034));
            Assert.That(times.Count, Is.InRange(14, 16));
        }

        /// <summary>Checks that seek positions are clamped to the timeline and validated.</summary>
        [Test]
        public void Seek_ClampsAndValidatesPositions()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);

            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Seek(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Seek(double.PositiveInfinity));

            decoder.Seek(-5);
            using (var first = decoder.ReadFrame())
            {
                Assert.That(first!.PresentationTime, Is.EqualTo(0).Within(0.001));
            }

            decoder.Seek(100);
            var tail = ReadAll(decoder);

            Assert.That(tail.Count, Is.EqualTo(1));
            Assert.That(tail[0], Is.EqualTo(29 / 30.0).Within(0.002));
        }

        /// <summary>Checks that frames already expired on the playback clock are discarded and the true endpoint is still delivered.</summary>
        [Test]
        public void FrameDeadline_DiscardsExpiredFramesAndKeepsEndpoint()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);
            decoder.FrameDeadlineProvider = () => new FrameDeadline(10, 10, double.NegativeInfinity);

            var times = ReadAll(decoder);

            Assert.That(decoder.DiscardedFrames, Is.EqualTo(30));
            Assert.That(times.Count, Is.EqualTo(1));
            Assert.That(times[0], Is.EqualTo(29 / 30.0).Within(0.002));
        }

        /// <summary>Checks that a frozen clock never discards frames.</summary>
        [Test]
        public void FrameDeadline_None_PresentsEveryFrame()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);
            decoder.FrameDeadlineProvider = () => FrameDeadline.None;

            Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
            Assert.That(decoder.DiscardedFrames, Is.EqualTo(0));
            Assert.That(FrameDeadline.None.Rate, Is.EqualTo(1));
            Assert.That(double.IsNegativeInfinity(FrameDeadline.None.Position), Is.True);
        }

        /// <summary>Checks that late frames above normal speed are thinned to the presentation rate.</summary>
        [Test]
        public void FrameDeadline_FastPlayback_ThrottlesConversion()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_clip, CancellationToken.None);
            decoder.FrameDeadlineProvider = () => new FrameDeadline(10, double.NegativeInfinity, double.NegativeInfinity, 4);

            var times = ReadAll(decoder);

            Assert.That(times.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(times.Count + decoder.DiscardedFrames, Is.InRange(30, 31));
        }

        /// <summary>Checks that a long decoding lag skips forward to a due keyframe instead of decoding the gap.</summary>
        [Test]
        public void FrameDeadline_LongLag_SeeksForwardToKeyframe()
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(_long, CancellationToken.None);
            decoder.FrameDeadlineProvider = () => new FrameDeadline(5, 5, 4.9);

            using var first = decoder.ReadFrame();

            Assert.That(decoder.CatchUpSeeks, Is.GreaterThanOrEqualTo(1));
            Assert.That(first!.PresentationTime, Is.EqualTo(5).Within(0.11));
            Assert.That(decoder.DiscardedFrames, Is.LessThan(49));
        }

        /// <summary>Checks that a hardware request without a hardware configuration for the codec falls back to software.</summary>
        [Test]
        public void HardwareRequest_WithoutCodecConfiguration_FallsBackToSoftware()
        {
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions { HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA });
            decoder.Open(_clip, CancellationToken.None);

            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("no hardware configuration"));
            Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
        }

        /// <summary>Checks that a missing MediaCodec decoder is reported while decoding continues in software.</summary>
        [Test]
        public void MediaCodecRequest_WithoutDecoder_FallsBackToSoftware()
        {
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions { HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC });
            decoder.Open(_clip, CancellationToken.None);

            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("mpeg4_mediacodec"));
            Assert.That(decoder.DecoderName, Is.EqualTo("mpeg4"));
        }

        /// <summary>Checks that the configured fallback backend is used when the preferred backend has no configuration.</summary>
        [Test]
        public void HardwareRequest_UsesFallbackOptions()
        {
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                FallbackHardwareOptions = new DecoderOptions()
            };
            using var decoder = new FFmpegVideoDecoder(options);
            decoder.Open(_clip, CancellationToken.None);

            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("AV_HWDEVICE_TYPE_D3D11VA"));
            Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
        }

        /// <summary>Checks that strict hardware decoding fails instead of silently using software.</summary>
        /// <param name="device">The requested hardware backend.</param>
        [TestCase(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)]
        [TestCase(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA)]
        public void StrictHardware_WithoutHardware_IsNotSupported(AVHWDeviceType device)
        {
            var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                RequireHardwareDecoding = true,
                KeepNativeFrames = true,
                HardwareDeviceType = device
            });

            Assert.Throws<NotSupportedException>(() => decoder.Open(_clip, CancellationToken.None));
            Assert.Throws<ObjectDisposedException>(() => decoder.ReadFrame());
        }

        /// <summary>Checks hardware decoding with CPU upload on the default D3D11VA device, or the reported software fallback.</summary>
        [Test]
        public void HardwareDecode_DefaultDevice_UploadsThroughCpuOrFallsBack()
        {
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                AcquireD3D11Device = () => IntPtr.Zero,
                HardwareDeviceDescription = "test adapter"
            });
            decoder.Open(_h264, CancellationToken.None);

            Assert.That(decoder.HardwareFallbackReason, Is.Not.Null);
            if (decoder.HardwareDecoding)
            {
                Assert.That(decoder.HardwareFallbackReason, Does.Contain("compatible D3D11 decode device is unavailable"));
            }

            var count = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }

                count++;
                Assert.That(frame.IsHardwareFrame, Is.False);
                Assert.That(frame.HardwareDecoded, Is.EqualTo(decoder.HardwareDecoding));
            }

            Assert.That(count, Is.EqualTo(30));
            if (decoder.HardwareDecoding)
            {
                Assert.That(decoder.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
                // A listed GPU names the independent device; without a list FFmpeg chooses it.
                Assert.That(decoder.DecoderDevice, Does.Contain("FFmpeg default hardware device")
                    .Or.Contain("Unity renderer GPU").Or.Contain("Unity renderer not identified"));
                Assert.That(decoder.ActiveHardwareDeviceType, Is.EqualTo(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA));
            }
        }

        /// <summary>Checks that a supplied device without a frame mapper still decodes through GPU synchronization and CPU upload.</summary>
        [Test]
        public void HardwareDecode_SuppliedDevice_RunsSynchronizationAndCpuFallback()
        {
            var device = CreateD3D11Device();
            var synchronization = new CountingSynchronization();
            var waits = 0;
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AcquireHardwareDevice = () => device,
                CreateHardwareSynchronization = _ => synchronization,
                CreateHardwareFrameMapper = _ => throw new NotSupportedException("no mapper"),
                WaitForHardwareFrame = (_, _, _) => waits++
            };
            using (var decoder = new FFmpegVideoDecoder(options))
            {
                decoder.Open(_h264, CancellationToken.None);
                if (!decoder.HardwareDecoding)
                {
                    Assert.Ignore("H.264 D3D11VA decoding is unavailable: " + decoder.HardwareFallbackReason);
                }

                Assert.That(decoder.HardwareFallbackReason, Does.Contain("no mapper"));
                Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
                Assert.That(decoder.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
                Assert.That(decoder.DecoderThreadCount, Is.EqualTo(1));
                Assert.That(synchronization.Waits, Is.GreaterThan(0));
                Assert.That(waits, Is.EqualTo(0));
                decoder.Seek(0.5);
                Assert.That(ReadAll(decoder).Count, Is.InRange(14, 16));
            }

            Assert.That(synchronization.Disposed, Is.True);
        }

        /// <summary>Checks native frame retention through a frame mapper and an explicit software copy of a GPU frame.</summary>
        [Test]
        public unsafe void HardwareDecode_FrameMapper_ReturnsNativeFrames()
        {
            var device = CreateD3D11Device();
            var mapper = new CloningMapper();
            var waits = 0;
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AcquireHardwareDevice = () => device,
                CreateHardwareSynchronization = _ => new CountingSynchronization(),
                CreateHardwareFrameMapper = _ => mapper,
                WaitForHardwareFrame = (_, _, _) => waits++
            };
            using (var decoder = new FFmpegVideoDecoder(options))
            {
                decoder.Open(_h264, CancellationToken.None);
                if (!decoder.HardwareDecoding)
                {
                    Assert.Ignore("H.264 D3D11VA decoding is unavailable: " + decoder.HardwareFallbackReason);
                }

                using var frame = decoder.ReadFrame();

                Assert.That(frame!.IsHardwareFrame, Is.True);
                Assert.That(frame.NativeFrame, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(frame.Data, Is.EqualTo(IntPtr.Zero));
                Assert.That(frame.TransferMode, Is.EqualTo("Native GPU frames"));
                Assert.That(frame.PixelFormat, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_D3D11));
                Assert.That(decoder.TransferMode, Is.EqualTo("Native GPU frames"));
                Assert.That(waits, Is.GreaterThan(0));
                Assert.That(mapper.Maps, Is.EqualTo(1));
                using var copy = frame.CopyToSoftware();
                Assert.That(copy.Width, Is.EqualTo(HardwareWidth));
                Assert.That(copy.HardwareDecoded, Is.True);
                Assert.That(copy.Data, Is.Not.EqualTo(IntPtr.Zero));
            }

            Assert.That(mapper.Disposed, Is.True);
        }

        /// <summary>Checks that a mapper failure keeps hardware decoding and falls back to CPU upload.</summary>
        [Test]
        public void HardwareDecode_MapperFailure_FallsBackToCpuUpload()
        {
            var device = CreateD3D11Device();
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AcquireHardwareDevice = () => device,
                MapHardwareFrame = _ => IntPtr.Zero
            };
            using var decoder = new FFmpegVideoDecoder(options);
            decoder.Open(_h264, CancellationToken.None);
            if (!decoder.HardwareDecoding)
            {
                Assert.Ignore("H.264 D3D11VA decoding is unavailable: " + decoder.HardwareFallbackReason);
            }

            using var frame = decoder.ReadFrame();

            Assert.That(frame!.IsHardwareFrame, Is.False);
            Assert.That(frame.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("Native frame mapping failed"));
        }

        /// <summary>Checks that a rejected hardware frame pool reports the configured failure.</summary>
        [Test]
        public void HardwareDecode_FramePoolRejection_IsReported()
        {
            var device = CreateD3D11Device();
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                RequireHardwareDecoding = true,
                AllowHardwareCpuUpload = false,
                AcquireHardwareDevice = () => device,
                ConfigureHardwareFrames = _ => -22
            };
            using var decoder = new FFmpegVideoDecoder(options);
            decoder.Open(_h264, CancellationToken.None);
            if (!decoder.HardwareDecoding)
            {
                Assert.Ignore("H.264 D3D11VA decoding is unavailable: " + decoder.HardwareFallbackReason);
            }

            var error = Assert.Throws<NotSupportedException>(() => decoder.ReadFrame());

            Assert.That(error!.Message, Does.Contain("frame pool configuration failed"));
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        /// <summary>The width of the clip that exceeds the 4096-pixel H.264 limit of common hardware decoders.</summary>
        private const int OversizedWidth = 4608;
        /// <summary>The number of frames in the oversized clip.</summary>
        private const int OversizedFrames = 10;
        /// <summary>Stores the lazily encoded oversized H.264 clip, or an empty string before the first use.</summary>
        private string _oversized = "";

        /// <summary>Gets an H.264 clip wider than hardware decoders accept, so their pixel-format selection rejects it.</summary>
        /// <returns>The path of the clip.</returns>
        private string OversizedClip()
        {
            if (_oversized.Length == 0)
            {
                _oversized = FFmpegTestMedia.CreateVideo(_directory, "oversized-h264.mp4", OversizedFrames, 30, VideoEncodingFormat.H264, OversizedWidth, 64);
            }

            return _oversized;
        }

        /// <summary>Gets the first DXGI hardware adapter or ignores the test when this machine has none.</summary>
        /// <param name="role">The role to assign to the adapter.</param>
        /// <returns>The adapter with the requested role.</returns>
        private static HardwareGpu FirstAdapter(HardwareGpuRole role)
        {
            var adapters = DxgiAdapterQuery.GetAdapters(false);
            if (adapters.Count == 0)
            {
                Assert.Ignore("This machine has no DXGI hardware adapter.");
            }

            return adapters[0].WithRole(role);
        }

        /// <summary>Gets the Microsoft Basic Render Driver, a software adapter on which D3D11VA devices cannot be created.</summary>
        /// <returns>The software adapter, listed as Unity's renderer.</returns>
        /// <remarks>FFmpeg silently opens the default adapter for a nonexistent index, so a real adapter is needed to make device creation fail.</remarks>
        private static HardwareGpu UnopenableRenderer()
        {
            foreach (var adapter in DxgiAdapterQuery.GetAdapters(true))
            {
                if (adapter.VendorId == 0x1414 && adapter.DeviceId == 0x8C)
                {
                    return adapter.WithRole(HardwareGpuRole.Renderer);
                }
            }

            Assert.Ignore("DXGI does not list the Microsoft Basic Render Driver.");
            return null!;
        }

        /// <summary>Reads every remaining frame and checks that each was decoded by hardware and uploaded through the CPU.</summary>
        /// <param name="decoder">The open decoder.</param>
        /// <returns>The presentation times in decoding order.</returns>
        private static List<double> ReadAllUploaded(FFmpegVideoDecoder decoder)
        {
            var times = new List<double>();
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    return times;
                }

                Assert.That(frame.IsHardwareFrame, Is.False);
                Assert.That(frame.HardwareDecoded, Is.True);
                Assert.That(frame.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
                times.Add(frame.PresentationTime);
            }
        }

        /// <summary>Checks that an unopenable renderer GPU is followed by hardware decoding with CPU upload on another GPU.</summary>
        [Test]
        public void HardwareDecode_RendererDeviceUnavailable_UsesOtherGpu()
        {
            var renderer = UnopenableRenderer();
            var other = FirstAdapter(HardwareGpuRole.Other);
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                HardwareGpus = new List<HardwareGpu> { renderer, other }
            });
            decoder.Open(_h264, CancellationToken.None);
            if (!decoder.HardwareDecoding)
            {
                Assert.Ignore("H.264 D3D11VA decoding is unavailable on " + other + ": " + decoder.HardwareFallbackReason);
            }

            Assert.That(decoder.DecoderDevice, Is.EqualTo("D3D11VA on " + other));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("Hardware device creation failed on " + renderer));
            Assert.That(decoder.ActiveHardwareDeviceType, Is.EqualTo(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA));
            Assert.That(ReadAllUploaded(decoder).Count, Is.EqualTo(30));
            Assert.That(decoder.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
        }

        /// <summary>Checks that the software codec route decodes in software instead of trying another GPU.</summary>
        [Test]
        public void HardwareDecode_SoftwareCodecRoute_SkipsOtherGpus()
        {
            var renderer = UnopenableRenderer();
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                HardwareFallbackRoute = HardwareFallbackRoute.SoftwareCodec,
                HardwareGpus = new List<HardwareGpu> { renderer, FirstAdapter(HardwareGpuRole.Other) }
            });
            decoder.Open(_h264, CancellationToken.None);

            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.DecoderDevice, Is.EqualTo("Software"));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("Hardware device creation failed on " + renderer));
            Assert.That(ReadAll(decoder).Count, Is.EqualTo(30));
        }

        /// <summary>Checks that a stream rejected by the renderer GPU reopens its pending packet on the next GPU, then in software.</summary>
        /// <remarks>This machine may have one GPU, so the same adapter is listed again as the other GPU and rejects the stream too.</remarks>
        [Test]
        public void HardwareDecode_RendererRejectsStream_TriesOtherGpuThenSoftware()
        {
            var renderer = FirstAdapter(HardwareGpuRole.Renderer);
            var other = renderer.WithRole(HardwareGpuRole.Other);
            using var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                HardwareGpus = new List<HardwareGpu> { renderer, other }
            });
            decoder.Open(OversizedClip(), CancellationToken.None);
            if (decoder.DecoderDevice != "D3D11VA on " + renderer)
            {
                Assert.Ignore("No D3D11VA device opened on " + renderer + ": " + decoder.HardwareFallbackReason);
            }

            // A GPU remains, so the renderer's codec is single-threaded and rejects the format synchronously.
            Assert.That(decoder.DecoderThreadCount, Is.EqualTo(1));
            var times = ReadAll(decoder);
            if (decoder.HardwareFallbackReason == null || !decoder.HardwareFallbackReason.Contains("rejected"))
            {
                Assert.Ignore(renderer + " decodes " + OversizedWidth + "-pixel-wide H.264.");
            }

            Assert.That(times.Count, Is.EqualTo(OversizedFrames));
            Assert.That(times[0], Is.EqualTo(0).Within(0.001));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("rejected the requested hardware pixel format on " + renderer + ". Trying the next GPU."));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("Reopened the h264 decoder on " + other + " with CPU upload."));
            Assert.That(decoder.HardwareFallbackReason, Does.Contain("rejected the requested hardware pixel format on " + other));
            Assert.That(decoder.HardwareDecoding, Is.False);
            Assert.That(decoder.DecoderDevice, Is.EqualTo("Software"));
            Assert.That(decoder.ActiveHardwareDeviceType, Is.EqualTo(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));
        }

        /// <summary>Checks that a stream rejected on a supplied renderer device moves to another GPU without the renderer's callbacks.</summary>
        [Test]
        public void HardwareDecode_SuppliedRendererRejectsStream_ReopensWithoutRendererCallbacks()
        {
            var renderer = FirstAdapter(HardwareGpuRole.Renderer);
            var other = renderer.WithRole(HardwareGpuRole.Other);
            var device = CreateD3D11Device();
            var synchronization = new CountingSynchronization();
            var mapper = new CloningMapper();
            var options = new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AcquireHardwareDevice = () => device,
                CreateHardwareSynchronization = _ => synchronization,
                CreateHardwareFrameMapper = _ => mapper,
                HardwareGpus = new List<HardwareGpu> { renderer, other }
            };
            using (var decoder = new FFmpegVideoDecoder(options))
            {
                decoder.Open(OversizedClip(), CancellationToken.None);
                if (!decoder.HardwareDecoding)
                {
                    Assert.Ignore("The supplied D3D11VA device was not used: " + decoder.HardwareFallbackReason);
                }

                var times = ReadAll(decoder);
                if (decoder.HardwareFallbackReason == null || !decoder.HardwareFallbackReason.Contains("rejected"))
                {
                    Assert.Ignore(renderer + " decodes " + OversizedWidth + "-pixel-wide H.264.");
                }

                Assert.That(times.Count, Is.EqualTo(OversizedFrames));
                Assert.That(times[0], Is.EqualTo(0).Within(0.001));
                // The supplied device belongs to the renderer, so the renderer entry is not opened again.
                Assert.That(decoder.HardwareFallbackReason, Does.StartWith("The decoder rejected the requested hardware pixel format. Trying the next GPU."));
                Assert.That(decoder.HardwareFallbackReason, Does.Contain("Reopened the h264 decoder on " + other + " with CPU upload."));
                Assert.That(decoder.HardwareFallbackReason, Does.Not.Contain(renderer.ToString()));
                Assert.That(synchronization.Disposed, Is.True);
                Assert.That(mapper.Disposed, Is.True);
                Assert.That(mapper.Maps, Is.EqualTo(0));
                Assert.That(decoder.DecoderDevice, Is.EqualTo("Software"));
            }
        }
#endif

        /// <summary>Checks the dimension validation helper.</summary>
        [Test]
        public void ValidateDimensions_RejectsInvalidSizes()
        {
            Assert.DoesNotThrow(() => FFmpegVideoDecoder.ValidateDimensions(4, 4, 16));
            Assert.Throws<NotSupportedException>(() => FFmpegVideoDecoder.ValidateDimensions(0, 4, 16));
            Assert.Throws<NotSupportedException>(() => FFmpegVideoDecoder.ValidateDimensions(4, -1, 16));
            Assert.Throws<NotSupportedException>(() => FFmpegVideoDecoder.ValidateDimensions(5, 4, 16));
            Assert.Throws<NotSupportedException>(() => FFmpegVideoDecoder.ValidateDimensions(65536, 65536, int.MaxValue));
        }

        /// <summary>Checks status translation and native error text.</summary>
        [Test]
        public void CheckAndErrorText_TranslateNativeStatus()
        {
            Assert.DoesNotThrow(() => FFmpegVideoDecoder.Check(0, "op"));
            var error = Assert.Throws<InvalidOperationException>(() => FFmpegVideoDecoder.Check(-22, "Configure"));

            Assert.That(error!.Message, Does.StartWith("Configure: "));
            Assert.That(error.Message, Does.Contain("(-22)"));
            Assert.That(FFmpegVideoDecoder.ErrorText(-22), Is.Not.Empty);
        }

        /// <summary>Checks protocol discovery and its argument validation.</summary>
        [Test]
        public void SupportsInputProtocol_FindsFileProtocol()
        {
            Assert.That(FFmpegVideoDecoder.SupportsInputProtocol("file"), Is.True);
            Assert.That(FFmpegVideoDecoder.SupportsInputProtocol("no-such-protocol"), Is.False);
            Assert.Throws<ArgumentException>(() => FFmpegVideoDecoder.SupportsInputProtocol(" "));
        }

        /// <summary>Checks rotation normalization to clockwise quarter turns.</summary>
        /// <param name="angle">The input angle.</param>
        /// <param name="expected">The normalized angle.</param>
        [TestCase(0.0, 0.0)]
        [TestCase(90.0, 90.0)]
        [TestCase(-90.0, 270.0)]
        [TestCase(451.0, 90.0)]
        [TestCase(44.0, 0.0)]
        [TestCase(-180.0, 180.0)]
        [TestCase(double.NaN, 0.0)]
        [TestCase(double.PositiveInfinity, 0.0)]
        public void NormalizeRotation_RoundsToQuarterTurns(double angle, double expected)
        {
            Assert.That(PrivateMember.InvokeStatic<double>(typeof(FFmpegVideoDecoder), "NormalizeRotation", angle), Is.EqualTo(expected));
        }

        /// <summary>Counts worker GPU synchronization calls.</summary>
        private sealed class CountingSynchronization : IHardwareDecodeSynchronization
        {
            /// <summary>Gets the number of completed waits.</summary>
            public int Waits { get; private set; }

            /// <summary>Gets whether the decoder released the synchronizer.</summary>
            public bool Disposed { get; private set; }

            /// <summary>Records one wait.</summary>
            /// <param name="cancellationToken">The decoding session token.</param>
            /// <param name="timeoutMilliseconds">The wait timeout.</param>
            public void Wait(CancellationToken cancellationToken, int timeoutMilliseconds)
            {
                Assert.That(timeoutMilliseconds, Is.GreaterThan(0));
                Waits++;
            }

            /// <summary>Records disposal.</summary>
            public void Dispose()
            {
                Disposed = true;
            }
        }

        /// <summary>Maps hardware frames by cloning their references.</summary>
        private sealed unsafe class CloningMapper : IHardwareFrameMapper
        {
            /// <summary>Gets the number of mapped frames.</summary>
            public int Maps { get; private set; }

            /// <summary>Gets whether the decoder released the mapper.</summary>
            public bool Disposed { get; private set; }

            /// <summary>Clones the borrowed frame.</summary>
            /// <param name="frame">The borrowed decoded frame.</param>
            /// <param name="cancellationToken">The decoding session token.</param>
            /// <param name="timeoutMilliseconds">The wait timeout.</param>
            /// <returns>An owned frame reference.</returns>
            public IntPtr Map(IntPtr frame, CancellationToken cancellationToken, int timeoutMilliseconds)
            {
                Maps++;
                return (IntPtr)ffmpeg.av_frame_clone((AVFrame*)frame);
            }

            /// <summary>Records disposal.</summary>
            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
