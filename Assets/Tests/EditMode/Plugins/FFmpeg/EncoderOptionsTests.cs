using System;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the validation and snapshot rules of encoder and decoder option objects.</summary>
    public class EncoderOptionsTests
    {
        /// <summary>Creates options that pass validation and can be modified by a single test.</summary>
        /// <returns>A valid option set.</returns>
        private static EncoderOptions CreateValid()
        {
            return new EncoderOptions
            {
                Width = 64,
                Height = 48,
                FrameRate = 30,
                MaximumSoftwareThreads = 2,
                BitRate = 100000,
                MaximumBitRate = 200000,
                Format = VideoEncodingFormat.MPEG4,
                PreferredEncoderType = VideoEncoderType.Software,
                RateControlMode = VideoRateControlMode.CBR
            };
        }

        /// <summary>Checks the documented default recording configuration.</summary>
        [Test]
        public void Defaults_DescribeFullHdSoftwareH264Vbr()
        {
            var options = new EncoderOptions();

            Assert.That(options.Width, Is.EqualTo(1920));
            Assert.That(options.Height, Is.EqualTo(1080));
            Assert.That(options.FrameRate, Is.EqualTo(60));
            Assert.That(options.PreferredEncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(options.Format, Is.EqualTo(VideoEncodingFormat.H264));
            Assert.That(options.BitRate, Is.EqualTo(8000000));
            Assert.That(options.MaximumBitRate, Is.EqualTo(16000000));
            Assert.That(options.RateControlMode, Is.EqualTo(VideoRateControlMode.VBR));
            Assert.That(options.MaximumSoftwareThreads, Is.EqualTo(Math.Min(Environment.ProcessorCount, 8)));
            Assert.That(options.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.OtherGpus));
            Assert.That(options.HardwareGpus, Is.Null);
        }

        /// <summary>Checks that validation returns an independent copy with every value preserved.</summary>
        [Test]
        public void ValidateAndClone_ReturnsIndependentEqualCopy()
        {
            var options = CreateValid();
            var gpus = new[] { new HardwareGpu("0", "GPU", HardwareGpu.AmdVendorId, 0x73DF) };
            options.HardwareFallbackRoute = HardwareFallbackRoute.SoftwareCodec;
            options.HardwareGpus = gpus;

            var copy = options.ValidateAndClone();
            options.Width = 1280;
            options.HardwareFallbackRoute = HardwareFallbackRoute.OtherGpus;

            Assert.That(copy, Is.Not.SameAs(options));
            Assert.That(copy.Width, Is.EqualTo(64));
            Assert.That(copy.Height, Is.EqualTo(48));
            Assert.That(copy.FrameRate, Is.EqualTo(30));
            Assert.That(copy.MaximumSoftwareThreads, Is.EqualTo(2));
            Assert.That(copy.BitRate, Is.EqualTo(100000));
            Assert.That(copy.MaximumBitRate, Is.EqualTo(200000));
            Assert.That(copy.Format, Is.EqualTo(VideoEncodingFormat.MPEG4));
            Assert.That(copy.PreferredEncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(copy.RateControlMode, Is.EqualTo(VideoRateControlMode.CBR));
            Assert.That(copy.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.SoftwareCodec));
            Assert.That(copy.HardwareGpus, Is.SameAs(gpus));
        }

        /// <summary>Checks that invalid widths are rejected, including odd and out-of-range values.</summary>
        /// <param name="width">The invalid width.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(16386)]
        public void ValidateAndClone_RejectsInvalidWidth(int width)
        {
            var options = CreateValid();
            options.Width = width;

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());

            Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.Width)));
        }

        /// <summary>Checks that invalid heights are rejected, including odd and out-of-range values.</summary>
        /// <param name="height">The invalid height.</param>
        [TestCase(0)]
        [TestCase(5)]
        [TestCase(16386)]
        public void ValidateAndClone_RejectsInvalidHeight(int height)
        {
            var options = CreateValid();
            options.Height = height;

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());

            Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.Height)));
        }

        /// <summary>Checks that the largest even dimensions are accepted because they still fit one RGBA allocation.</summary>
        [Test]
        public void ValidateAndClone_AcceptsLargestDimensions()
        {
            var options = CreateValid();
            options.Width = 16384;
            options.Height = 16384;

            var copy = options.ValidateAndClone();

            Assert.That(copy.Width, Is.EqualTo(16384));
            Assert.That(copy.Height, Is.EqualTo(16384));
        }

        /// <summary>Checks the smallest accepted even dimensions.</summary>
        [Test]
        public void ValidateAndClone_AcceptsSmallestDimensions()
        {
            var options = CreateValid();
            options.Width = 2;
            options.Height = 2;

            Assert.That(options.ValidateAndClone().Width, Is.EqualTo(2));
        }

        /// <summary>Checks the inclusive frame-rate bounds.</summary>
        /// <param name="frameRate">The frame rate to validate.</param>
        /// <param name="valid">Whether the value is accepted.</param>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(240, true)]
        [TestCase(241, false)]
        public void ValidateAndClone_ChecksFrameRateBounds(int frameRate, bool valid)
        {
            var options = CreateValid();
            options.FrameRate = frameRate;

            if (valid)
            {
                Assert.That(options.ValidateAndClone().FrameRate, Is.EqualTo(frameRate));
            }
            else
            {
                var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());
                Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.FrameRate)));
            }
        }

        /// <summary>Checks the inclusive software worker bounds.</summary>
        /// <param name="threads">The worker count to validate.</param>
        /// <param name="valid">Whether the value is accepted.</param>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(128, true)]
        [TestCase(129, false)]
        public void ValidateAndClone_ChecksThreadBounds(int threads, bool valid)
        {
            var options = CreateValid();
            options.MaximumSoftwareThreads = threads;

            if (valid)
            {
                Assert.That(options.ValidateAndClone().MaximumSoftwareThreads, Is.EqualTo(threads));
            }
            else
            {
                var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());
                Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.MaximumSoftwareThreads)));
            }
        }

        /// <summary>Checks the bitrate ordering contract between target, maximum and the 32-bit limit.</summary>
        /// <param name="target">The target bitrate.</param>
        /// <param name="maximum">The maximum bitrate.</param>
        /// <param name="valid">Whether the pair is accepted.</param>
        [TestCase(999L, 2000L, false)]
        [TestCase(1000L, 1000L, true)]
        [TestCase(5000L, 4999L, false)]
        [TestCase(5000L, 2147483647L, true)]
        [TestCase(5000L, 2147483648L, false)]
        public void ValidateAndClone_ChecksBitRateOrdering(long target, long maximum, bool valid)
        {
            var options = CreateValid();
            options.BitRate = target;
            options.MaximumBitRate = maximum;

            if (valid)
            {
                Assert.That(options.ValidateAndClone().MaximumBitRate, Is.EqualTo(maximum));
            }
            else
            {
                var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());
                Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.MaximumBitRate)));
            }
        }

        /// <summary>Checks that undefined enum values are rejected for each enum option.</summary>
        /// <param name="field">The option receiving an undefined value.</param>
        [TestCase("type")]
        [TestCase("route")]
        [TestCase("format")]
        [TestCase("mode")]
        public void ValidateAndClone_RejectsUndefinedEnums(string field)
        {
            var options = CreateValid();
            switch (field)
            {
                case "type":
                    options.PreferredEncoderType = (VideoEncoderType)42;
                    break;
                case "route":
                    options.HardwareFallbackRoute = (HardwareFallbackRoute)42;
                    break;
                case "format":
                    options.Format = (VideoEncodingFormat)42;
                    break;
                default:
                    options.RateControlMode = (VideoRateControlMode)42;
                    break;
            }

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndClone());

            Assert.That(error!.ParamName, Is.EqualTo(nameof(EncoderOptions.Format)));
        }

        /// <summary>Checks the documented decoder defaults derived from the processor count.</summary>
        [Test]
        public void DecoderOptions_DefaultsMatchDocumentation()
        {
            var options = new DecoderOptions();
            var processors = Environment.ProcessorCount;
            var conversion = processors <= 4 ? 1 : Math.Min(processors - 2, 4);

            Assert.That(options.IOTimeoutMilliseconds, Is.EqualTo(15000));
            Assert.That(options.MaximumPixelCount, Is.EqualTo(4096 * 4096));
            Assert.That(options.ThreadCount, Is.EqualTo(Math.Min(processors, 8)));
            Assert.That(options.ConversionThreadCount, Is.EqualTo(conversion));
            Assert.That(options.KeepNativeFrames, Is.False);
            Assert.That(options.AllowHardwareCpuUpload, Is.True);
            Assert.That(options.RequireHardwareDecoding, Is.False);
            Assert.That(options.HardwareDeviceType, Is.EqualTo(global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));
            Assert.That(options.HardwareDeviceDescription, Is.Null);
            Assert.That(options.AcquireD3D11Device, Is.Null);
            Assert.That(options.FallbackHardwareOptions, Is.Null);
            Assert.That(options.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.OtherGpus));
            Assert.That(options.HardwareGpus, Is.Null);
        }

        /// <summary>Checks that options for an independent device keep scalar settings but drop everything bound to Unity's renderer.</summary>
        [Test]
        public void DecoderOptions_CopyForIndependentDevice_DropsRendererBindings()
        {
            var gpus = new[] { new HardwareGpu("0", "GPU", HardwareGpu.NvidiaVendorId, 0x2504) };
            var options = new DecoderOptions
            {
                IOTimeoutMilliseconds = 1234,
                MaximumPixelCount = 4096,
                ThreadCount = 3,
                ConversionThreadCount = 2,
                KeepNativeFrames = true,
                AllowHardwareCpuUpload = true,
                HardwareDeviceDescription = "renderer",
                HardwareDeviceType = global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA,
                HardwareFallbackRoute = HardwareFallbackRoute.OtherGpus,
                HardwareGpus = gpus,
                AcquireD3D11Device = () => IntPtr.Zero,
                AcquireHardwareDevice = () => IntPtr.Zero,
                MapHardwareFrame = frame => frame,
                CreateHardwareSession = (_, _) => null,
                CreateHardwareSynchronization = _ => null!,
                CreateHardwareFrameMapper = _ => null!,
                ConfigureHardwareFrames = _ => 0,
                WaitForHardwareFrame = (_, _, _) => { },
                FallbackHardwareOptions = new DecoderOptions()
            };

            var copy = options.CopyForIndependentDevice(global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);

            Assert.That(copy, Is.Not.SameAs(options));
            Assert.That(copy.HardwareDeviceType, Is.EqualTo(global::FFmpeg.AutoGen.AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA));
            Assert.That(copy.IOTimeoutMilliseconds, Is.EqualTo(1234));
            Assert.That(copy.MaximumPixelCount, Is.EqualTo(4096));
            Assert.That(copy.ThreadCount, Is.EqualTo(3));
            Assert.That(copy.ConversionThreadCount, Is.EqualTo(2));
            Assert.That(copy.AllowHardwareCpuUpload, Is.True);
            Assert.That(copy.HardwareFallbackRoute, Is.EqualTo(HardwareFallbackRoute.OtherGpus));
            Assert.That(copy.HardwareGpus, Is.SameAs(gpus));
            Assert.That(copy.KeepNativeFrames, Is.False);
            Assert.That(copy.HardwareDeviceDescription, Is.Null);
            Assert.That(copy.FallbackHardwareOptions, Is.Null);
            Assert.That(copy.AcquireD3D11Device, Is.Null);
            Assert.That(copy.AcquireHardwareDevice, Is.Null);
            Assert.That(copy.MapHardwareFrame, Is.Null);
            Assert.That(copy.CreateHardwareSession, Is.Null);
            Assert.That(copy.CreateHardwareSynchronization, Is.Null);
            Assert.That(copy.CreateHardwareFrameMapper, Is.Null);
            Assert.That(copy.ConfigureHardwareFrames, Is.Null);
            Assert.That(copy.WaitForHardwareFrame, Is.Null);
            Assert.That(options.KeepNativeFrames, Is.True);
            Assert.That(options.AcquireHardwareDevice, Is.Not.Null);
        }

        /// <summary>Checks that a decoder options copy is shallow: values are copied and callbacks are shared.</summary>
        [Test]
        public void DecoderOptions_CopyIsShallow()
        {
            Func<IntPtr> acquire = () => IntPtr.Zero;
            var fallback = new DecoderOptions();
            var options = new DecoderOptions
            {
                IOTimeoutMilliseconds = 1234,
                KeepNativeFrames = true,
                HardwareDeviceDescription = "device",
                AcquireHardwareDevice = acquire,
                FallbackHardwareOptions = fallback
            };

            var copy = options.Copy();
            copy.IOTimeoutMilliseconds = 1;

            Assert.That(copy, Is.Not.SameAs(options));
            Assert.That(options.IOTimeoutMilliseconds, Is.EqualTo(1234));
            Assert.That(copy.KeepNativeFrames, Is.True);
            Assert.That(copy.HardwareDeviceDescription, Is.EqualTo("device"));
            Assert.That(copy.AcquireHardwareDevice, Is.SameAs(acquire));
            Assert.That(copy.FallbackHardwareOptions, Is.SameAs(fallback));
        }
    }
}
