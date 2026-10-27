using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies camera recording through GPU readback and the background encoder while Unity runs the player loop.</summary>
    public class FFmpegCameraCapturerPlayModeTests
    {
        /// <summary>Owns the output files of the current test.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the output directory and object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
            _objects = new TestObjects();
        }

        /// <summary>Destroys created objects and output files.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Creates small recording options.</summary>
        /// <returns>The options.</returns>
        private static EncoderOptions Options()
        {
            return new EncoderOptions
            {
                Width = 64,
                Height = 48,
                FrameRate = 30,
                Format = VideoEncodingFormat.MPEG4,
                MaximumSoftwareThreads = 1,
                BitRate = 200000,
                MaximumBitRate = 400000,
                RateControlMode = VideoRateControlMode.CBR
            };
        }

        /// <summary>Creates a camera and a capturer attached to it.</summary>
        /// <param name="explicitTarget">Whether the camera renders into an explicit render texture.</param>
        /// <param name="camera">Receives the created camera.</param>
        /// <returns>The capturer.</returns>
        private FFmpegCameraCapturer CreateCapturer(bool explicitTarget, out Camera camera)
        {
            camera = _objects.Add<Camera>("Recorded camera");
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.7f, 0.4f, 1f);
            camera.enabled = false;
            if (explicitTarget)
            {
                camera.targetTexture = _objects.Track(new RenderTexture(64, 48, 24));
            }

            var capturer = camera.gameObject.AddComponent<FFmpegCameraCapturer>();
            capturer.Options = Options();
            return capturer;
        }

        /// <summary>Renders the camera once per frame for the requested wall-clock time.</summary>
        /// <param name="camera">The recorded camera.</param>
        /// <param name="seconds">The recording time.</param>
        /// <returns>The rendering coroutine.</returns>
        private static IEnumerator RenderFor(Camera camera, double seconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                if (camera != null)
                {
                    camera.Render();
                }

                yield return null;
            }
        }

        /// <summary>Checks whether this graphics device supports GPU conversion at the test recording size.</summary>
        /// <returns>True when a GPU converter can be created.</returns>
        private static bool SupportsGpuConversion()
        {
            var options = Options();
            using var converter = CaptureGpuConverter.TryCreate(options.Width, options.Height, true, out _);
            return converter != null;
        }

        /// <summary>Decodes a recording and returns its frame count.</summary>
        /// <param name="path">The recorded file.</param>
        /// <param name="width">Receives the decoded width.</param>
        /// <returns>The number of decoded frames.</returns>
        private static int CountFrames(string path, out int width)
        {
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            width = decoder.Width;
            var count = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    return count;
                }

                count++;
            }
        }

        /// <summary>Checks the state reported before any recording and synchronous property validation.</summary>
        [Test]
        public void Idle_ReportsNoRecording()
        {
            var capturer = CreateCapturer(true, out var camera);

            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Idle));
            Assert.That(capturer.IsRecording, Is.False);
            Assert.That(capturer.IsPaused, Is.False);
            Assert.That(capturer.CanStartRecording, Is.True);
            Assert.That(capturer.OutputPath, Is.Null);
            Assert.That(capturer.LastError, Is.Null);
            Assert.That(capturer.EncoderName, Is.Null);
            Assert.That(capturer.EncoderType, Is.Null);
            Assert.That(capturer.RateControlMode, Is.Null);
            Assert.That(capturer.HardwareFallbackReason, Is.Null);
            Assert.That(capturer.EncodingFormat, Is.Null);
            Assert.That(capturer.Width, Is.EqualTo(0));
            Assert.That(capturer.Height, Is.EqualTo(0));
            Assert.That(capturer.FrameRate, Is.EqualTo(0));
            Assert.That(capturer.BitRate, Is.EqualTo(0));
            Assert.That(capturer.MaximumBitRate, Is.EqualTo(0));
            Assert.That(capturer.CurrentBitRate, Is.EqualTo(0));
            Assert.That(capturer.EncodedFrames, Is.EqualTo(0));
            Assert.That(capturer.BytesWritten, Is.EqualTo(0));
            Assert.That(capturer.SoftwareThreadCount, Is.EqualTo(0));
            Assert.That(capturer.SoftwareThreadingMode, Is.EqualTo("none"));
            Assert.That(capturer.ConverterThreadCount, Is.EqualTo(0));
            Assert.That(capturer.UsesGpuColorConversion, Is.False);
            Assert.That(capturer.PreferGpuColorConversion, Is.True);
            Assert.That(capturer.DroppedFrames, Is.EqualTo(0));
            Assert.That(capturer.TimeSeconds, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => capturer.BufferedFrameLimit = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => capturer.BufferedFrameLimit = 9);
            Assert.Throws<ArgumentNullException>(() => capturer.Options = null!);
            capturer.BufferedFrameLimit = 4;
            capturer.ReadbackOrientation = CameraReadbackOrientation.TopFirst;
            capturer.TargetCamera = camera;
            Assert.That(capturer.BufferedFrameLimit, Is.EqualTo(4));
            Assert.That(capturer.ReadbackOrientation, Is.EqualTo(CameraReadbackOrientation.TopFirst));
            Assert.That(capturer.TargetCamera, Is.SameAs(camera));
            capturer.PauseRecording();
            capturer.ResumeRecording();
            capturer.StopRecording();
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Idle));
        }

        /// <summary>Checks that invalid start requests fault the returned task with the documented exception.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartRecordingAsync_RejectsInvalidRequests()
        {
            var capturer = CreateCapturer(true, out _);
            var path = FFmpegTestMedia.PathIn(_directory, "invalid.mkv");

            Assert.That(capturer.StartRecordingAsync(" ").Exception!.InnerException, Is.InstanceOf<ArgumentException>());
            Assert.That(capturer.StartRecordingAsync("http://example.invalid/out.mkv").Exception!.InnerException, Is.InstanceOf<ArgumentException>());
            capturer.Options = new EncoderOptions { Width = 3 };
            Assert.That(capturer.StartRecordingAsync(path).Exception!.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
            capturer.Options = Options();
            capturer.ReadbackOrientation = (CameraReadbackOrientation)9;
            Assert.That(capturer.StartRecordingAsync(path).Exception!.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
            capturer.ReadbackOrientation = CameraReadbackOrientation.Automatic;
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.That(capturer.StartRecordingAsync(path, source.Token).IsCanceled, Is.True);
            }

            capturer.enabled = false;
            Assert.That(capturer.StartRecordingAsync(path).Exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(capturer.CanStartRecording, Is.False);

            var orphan = _objects.Add<FFmpegCameraCapturer>("No camera");
            Assert.That(orphan.StartRecordingAsync(path).Exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Idle));
            yield return null;
        }

        /// <summary>Checks a complete explicit render-texture recording, its diagnostics and the decoded output.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Record_ExplicitTarget_ProducesDecodableFile()
        {
            var capturer = CreateCapturer(true, out var camera);
            var started = 0;
            var stopped = 0;
            capturer.Started += _ => started++;
            capturer.Stopped += _ => stopped++;
            var path = FFmpegTestMedia.PathIn(_directory, "explicit.mkv");

            var start = capturer.StartRecordingAsync(path);
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Preparing));
            Assert.That(capturer.CanStartRecording, Is.False);
            yield return PlayModeWait.Until(() => start.IsCompleted);

            Assert.That(start.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Recording));
            Assert.That(capturer.IsRecording, Is.True);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(capturer.OutputPath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(capturer.EncoderName, Is.EqualTo("mpeg4"));
            Assert.That(capturer.EncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(capturer.RateControlMode, Is.EqualTo(VideoRateControlMode.CBR));
            Assert.That(capturer.EncodingFormat, Is.EqualTo(VideoEncodingFormat.MPEG4));
            Assert.That(capturer.Width, Is.EqualTo(64));
            Assert.That(capturer.Height, Is.EqualTo(48));
            Assert.That(capturer.FrameRate, Is.EqualTo(30));
            Assert.That(capturer.BitRate, Is.EqualTo(200000));
            Assert.That(capturer.MaximumBitRate, Is.EqualTo(200000));
            Assert.That(capturer.SoftwareThreadCount, Is.EqualTo(1));
            // GPU conversion replaces the CPU converter wherever the device supports it.
            Assert.That(capturer.UsesGpuColorConversion, Is.EqualTo(SupportsGpuConversion()));
            Assert.That(capturer.ConverterThreadCount, Is.EqualTo(capturer.UsesGpuColorConversion ? 0 : 1));

            yield return RenderFor(camera, 0.6);
            var stop = capturer.StopRecordingAsync();
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopping));
            yield return PlayModeWait.Until(() => stop.IsCompleted);

            Assert.That(stop.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
            Assert.That(stopped, Is.EqualTo(1));
            Assert.That(capturer.CanStartRecording, Is.True);
            Assert.That(capturer.EncodedFrames, Is.GreaterThan(3));
            Assert.That(capturer.BytesWritten, Is.GreaterThan(0));
            Assert.That(capturer.TimeSeconds, Is.GreaterThan(0.3));
            Assert.That(capturer.DroppedFrames, Is.EqualTo(capturer.SourceGapFrames + capturer.BufferOverflowFrames));
            Assert.That(CountFrames(path, out var width), Is.EqualTo(capturer.EncodedFrames));
            Assert.That(width, Is.EqualTo(64));

            yield return PlayModeWait.Frames(2);
            Assert.That(stopped, Is.EqualTo(1));
        }

        /// <summary>Checks that declining GPU conversion records RGBA readbacks through the CPU converter.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Record_WithoutGpuConversion_UsesCpuConverter()
        {
            var capturer = CreateCapturer(true, out var camera);
            capturer.PreferGpuColorConversion = false;
            var path = FFmpegTestMedia.PathIn(_directory, "cpu.mkv");
            var start = capturer.StartRecordingAsync(path);
            yield return PlayModeWait.Until(() => start.IsCompleted);

            Assert.That(start.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(capturer.UsesGpuColorConversion, Is.False);
            Assert.That(capturer.ConverterThreadCount, Is.EqualTo(1));
            yield return RenderFor(camera, 0.4);
            var stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);

            Assert.That(stop.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(capturer.EncodedFrames, Is.GreaterThan(2));
            Assert.That(CountFrames(path, out _), Is.EqualTo(capturer.EncodedFrames));
        }

        /// <summary>Checks that a recording writes its named samples into an ordinary, non-deep profiler capture, from the main thread and the encoder worker.</summary>
        /// <remarks>
        /// Recorder and ProfilerRecorder do not observe Profiler.BeginSample samples, so the test enables a binary capture and
        /// looks for each sample name, which the capture stores as UTF-16 text.
        /// </remarks>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Record_EmitsProfilerSamples()
        {
            if (!UnityEngine.Profiling.Profiler.supported)
            {
                Assert.Ignore("The profiler is unavailable in this player.");
            }

            var capturer = CreateCapturer(true, out var camera);
            var profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.logFile = Path.Combine(_directory.Location, "profile");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            try
            {
                var start = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "profiled.mkv"));
                yield return PlayModeWait.Until(() => start.IsCompleted);
                Assert.That(start.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                yield return RenderFor(camera, 0.3);
                var stop = capturer.StopRecordingAsync();
                yield return PlayModeWait.Until(() => stop.IsCompleted);
                Assert.That(stop.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                // The capture still has to receive the frame that contains the encoder's final samples.
                yield return PlayModeWait.Frames(2);
            }
            finally
            {
                UnityEngine.Profiling.Profiler.enabled = profilerWasEnabled;
                UnityEngine.Profiling.Profiler.enableBinaryLog = false;
                UnityEngine.Profiling.Profiler.logFile = string.Empty;
            }

            var capture = _directory.Directory.FindFile("profile.raw");
            Assert.That(capture, Is.Not.Null, "The profiler wrote no binary capture.");
            var bytes = capture!.ReadAllBytes();
            var names = new[]
            {
                "FFmpeg.Capturer.Capture", "FFmpeg.Capturer.CompleteReadback", "FFmpeg.Capturer.CopyReadback", "FFmpeg.Encoder.Open",
                "FFmpeg.Encoder.Encode", capturer.UsesGpuColorConversion ? "FFmpeg.Encoder.CopyNV12" : "FFmpeg.Encoder.ConvertRGBA",
                "FFmpeg.Encoder.Software.SendFrame", "FFmpeg.Encoder.Software.ReceivePacket", "FFmpeg.Encoder.WritePacket",
                "FFmpeg.Encoder.Complete"
            };
            foreach (var name in names)
            {
                Assert.That(Contains(bytes, System.Text.Encoding.Unicode.GetBytes(name)), Is.True, name);
            }
        }

        /// <summary>Checks whether a byte sequence occurs in a buffer.</summary>
        /// <param name="buffer">The buffer to search.</param>
        /// <param name="sequence">The nonempty sequence to find.</param>
        /// <returns>True when the sequence occurs at any offset.</returns>
        private static bool Contains(byte[] buffer, byte[] sequence)
        {
            var first = sequence[0];
            for (var offset = Array.IndexOf(buffer, first); offset >= 0 && offset + sequence.Length <= buffer.Length;
                offset = Array.IndexOf(buffer, first, offset + 1))
            {
                if (buffer.AsSpan(offset, sequence.Length).SequenceEqual(sequence))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Checks that recording a main-display camera registers with the MRP capture bridge only while recording.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Record_MainDisplay_UsesMajCaptureBridge()
        {
            if (!(GraphicsSettings.currentRenderPipeline is MajRenderPipelineAsset))
            {
                Assert.Ignore("The project does not use the Maj Render Pipeline.");
            }

            var capturer = CreateCapturer(false, out var camera);
            capturer.ReadbackOrientation = CameraReadbackOrientation.TopFirst;
            var path = FFmpegTestMedia.PathIn(_directory, "display.mkv");
            var start = capturer.StartRecordingAsync(path);
            yield return PlayModeWait.Until(() => start.IsCompleted);

            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera).Length, Is.EqualTo(1));
            yield return RenderFor(camera, 0.4);

            capturer.PauseRecording();
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Paused));
            Assert.That(capturer.IsPaused, Is.True);
            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera).Length, Is.EqualTo(0));
            var pausedTime = capturer.TimeSeconds;
            yield return RenderFor(camera, 0.1);
            Assert.That(capturer.TimeSeconds, Is.EqualTo(pausedTime));

            capturer.ResumeRecording();
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Recording));
            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera).Length, Is.EqualTo(1));
            yield return RenderFor(camera, 0.2);

            var stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);

            Assert.That(stop.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(MajCameraCaptureBridge.GetCaptureActions(camera).Length, Is.EqualTo(0));
            Assert.That(capturer.EncodedFrames, Is.GreaterThan(2));
            Assert.That(CountFrames(path, out _), Is.EqualTo(capturer.EncodedFrames));
        }

        /// <summary>Checks that an existing output file fails the start without overwriting it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ExistingFile_ReportsError()
        {
            PlayModeWait.ExpectErrorLogs();
            var capturer = CreateCapturer(true, out _);
            string? reported = null;
            capturer.ErrorReceived += (_, message) => reported = message;
            var existing = _directory.WriteText("exists.mkv", "keep");

            var start = capturer.StartRecordingAsync(existing.Location);
            yield return PlayModeWait.Until(() => start.IsCompleted);
            yield return PlayModeWait.FlushLogs();

            Assert.That(start.Exception!.InnerException, Is.InstanceOf<IOException>());
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Error));
            Assert.That(capturer.LastError, Is.Not.Null);
            Assert.That(reported, Is.EqualTo(capturer.LastError));
            Assert.That(existing.ReadAllText(), Is.EqualTo("keep"));
        }

        /// <summary>Checks that a second start while recording is rejected and that a stopped capturer can record again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_WhileRecording_IsRejectedAndRestartWorks()
        {
            var capturer = CreateCapturer(true, out var camera);
            var first = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "first.mkv"));
            yield return PlayModeWait.Until(() => first.IsCompleted);

            var second = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "second.mkv"));
            Assert.That(second.Exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
            yield return RenderFor(camera, 0.2);
            var stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);

            var again = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "again.mkv"));
            yield return PlayModeWait.Until(() => again.IsCompleted);
            Assert.That(again.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Recording));
            Assert.That(capturer.EncodedFrames, Is.EqualTo(0));
            stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
        }

        /// <summary>Checks that disabling the component stops capture and that the stop can still be awaited.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Disable_StopsRecording()
        {
            var capturer = CreateCapturer(true, out var camera);
            var start = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "disabled.mkv"));
            yield return PlayModeWait.Until(() => start.IsCompleted);
            yield return RenderFor(camera, 0.1);

            capturer.enabled = false;
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopping));
            var stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);

            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
        }

        /// <summary>Checks that destroying the recorded camera stops the recording on the next update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DestroyedCamera_StopsRecording()
        {
            var host = _objects.Add<FFmpegCameraCapturer>("Capturer host");
            host.Options = Options();
            var camera = _objects.Add<Camera>("Separate camera");
            camera.enabled = false;
            camera.targetTexture = _objects.Track(new RenderTexture(64, 48, 24));
            host.TargetCamera = camera;
            var stopped = 0;
            host.Stopped += _ => stopped++;
            var start = host.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "destroyed.mkv"));
            yield return PlayModeWait.Until(() => start.IsCompleted);

            Object.Destroy(camera.gameObject);
            yield return PlayModeWait.Until(() => host.State == CameraCaptureState.Stopped);

            Assert.That(host.State, Is.EqualTo(CameraCaptureState.Stopped));
            Assert.That(stopped, Is.EqualTo(1));
        }

        /// <summary>Checks that canceling the recording lifetime ends the recording without reporting an error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancellation_EndsRecordingWithoutError()
        {
            var capturer = CreateCapturer(true, out var camera);
            using var source = new CancellationTokenSource();
            var start = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "canceled.mkv"), source.Token);
            yield return PlayModeWait.Until(() => start.IsCompleted);
            yield return RenderFor(camera, 0.1);

            source.Cancel();
            yield return PlayModeWait.Until(() => capturer.State == CameraCaptureState.Stopped);

            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
            Assert.That(capturer.LastError, Is.Null);
            var stop = capturer.StopRecordingAsync();
            yield return PlayModeWait.Until(() => stop.IsCompleted);
            Assert.That(stop.IsCanceled, Is.True);
            capturer.ResumeRecording();
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
        }

        /// <summary>Checks that controls called from another thread are rejected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Controls_FromAnotherThread_AreRejected()
        {
            var capturer = CreateCapturer(true, out _);
            var task = Task.Run(() => capturer.PauseRecording());
            yield return PlayModeWait.Until(() => task.IsCompleted);

            Assert.That(task.Exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
        }

        /// <summary>Checks that a paused recording whose session has ended is stopped instead of resumed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Resume_AfterSessionEnded_Stops()
        {
            var capturer = CreateCapturer(true, out var camera);
            using var source = new CancellationTokenSource();
            var start = capturer.StartRecordingAsync(FFmpegTestMedia.PathIn(_directory, "resume.mkv"), source.Token);
            yield return PlayModeWait.Until(() => start.IsCompleted);
            capturer.PauseRecording();
            capturer.PauseRecording();

            source.Cancel();
            capturer.ResumeRecording();

            Assert.That(capturer.State, Is.Not.EqualTo(CameraCaptureState.Recording));
            yield return PlayModeWait.Until(() => capturer.State == CameraCaptureState.Stopped);
            Assert.That(capturer.State, Is.EqualTo(CameraCaptureState.Stopped));
        }
    }
}
