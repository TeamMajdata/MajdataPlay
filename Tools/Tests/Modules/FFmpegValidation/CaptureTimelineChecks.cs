#nullable enable
using System;
using MajdataPlay.FFmpeg.Internal;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks that camera renders are assigned frame timestamps without jitter-induced drops or clock drift.</summary>
    internal static class CaptureTimelineChecks
    {
        /// <summary>Sets the simulated recording frame rate.</summary>
        private const double CaptureRate = 60;
        /// <summary>Sets the number of simulated renders per scenario, about the 9000 frames of the reported recording.</summary>
        private const int Renders = 9000;
        /// <summary>Sets the uniform render timing jitter, in seconds either way, of a busy main thread timed at the render.</summary>
        private const double JitterSeconds = 0.003;
        /// <summary>Sets the uniform jitter, in seconds either way, of renders timed at the start of their frame.</summary>
        private const double FrameStartJitterSeconds = 0.001;
        /// <summary>Sets the allowed timestamp error in frame periods, the production tolerance plus rounding slack.</summary>
        private const double MaximumError = CaptureFrameTimeline.Tolerance + 1e-9;
        /// <summary>Counts the assertions in the current run.</summary>
        private static int s_checks;

        /// <summary>Runs every render-rate scenario.</summary>
        /// <returns>The number of assertions that passed.</returns>
        public static int Run()
        {
            s_checks = 0;
            TestMatchedRateAcrossPhases();
            TestBoundaryPhaseRegression();
            TestSlightlySlowDisplay();
            TestSlightlyFastDisplay();
            TestFastRenders();
            TestSlowRenders();
            TestStall();
            TestReset();
            return s_checks;
        }

        /// <summary>Checks that renders at the capture rate keep every frame for any phase between render and clock.</summary>
        /// <remarks>
        /// Frame-start timing keeps every frame. With the larger jitter of render-time timing, a first render whose own jitter places
        /// the timeline near a limit may cost one frame while the timeline resynchronizes once.
        /// </remarks>
        private static void TestMatchedRateAcrossPhases()
        {
            for (var phase = 0; phase < 40; phase++)
            {
                var result = Simulate(CaptureRate, phase / 40.0, FrameStartJitterSeconds, 0, 0, 7 + phase);
                Check(result.Rejected == 0 && result.Gaps == 0, "matched-rate frame-start renders keep every frame at phase " + phase
                    + "/40: rejected " + result.Rejected + ", gaps " + result.Gaps);
                Check(result.MaximumError <= MaximumError, "matched-rate timestamps stay within the tolerance, got " + result.MaximumError);
                result = Simulate(CaptureRate, phase / 40.0, JitterSeconds, 0, 0, 7 + phase);
                Check(result.Rejected + result.Gaps <= 1, "matched-rate render-time renders lose at most one frame at phase " + phase
                    + "/40: rejected " + result.Rejected + ", gaps " + result.Gaps);
                Check(result.MaximumError <= MaximumError, "jittered matched-rate timestamps stay within the tolerance, got " + result.MaximumError);
            }
        }

        /// <summary>Checks the reported failure: renders near a frame boundary used to alternate between a rejected duplicate and a skip.</summary>
        private static void TestBoundaryPhaseRegression()
        {
            var result = Simulate(CaptureRate, 0.02, JitterSeconds, 0, 0, 3);
            Console.WriteLine("Capture timeline at a frame-boundary phase with " + (JitterSeconds * 1000) + " ms render jitter over " + Renders
                + " renders: per-render floor timestamps reported " + result.FloorGaps + " dropped frames and discarded " + result.FloorDuplicates
                + " renders; the timeline rejected " + result.Rejected + " and skipped " + result.Gaps + ".");
            Check(result.FloorGaps > Renders / 20 && result.FloorDuplicates > Renders / 20,
                "the scenario reproduces the duplicate-then-skip drops of per-render quantization, got " + result.FloorGaps);
            Check(result.Rejected == 0 && result.Gaps == 0, "the timeline keeps every render at a boundary phase");
        }

        /// <summary>Checks that a display slower than the capture rate skips only the genuinely missing frames, without chatter.</summary>
        private static void TestSlightlySlowDisplay()
        {
            var rate = 59.94;
            var result = Simulate(rate, 0.3, JitterSeconds, 0, 0, 11);
            var expected = Renders * ((CaptureRate / rate) - 1);
            Check(result.Rejected == 0, "a slower display rejects no render");
            Check(result.Gaps >= expected - 1 && result.Gaps <= expected + 2, "a slower display skips only missing frames: expected about "
                + expected.ToString("0.0") + ", got " + result.Gaps);
            Check(result.MaximumError <= MaximumError, "slower-display timestamps stay within the tolerance");
        }

        /// <summary>Checks that a display faster than the capture rate rejects only surplus renders, without chatter.</summary>
        private static void TestSlightlyFastDisplay()
        {
            var rate = 60.06;
            var result = Simulate(rate, 0.3, JitterSeconds, 0, 0, 13);
            var expected = Renders * (1 - (CaptureRate / rate));
            Check(result.Gaps == 0, "a faster display skips no frame");
            Check(result.Rejected >= expected - 1 && result.Rejected <= expected + 2, "a faster display rejects only surplus renders: expected about "
                + expected.ToString("0.0") + ", got " + result.Rejected);
            Check(result.MaximumError <= MaximumError, "faster-display timestamps stay within the tolerance");
        }

        /// <summary>Checks that renders faster than the capture rate keep the render closest to each frame time.</summary>
        private static void TestFastRenders()
        {
            var result = Simulate(144, 0.1, JitterSeconds, 0, 0, 17);
            var frames = Renders / 144.0 * CaptureRate;
            Check(result.Gaps == 0, "fast renders fill every frame");
            Check(Math.Abs(result.Accepted - frames) <= 2, "fast renders are sampled at the capture rate, got " + result.Accepted);
            Check(result.MaximumError <= MaximumError && result.MeanError < 0.25, "fast-render timestamps are close to their frames: maximum "
                + result.MaximumError + ", mean " + result.MeanError);
        }

        /// <summary>Checks that renders slower than the capture rate are all kept with their real timing.</summary>
        private static void TestSlowRenders()
        {
            var result = Simulate(30, 0.4, JitterSeconds, 0, 0, 19);
            Check(result.Rejected == 0 && result.Accepted == Renders, "slow renders are all recorded");
            Check(Math.Abs(result.Gaps - (Renders - 1)) <= 2, "slow renders leave one frame gap each, got " + result.Gaps);
            Check(result.MaximumError <= MaximumError, "slow-render timestamps stay within the tolerance");
        }

        /// <summary>Checks that a render stall skips the stalled frames and resynchronizes to the clock.</summary>
        private static void TestStall()
        {
            var result = Simulate(CaptureRate, 0.2, FrameStartJitterSeconds, 4000, 0.2, 23);
            Check(result.Rejected == 0, "a stall rejects no render");
            Check(result.Gaps >= 11 && result.Gaps <= 13, "a 200 ms stall skips about 12 frames, got " + result.Gaps);
            Check(result.MaximumError <= MaximumError, "timestamps resynchronize after a stall, got " + result.MaximumError);
        }

        /// <summary>Checks that a reset starts a new recording from the nearest frame.</summary>
        private static void TestReset()
        {
            var timeline = new CaptureFrameTimeline();
            Check(timeline.TryAssign(5.2, out var first) && first == 5 && timeline.SourceGapFrames == 5, "the first render uses the nearest frame");
            Check(!timeline.TryAssign(5.21, out _), "a second render at nearly the same time is rejected");
            timeline.Reset();
            Check(timeline.LastFrameIndex == -1 && timeline.SourceGapFrames == 0, "reset forgets the previous recording");
            Check(timeline.TryAssign(0.3, out var restarted) && restarted == 0, "a reset timeline starts at frame zero");
        }

        /// <summary>Feeds jittered renders to a timeline and to the previous per-render floor quantization.</summary>
        /// <param name="renderRate">The mean render rate in hertz.</param>
        /// <param name="phase">The offset of the first render in capture frame periods.</param>
        /// <param name="jitterSeconds">The uniform render timing jitter in seconds either way.</param>
        /// <param name="stallAt">The render before which a stall occurs, or zero for none.</param>
        /// <param name="stallSeconds">The stall duration in seconds.</param>
        /// <param name="seed">The random seed for reproducible jitter.</param>
        /// <returns>The counts and timestamp errors.</returns>
        private static SimulationResult Simulate(double renderRate, double phase, double jitterSeconds, int stallAt, double stallSeconds, int seed)
        {
            var random = new Random(seed);
            var timeline = new CaptureFrameTimeline();
            var result = new SimulationResult();
            var previousIndex = -1L;
            var firstIndex = -1L;
            var previousFloor = -1L;
            var errorSum = 0.0;
            var ordered = true;
            for (var render = 0; render < Renders; render++)
            {
                var stall = stallAt > 0 && render >= stallAt ? stallSeconds : 0;
                var seconds = (render / renderRate) + stall + (((random.NextDouble() * 2) - 1) * jitterSeconds);
                var position = Math.Max(0, phase + (seconds * CaptureRate));
                var floor = (long)position;
                if (floor <= previousFloor)
                {
                    result.FloorDuplicates++;
                }
                else
                {
                    if (previousFloor >= 0)
                    {
                        result.FloorGaps += floor - previousFloor - 1;
                    }
                    previousFloor = floor;
                }
                if (!timeline.TryAssign(position, out var index))
                {
                    result.Rejected++;
                    continue;
                }
                ordered &= index > previousIndex && index >= 0;
                if (firstIndex < 0)
                {
                    firstIndex = index;
                }
                previousIndex = index;
                result.Accepted++;
                var error = Math.Abs(position - index);
                errorSum += error;
                result.MaximumError = Math.Max(result.MaximumError, error);
            }
            Check(ordered, "frame timestamps are nonnegative and strictly increase");
            result.Gaps = timeline.SourceGapFrames - firstIndex;
            result.MeanError = errorSum / Math.Max(1, result.Accepted);
            return result;
        }

        /// <summary>Throws when a validation condition fails.</summary>
        /// <param name="condition">The observed condition.</param>
        /// <param name="message">The failure description.</param>
        private static void Check(bool condition, string message)
        {
            s_checks++;
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        /// <summary>Holds the outcome of one simulated recording.</summary>
        private sealed class SimulationResult
        {
            /// <summary>Gets or sets the number of renders recorded as frames.</summary>
            public long Accepted { get; set; }
            /// <summary>Gets or sets the number of renders the timeline rejected.</summary>
            public long Rejected { get; set; }
            /// <summary>Gets or sets the number of frames the timeline skipped after the first render.</summary>
            public long Gaps { get; set; }
            /// <summary>Gets or sets the frames that per-render floor timestamps reported as dropped, like the previous capturer.</summary>
            public long FloorGaps { get; set; }
            /// <summary>Gets or sets the renders that per-render floor timestamps discarded as duplicates of the previous frame.</summary>
            public long FloorDuplicates { get; set; }
            /// <summary>Gets or sets the largest distance between a render time and its timestamp, in frame periods.</summary>
            public double MaximumError { get; set; }
            /// <summary>Gets or sets the mean distance between a render time and its timestamp, in frame periods.</summary>
            public double MeanError { get; set; }
        }
    }
}
