#nullable enable
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.IO.Storage;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Exercises the production managed MediaCodec encoder in an isolated Android Player.</summary>
    public sealed class AndroidEncoderDeviceSmoke : MonoBehaviour
    {
        /// <summary>Collects the selected component, native options and roundtrip evidence.</summary>
        private readonly StringBuilder _evidence = new StringBuilder();
        /// <summary>Counts assertions completed by the worker.</summary>
        private int _checks;

        /// <summary>Runs observed worker-owned recording checks and saves a device report before quitting.</summary>
        private async void Start()
        {
            Application.runInBackground = true;
            var report = Path.Combine(Application.persistentDataPath, "ffmpeg-encoder-smoke.txt");
            var directory = Path.Combine(Application.persistentDataPath, "encoders-" + Guid.NewGuid().ToString("N"));
            var token = destroyCancellationToken;
            var format = FFmpegPlayerSmoke.Argument("-encoderFormat") == "HEVC" ? VideoEncodingFormat.HEVC : VideoEncodingFormat.H264;
            var standardSizeOnly = FFmpegPlayerSmoke.Argument("-encoderStandardSizeOnly") == "true";
            var expectCbrSoftwareFallback = FFmpegPlayerSmoke.Argument("-encoderExpectCbrSoftwareFallback") == "true";
            var success = false;
            try
            {
                FileSystem.Directory.Create(directory);
                _evidence.AppendLine("OUTPUT: " + directory);
                MajDebug.SetLogWriter(new StreamWriter(FileSystem.File.OpenWrite(report + ".diagnostics.log", overwrite: true)));
                await Task.Run(() => Run(directory, format, standardSizeOnly, expectCbrSoftwareFallback, token), token);
                success = true;
            }
            catch (Exception error)
            {
                _evidence.AppendLine(error.ToString());
            }

            var result = (success ? "PASS: " : "FAIL: ") + _checks + " managed Android encoder assertions\n" + _evidence;
            using (var writer = new StreamWriter(FileSystem.File.OpenWrite(report, overwrite: true)))
            {
                writer.Write(result);
            }
            Debug.Log(result);
            MajDebug.FlushLog();
            Application.Quit(success ? 0 : 1);
        }

        /// <summary>Tests CBR and VBR at a small size and at the size and frame rate from the reported failure.</summary>
        /// <param name="directory">The unique local output directory.</param>
        /// <param name="format">The H.264 or HEVC format supported by the connected device.</param>
        /// <param name="standardSizeOnly">Whether to skip the reported high frame-rate dimensions.</param>
        /// <param name="expectCbrSoftwareFallback">Whether this device must reject hardware CBR and use the same-format software encoder.</param>
        /// <param name="token">Cancellation bound to the scene component.</param>
        /// <exception cref="InvalidOperationException">Hardware selection, native options or a roundtrip assertion fails.</exception>
        private void Run(string directory, VideoEncodingFormat format, bool standardSizeOnly, bool expectCbrSoftwareFallback, CancellationToken token)
        {
            foreach (var highRate in standardSizeOnly ? new[] { false } : new[] { false, true })
            {
                foreach (var mode in new[] { VideoRateControlMode.CBR, VideoRateControlMode.VBR })
                {
                    var options = new EncoderOptions
                    {
                        Width = highRate ? 1016 : 320,
                        Height = highRate ? 1524 : 192,
                        FrameRate = highRate ? 120 : 30,
                        BitRate = highRate ? 8000000 : 200000,
                        MaximumBitRate = highRate ? 16000000 : 400000,
                        RateControlMode = mode,
                        Format = format,
                        PreferredEncoderType = VideoEncoderType.Hardware,
                        MaximumSoftwareThreads = 4
                    };
                    Roundtrip(directory, options, expectCbrSoftwareFallback && mode == VideoRateControlMode.CBR, token);
                }
            }
        }

        /// <summary>Encodes sixty real frames, drains the file and validates every software-decoded frame.</summary>
        /// <param name="directory">The unique local output directory.</param>
        /// <param name="options">The requested dimensions, frame rate and bitrate mode.</param>
        /// <param name="expectSoftware">Whether CBR must explicitly fall back because the device does not advertise that mode.</param>
        /// <param name="token">Cancellation bound to the scene component.</param>
        /// <exception cref="InvalidOperationException">An encoder or decoded frame violates an assertion.</exception>
        private void Roundtrip(string directory, EncoderOptions options, bool expectSoftware, CancellationToken token)
        {
            const int frameCount = 60;
            var file = Path.Combine(directory, options.Format + "-" + options.Width + "x" + options.Height + "-" + options.RateControlMode + ".mp4");
            var pixels = new byte[options.Width * options.Height * 4];
            for (var y = 0; y < options.Height; y++)
            {
                for (var x = 0; x < options.Width; x++)
                {
                    var offset = (y * options.Width + x) * 4;
                    pixels[offset + (y < options.Height / 2 ? 0 : 2)] = 255;
                    pixels[offset + 3] = 255;
                }
            }

            using (var encoder = new FFmpegVideoEncoder(options))
            {
                encoder.Open(file, token);
                if (expectSoftware)
                {
                    var expected = options.Format == VideoEncodingFormat.H264 ? "libx264" : "libx265";
                    Check(encoder.EncoderType == VideoEncoderType.Software && encoder.EncoderName == expected, "same-format software fallback");
                    Check(encoder.HardwareFallbackReason?.Contains("bitrate mode unsupported") == true,
                        "unsupported driver mode is reported instead of unconditional CBR rejection");
                    _evidence.AppendLine("FALLBACK: " + encoder.HardwareFallbackReason);
                }
                else
                {
                    var expected = options.Format == VideoEncodingFormat.H264 ? "h264_mediacodec" : "hevc_mediacodec";
                    Check(encoder.EncoderType == VideoEncoderType.Hardware && encoder.EncoderName == expected,
                        "hardware MediaCodec selected: " + encoder.HardwareFallbackReason);
                    Check(encoder.HardwareFallbackReason == null && encoder.SoftwareThreadCount == 0, "no software fallback");
                    CheckNativeMode(encoder, options.RateControlMode);
                }
                Check(encoder.RateControlMode == options.RateControlMode && encoder.IsMaximumBitRateEnforced == expectSoftware,
                    "requested mode with honest maximum-rate diagnostics");
                for (var index = 0; index < frameCount; index++)
                {
                    encoder.Encode(pixels, index + (index >= frameCount / 2 ? 1 : 0), false);
                }
                encoder.Complete();
                encoder.Complete();
                Check(encoder.EncodedFrames == frameCount && encoder.BytesWritten > 0, "all frames accepted and file finalized");
                _evidence.AppendLine("ENCODED: " + options.Width + "x" + options.Height + "@" + options.FrameRate
                    + " mode=" + encoder.RateControlMode + " encoder=" + encoder.EncoderName + " frames=" + encoder.EncodedFrames
                    + " measured=" + encoder.CurrentBitRate + " maximumEnforced=" + encoder.IsMaximumBitRateEnforced);
            }

            using var decoder = new FFmpegVideoDecoder(new DecoderOptions { ThreadCount = 2 });
            decoder.Open(file, token);
            var decoded = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }
                Check(decoded < frameCount, "no extra decoded frames");
                Check(frame.Width == options.Width && frame.Height == options.Height, "cropped dimensions match the recording");
                var expected = (decoded + (decoded >= frameCount / 2 ? 1 : 0)) / (double)options.FrameRate;
                Check(Math.Abs(frame.PresentationTime - expected) < 0.001, "PTS preserve the deliberate frame gap");
                // Production decoder pixels are bottom-up for Unity texture upload.
                var bottom = 8 * frame.Stride + 8 * 4;
                var top = (frame.Height - 9) * frame.Stride + 8 * 4;
                Check(Marshal.ReadByte(frame.Data, bottom + 2) > 200 && Marshal.ReadByte(frame.Data, bottom) < 50
                    && Marshal.ReadByte(frame.Data, top) > 200 && Marshal.ReadByte(frame.Data, top + 2) < 50,
                    "red top and blue bottom survive encoding, padding and decode");
                decoded++;
            }
            using var eof = decoder.ReadFrame();
            Check(decoded == frameCount && eof == null, "sixty frames and stable decoder EOF");
            _evidence.AppendLine("DECODED: " + file + "; frames=" + decoded + "; pixels/PTS/drain PASS");
        }

        /// <summary>Reads the actual FFmpeg private option to catch an incorrect managed CBR-to-VBR mapping.</summary>
        /// <param name="encoder">The opened worker-owned production encoder.</param>
        /// <param name="mode">The requested driver rate-control mode.</param>
        /// <exception cref="InvalidOperationException">Native MediaCodec options do not match the request.</exception>
        private unsafe void CheckNativeMode(FFmpegVideoEncoder encoder, VideoRateControlMode mode)
        {
            var field = typeof(FFmpegVideoEncoder).GetField("_codec", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Encoder codec metadata was stripped.");
            var context = (AVCodecContext*)Pointer.Unbox(field.GetValue(encoder)!);
            long actual = -1;
            Check(ffmpeg.av_opt_get_int(context->priv_data, "bitrate_mode", 0, &actual) >= 0
                && actual == (mode == VideoRateControlMode.CBR ? 2 : 1), "native bitrate_mode matches CBR/VBR");
            _evidence.AppendLine("NATIVE: bitrate_mode=" + actual);
        }

        /// <summary>Requires an invariant and counts successful assertions.</summary>
        /// <param name="condition">The condition that must hold.</param>
        /// <param name="message">The failing invariant.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            _checks++;
        }
    }
}
