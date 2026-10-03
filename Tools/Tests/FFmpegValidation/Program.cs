using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.Video.Internal;
using FFmpeg.AutoGen;

static class Program
{
    static int _checks;
    static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
    static int Main(string[] args)
    {
        try
        {
            TestClock();
            if (args.Length != 2) { Console.WriteLine("PASS: clock; use <native-directory> <media> for native decode tests."); return 0; }
            string native = Path.GetFullPath(args[0]);
            NativeLibrary.SetDllImportResolver(typeof(FFmpeg.AutoGen.ffmpeg).Assembly, (name, assembly, paths) =>
            {
                string file = Path.Combine(native, name + ".dll");
                return File.Exists(file) ? NativeLibrary.Load(file) : IntPtr.Zero;
            });
            // Dependencies are resolved by the OS; limit this change to the test process.
            if (!SetDllDirectory(native)) throw new Exception("SetDllDirectory failed.");
            _checks += ConverterChecks.Run();
            TestHardwareRequirement(Path.GetFullPath(args[1]));
            TestConversion();
            TestDecoder(Path.GetFullPath(args[1]));
            TestSession(Path.GetFullPath(args[1]));
            Console.WriteLine("PASS: " + _checks + " assertions; real FFmpeg decode, seek, cancellation, bounded queue and clock.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void TestClock()
    {
        double now = 10;
        var clock = new PlaybackClock(() => now);
        clock.Set(3); clock.Start(); now += 2;
        Check(clock.Position == 5, "real-time clock");
        clock.Rate = 2; Check(clock.Position == 5, "rate change continuity");
        now += 2; Check(clock.Position == 9, "double speed");
        clock.Pause(); now += 100; Check(clock.Position == 9, "pause freezes timeline");
        clock.Set(1); clock.Start(); now += 1; Check(clock.Position == 3, "seek/resume");
        bool rejected = false;
        try { clock.Rate = double.NaN; } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid rate rejected");
    }
    static void TestHardwareRequirement(string media)
    {
        // The strict path must stop before software decoding/upload, even when
        // a codec exists and no graphics device could be configured.
        foreach (var device in new[] { AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA })
        {
            bool rejected = false;
            using (var decoder = new FFmpegVideoDecoder(new DecoderOptions {
                RequireHardwareDecoding = true, KeepNativeFrames = true, HardwareDeviceType = device }))
                try { decoder.Open(media, CancellationToken.None); }
                catch (NotSupportedException) { rejected = true; }
            Check(rejected, "strict hardware requirement rejects missing device: " + device);
        }
        int releases = 0;
        var native = new DecodedVideoFrame(new IntPtr(123), value => { Check(value == new IntPtr(123), "native image identity"); releases++; });
        Check(native.IsHardwareFrame && native.Data == IntPtr.Zero && native.DataSize == 0, "native images never advertise CPU pixels");
        native.Dispose(); native.Dispose();
        Check(releases == 1 && !native.IsHardwareFrame, "native image has single owned release");
    }
    static void TestDecoder(string media)
    {
        using (var decoder = new FFmpegVideoDecoder())
        {
            decoder.Open(media, CancellationToken.None);
            Check(decoder.Width > 0 && decoder.Height > 0 && decoder.Duration > 0, "metadata");
            Console.WriteLine($"{decoder.CodecName}: {decoder.Width}x{decoder.Height}, {decoder.Duration:F3}s, {decoder.FrameRate:F3}fps");
            double previous = -1;
            for (int i = 0; i < 12; i++)
                using (var frame = decoder.ReadFrame())
                {
                    Check(frame != null, "first 12 frames exist");
                    Check(frame.PresentationTime >= previous, "presentation order");
                    Check(frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4, "packed RGBA");
                    previous = frame.PresentationTime;
                }
            double target = Math.Min(decoder.Duration / 2, 2);
            decoder.Seek(target);
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.PresentationTime + frame.Duration >= target - 0.05, "accurate forward seek");
                Check(frame.PresentationTime < target + 0.2, "seek not excessive");
            }
            decoder.Seek(0);
            using (var frame = decoder.ReadFrame()) Check(frame != null && frame.PresentationTime < 0.2, "backward seek flushes codec");
            decoder.Seek(Math.Max(0, decoder.Duration - 0.3));
            int drained = 0;
            double finalTimestamp = -1;
            int finalPixel = 0;
            DecodedVideoFrame decoded;
            while ((decoded = decoder.ReadFrame()) != null)
            {
                finalTimestamp = decoded.PresentationTime;
                finalPixel = Marshal.ReadInt32(decoded.Data);
                decoded.Dispose();
                if (++drained > 300) throw new Exception("EOF did not drain");
            }
            Check(drained > 0, "delayed frames drained before EOF");
            decoder.Seek(0);
            using (var first = decoder.ReadFrame()) Check(first != null, "reset before endpoint seek");
            decoder.Seek(decoder.Duration);
            using (var last = decoder.ReadFrame())
            {
                Check(last != null, "100% seek presents a frame instead of stale first frame");
                Check(Math.Abs(last.PresentationTime - finalTimestamp) < 0.000001, "100% seek selects actual final frame");
                Check(Marshal.ReadInt32(last.Data) == finalPixel, "100% seek retains final frame pixels");
            }
            Check(decoder.ReadFrame() == null, "100% seek emits final frame once then EOF");
            decoder.Seek(0);
            using (var first = decoder.ReadFrame()) Check(first != null && first.PresentationTime < 0.2, "seek after endpoint releases preroll and resets decoder");
        }
        using (var cancel = new CancellationTokenSource())
        using (var decoder = new FFmpegVideoDecoder())
        {
            cancel.Cancel();
            bool cancelled = false;
            try { decoder.Open(media, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancelled open");
        }
    }
    static unsafe void TestConversion()
    {
        var source = ffmpeg.av_frame_alloc();
        if (source == null) throw new OutOfMemoryException();
        try
        {
            source->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
            source->width = 2; source->height = 2;
            FFmpegVideoDecoder.Check(ffmpeg.av_frame_get_buffer(source, 32), "allocate color fixture");
            // RGBA on little endian: top row red/green, bottom row blue/white.
            uint* row = (uint*)source->data[0]; row[0] = 0xff0000ff; row[1] = 0xff00ff00;
            row = (uint*)(source->data[0] + source->linesize[0]); row[0] = 0xffff0000; row[1] = 0xffffffff;
            uint[][] expected = {
                new uint[] { 0xffff0000, 0xffffffff, 0xff0000ff, 0xff00ff00 },
                new uint[] { 0xffffffff, 0xff00ff00, 0xffff0000, 0xff0000ff },
                new uint[] { 0xff00ff00, 0xff0000ff, 0xffffffff, 0xffff0000 },
                new uint[] { 0xff0000ff, 0xffff0000, 0xff00ff00, 0xffffffff }
            };
            using (var converter = new VideoFrameConverter())
            {
                for (int rotation = 0; rotation < 4; rotation++)
                using (var frame = converter.Convert(source, 0, 0.1, rotation * 90, 4))
                {
                    var data = (uint*)frame.Data;
                    for (int pixel = 0; pixel < 4; pixel++)
                        Check(data[pixel] == expected[rotation][pixel], "RGBA corner orientation " + rotation + "/" + pixel);
                }
            }
        }
        finally { ffmpeg.av_frame_free(&source); }
    }
    static void TestSession(string media)
    {
        using (var session = new VideoDecodeSession(media, new DecoderOptions(), 3))
        {
            Wait(() => session.BufferedFrames == 3 || session.Error != null);
            Check(session.Error == null, "worker decoded");
            Thread.Sleep(100);
            Check(session.BufferedFrames == 3, "bounded preload queue");
            session.Seek(1);
            session.Seek(0.5);
            Wait(() => session.BufferedFrames > 0 || session.Error != null);
            if (session.Error != null) throw session.Error;
            using (var frame = session.TakeFrame()) Check(frame.PresentationTime >= 0.45 && frame.PresentationTime < 0.7, "latest seek wins; no stale queued frames");
        }
        for (int i = 0; i < 8; i++) new VideoDecodeSession(media, new DecoderOptions(), 2).Dispose();
        Check(true, "rapid open/close cancellation");
    }
    static void Wait(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException(); Thread.Sleep(10); }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string directory);
}
