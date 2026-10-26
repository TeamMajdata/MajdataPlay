using System;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies ownership, pooling and software copies of decoded presentation frames.</summary>
    public unsafe class DecodedVideoFrameTests
    {
        /// <summary>Checks that a native image frame requires a release callback.</summary>
        [Test]
        public void NativeImageConstructor_RequiresReleaseCallback()
        {
            Assert.Throws<ArgumentNullException>(() => new DecodedVideoFrame(new IntPtr(1), null!));
        }

        /// <summary>Checks that a native image is released exactly once and never reports CPU pixels.</summary>
        [Test]
        public void NativeImage_IsReleasedOnce()
        {
            var releases = 0;
            var image = new IntPtr(123);
            var frame = new DecodedVideoFrame(image, value =>
            {
                Assert.That(value, Is.EqualTo(image));
                releases++;
            });

            Assert.That(frame.IsHardwareFrame, Is.True);
            Assert.That(frame.NativeImage, Is.EqualTo(image));
            Assert.That(frame.Data, Is.EqualTo(IntPtr.Zero));
            Assert.That(frame.DataSize, Is.EqualTo(0));

            frame.Dispose();
            frame.Dispose();

            Assert.That(releases, Is.EqualTo(1));
            Assert.That(frame.IsHardwareFrame, Is.False);
            Assert.That(frame.NativeImage, Is.EqualTo(IntPtr.Zero));
        }

        /// <summary>Checks that native pixel and frame resources are still released when the image callback fails.</summary>
        [Test]
        public void Dispose_ReleaseFailure_StillFreesNativeResources()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            var frame = new DecodedVideoFrame(new IntPtr(5), _ => throw new InvalidOperationException("release"));
            frame.SetPixels((IntPtr)ffmpeg.av_malloc(16));
            frame.SetNativeFrame((IntPtr)ffmpeg.av_frame_alloc());

            var error = Assert.Throws<InvalidOperationException>(() => frame.Dispose());

            Assert.That(error!.Message, Is.EqualTo("release"));
            Assert.That(frame.Data, Is.EqualTo(IntPtr.Zero));
            Assert.That(frame.NativeFrame, Is.EqualTo(IntPtr.Zero));
        }

        /// <summary>Checks the CPU layout properties of an RGBA frame and the release of its buffer.</summary>
        [Test]
        public void CpuFrame_ReportsLayoutAndReleasesBuffer()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            var frame = new DecodedVideoFrame((IntPtr)ffmpeg.av_malloc(3 * 2 * 4), IntPtr.Zero)
            {
                Width = 3,
                Height = 2
            };

            Assert.That(frame.Stride, Is.EqualTo(12));
            Assert.That(frame.DataSize, Is.EqualTo(24));
            Assert.That(frame.IsHardwareFrame, Is.False);
            Assert.That(frame.PixelAspectRatio, Is.EqualTo(1));
            Assert.That(frame.TransferMode, Is.Null);

            frame.Dispose();

            Assert.That(frame.Data, Is.EqualTo(IntPtr.Zero));
            Assert.That(frame.DataSize, Is.EqualTo(0));
        }

        /// <summary>Checks that the RGBA stride overflows instead of wrapping for impossible widths.</summary>
        [Test]
        public void Stride_OverflowingWidth_Throws()
        {
            var frame = new DecodedVideoFrame(IntPtr.Zero, IntPtr.Zero)
            {
                Width = int.MaxValue
            };

            Assert.Throws<OverflowException>(() => _ = frame.Stride);
            frame.Dispose();
        }

        /// <summary>Checks that an owned native frame marks the frame as hardware and is freed on disposal.</summary>
        [Test]
        public void NativeFrame_IsOwnedAndFreed()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            var native = ffmpeg.av_frame_alloc();
            var frame = new DecodedVideoFrame(IntPtr.Zero, (IntPtr)native);

            Assert.That(frame.IsHardwareFrame, Is.True);
            Assert.That(frame.NativeFrame, Is.EqualTo((IntPtr)native));

            frame.Dispose();

            Assert.That(frame.NativeFrame, Is.EqualTo(IntPtr.Zero));
            Assert.That(frame.IsHardwareFrame, Is.False);
        }

        /// <summary>Checks that copying to software requires an owned native frame.</summary>
        [Test]
        public void CopyToSoftware_WithoutNativeFrame_Throws()
        {
            using var frame = new DecodedVideoFrame(IntPtr.Zero, IntPtr.Zero);

            Assert.Throws<InvalidOperationException>(() => frame.CopyToSoftware());
        }

        /// <summary>Checks that copying a retained CPU-format native frame produces an independent RGBA frame.</summary>
        [Test]
        public void CopyToSoftware_ConvertsNativeFrame()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            var native = NativeFrames.Allocate(4, 2, AVPixelFormat.AV_PIX_FMT_RGBA);
            NativeFrames.FillRgba(native);
            using var frame = new DecodedVideoFrame(IntPtr.Zero, (IntPtr)native)
            {
                PresentationTime = 2,
                Duration = 0.5,
                CurrentBitRate = 777
            };

            using var copy = frame.CopyToSoftware();

            Assert.That(copy, Is.Not.SameAs(frame));
            Assert.That(copy.Width, Is.EqualTo(4));
            Assert.That(copy.Height, Is.EqualTo(2));
            Assert.That(copy.HardwareDecoded, Is.True);
            Assert.That(copy.CurrentBitRate, Is.EqualTo(777));
            Assert.That(copy.TransferMode, Is.EqualTo("Hardware decode + CPU RGBA upload"));
            Assert.That(copy.PresentationTime, Is.EqualTo(2));
            Assert.That(NativeFrames.ReadPixel(copy.Data, 4, 1, 2), Is.EqualTo(NativeFrames.Pattern(2, 0)));
            Assert.That(frame.NativeFrame, Is.EqualTo((IntPtr)native));
        }

        /// <summary>Checks pool capacity validation.</summary>
        [Test]
        public void Pool_RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedVideoFramePool(0));
        }

        /// <summary>Checks that rented frames are reset, exhausted pools throw and disposal returns frames once.</summary>
        [Test]
        public void Pool_RentResetsAndReturnsFrames()
        {
            var pool = new DecodedVideoFramePool(2);
            var first = pool.Rent();
            first.Width = 10;
            first.Height = 4;
            first.PresentationTime = 3;
            first.Duration = 1;
            first.RotationDegrees = 90;
            first.CurrentBitRate = 5;
            first.PixelAspectRatio = 2;
            first.HardwareDecoded = true;
            first.TransferMode = "mode";
            first.PixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;
            var second = pool.Rent();

            Assert.Throws<InvalidOperationException>(() => pool.Rent());

            first.Dispose();
            first.Dispose();
            var again = pool.Rent();

            Assert.That(again, Is.SameAs(first));
            Assert.That(again.Width, Is.EqualTo(0));
            Assert.That(again.Height, Is.EqualTo(0));
            Assert.That(again.PresentationTime, Is.EqualTo(0));
            Assert.That(again.Duration, Is.EqualTo(0));
            Assert.That(again.RotationDegrees, Is.EqualTo(0));
            Assert.That(again.CurrentBitRate, Is.EqualTo(0));
            Assert.That(again.PixelAspectRatio, Is.EqualTo(1));
            Assert.That(again.HardwareDecoded, Is.False);
            Assert.That(again.TransferMode, Is.Null);
            Assert.That(again.PixelFormat, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_NONE));
            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            second.Dispose();
            again.Dispose();
        }

        /// <summary>Checks that a pooled frame owning a native image releases it before returning to the pool.</summary>
        [Test]
        public void Pool_ReturnsFrameAfterReleasingImage()
        {
            var pool = new DecodedVideoFramePool(1);
            var frame = pool.Rent();
            var released = IntPtr.Zero;
            frame.SetNativeImage(new IntPtr(9), value => released = value);

            frame.Dispose();

            Assert.That(released, Is.EqualTo(new IntPtr(9)));
            Assert.That(pool.Rent(), Is.SameAs(frame));
        }
    }
}
