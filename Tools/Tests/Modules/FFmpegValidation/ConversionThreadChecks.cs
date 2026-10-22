#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg.Internal;

/// <summary>Validates threaded RGBA conversion against single-threaded conversion with real libswscale.</summary>
/// <remarks>No Unity graphics device or hardware decoder is required.</remarks>
internal static unsafe class ConversionThreadChecks
{
    /// <summary>Lists the YUV source formats whose threaded output must match single-threaded output.</summary>
    private static readonly AVPixelFormat[] s_formats =
    {
        AVPixelFormat.AV_PIX_FMT_YUV420P,
        AVPixelFormat.AV_PIX_FMT_YUV420P10LE,
        AVPixelFormat.AV_PIX_FMT_NV12,
        AVPixelFormat.AV_PIX_FMT_P010LE,
        AVPixelFormat.AV_PIX_FMT_YUV444P
    };
    /// <summary>Lists tall, odd frame heights that give every thread at least 64 rows and an uneven last band.</summary>
    private static readonly int[] s_heights = { 513, 1079, 1081 };
    /// <summary>Counts assertions made by this validation.</summary>
    private static int s_checks;

    /// <summary>Runs executor, byte-exact conversion, allocation, decoder wiring, and timing checks.</summary>
    /// <param name="media">A seekable media file decoded by the software decoder.</param>
    /// <returns>The number of assertions made.</returns>
    /// <exception cref="Exception">A check failed.</exception>
    public static int Run(string media)
    {
        var before = s_checks;
        CheckDefaultBudget();
        CheckExecutor();
        CheckByteExact();
        CheckZeroAllocation();
        CheckDecoder(media);
        PrintTiming();
        return s_checks - before;
    }

    /// <summary>Runs only the process-wide zero-allocation check for threaded conversion.</summary>
    /// <returns>The number of assertions made.</returns>
    /// <exception cref="Exception">A check failed.</exception>
    public static int RunAllocation()
    {
        var before = s_checks;
        CheckZeroAllocation();
        return s_checks - before;
    }

    /// <summary>Checks the default conversion thread budget and its relation to the codec thread budget.</summary>
    private static void CheckDefaultBudget()
    {
        var cores = Environment.ProcessorCount;
        var expected = cores <= 4 ? 1 : Math.Clamp(cores - 2, 1, 4);
        var options = new DecoderOptions();
        Check(options.ConversionThreadCount == expected, "Default conversion threads are clamp(cores - 2, 1, 4), or 1 with at most four cores.");
        Check(options.Copy().ConversionThreadCount == expected, "Option copies retain the conversion thread count.");
        Console.WriteLine("Conversion thread budget: " + options.ConversionThreadCount + " conversion / " + options.ThreadCount
            + " codec threads on " + cores + " logical processors.");
    }

