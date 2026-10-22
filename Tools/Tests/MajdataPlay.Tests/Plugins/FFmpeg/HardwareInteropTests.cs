using System;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the managed side of the native graphics bridge: capability reporting, diagnostics and argument handling.</summary>
    public unsafe class HardwareInteropTests
    {
        /// <summary>Ignores the test when the native graphics bridge cannot be loaded.</summary>
        private static void RequireBridge()
        {
            try
            {
                _ = VulkanVideoInterop.VideoStatus();
            }
            catch (DllNotFoundException error)
            {
                Assert.Ignore("The FFmpeg graphics bridge is unavailable: " + error.Message);
            }
        }

        /// <summary>Checks that the reported capabilities and availability reason are consistent.</summary>
        [Test]
        public void Capabilities_AreConsistentWithAvailability()
        {
            var available = HardwareVideoPresenter.IsAvailable;

            if (available)
            {
                Assert.That(HardwareVideoPresenter.AvailabilityReason, Is.Null);
            }
            else
            {
                Assert.That(HardwareVideoPresenter.AvailabilityReason, Is.Not.Null.And.Not.Empty);
            }

            Assert.That(HardwareVideoPresenter.SupportsMetal, Is.False);
            Assert.That(HardwareVideoPresenter.SupportsLinuxVulkan, Is.False);
            Assert.That(HardwareVideoPresenter.SupportsAndroidVulkan, Is.False);
            if (HardwareVideoPresenter.SupportsD3D11 || HardwareVideoPresenter.SupportsD3D12Video || HardwareVideoPresenter.SupportsVulkanVideoDecoding)
            {
                Assert.That(available, Is.True);
            }
        }

        /// <summary>Checks the native-backend status description for both renderer-native APIs.</summary>
        [Test]
        public void DescribeNativeBackendStatus_ReportsStatus()
        {
            var d3d12 = HardwareVideoPresenter.DescribeNativeBackendStatus(GraphicsDeviceType.Direct3D12);
            var vulkan = HardwareVideoPresenter.DescribeNativeBackendStatus(GraphicsDeviceType.Vulkan);

            Assert.That(d3d12, Is.Not.Empty);
            Assert.That(vulkan, Is.Not.Empty);
            if (HardwareVideoPresenter.IsAvailable)
            {
                Assert.That(d3d12, Does.StartWith("D3D12VA status 0x"));
                Assert.That(vulkan, Does.Contain("native code"));
            }
        }

        /// <summary>Checks every documented bridge initialization status message.</summary>
        /// <param name="status">The native status.</param>
        /// <param name="fragment">A fragment of the expected message.</param>
        [TestCase(1, "has not initialized")]
        [TestCase(2, "could not resolve")]
        [TestCase(3, "no implemented native")]
        [TestCase(4, "D3D11 device interface")]
        [TestCase(5, "D3D11 graphics device")]
        [TestCase(6, "immediate context")]
        [TestCase(7, "Metal")]
        [TestCase(8, "D3D12 V8")]
        [TestCase(9, "D3D12 graphics device")]
        [TestCase(10, "Preload")]
        [TestCase(100, "Vulkan sharing extensions")]
        [TestCase(101, "conversion shader")]
        [TestCase(200, "renderD")]
        [TestCase(201, "without VAAPI")]
        [TestCase(202, "VAAPI driver")]
        [TestCase(300, "API 26")]
        [TestCase(77, "status 0x0000004D")]
        public void DescribeInitializationFailure_ExplainsStatus(int status, string fragment)
        {
            var message = PrivateMember.InvokeStatic<string>(typeof(HardwareVideoPresenter), "DescribeInitializationFailure", status);

            Assert.That(message, Does.Contain(fragment));
        }

        /// <summary>Checks every documented native transport error message and the numeric suffix.</summary>
        [Test]
        public void DescribeError_ExplainsEveryCode()
        {
            var codes = new[] { 102, 103, 104, 203, 204, 205, 206, 207, 208, 2001, 2002, 2003, 2004, 2006, 2010, 400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 414, 415, 416 };
            var generic = VulkanVideoInterop.DescribeError(1);
            foreach (var code in codes)
            {
                var message = VulkanVideoInterop.DescribeError(-code);
                Assert.That(message, Does.EndWith("(native code " + -code + ")"));
                Assert.That(message, Is.Not.EqualTo(generic.Replace("(native code 1)", "(native code " + -code + ")")), "code " + code);
            }

            Assert.That(generic, Does.StartWith("The native hardware image operation failed."));
            Assert.That(VulkanVideoInterop.DescribeError(int.MinValue), Does.Contain(int.MinValue.ToString()));
        }

        /// <summary>Checks the platform-gated interop entry points that are unavailable on Windows.</summary>
        [Test]
        public void PlatformGatedEntryPoints_AreNotSupportedOnWindows()
        {
            Assert.Throws<PlatformNotSupportedException>(() => VulkanVideoInterop.AcquireLinuxDevice());
            Assert.Throws<PlatformNotSupportedException>(() => VulkanVideoInterop.MapLinuxFrame(IntPtr.Zero));
            Assert.Throws<PlatformNotSupportedException>(() => VulkanVideoInterop.CreateAndroidSession(16, 16));
            Assert.Throws<PlatformNotSupportedException>(() => VulkanVideoInterop.InitializeAndroid());
            Assert.Throws<PlatformNotSupportedException>(() => AndroidContentInterop.Initialize());
        }

        /// <summary>Checks the native bridge's handling of null and foreign arguments.</summary>
        [Test]
        public void NativeEntryPoints_RejectInvalidArguments()
        {
            RequireBridge();
            FFmpegTestMedia.RequireNativeLibraries();

            Assert.That(VulkanVideoInterop.GetD3D11StageSource(IntPtr.Zero), Is.EqualTo(IntPtr.Zero));
            Assert.That(VulkanVideoInterop.ConfigureVideoFrames(IntPtr.Zero), Is.LessThan(0));
            Assert.Throws<NotSupportedException>(() => VulkanVideoInterop.CreateD3D11Synchronization(IntPtr.Zero));
            Assert.Throws<NotSupportedException>(() => VulkanVideoInterop.CreateD3D11FrameMapper(IntPtr.Zero));
            Assert.Throws<NotSupportedException>(() => VulkanVideoInterop.WaitForVideoFrame(IntPtr.Zero, CancellationToken.None, 100));
            Assert.Throws<NotSupportedException>(() => D3D12VideoInterop.WaitForVideoFrame(IntPtr.Zero, CancellationToken.None, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => D3D12VideoInterop.WaitForVideoFrame(IntPtr.Zero, CancellationToken.None, 0));
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.Throws<OperationCanceledException>(() => D3D12VideoInterop.WaitForVideoFrame(IntPtr.Zero, source.Token, 100));
                Assert.Throws<OperationCanceledException>(() => VulkanVideoInterop.WaitForVideoFrame(IntPtr.Zero, source.Token, 100));
            }

            Assert.DoesNotThrow(() => VulkanVideoInterop.Error(IntPtr.Zero));
            var presenter = VulkanVideoInterop.Create();
            if (presenter != IntPtr.Zero)
            {
                Assert.That(VulkanVideoInterop.Error(presenter), Is.EqualTo(0));
                VulkanVideoInterop.Release(presenter);
            }

            Assert.DoesNotThrow(() => VulkanVideoInterop.Cancel(IntPtr.Zero));
            Assert.DoesNotThrow(() => VulkanVideoInterop.Release(IntPtr.Zero));
            Assert.That(VulkanVideoInterop.PollRetiredFrames(), Is.GreaterThanOrEqualTo(0));
            Assert.That(HardwareVideoPresenter.CollectRetiredTextures(), Is.True);
        }

        /// <summary>Checks that a foreign CPU frame is never reported as a completed D3D12 frame.</summary>
        [Test]
        public void D3D12Wait_ForeignFrame_IsNotSupported()
        {
            RequireBridge();
            FFmpegTestMedia.RequireNativeLibraries();
            var frame = NativeFrames.Allocate(4, 4, AVPixelFormat.AV_PIX_FMT_RGBA);
            try
            {
                var error = Assert.Throws<NotSupportedException>(() => D3D12VideoInterop.WaitForVideoFrame((IntPtr)frame, CancellationToken.None, 100));

                Assert.That(error!.Message, Does.Contain("D3D12VA GPU completion query failed"));
            }
            finally
            {
                NativeFrames.Free(frame);
            }
        }

        /// <summary>Checks worker-side D3D11VA completion queries on a real hardware device.</summary>
        [Test]
        public void D3D11Synchronization_WaitsOnRealDevice()
        {
            RequireBridge();
            FFmpegTestMedia.RequireNativeLibraries();
            AVBufferRef* device = null;
            if (ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
            {
                Assert.Ignore("D3D11VA is unavailable on this machine.");
            }

            try
            {
                using var synchronization = VulkanVideoInterop.CreateD3D11Synchronization((IntPtr)device);
                synchronization.Wait(CancellationToken.None, 5000);
                synchronization.Wait(CancellationToken.None, 5000);
                using var source = new CancellationTokenSource();
                source.Cancel();

                Assert.Throws<OperationCanceledException>(() => synchronization.Wait(source.Token, 5000));
                synchronization.Dispose();
            }
            finally
            {
                ffmpeg.av_buffer_unref(&device);
            }
        }

        /// <summary>Checks that a D3D11 staging pool can be created for a real device and released.</summary>
        [Test]
        public void D3D11FrameMapper_CreatesAndReleasesStagingPool()
        {
            RequireBridge();
            FFmpegTestMedia.RequireNativeLibraries();
            AVBufferRef* device = null;
            if (ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
            {
                Assert.Ignore("D3D11VA is unavailable on this machine.");
            }

            try
            {
                MajdataPlay.FFmpeg.Internal.IHardwareFrameMapper? mapper = null;
                try
                {
                    mapper = VulkanVideoInterop.CreateD3D11FrameMapper((IntPtr)device);
                }
                catch (NotSupportedException error)
                {
                    Assert.That(error.Message, Does.Contain("independent GPU presentation surfaces"));
                    return;
                }

                mapper!.Dispose();
                mapper.Dispose();
            }
            finally
            {
                ffmpeg.av_buffer_unref(&device);
            }
        }
    }
}
