#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Validation;
using FFmpeg.AutoGen;

/// <summary>Runs the isolated managed and real native FFmpeg validation entry points.</summary>
static class Program
{
    static int _checks;
    static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
    /// <summary>Runs the selected validation mode and reports its observed assertions.</summary>
    /// <param name="args">The native library directory, media or output directory, and optional validation mode.</param>
    /// <returns>Zero when the selected checks pass; one when validation raises an error.</returns>
    static int Main(string[] args)
    {
        try
        {
            TestClock();
            _checks += VideoBitRateChecks.Run();
            _checks += AllocationChecks.RunManaged();
            _checks += EncodingChecks.RunManaged();
            bool av1 = args.Length == 3 && args[2] == "--av1";
            bool av1Unavailable = args.Length == 3 && args[2] == "--av1-unavailable";
            bool allocations = args.Length == 3 && args[2] == "--allocations";
            bool softwareAllocations = args.Length == 3 && args[2] == "--allocations-software";
            bool encoding = args.Length == 3 && args[2] == "--encode";
            bool encodingUnavailable = args.Length == 3 && args[2] == "--encode-unavailable";
            bool encodingHardware = args.Length == 3 && args[2] == "--encode-hardware";
            bool uncheckedAmf = args.Length == 3 && args[2] == "--encode-unchecked-amf";
            var encodingSoftware = args.Length == 3 && args[2] == "--encode-software";
            if (args.Length != 2 && !av1 && !av1Unavailable && !allocations && !softwareAllocations
                && !encoding && !encodingUnavailable && !encodingHardware && !uncheckedAmf && !encodingSoftware)
            {
                Console.WriteLine("PASS: " + _checks + " assertions; clock, bitrate, frame pool and encoder options; use <native-directory> <media> [--av1|--av1-unavailable|--allocations|--allocations-software], or <native-directory> <output-directory> --encode[|-unavailable|-hardware|-unchecked-amf|-software].");
                return 0;
            }
            string native = Path.GetFullPath(args[0]);
            NativeLibrary.SetDllImportResolver(typeof(FFmpeg.AutoGen.ffmpeg).Assembly, (name, assembly, paths) =>
            {
                string file = Path.Combine(native, name + ".dll");
                return File.Exists(file) ? NativeLibrary.Load(file) : IntPtr.Zero;
            });
            // Dependencies are resolved by the OS; limit this change to the test process.
            if (!SetDllDirectory(native)) throw new Exception("SetDllDirectory failed.");
            if (encodingSoftware)
            {
                _checks += EncodingChecks.RunSoftware(args[1]);
                Console.WriteLine("PASS: " + _checks + " assertions; all four software codecs, CBR/VBR, complex/flat pixels, PTS, threads, immutable diagnostics and cleanup.");
                return 0;
            }
            if (uncheckedAmf)
            {
                _checks += EncodingChecks.RunUncheckedAmf(args[1]);
                Console.WriteLine("PASS: " + _checks + " assertions; unchecked AMF rejected with native patch diagnostics and preserved output.");
                return 0;
            }
            if (encoding || encodingUnavailable || encodingHardware)
            {
                _checks += EncodingChecks.RunNative(args[1], encodingUnavailable, encodingHardware);
                Console.WriteLine("PASS: " + _checks + " assertions; encoder options, native encoder selection, output and cleanup.");
                return 0;
            }
            if (allocations || softwareAllocations)
            {
                _checks += AllocationChecks.RunNative(Path.GetFullPath(args[1]), allocations);
                Console.WriteLine("PASS: " + _checks + " assertions; zero managed allocation after warmup and frame ownership.");
                return 0;
            }
            if (av1Unavailable)
            {
                TestAv1SoftwareUnavailable(Path.GetFullPath(args[1]));
                Console.WriteLine("PASS: " + _checks + " assertions; missing AV1 software decoder is rejected before packet submission.");
                return 0;
            }
            if (av1)
            {
                string media = Path.GetFullPath(args[1]);
                TestDecoder(media);
                TestSession(media);
                TestAv1SoftwareFallback(media);
                Console.WriteLine("PASS: " + _checks + " assertions; AV1 software pixels, seek, session and hardware selection/fallback policy.");
                return 0;
            }
            _checks += ConverterChecks.Run();
            TestHardwareRequirement(Path.GetFullPath(args[1]));
            TestHardwareCpuUpload(Path.GetFullPath(args[1]));
            TestHardwareMappingFailure(Path.GetFullPath(args[1]));
            TestHardwareBackendFallback(Path.GetFullPath(args[1]));
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
            Check(!decoder.HardwareDecoding && !string.IsNullOrEmpty(decoder.DecoderName), "default CPU decoder reports software identity");
            Check(decoder.Width > 0 && decoder.Height > 0 && decoder.Duration > 0, "metadata");
            Console.WriteLine($"{decoder.CodecName}: {decoder.Width}x{decoder.Height}, {decoder.Duration:F3}s, {decoder.FrameRate:F3}fps");
            double previous = -1;
            long firstBitRate = 0;
            for (int i = 0; i < 12; i++)
                using (var frame = decoder.ReadFrame())
                {
                    Check(frame != null, "first 12 frames exist");
                    Check(frame.PresentationTime >= previous, "presentation order");
                    Check(frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4, "packed RGBA");
                    Check(frame.CurrentBitRate > 0, "software frame carries its compressed-video bitrate");
                    if (i == 0) firstBitRate = frame.CurrentBitRate;
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
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.PresentationTime < 0.2, "backward seek flushes codec");
                Check(frame.CurrentBitRate == firstBitRate, "backward seek clears bitrate history and reproduces first-frame estimate");
            }
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
                Check(last.CurrentBitRate > 0, "endpoint seek retains final frame bitrate");
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
    static void TestHardwareCpuUpload(string media)
    {
        using (var decoder = new FFmpegVideoDecoder(new DecoderOptions {
            HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            KeepNativeFrames = false, AllowHardwareCpuUpload = true }))
        {
            // This owns a separate D3D11 video device. The test has no Unity graphics
            // context or interop bridge callback, so success must use real HW download.
            decoder.Open(media, CancellationToken.None);
            Check(decoder.HardwareDecoding, "D3D11VA CPU transport keeps actual hardware decoding");
            Check(!string.IsNullOrEmpty(decoder.DecoderName) && !string.IsNullOrEmpty(decoder.DecoderDevice), "hardware decoder name/device diagnostics");
            double previous = -1;
            for (int i = 0; i < 6; i++)
                using (var frame = decoder.ReadFrame())
                {
                    Check(frame != null && frame.HardwareDecoded, "downloaded frame retains hardware decode identity");
                    Check(!frame.IsHardwareFrame && frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4,
                        "hardware download delivers packed CPU RGBA, not a native texture handle");
                    Check(frame.PresentationTime >= previous, "hardware CPU frames stay ordered");
                    Check(frame.CurrentBitRate > 0, "hardware frame carries its compressed-video bitrate");
                    previous = frame.PresentationTime;
                }
            double target = Math.Min(1, decoder.Duration / 2);
            decoder.Seek(target);
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.HardwareDecoded && frame.PresentationTime + frame.Duration >= target - 0.05,
                    "hardware CPU decode remains seekable");
                var pixels = new byte[frame.DataSize];
                Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
                int low = 765, high = 0;
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    int brightness = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                    low = Math.Min(low, brightness); high = Math.Max(high, brightness);
                }
                Check(high - low > 15, "real hardware-decoded pixels survive CPU conversion");
            }
            Console.WriteLine("Hardware CPU transport: " + decoder.DecoderName + "; " + decoder.DecoderDevice);
        }
    }
    private static void TestAv1SoftwareUnavailable(string media)
    {
        foreach (bool requestHardware in new[] { false, true })
        {
            int attempts = 0;
            var options = requestHardware ? new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                AllowHardwareCpuUpload = false,
                AcquireHardwareDevice = () => { attempts++; throw new NotSupportedException("injected AV1 device initialization failure"); }
            } : new DecoderOptions();
            using (var decoder = new FFmpegVideoDecoder(options))
            {
                bool rejected = false;
                try { decoder.Open(media, CancellationToken.None); }
                catch (NotSupportedException error)
                {
                    rejected = error.Message.Contains("libdav1d") && error.Message.Contains("libaom-av1");
                }
                Check(rejected, "missing AV1 software support is identified during open");
                Check(attempts == (requestHardware ? 1 : 0), "missing software support preserves the initial AV1 hardware attempt");
            }
        }
    }

    private static unsafe void TestAv1SoftwareFallback(string media)
    {
        var hardwareCodec = ffmpeg.avcodec_find_decoder_by_name("av1");
        Check(hardwareCodec != null && hardwareCodec->id == AVCodecID.AV_CODEC_ID_AV1,
            "native AV1 hardware decoder remains available beside external software decoders");
        foreach (var backend in new[] { AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA, AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN })
        {
            bool advertised = false;
            for (int index = 0; ; index++)
            {
                var configuration = ffmpeg.avcodec_get_hw_config(hardwareCodec, index);
                if (configuration == null) break;
                advertised |= configuration->device_type == backend && (configuration->methods & 1) != 0;
            }
            Check(advertised, "native AV1 decoder retains " + backend + " support");
        }

        using (var decoder = new FFmpegVideoDecoder())
        {
            decoder.Open(media, CancellationToken.None);
            CheckAv1SoftwareIdentity(decoder);
            CheckAv1Pixels(decoder, 0);
            Console.WriteLine("AV1 software decoder: " + decoder.DecoderName + "; " + decoder.TransferMode);
        }
        const string failure = "injected AV1 device initialization failure";
        foreach (bool strict in new[] { false, true })
        {
            int attempts = 0;
            using (var decoder = new FFmpegVideoDecoder(new DecoderOptions
            {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true,
                // Prevent creation of an independent hardware device after the injected failure.
                AllowHardwareCpuUpload = false,
                RequireHardwareDecoding = strict,
                AcquireHardwareDevice = () => { attempts++; throw new NotSupportedException(failure); }
            }))
            {
                if (strict)
                {
                    bool rejected = false;
                    try { decoder.Open(media, CancellationToken.None); }
                    catch (NotSupportedException error) { rejected = error.Message.Contains(failure); }
                    Check(rejected, "strict AV1 mode rejects failed hardware instead of using software");
                }
                else
                {
                    decoder.Open(media, CancellationToken.None);
                    CheckAv1SoftwareIdentity(decoder);
                    Check(decoder.HardwareFallbackReason.Contains(failure), "AV1 fallback retains device failure diagnostics");
                    CheckAv1Pixels(decoder, 0);
                    double target = Math.Min(1, decoder.Duration / 2);
                    decoder.Seek(target);
                    CheckAv1Pixels(decoder, target);
                    decoder.Seek(0);
                    CheckAv1Pixels(decoder, 0);
                }
                Check(attempts == 1, "AV1 hardware preference selects the native decoder before software fallback");
            }
        }
    }

    private static void CheckAv1SoftwareIdentity(FFmpegVideoDecoder decoder)
    {
        Check(decoder.CodecName == "av1", "AV1 mode requires a real AV1 fixture");
        Check(decoder.DecoderName == "libdav1d" || decoder.DecoderName == "libaom-av1",
            "software AV1 uses an external decoder instead of the hardware-only native AV1 decoder");
        Check(!decoder.HardwareDecoding && decoder.DecoderDevice == "Software" &&
            decoder.TransferMode == "Software RGBA upload", "AV1 software fallback reports actual decoder and transport");
    }

    private static void CheckAv1Pixels(FFmpegVideoDecoder decoder, double position)
    {
        using (var frame = decoder.ReadFrame())
        {
            Check(frame != null && !frame.HardwareDecoded && !frame.IsHardwareFrame,
                "AV1 software decoder returns a CPU frame");
            Check(frame.Data != IntPtr.Zero && frame.DataSize == frame.Width * frame.Height * 4,
                "AV1 software output contains packed RGBA pixels");
            Check(frame.PresentationTime + frame.Duration >= position - 0.05 && frame.PresentationTime < position + 0.2,
                "AV1 software frame preserves seek position");
            var pixels = new byte[frame.DataSize];
            Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
            int low = 765, high = 0;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                int brightness = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                low = Math.Min(low, brightness); high = Math.Max(high, brightness);
            }
            Check(high - low > 15, "AV1 decoded pixels are nonuniform");
        }
    }
    static unsafe void TestHardwareMappingFailure(string media)
    {
        foreach (bool strict in new[] { false, true })
        {
            int mappings = 0;
            var options = new DecoderOptions {
                HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                KeepNativeFrames = true, AllowHardwareCpuUpload = true, RequireHardwareDecoding = strict,
                AcquireHardwareDevice = () => {
                    AVBufferRef* device = null;
                    FFmpegVideoDecoder.Check(ffmpeg.av_hwdevice_ctx_create(&device,
                        AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "create mapping test D3D11 device");
                    return (IntPtr)device;
                },
                MapHardwareFrame = _ => { mappings++; throw new NotSupportedException("injected mapper failure"); }
            };
            using (var decoder = new FFmpegVideoDecoder(options))
            {
                decoder.Open(media, CancellationToken.None);
                if (strict)
                {
                    bool rejected = false;
                    try { using (decoder.ReadFrame()) { } }
                    catch (NotSupportedException error) { rejected = error.Message.Contains("injected mapper failure"); }
                    Check(rejected, "strict GPU mode refuses mapper failure instead of downloading CPU pixels");
                }
                else
                {
                    for (int index = 0; index < 2; index++)
                        using (var frame = decoder.ReadFrame())
                            Check(frame != null && frame.HardwareDecoded && !frame.IsHardwareFrame && frame.Data != IntPtr.Zero,
                                "native mapper failure downloads the original hardware frame");
                    Check(decoder.HardwareDecoding, "mapper failure preserves hardware decoder identity");
                    Check(decoder.HardwareFallbackReason.Contains("injected mapper failure"), "mapper failure remains diagnosable");
                }
                Check(mappings == 1, "failed native mapping is attempted only once per session");
            }
        }
    }
    static unsafe void TestHardwareBackendFallback(string media)
    {
        foreach (var backend in new[] { AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA, AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN })
        {
            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            bool advertised = false;
            for (int index = 0; ; index++)
            {
                var config = ffmpeg.avcodec_get_hw_config(codec, index);
                if (config == null) break;
                advertised |= config->device_type == backend && (config->methods & 1) != 0;
            }
            Check(advertised, "staged H.264 decoder advertises " + backend);
            foreach (bool strict in new[] { false, true })
            {
                int attempts = 0;
                var options = new DecoderOptions {
                    HardwareDeviceType = backend, KeepNativeFrames = true,
                    RequireHardwareDecoding = strict, AllowHardwareCpuUpload = false,
                    AcquireHardwareDevice = () => { attempts++; throw new NotSupportedException("injected native API device failure"); },
                    FallbackHardwareOptions = new DecoderOptions {
                        HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                        KeepNativeFrames = strict, RequireHardwareDecoding = strict, AllowHardwareCpuUpload = !strict,
                        AcquireHardwareDevice = () => {
                            AVBufferRef* device = null;
                            FFmpegVideoDecoder.Check(ffmpeg.av_hwdevice_ctx_create(&device,
                                AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "create native backend fallback device");
                            return (IntPtr)device;
                        }
                    }
                };
                using (var decoder = new FFmpegVideoDecoder(options))
                {
                    decoder.Open(media, CancellationToken.None);
                    Check(attempts == 1, "preferred API is attempted once before platform fallback");
                    Check(decoder.ActiveHardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                        "failed native API selects the platform hardware decoder");
                    Check(decoder.HardwareFallbackReason.Contains("injected native API device failure"), "native API failure remains diagnosable");
                    using (var frame = decoder.ReadFrame())
                    {
                        Check(frame != null && frame.HardwareDecoded, "platform fallback actually decodes a hardware frame");
                        Check(strict ? frame.IsHardwareFrame && frame.Data == IntPtr.Zero : !frame.IsHardwareFrame && frame.Data != IntPtr.Zero,
                            "platform fallback preserves the CPU transport policy");
                    }
                    double position = Math.Min(1, decoder.Duration / 2);
                    decoder.Seek(position);
                    using (var frame = decoder.ReadFrame())
                        Check(frame != null && frame.HardwareDecoded && frame.PresentationTime + frame.Duration >= position - 0.05,
                            "platform backend remains seekable after native API failure");
                }
            }
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