    /// <summary>Checks job identity, completion, exception propagation, and lazy thread creation of the band executor.</summary>
    private static void CheckExecutor()
    {
        var executor = new VideoBandExecutor(4);
        try
        {
            var first = new RecordingJob();
            var second = new RecordingJob();
            executor.Run(first, 1);
            Check(executor.StartedThreadCount == 0 && first.Counts[0] == 1, "A single band runs inline without starting threads.");
            first.Reset();
            var sawWorker = false;
            for (var run = 0; run < 20000; run++)
            {
                var job = (run & 1) == 0 ? first : second;
                var other = (run & 1) == 0 ? second : first;
                var bandCount = 2 + (run % 23);
                job.SlowBand = run % 7 == 0 ? run % bandCount : -1;
                executor.Run(job, bandCount);
                for (var band = 0; band < RecordingJob.Capacity; band++)
                {
                    var expected = band < bandCount ? 1 : 0;
                    if (job.Counts[band] != expected || job.Completed[band] != expected || other.Counts[band] != 0)
                    {
                        Check(false, "Run " + run + " executed band " + band + " of the wrong job or not exactly once.");
                    }

                    sawWorker |= expected == 1 && job.ThreadIds[band] != Environment.CurrentManagedThreadId;
                }

                job.Reset();
            }

            Check(true, "20000 alternating runs execute each band of only the published job exactly once and complete it before returning.");
            Check(executor.StartedThreadCount == 3 && sawWorker, "Multi-band runs start the lazy band threads and use them.");

            first.FailingBand = 5;
            first.SlowBand = 2;
            var rethrown = false;
            try
            {
                executor.Run(first, 8);
            }
            catch (InvalidOperationException error)
            {
                rethrown = error.Message == RecordingJob.FailureMessage;
            }

            var allCompleted = true;
            for (var band = 0; band < 8; band++)
            {
                allCompleted &= first.Counts[band] == 1 && first.Completed[band] == 1;
            }

            Check(rethrown && allCompleted, "A failing band is rethrown only after every claimed band has completed.");
            first.Reset();
            first.FailingBand = -1;
            first.SlowBand = -1;
            executor.Run(first, 8);
            Check(first.Counts[7] == 1 && first.Completed[0] == 1, "The executor remains usable after a band failure.");
        }
        finally
        {
            executor.Dispose();
        }

        var disposedRejected = false;
        try
        {
            executor.Run(new RecordingJob(), 2);
        }
        catch (ObjectDisposedException)
        {
            disposedRejected = true;
        }

        Check(disposedRejected, "A disposed executor rejects new jobs.");
        Console.WriteLine("Band executor: job identity, completion, rethrow and lazy threads passed.");
    }

    /// <summary>Checks byte-identical RGBA output for 1 versus 2 through 8 threads across formats, sizes, ranges, and rotations.</summary>
    private static void CheckByteExact()
    {
        var converters = new VideoFrameConverter[9];
        for (var threads = 1; threads <= 8; threads++)
        {
            converters[threads] = new VideoFrameConverter(null, threads);
        }

        var comparisons = 0;
        var watch = Stopwatch.StartNew();
        try
        {
            foreach (var format in s_formats)
            {
                foreach (var height in s_heights)
                {
                    for (var width = 1; width <= 65; width++)
                    {
                        comparisons += CompareThreads(converters, format, width, height);
                    }
                }

                // Quarter turns band output rows, which are source columns, so wide short frames
                // give every thread at least 64 rotated rows and an uneven last band.
                foreach (var width in s_heights)
                {
                    foreach (var height in new[] { 1, 2, 3, 15, 16, 17, 33, 65 })
                    {
                        comparisons += CompareThreads(converters, format, width, height);
                    }
                }

                // A wide frame exercises full SIMD rows and the vertical-chroma path on every thread.
                comparisons += CompareThreads(converters, format, 1920, 1081);
            }
        }
        finally
        {
            for (var threads = 1; threads <= 8; threads++)
            {
                converters[threads].Dispose();
            }
        }

        Console.WriteLine("Threaded conversion: " + comparisons + " byte-exact 1-vs-N comparisons (N = 2..8) in "
            + watch.Elapsed.TotalSeconds.ToString("F1") + " s.");
    }

