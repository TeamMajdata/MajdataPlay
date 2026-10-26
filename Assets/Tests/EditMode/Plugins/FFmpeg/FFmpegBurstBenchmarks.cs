using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;
using Unity.Burst;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Measures the Burst video and audio kernels against the managed loops and libswscale path they replaced.</summary>
    /// <remarks>
    /// Run by name, for example with <c>Run-UnityTests.ps1 -TestFilter FFmpegBurstBenchmarks -BurstSynchronousCompilation</c>,
    /// so the kernels are Burst-compiled before they are timed. Editor timings use the Editor's Mono JIT for the managed
    /// baselines; IL2CPP Players compile those baselines ahead of time and narrow the gap. Results are logged, not asserted.
    /// </remarks>
    [Explicit("Benchmark; select it by name.")]
    [Category("Benchmark")]
    public unsafe class FFmpegBurstBenchmarks
    {
        /// <summary>The benchmark frame width.</summary>
        private const int Width = 1920;
        /// <summary>The benchmark frame height.</summary>
        private const int Height = 1080;
        /// <summary>The number of untimed calls before measuring.</summary>
        private const int WarmUpIterations = 5;
        /// <summary>The number of timed calls whose median is reported.</summary>
        private const int TimedIterations = 41;

        /// <summary>Skips the fixture when the native FFmpeg libraries are unavailable and logs whether Burst code runs.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            Report("Burst enabled=" + BurstCompiler.IsEnabled + ", probe runs Burst code=" + (BurstProbe.IsBurstCompiled() == 1)
                + ", processors=" + Environment.ProcessorCount);
        }

        /// <summary>Times the decoder's flip and rotation copy of one 1080p RGBA frame for every quarter turn.</summary>
        [Test]
        public void RotationCopy_1080p()
        {
            var source = NativeFrames.Allocate(Width, Height, AVPixelFormat.AV_PIX_FMT_RGBA);
            var destination = (uint*)Marshal.AllocHGlobal(Width * Height * 4);
            try
            {
                NativeFrames.FillRgba(source);
                var pixels = source->data[0];
                var stride = source->linesize[0];
                for (var turns = 0; turns < 4; turns++)
                {
                    var lines = (turns & 1) == 0 ? Height : Width;
                    var quarterTurns = turns;
                    var legacy = MedianMilliseconds(() => LegacyManagedLoops.CopyRgbaLines(pixels, stride, destination, Width, Height,
                        quarterTurns, 0, lines));
                    var burst = MedianMilliseconds(() => VideoPixelKernels.CopyRgbaLines(pixels, stride, destination, Width, Height,
                        quarterTurns, 0, lines));
                    ReportComparison("RGBA copy " + (turns * 90) + " degrees", legacy, burst);
                }
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)destination);
                NativeFrames.Free(source);
            }
        }

        /// <summary>Times the capture conversion of one bottom-first 1080p RGBA frame to each encoder input format.</summary>
        /// <param name="format">The encoder input format.</param>
        [TestCase(AVPixelFormat.AV_PIX_FMT_YUV420P)]
        [TestCase(AVPixelFormat.AV_PIX_FMT_NV12)]
        public void CaptureConversion_1080p(AVPixelFormat format)
        {
            var pixels = new byte[Width * Height * 4];
            new Random(1).NextBytes(pixels);
            var destination = NativeFrames.Allocate(Width, Height, format);
            try
            {
                foreach (var threads in new[] { 1, 4 })
                {
                    using var legacy = new LegacySwscaleCapture(Width, Height, format, threads);
                    using var converter = new CaptureFrameConverter(Width, Height, threads);
                    var legacyTime = MedianMilliseconds(() => legacy.Convert(pixels, true, destination));
                    var burstTime = MedianMilliseconds(() => converter.Convert(pixels, true, destination));
                    ReportComparison(format + " capture conversion, " + threads + " thread(s)", legacyTime, burstTime);
                }
            }
            finally
            {
                NativeFrames.Free(destination);
            }
        }

        /// <summary>Times the PCM16 conversion of one minute of 48 kHz stereo float samples.</summary>
        [Test]
        public void Pcm16Conversion_OneMinuteStereo()
        {
            var source = new float[48000 * 2 * 60];
            var random = new Random(2);
            for (var i = 0; i < source.Length; i++)
            {
                source[i] = (float)((random.NextDouble() * 2.2) - 1.1);
            }

            var destination = new short[source.Length];
            var legacy = MedianMilliseconds(() => LegacyManagedLoops.ConvertToPcm16(source, destination));
            var burst = MedianMilliseconds(() => FFmpegMediaMuxer.ConvertToPcm16(source, destination));
            ReportComparison("PCM16 conversion, 60 s stereo", legacy, burst);
        }

        /// <summary>Runs an action untimed a few times, then reports the median duration of the timed runs.</summary>
        /// <param name="action">The action to time.</param>
        /// <returns>The median duration in milliseconds.</returns>
        private static double MedianMilliseconds(Action action)
        {
            for (var i = 0; i < WarmUpIterations; i++)
            {
                action();
            }

            var samples = new List<double>(TimedIterations);
            for (var i = 0; i < TimedIterations; i++)
            {
                var start = Stopwatch.GetTimestamp();
                action();
                samples.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }

            samples.Sort();
            return samples[samples.Count / 2];
        }

        /// <summary>Logs the replaced and Burst timings of one workload.</summary>
        /// <param name="workload">The workload description.</param>
        /// <param name="legacy">The median time of the replaced implementation, in milliseconds.</param>
        /// <param name="burst">The median time of the Burst implementation, in milliseconds.</param>
        private static void ReportComparison(string workload, double legacy, double burst)
        {
            Report(workload + ": replaced " + legacy.ToString("F3") + " ms, Burst " + burst.ToString("F3") + " ms, "
                + (legacy / burst).ToString("F2") + "x");
        }

        /// <summary>Writes one benchmark line to the test output and the Unity log.</summary>
        /// <param name="message">The line to write.</param>
        private static void Report(string message)
        {
            TestContext.WriteLine("[Benchmark] " + message);
            UnityEngine.Debug.Log("[Benchmark] " + message);
        }

        /// <summary>Reports whether Burst-compiled code is running, using a method Burst compiles the same way as the kernels.</summary>
        [BurstCompile]
        internal static class BurstProbe
        {
            /// <summary>Returns one when Burst compiled this method and zero when it runs as managed code.</summary>
            /// <returns>One for Burst code; otherwise, zero.</returns>
            [BurstCompile]
            public static int IsBurstCompiled()
            {
                var compiled = 1;
                MarkManaged(ref compiled);
                return compiled;
            }

            /// <summary>Clears the flag; Burst removes this call from compiled code.</summary>
            /// <param name="compiled">The flag to clear.</param>
            [BurstDiscard]
            private static void MarkManaged(ref int compiled)
            {
                compiled = 0;
            }
        }
    }
}
