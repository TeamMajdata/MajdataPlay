#nullable enable
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using MajdataPlay.Recording;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MajdataPlay.Tests.RecorderLifecycleValidation
{
    /// <summary>Exercises production recording lifecycle and file ownership without native media services.</summary>
    internal static class Program
    {
        /// <summary>Counts completed assertions.</summary>
        private static int s_assertions;

        /// <summary>Runs each isolated recording case after resetting in-memory fixtures.</summary>
        /// <returns>Zero if all cases pass, or one after a regression.</returns>
        private static async Task<int> Main()
        {
            var tests = new (string Name, Func<Task> Run)[]
            {
                ("successful output removes temporary sources", TestSuccessAsync),
                ("drain read failure waits for video and retains sources", TestReadFailureAsync),
                ("drain write failure is observed", TestWriteFailureAsync),
                ("mux failure preserves video fallback and PCM", TestMuxFallbackAsync),
                ("mux and fallback move failures retain both sources", TestMuxAndMoveFailureAsync),
                ("output move failure retains recoverable sources", TestMoveFailureAsync),
                ("video finalization failure closes audio and retains sources", TestVideoStopFailureAsync),
                ("audio stop failure still finalizes video and drain", TestAudioStopFailureAsync),
                ("path creation failure restores idle and permits retry", TestPathFailureAsync),
                ("capturer start failure closes PCM and permits retry", TestStartFailureAsync),
                ("Dispose waits for output before releasing owned audio", TestDisposeAsync),
                ("LateUpdate detects drain failure", TestUnexpectedDrainFailureAsync)
            };
            var failed = 0;
            foreach (var test in tests)
            {
                TestStorage.Reset();
                try
                {
                    await test.Run().WaitAsync(TimeSpan.FromSeconds(5));
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
            var marker = failed == 0 ? "RECORDER_LIFECYCLE_VALIDATION_PASSED" : "RECORDER_LIFECYCLE_VALIDATION_FAILED";
            Console.WriteLine($"{marker} ({tests.Length - failed} passed, {failed} failed, {s_assertions} assertions)");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Checks normal start, audio drain, mux, output move, and cleanup.</summary>
        private static async Task TestSuccessAsync()
        {
            var fixture = CreateFixture();
            await fixture.Recorder.StartRecordAsync();
            await fixture.Audio.ReadReached.Task;
            Assert(fixture.Recorder.IsRecording, "Start must enter Recording.");
            Assert(fixture.Audio.StartCalls == 1, "Audio must begin with the capturer Started event.");
            await fixture.Recorder.StopRecordAsync();
            Assert(!fixture.Recorder.IsRecording, "Stop must leave Recording.");
            Assert(fixture.Capturer.StopCalls == 1 && fixture.Audio.StopCalls == 1, "Both sources must stop once.");
            Assert(TestStorage.MuxCalls == 1 && OutputFiles().Length == 1, "One combined output must be written.");
            Assert(TemporaryFiles().Length == 0, "Successful output must remove every temporary source.");
        }

        /// <summary>Checks that drain errors propagate and another source's finalization is still awaited.</summary>
        private static async Task TestReadFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new IOException("simulated PCM read failure");
            fixture.Audio.ReadFailure = error;
            fixture.Capturer.StopGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await fixture.Recorder.StartRecordAsync();
            await fixture.Audio.ReadReached.Task;
            var stop = fixture.Recorder.StopRecordAsync();
            Assert(!stop.IsCompleted && fixture.Capturer.StopCalls == 1, "Video must be stopped and awaited after an audio drain failure.");
            fixture.Capturer.StopGate.SetResult(true);
            await ExpectExceptionAsync(stop, error);
            AssertRecoverableSources();
            Assert(TestStorage.MuxCalls == 0, "A failed PCM drain must not silently mux truncated audio.");
        }

        /// <summary>Checks that disk writes fail the actual single-layer background task.</summary>
        private static async Task TestWriteFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new IOException("simulated PCM disk full");
            TestStorage.AudioWriteFailure = error;
            await fixture.Recorder.StartRecordAsync();
            await fixture.Audio.ReadReached.Task;
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            AssertRecoverableSources();
            Assert(TestStorage.MuxCalls == 0, "A PCM write error must propagate before muxing.");
        }

        /// <summary>Checks the documented video-only fallback while keeping PCM for recovery.</summary>
        private static async Task TestMuxFallbackAsync()
        {
            var fixture = CreateFixture();
            var error = new InvalidOperationException("simulated mux failure");
            TestStorage.MuxFailure = error;
            await fixture.Recorder.StartRecordAsync();
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            Assert(OutputFiles().Single().EndsWith("_video-only.mkv", StringComparison.Ordinal), "Mux failure must preserve a video-only output when moving works.");
            Assert(TemporaryFiles().Single().EndsWith(".audio.pcm", StringComparison.Ordinal), "Failed muxing must preserve PCM for recovery.");
        }

        /// <summary>Checks that failed fallback storage does not delete the original recordings.</summary>
        private static async Task TestMuxAndMoveFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new InvalidOperationException("simulated mux failure");
            TestStorage.MuxFailure = error;
            TestStorage.MoveFailure = new IOException("simulated disconnected output device");
            await fixture.Recorder.StartRecordAsync();
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            AssertRecoverableSources();
            Assert(OutputFiles().Length == 0, "A disconnected output device must not invent a saved result.");
            Assert(MajDebug.Messages.Any(message => message.Contains(".video.mkv", StringComparison.Ordinal)
                && message.Contains(".audio.pcm", StringComparison.Ordinal)), "Failure logs must identify both recoverable source paths.");
        }

        /// <summary>Checks source retention when muxing worked but moving the result failed.</summary>
        private static async Task TestMoveFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new IOException("simulated read-only output directory");
            TestStorage.MoveFailure = error;
            await fixture.Recorder.StartRecordAsync();
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            AssertRecoverableSources();
            Assert(TestStorage.MuxCalls == 1 && OutputFiles().Length == 0, "A successful mux must not erase its sources when output transfer failed.");
        }

        /// <summary>Checks that video finalization failure still closes the PCM worker.</summary>
        private static async Task TestVideoStopFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new IOException("simulated video trailer failure");
            fixture.Capturer.StopFailure = error;
            await fixture.Recorder.StartRecordAsync();
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            AssertRecoverableSources();
            Assert(!fixture.Audio.IsRecording && !fixture.Recorder.IsRecording, "A failed video trailer must not leave the audio source running.");
        }

        /// <summary>Checks that audio Stop failure cannot bypass camera stop or drain cancellation.</summary>
        private static async Task TestAudioStopFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new InvalidOperationException("simulated audio source stop failure");
            fixture.Audio.StopFailure = error;
            await fixture.Recorder.StartRecordAsync();
            await ExpectExceptionAsync(fixture.Recorder.StopRecordAsync(), error);
            Assert(fixture.Capturer.StopCalls == 1, "Video must stop even when audio Stop throws.");
            AssertRecoverableSources();
            Assert(!fixture.Recorder.IsRecording, "Cleanup failure must restore Idle.");
        }

        /// <summary>Checks that failure before creating capture files cannot leave Starting latched.</summary>
        private static async Task TestPathFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new IOException("simulated temporary directory initialization failure");
            TestStorage.PathFailure = error;
            await ExpectExceptionAsync(fixture.Recorder.StartRecordAsync(), error);
            Assert(!fixture.Recorder.IsRecording, "Path creation failure must restore Idle.");
            TestStorage.PathFailure = null;
            await fixture.Recorder.StartRecordAsync();
            Assert(fixture.Recorder.IsRecording, "A failed path creation must permit a later retry.");
            await fixture.Recorder.StopRecordAsync();
        }

        /// <summary>Checks ownership cleanup after the capturer rejects initialization.</summary>
        private static async Task TestStartFailureAsync()
        {
            var fixture = CreateFixture();
            var error = new NotSupportedException("simulated unavailable encoder");
            fixture.Capturer.StartFailure = error;
            await ExpectExceptionAsync(fixture.Recorder.StartRecordAsync(), error);
            Assert(!fixture.Recorder.IsRecording && TemporaryFiles().Length == 0, "Start failure must close and remove unrecorded files.");
            fixture.Capturer.StartFailure = null;
            await fixture.Recorder.StartRecordAsync();
            await fixture.Recorder.StopRecordAsync();
            Assert(OutputFiles().Length == 1, "A rejected start must permit a successful retry.");
        }

        /// <summary>Checks that asynchronous Dispose waits for pending video before releasing audio.</summary>
        private static async Task TestDisposeAsync()
        {
            var fixture = CreateFixture();
            fixture.Capturer.StopGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await fixture.Recorder.StartRecordAsync();
            fixture.Recorder.Dispose();
            fixture.Recorder.Dispose();
            Assert(!fixture.Audio.Disposed.Task.IsCompleted, "Owned audio must outlive pending video finalization.");
            Assert(!fixture.Recorder.IsConnected, "Dispose must close the public connection immediately.");
            fixture.Capturer.StopGate.SetResult(true);
            await fixture.Audio.Disposed.Task;
            Assert(fixture.Capturer.StopCalls == 1, "Repeated Dispose must not duplicate source shutdown.");
            Assert(OutputFiles().Length == 1 && TemporaryFiles().Length == 0, "Dispose must finish output before releasing owned audio.");
        }

        /// <summary>Checks that polling notices a background source failure without another explicit stop call.</summary>
        private static async Task TestUnexpectedDrainFailureAsync()
        {
            var fixture = CreateFixture();
            fixture.Audio.ReadFailure = new IOException("simulated background PCM failure");
            await fixture.Recorder.StartRecordAsync();
            await fixture.Audio.ReadReached.Task;
            while (fixture.Recorder.IsRecording)
            {
                fixture.Recorder.OnLateUpdate();
                await Task.Yield();
            }
            fixture.Recorder.Dispose();
            await fixture.Audio.Disposed.Task;
            Assert(fixture.Capturer.StopCalls == 1, "LateUpdate must stop video when the audio drain faults.");
            AssertRecoverableSources();
        }

        /// <summary>Creates one production recorder with narrow in-memory source substitutes.</summary>
        /// <returns>The production recorder and its controllable sources.</returns>
        private static Fixture CreateFixture()
        {
            var capturer = new FFmpegCameraCapturer();
            var audio = new TestAudioRecorder();
            var recorder = new FFmpegRecorder(capturer, audio, MediaContainerFormat.MP4);
            recorder.SetOutputName("fixture-song");
            return new Fixture(recorder, capturer, audio);
        }

        /// <summary>Lists only fixture output file paths.</summary>
        /// <returns>The saved output paths.</returns>
        private static string[] OutputFiles()
        {
            return TestStorage.Files.Keys.Where(path => path.StartsWith("/fixture/output", StringComparison.Ordinal)).ToArray();
        }

        /// <summary>Lists only fixture temporary file paths.</summary>
        /// <returns>The remaining temporary paths.</returns>
        private static string[] TemporaryFiles()
        {
            return TestStorage.Files.Keys.Where(path => path.StartsWith("/fixture/temp", StringComparison.Ordinal)).ToArray();
        }

        /// <summary>Checks that failed output retains closed and recoverable source files.</summary>
        private static void AssertRecoverableSources()
        {
            var temporary = TemporaryFiles();
            Assert(temporary.Any(path => path.EndsWith(".video.mkv", StringComparison.Ordinal)), "The recoverable video source must remain.");
            Assert(temporary.Any(path => path.EndsWith(".audio.pcm", StringComparison.Ordinal)), "The recoverable PCM source must remain.");
            Assert(temporary.All(path => TestStorage.Files[path].Closed), "Finalization must close every source before reporting failure.");
        }

        /// <summary>Checks that cleanup reports the original source or output failure.</summary>
        /// <param name="operation">The production lifecycle operation.</param>
        /// <param name="expected">The original failure object.</param>
        /// <returns>A task completing after the expected error is observed.</returns>
        private static async Task ExpectExceptionAsync(Task operation, Exception expected)
        {
            try
            {
                await operation;
            }
            catch (Exception exception)
            {
                Assert(ReferenceEquals(exception, expected), "The original failure must reach the caller.");
                return;
            }
            throw new InvalidOperationException("The expected recording failure was swallowed.");
        }

        /// <summary>Counts and enforces one lifecycle condition.</summary>
        /// <param name="condition">Whether the check passed.</param>
        /// <param name="message">The failure explanation.</param>
        private static void Assert(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Retains the production recorder and independent source substitutes.</summary>
        /// <param name="Recorder">The production wrapper.</param>
        /// <param name="Capturer">The controlled camera source.</param>
        /// <param name="Audio">The controlled PCM source.</param>
        private sealed record Fixture(FFmpegRecorder Recorder, FFmpegCameraCapturer Capturer, TestAudioRecorder Audio);
    }
}