    /// <summary>Converts one source size in every range and rotation and compares each thread count with one thread.</summary>
    /// <param name="converters">Converters indexed by thread count, from 1 through 8.</param>
    /// <param name="format">The source pixel format.</param>
    /// <param name="width">The source width in pixels.</param>
    /// <param name="height">The source height in pixels.</param>
    /// <returns>The number of byte-exact comparisons made.</returns>
    private static int CompareThreads(VideoFrameConverter[] converters, AVPixelFormat format, int width, int height)
    {
        var comparisons = 0;
        var frame = CreateFrame(format, width, height);
        try
        {
            foreach (var range in new[] { AVColorRange.AVCOL_RANGE_MPEG, AVColorRange.AVCOL_RANGE_JPEG })
            {
                frame->color_range = range;
                for (var turn = 0; turn < 4; turn++)
                {
                    using (var reference = converters[1].Convert(frame, 0, 0.04, turn * 90, int.MaxValue / 4))
                    {
                        if (converters[1].ScaleThreadCount != 1 || converters[1].CopyBandCount != 1 || converters[1].StartedCopyThreadCount != 0)
                        {
                            Check(false, "One conversion thread must use one libswscale thread and an inline copy.");
                        }

                        CheckReference(reference, format, width, height, range, turn);
                        for (var threads = 2; threads <= 8; threads++)
                        {
                            var converter = converters[threads];
                            using (var result = converter.Convert(frame, 0, 0.04, turn * 90, int.MaxValue / 4))
                            {
                                // Bands partition output rows: source rows unrotated, source columns for quarter turns.
                                var outputRows = (turn & 1) == 0 ? height : width;
                                if (converter.ScaleThreadCount != threads || converter.CopyBandCount < Math.Min(2, outputRows / 64))
                                {
                                    Check(false, threads + " conversion threads used " + converter.ScaleThreadCount
                                        + " libswscale threads and " + converter.CopyBandCount + " copy bands for "
                                        + width + "x" + height + " " + format + ".");
                                }

                                if (result.DataSize != reference.DataSize || result.Width != reference.Width || result.Height != reference.Height
                                    || !new ReadOnlySpan<byte>((void*)result.Data, result.DataSize).SequenceEqual(
                                        new ReadOnlySpan<byte>((void*)reference.Data, reference.DataSize)))
                                {
                                    Check(false, threads + "-thread RGBA differs from 1 thread at " + width + "x" + height + " "
                                        + format + " " + range + " rotation " + (turn * 90) + ".");
                                }

                                comparisons++;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
        }

        Check(true, "Threads 2..8 match one thread at " + width + "x" + height + " " + format + ".");
        return comparisons;
    }

    /// <summary>Checks the single-threaded output against FFmpeg's single-call sws_scale and the expected orientation.</summary>
    /// <param name="reference">The single-threaded converter output.</param>
    /// <param name="format">The source pixel format.</param>
    /// <param name="width">The source width in pixels.</param>
    /// <param name="height">The source height in pixels.</param>
    /// <param name="range">The source color range.</param>
    /// <param name="turn">The clockwise rotation in quarter turns.</param>
    private static void CheckReference(DecodedVideoFrame reference, AVPixelFormat format, int width, int height, AVColorRange range, int turn)
    {
        if (width % 16 != 1 || range != AVColorRange.AVCOL_RANGE_MPEG)
        {
            return;
        }

        // Compare a sample of configurations with an independent sws_scale conversion into an aligned frame.
        var frame = CreateFrame(format, width, height);
        var rgba = ffmpeg.av_frame_alloc();
        var context = ffmpeg.sws_getContext(width, height, format, width, height, AVPixelFormat.AV_PIX_FMT_RGBA,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        try
        {
            frame->color_range = range;
            rgba->width = width;
            rgba->height = height;
            rgba->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
            FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(rgba, 64), "Allocate reference RGBA frame");
            var matrix = height > 576 ? ffmpeg.SWS_CS_ITU709 : ffmpeg.SWS_CS_ITU601;
            var coefficients = *(int_array4*)ffmpeg.sws_getCoefficients(matrix);
            FFmpegVideoDecoder.Check(ffmpeg.sws_setColorspaceDetails(context, in coefficients, 0, in coefficients, 1, 0, 1 << 16, 1 << 16),
                "Configure reference color conversion");
            var rows = ffmpeg.sws_scale(context, frame->data.ToArray(), frame->linesize.ToArray(), 0, height, rgba->data.ToArray(),
                rgba->linesize.ToArray());
            Check(rows == height, "Reference sws_scale converts every row.");
            var output = (uint*)reference.Data;
            var outputWidth = reference.Width;
            var outputHeight = reference.Height;
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(rgba->data[0] + (y * rgba->linesize[0]));
                for (var x = 0; x < width; x++)
                {
                    int dx;
                    int dy;
                    if (turn == 0)
                    {
                        dx = x;
                        dy = y;
                    }
                    else if (turn == 1)
                    {
                        dx = height - 1 - y;
                        dy = x;
                    }
                    else if (turn == 2)
                    {
                        dx = width - 1 - x;
                        dy = height - 1 - y;
                    }
                    else
                    {
                        dx = y;
                        dy = width - 1 - x;
                    }

                    if (output[((outputHeight - 1 - dy) * outputWidth) + dx] != row[x])
                    {
                        Check(false, "Slice conversion differs from single-call sws_scale at " + width + "x" + height + " "
                            + format + " rotation " + (turn * 90) + " (" + x + "," + y + ").");
                    }
                }
            }

            Check(true, "Slice conversion matches sws_scale and orientation at " + width + "x" + height + " " + format + ".");
        }
        finally
        {
            ffmpeg.sws_freeContext(context);
            ffmpeg.av_frame_free(&rgba);
            ffmpeg.av_frame_free(&frame);
        }
    }

    /// <summary>Checks zero managed allocation across all threads for steady-state threaded conversion.</summary>
    private static void CheckZeroAllocation()
    {
        var baseline = GC.GetTotalAllocatedBytes(true);
        var probe = new byte[256];
        var observed = GC.GetTotalAllocatedBytes(true) - baseline;
        GC.KeepAlive(probe);
        Check(observed >= 256, "The process-wide precise allocation counter observes a known allocation.");
        foreach (var format in new[] { AVPixelFormat.AV_PIX_FMT_YUV420P, AVPixelFormat.AV_PIX_FMT_NV12, AVPixelFormat.AV_PIX_FMT_P010LE })
        {
            var frame = CreateFrame(format, 1280, 721);
            try
            {
                using (var converter = new VideoFrameConverter(new DecodedVideoFramePool(2), 4))
                {
                    for (var index = 0; index < 64; index++)
                    {
                        converter.Convert(frame, 0, 0.04, (index % 4) * 90, int.MaxValue / 4).Dispose();
                    }

                    var allocated = GC.GetTotalAllocatedBytes(true);
                    for (var index = 0; index < 256; index++)
                    {
                        converter.Convert(frame, index * 0.04, 0.04, (index % 4) * 90, int.MaxValue / 4).Dispose();
                    }

                    allocated = GC.GetTotalAllocatedBytes(true) - allocated;
                    Check(converter.ScaleThreadCount == 4 && converter.CopyBandCount > 1 && converter.StartedCopyThreadCount == 3,
                        "The allocation measurement engages libswscale and copy threads.");
                    Check(allocated == 0, "Threaded " + format + " conversion allocated " + allocated + " managed bytes across all threads for 256 frames.");
                    Console.WriteLine("Threaded converter " + format + ": " + allocated + " managed bytes (all threads) / 256 frames (after warmup).");
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
            }
        }
    }

    /// <summary>Checks that the decoder passes the configured conversion threads and produces identical pixels.</summary>
    /// <param name="media">A seekable media file decoded by the software decoder.</param>
    private static void CheckDecoder(string media)
    {
        var single = new DecoderOptions { ConversionThreadCount = 1 };
        var threaded = new DecoderOptions { ConversionThreadCount = 4 };
        using (var first = new FFmpegVideoDecoder(single))
        using (var second = new FFmpegVideoDecoder(threaded))
        {
            first.Open(media, CancellationToken.None);
            second.Open(media, CancellationToken.None);
            for (var index = 0; index < 24; index++)
            {
                using (var expected = first.ReadFrame())
                using (var actual = second.ReadFrame())
                {
                    Check(expected != null && actual != null, "Both decoders return frame " + index + ".");
                    var same = expected!.PresentationTime == actual!.PresentationTime && expected.DataSize == actual.DataSize
                        && new ReadOnlySpan<byte>((void*)expected.Data, expected.DataSize).SequenceEqual(
                            new ReadOnlySpan<byte>((void*)actual.Data, actual.DataSize));
                    Check(same, "Decoded frame " + index + " is identical with 1 and 4 conversion threads.");
                }
            }

            Check(first.ConversionScaleThreadCount == 1 && second.ConversionScaleThreadCount == 4,
                "The decoder converts with DecoderOptions.ConversionThreadCount libswscale threads.");
        }

        foreach (var requested in new[] { 0, -3, 64 })
        {
            using (var decoder = new FFmpegVideoDecoder(new DecoderOptions { ConversionThreadCount = requested }))
            {
                decoder.Open(media, CancellationToken.None);
                using (var frame = decoder.ReadFrame())
                {
                    Check(frame != null && decoder.ConversionScaleThreadCount == Math.Clamp(requested, 1, 16),
                        "ConversionThreadCount " + requested + " is clamped to 1 through 16.");
                }
            }
        }

        Console.WriteLine("Decoder conversion threads: 1 vs 4 identical frames and option clamping passed.");
    }

    /// <summary>Prints conversion timings for 1080p and 4K YUV 4:2:0 frames at several thread counts.</summary>
    private static void PrintTiming()
    {
        foreach (var size in new[] { (Width: 1920, Height: 1080), (Width: 3840, Height: 2160) })
        {
            foreach (var format in new[] { AVPixelFormat.AV_PIX_FMT_YUV420P, AVPixelFormat.AV_PIX_FMT_NV12 })
            {
                foreach (var turn in new[] { 0, 1 })
                {
                    var frame = CreateFrame(format, size.Width, size.Height);
                    try
                    {
                        var line = "Timing " + size.Width + "x" + size.Height + " " + format + " rotation " + (turn * 90) + ":";
                        foreach (var threads in new[] { 1, 2, 4, 8 })
                        {
                            using (var converter = new VideoFrameConverter(new DecodedVideoFramePool(2), threads))
                            {
                                for (var index = 0; index < 10; index++)
                                {
                                    converter.Convert(frame, 0, 0.04, turn * 90, int.MaxValue / 4).Dispose();
                                }

                                var count = size.Width > 2000 ? 40 : 100;
                                var watch = Stopwatch.StartNew();
                                for (var index = 0; index < count; index++)
                                {
                                    converter.Convert(frame, 0, 0.04, turn * 90, int.MaxValue / 4).Dispose();
                                }

                                line += " " + threads + "T " + (watch.Elapsed.TotalMilliseconds / count).ToString("F2") + " ms";
                            }
                        }

                        Console.WriteLine(line);
                    }
                    finally
                    {
                        ffmpeg.av_frame_free(&frame);
                    }
                }
            }
        }
    }

    /// <summary>Allocates a source frame filled with horizontal luma and vertical chroma gradients.</summary>
    /// <param name="format">One of the formats in <see cref="s_formats"/>.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>An owned frame that the caller frees with av_frame_free.</returns>
    private static AVFrame* CreateFrame(AVPixelFormat format, int width, int height)
    {
        var frame = ffmpeg.av_frame_alloc();
        if (frame == null)
        {
            throw new OutOfMemoryException();
        }

        frame->width = width;
        frame->height = height;
        frame->format = (int)format;
        FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(frame, 32), "Allocate threaded conversion fixture");
        var highDepth = format == AVPixelFormat.AV_PIX_FMT_YUV420P10LE || format == AVPixelFormat.AV_PIX_FMT_P010LE;
        var shift = format == AVPixelFormat.AV_PIX_FMT_P010LE ? 6 : 0;
        var maximum = highDepth ? 1023 : 255;
        var interleaved = format == AVPixelFormat.AV_PIX_FMT_NV12 || format == AVPixelFormat.AV_PIX_FMT_P010LE;
        var chromaWidth = format == AVPixelFormat.AV_PIX_FMT_YUV444P ? width : (width + 1) / 2;
        var chromaHeight = format == AVPixelFormat.AV_PIX_FMT_YUV444P ? height : (height + 1) / 2;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Store(frame, 0, x, y, ((x * 13) + (y * 29)) % (maximum + 1), highDepth, shift);
            }
        }

        for (var y = 0; y < chromaHeight; y++)
        {
            for (var x = 0; x < chromaWidth; x++)
            {
                // Chroma changes mostly by row, so a misplaced slice boundary changes colors.
                var u = ((y * 37) + (x * 3)) % (maximum + 1);
                var v = (maximum - ((y * 53) % (maximum + 1)) + (x * 5)) % (maximum + 1);
                if (interleaved)
                {
                    Store(frame, 1, x * 2, y, u, highDepth, shift);
                    Store(frame, 1, (x * 2) + 1, y, v, highDepth, shift);
                }
                else
                {
                    Store(frame, 1, x, y, u, highDepth, shift);
                    Store(frame, 2, x, y, v, highDepth, shift);
                }
            }
        }

        return frame;
    }

    /// <summary>Stores one sample in a frame plane.</summary>
    /// <param name="frame">The frame to write.</param>
    /// <param name="plane">The plane index.</param>
    /// <param name="x">The sample column within the plane.</param>
    /// <param name="y">The sample row within the plane.</param>
    /// <param name="value">The sample value before shifting.</param>
    /// <param name="highDepth">Whether samples are 16-bit little-endian words.</param>
    /// <param name="shift">The left shift applied to 16-bit samples.</param>
    private static void Store(AVFrame* frame, int plane, int x, int y, int value, bool highDepth, int shift)
    {
        var row = frame->data[(uint)plane] + (y * frame->linesize[(uint)plane]);
        if (highDepth)
        {
            ((ushort*)row)[x] = (ushort)(value << shift);
        }
        else
        {
            row[x] = (byte)value;
        }
    }

    /// <summary>Records an assertion and throws when it fails.</summary>
    /// <param name="condition">The asserted condition.</param>
    /// <param name="message">The failure description.</param>
    /// <exception cref="Exception">The condition is false.</exception>
    private static void Check(bool condition, string message)
    {
        s_checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    /// <summary>Records which bands of a job ran, on which thread, and whether they finished.</summary>
    private sealed class RecordingJob : IVideoBandJob
    {
        /// <summary>The largest band count recorded by this job.</summary>
        internal const int Capacity = 32;
        /// <summary>The message of the exception thrown by <see cref="FailingBand"/>.</summary>
        internal const string FailureMessage = "Expected band failure.";
        /// <summary>Counts executions of each band.</summary>
        internal int[] Counts { get; } = new int[Capacity];
        /// <summary>Marks bands whose execution finished, including failing bands.</summary>
        internal int[] Completed { get; } = new int[Capacity];
        /// <summary>Records the managed thread that executed each band.</summary>
        internal int[] ThreadIds { get; } = new int[Capacity];
        /// <summary>Gets or sets the band that throws, or -1.</summary>
        internal int FailingBand { get; set; } = -1;
        /// <summary>Gets or sets the band that spins before finishing, or -1.</summary>
        internal int SlowBand { get; set; } = -1;

        /// <summary>Records one band execution.</summary>
        /// <param name="band">The zero-based band index.</param>
        /// <exception cref="InvalidOperationException">The band is <see cref="FailingBand"/>.</exception>
        public void Execute(int band)
        {
            Interlocked.Increment(ref Counts[band]);
            ThreadIds[band] = Environment.CurrentManagedThreadId;
            try
            {
                if (band == SlowBand)
                {
                    Thread.SpinWait(20000);
                }

                if (band == FailingBand)
                {
                    throw new InvalidOperationException(FailureMessage);
                }
            }
            finally
            {
                Volatile.Write(ref Completed[band], 1);
            }
        }

        /// <summary>Clears the recorded executions.</summary>
        internal void Reset()
        {
            Array.Clear(Counts, 0, Capacity);
            Array.Clear(Completed, 0, Capacity);
            Array.Clear(ThreadIds, 0, Capacity);
        }
    }
}
