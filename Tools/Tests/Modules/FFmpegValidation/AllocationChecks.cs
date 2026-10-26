using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;

internal static unsafe class AllocationChecks
{
    /// <summary>Sets the number of frames encoded before measuring, which covers encoder start-up and buffer growth.</summary>
    private const int EncodingWarmupFrames = 120;
    /// <summary>Sets the number of steady-state frames whose encoder-thread allocations are measured.</summary>
    private const int EncodingMeasuredFrames = 300;
    private static int _checks;

    public static int RunManaged()
    {
        int before = _checks;
        long baseline = GC.GetAllocatedBytesForCurrentThread();
        var allocationProbe = new byte[256];
        long measured = GC.GetAllocatedBytesForCurrentThread() - baseline;
        GC.KeepAlive(allocationProbe);
        Check(measured >= 256, "The runtime allocation counter must observe a known managed allocation.");
        var pool = new DecodedVideoFramePool(2);
        var first = pool.Rent();
        var second = pool.Rent();
        Check(!ReferenceEquals(first, second), "Live frame leases must not alias.");
        CheckExhausted(pool);
        first.Width = 12;
        first.Height = 9;
        first.PixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;
        first.PresentationTime = 1.25;
        first.Duration = 0.04;
        first.CurrentBitRate = 123456;
        first.RotationDegrees = 90;
        first.PixelAspectRatio = 2;
        first.HardwareDecoded = true;
        first.TransferMode = "test transport";
        int releases = 0;
        first.SetNativeImage(new IntPtr(123), image =>
        {
            Check(image == new IntPtr(123), "Pooled native image retains its identity.");
            releases++;
        });
        first.Dispose();
        first.Dispose();
        Check(releases == 1, "Repeated disposal releases an image only once.");

        var reused = pool.Rent();
        Check(ReferenceEquals(first, reused), "A returned frame container is reused.");
        Check(reused.Width == 0 && reused.Height == 0 && reused.PixelFormat == AVPixelFormat.AV_PIX_FMT_NONE &&
            reused.PresentationTime == 0 && reused.Duration == 0 && reused.CurrentBitRate == 0 &&
            reused.RotationDegrees == 0 && reused.PixelAspectRatio == 1 && !reused.HardwareDecoded &&
            reused.TransferMode == null && reused.Data == IntPtr.Zero && reused.NativeFrame == IntPtr.Zero &&
            reused.NativeImage == IntPtr.Zero && reused.DataSize == 0 && !reused.IsHardwareFrame,
            "A new lease resets every presentation field and native pointer.");
        CheckExhausted(pool);
        var releaseThread = new Thread(reused.Dispose);
        releaseThread.Start();
        Check(releaseThread.Join(TimeSpan.FromSeconds(5)), "A frame can be returned from another thread.");
        reused = pool.Rent();
        Check(ReferenceEquals(first, reused) && !ReferenceEquals(second, reused),
            "Cross-thread return does not recycle a live frame.");
        reused.Dispose();
        second.Dispose();
        TestFailedRelease();

        for (int index = 0; index < 64; index++)
            pool.Rent().Dispose();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 4096; index++)
            pool.Rent().Dispose();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        CheckZeroAllocation(allocated, "Frame pool", 4096);
        return _checks - before;
    }

    public static int RunNative(string media, bool includeHardware)
    {
        int before = _checks;
        TestConverter(AVPixelFormat.AV_PIX_FMT_RGBA);
        TestConverter(AVPixelFormat.AV_PIX_FMT_YUV420P);
        _checks += ConversionThreadChecks.RunAllocation();
        TestDecoder(media, new DecoderOptions(), false, false, "Software decoder");
        TestPublicFrameOwnership(media);
        TestSessionStress(media, 1);
        TestSessionStress(media, 8);
        if (includeHardware)
        {
            TestDecoder(media, new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = false,
                AllowHardwareCpuUpload = true
            }, true, false, "D3D11VA CPU upload");
            TestDecoder(media, new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AllowHardwareCpuUpload = false,
                RequireHardwareDecoding = true,
                AcquireHardwareDevice = CreateD3D11Device
            }, true, true, "D3D11VA native frame");
        }
        return _checks - before;
    }

    private static void TestFailedRelease()
    {
        var pool = new DecodedVideoFramePool(1);
        var frame = pool.Rent();
        int releases = 0;
        frame.SetNativeImage(new IntPtr(321), image =>
        {
            releases++;
            throw new InvalidOperationException("Expected image release failure.");
        });
        bool reported = false;
        try { frame.Dispose(); }
        catch (InvalidOperationException error) { reported = error.Message == "Expected image release failure."; }
        frame.Dispose();
        Check(reported && releases == 1, "A failing image release is reported once.");
        using (var next = pool.Rent())
        {
            Check(ReferenceEquals(frame, next) && next.NativeImage == IntPtr.Zero,
                "A failing image release still returns its cleared container to the pool.");
            CheckExhausted(pool);
        }
    }

    private static void TestConverter(AVPixelFormat format)
    {
        var source = ffmpeg.av_frame_alloc();
        if (source == null) throw new OutOfMemoryException();
        try
        {
            source->width = 16;
            source->height = 8;
            source->format = (int)format;
            source->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(source, 32), "Allocate allocation-test fixture");
            int planes = format == AVPixelFormat.AV_PIX_FMT_RGBA ? 1 : 3;
            for (int plane = 0; plane < planes; plane++)
            {
                int height = plane == 0 ? source->height : source->height / 2;
                for (int index = 0; index < source->linesize[(uint)plane] * height; index++)
                    source->data[(uint)plane][index] = 128;
            }
            using (var converter = new VideoFrameConverter(new DecodedVideoFramePool(2)))
            {
                for (int index = 0; index < 64; index++)
                    converter.Convert(source, 0, 0.04, index % 4 * 90, 1024).Dispose();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 512; index++)
                    converter.Convert(source, index * 0.04, 0.04, index % 4 * 90, 1024).Dispose();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                CheckZeroAllocation(allocated, "Converter " + format, 512);
                using (var first = converter.Convert(source, 1, 0.04, 0, 1024))
                using (var second = converter.Convert(source, 2, 0.04, 90, 1024))
                {
                    Check(!ReferenceEquals(first, second) && first.Data != second.Data,
                        "Concurrent conversion results independently own their pixels.");
                    Check(first.PresentationTime == 1 && first.Width == 16 && first.Height == 8 &&
                        second.PresentationTime == 2 && second.Width == 8 && second.Height == 16,
                        "A second conversion does not overwrite a live frame's metadata.");
                }
            }
        }
        finally { ffmpeg.av_frame_free(&source); }
    }

    private static void TestDecoder(string media, DecoderOptions options, bool hardware, bool native, string label)
    {
        var pool = new DecodedVideoFramePool(4);
        using (var decoder = new FFmpegVideoDecoder(options, pool))
        {
            decoder.Open(media, CancellationToken.None);
            for (int index = 0; index < 32; index++)
            {
                using (var frame = decoder.ReadFrame())
                    Check(frame != null, "Allocation fixture must contain at least 32 warmup frames.");
            }
            decoder.Seek(0);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int frames = 0;
            bool valid = true;
            double previous = double.NegativeInfinity;
            for (int index = 0; index < 120; index++)
            {
                using (var frame = decoder.ReadFrame())
                {
                    if (frame == null) break;
                    valid &= frame.HardwareDecoded == hardware && frame.IsHardwareFrame == native &&
                        frame.PresentationTime >= previous && frame.Width > 0 && frame.Height > 0;
                    valid &= native ? frame.NativeFrame != IntPtr.Zero && frame.Data == IntPtr.Zero :
                        frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4;
                    previous = frame.PresentationTime;
                    frames++;
                }
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(frames >= 32 && valid, label + " must preserve actual transport, pixels and ordered timestamps.");
            CheckZeroAllocation(allocated, label, frames);

            decoder.Seek(0);
            var held = decoder.ReadFrame();
            Check(held != null, label + " returns a frame to hold beyond decoder disposal.");
            try
            {
                using (var other = decoder.ReadFrame())
                    Check(other != null && !ReferenceEquals(held, other), label + " never reuses a live frame.");
                decoder.Dispose();
                Check(native ? held.NativeFrame != IntPtr.Zero : held.Data != IntPtr.Zero,
                    label + " held frame resources survive decoder disposal.");
                if (!native) _ = Marshal.ReadByte(held.Data);
            }
            finally
            {
                held.Dispose();
                held.Dispose();
            }
            Check(held.Data == IntPtr.Zero && held.NativeFrame == IntPtr.Zero,
                label + " held resources are released after decoder disposal.");
            using (var first = pool.Rent())
            using (var second = pool.Rent())
            using (var third = pool.Rent())
            using (var fourth = pool.Rent())
                CheckExhausted(pool);
        }
    }

    private static void TestPublicFrameOwnership(string media)
    {
        using (var decoder = new FFmpegVideoDecoder())
        {
            decoder.Open(media, CancellationToken.None);
            var previous = decoder.ReadFrame();
            Check(previous != null, "Public decoder returns an independently owned frame.");
            previous.Dispose();
            using (var next = decoder.ReadFrame())
            {
                Check(next != null && !ReferenceEquals(previous, next), "Public decoder does not recycle old objects.");
                IntPtr pixels = next.Data;
                previous.Dispose();
                Check(pixels != IntPtr.Zero && next.Data == pixels && previous.Data == IntPtr.Zero,
                    "Disposing an old public frame cannot release a later frame's pixels.");
                _ = Marshal.ReadByte(next.Data);
            }
        }
    }

    private static void TestSessionStress(string media, int capacity)
    {
        using (var session = new VideoDecodeSession(media, new DecoderOptions(), capacity))
        {
            WaitBuffered(session, capacity);
            var held = session.TakeFrame();
            Check(held != null, "Session stress obtains the presenter's held frame.");
            try
            {
                for (int index = 0; index < 48; index++)
                {
                    WaitBuffered(session, capacity);
                    var next = session.TakeFrame();
                    Check(next != null && !ReferenceEquals(held, next), "Session never recycles the displayed frame.");
                    Check(next.PresentationTime >= held.PresentationTime, "Session frames preserve presentation order.");
                    held.Dispose();
                    held = next;
                }
                // Keep the renderer's old frame alive while queued frames are flushed
                // and the worker may be finishing an obsolete revision.
                for (int index = 0; index < 16; index++) session.Seek(index % 2 == 0 ? 0.25 : 1);
                session.Seek(0.5);
                WaitBuffered(session, capacity);
                using (var next = session.TakeFrame())
                {
                    Check(next != null && !ReferenceEquals(held, next), "Seek preserves ownership of the displayed frame.");
                    Check(next.PresentationTime >= 0.45 && next.PresentationTime < 0.7,
                        "Session stress presents the latest seek revision.");
                }
                IntPtr pixels = held.Data;
                byte sample = Marshal.ReadByte(pixels);
                session.Dispose();
                Check(held.Data == pixels && Marshal.ReadByte(held.Data) == sample,
                    "Closing a session preserves the displayed frame until it is returned.");
                Check(session.Error == null, "Bounded session stress has no pool exhaustion or worker failure.");
            }
            finally { held.Dispose(); }
        }
        Console.WriteLine("Session capacity " + capacity + ": held-frame playback, repeated seek and close passed.");
    }

    private static void WaitBuffered(VideoDecodeSession session, int count)
    {
        long start = Stopwatch.GetTimestamp();
        while (session.BufferedFrames < count)
        {
            if (session.Error != null) throw session.Error;
            if (Stopwatch.GetTimestamp() - start > 20 * Stopwatch.Frequency) throw new TimeoutException("Session stress did not refill its queue.");
            Thread.Sleep(1);
        }
        if (session.Error != null) throw session.Error;
    }

    private static IntPtr CreateD3D11Device()
    {
        AVBufferRef* device = null;
        FFmpegVideoDecoder.Check(ffmpeg.av_hwdevice_ctx_create(&device,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "Create allocation-test D3D11 device");
        return (IntPtr)device;
    }

    private static void CheckExhausted(DecodedVideoFramePool pool)
    {
        bool rejected = false;
        try { using (pool.Rent()) { } }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "A full pool rejects a new lease without duplicating or expanding its containers.");
    }

    /// <summary>Requires steady-state camera encoding to allocate no managed memory on its encoder thread.</summary>
    /// <param name="outputDirectory">An isolated parent directory for the temporary Matroska outputs.</param>
    /// <returns>The number of assertions that passed.</returns>
    public static int RunEncoding(string outputDirectory)
    {
        var before = _checks;
        var directory = Path.Combine(Path.GetFullPath(outputDirectory), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        TestEncoderThread(Path.Combine(directory, "encoder.mkv"));
        TestEncodeSession(Path.Combine(directory, "session.mkv"));
        return _checks - before;
    }

    /// <summary>Measures the exact managed allocation of the encoder calls the capture worker makes for each frame.</summary>
    /// <param name="path">The new Matroska output, the same container as the capturer's temporary video.</param>
    private static void TestEncoderThread(string path)
    {
        var options = CreateCaptureOptions();
        var pixels = new byte[options.Width * options.Height * 4];
        using (var encoder = new FFmpegVideoEncoder(options))
        {
            encoder.Open(path, CancellationToken.None);
            var frameIndex = 0L;
            for (var frame = 0; frame < EncodingWarmupFrames; frame++)
            {
                frameIndex = EncodeChangedFrame(encoder, pixels, frameIndex, frame);
            }

            var bytesWritten = encoder.BytesWritten;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < EncodingMeasuredFrames; frame++)
            {
                frameIndex = EncodeChangedFrame(encoder, pixels, frameIndex, frame);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(encoder.BytesWritten > bytesWritten, "The measured frames must write packets through the encoder's muxer callbacks.");
            CheckZeroAllocation(allocated, "Camera encoder " + encoder.EncoderName + " encode and mux", EncodingMeasuredFrames);
            encoder.Complete();
        }
    }

    /// <summary>Drives the production capture session and requires its encoder worker to stay allocation-free.</summary>
    /// <param name="path">The new Matroska output.</param>
    /// <remarks>
    /// The worker thread cannot be measured from outside, so the process-wide counter is used while this thread only reserves and
    /// completes buffers. A budget of one byte per frame tolerates runtime background work but not a per-frame allocation.
    /// </remarks>
    private static void TestEncodeSession(string path)
    {
        var session = new VideoEncodeSession(path, CreateCaptureOptions(), 3, CancellationToken.None);
        Check(session.Ready.Wait(TimeSpan.FromSeconds(30)), "The capture session opens its encoder.");
        var frameIndex = DriveSession(session, 0, EncodingWarmupFrames);
        var allocated = GC.GetTotalAllocatedBytes(true);
        frameIndex = DriveSession(session, frameIndex, EncodingMeasuredFrames);
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        session.Stop();
        Check(session.Completion.Wait(TimeSpan.FromSeconds(30)), "The capture session finalizes its output.");
        Check(session.Error == null && session.EncodedFrames == frameIndex, "The capture session encodes every submitted frame.");
        Check(allocated < EncodingMeasuredFrames, "Capture session worker allocated " + allocated + " managed bytes for "
            + EncodingMeasuredFrames + " frames.");
        Console.WriteLine("Capture session worker: " + allocated + " process-wide managed bytes / " + EncodingMeasuredFrames
            + " frames (after warmup).");
    }

    /// <summary>Submits frames like completed GPU readbacks and waits until the worker has encoded all of them.</summary>
    /// <param name="session">The open capture session.</param>
    /// <param name="firstFrameIndex">The capture timestamp of the first submitted frame.</param>
    /// <param name="frames">The number of frames to submit.</param>
    /// <returns>The timestamp following the last submitted frame.</returns>
    private static long DriveSession(VideoEncodeSession session, long firstFrameIndex, int frames)
    {
        var frameIndex = firstFrameIndex;
        for (var frame = 0; frame < frames; frame++)
        {
            VideoEncodeSession.CaptureFrame reserved;
            while ((reserved = session.TryReserve(frameIndex, false)) == null)
            {
                Thread.Sleep(1);
            }
            session.CompleteReadback(reserved, true);
            frameIndex++;
        }
        // A timestamp avoids allocating a Stopwatch inside the measured window.
        var deadline = Stopwatch.GetTimestamp() + (30 * Stopwatch.Frequency);
        while (session.EncodedFrames < frameIndex && Stopwatch.GetTimestamp() < deadline)
        {
            Thread.Sleep(1);
        }
        return frameIndex;
    }

    /// <summary>Changes a few pixels and encodes them, skipping a timestamp now and then like a dropped capture.</summary>
    /// <param name="encoder">The open encoder.</param>
    /// <param name="pixels">The reusable packed RGBA frame.</param>
    /// <param name="frameIndex">The timestamp of this frame.</param>
    /// <param name="frame">The loop counter that selects the changed pixels.</param>
    /// <returns>The timestamp of the next frame.</returns>
    private static long EncodeChangedFrame(FFmpegVideoEncoder encoder, byte[] pixels, long frameIndex, int frame)
    {
        for (var offset = (frame * 4099) % 4096; offset < pixels.Length; offset += 4096)
        {
            pixels[offset] ^= 0x5A;
        }
        encoder.Encode(pixels, frameIndex, false);
        return frameIndex + (frame % 7 == 6 ? 2 : 1);
    }

    /// <summary>Creates small H.264 options that use the capturer's software encoder, container, and rate control.</summary>
    /// <returns>The encoder options.</returns>
    private static EncoderOptions CreateCaptureOptions()
    {
        return new EncoderOptions
        {
            Width = 320,
            Height = 192,
            FrameRate = 60,
            PreferredEncoderType = VideoEncoderType.Software,
            MaximumSoftwareThreads = 2,
            Format = VideoEncodingFormat.H264,
            BitRate = 1_000_000,
            MaximumBitRate = 2_000_000,
            RateControlMode = VideoRateControlMode.VBR
        };
    }

    private static void CheckZeroAllocation(long allocated, string label, int frames)
    {
        Check(allocated == 0, label + " allocated " + allocated + " managed bytes for " + frames + " frames.");
        Console.WriteLine(label + ": " + allocated + " managed bytes / " + frames + " frames (after warmup).");
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
}
