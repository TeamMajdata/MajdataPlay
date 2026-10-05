#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Checks camera recording in an isolated real Unity Player.</summary>
public sealed class FFmpegCameraSmoke : MonoBehaviour
{
    /// <summary>Stores the component under test.</summary>
    private FFmpegCameraCapturer? _capturer;
    /// <summary>Stores the output report path.</summary>
    private string _report = "";
    /// <summary>Stores the first test failure.</summary>
    private Exception? _failure;
    /// <summary>Counts successful assertions.</summary>
    private int _checks;
    /// <summary>Indicates that the fixture expects a URP overlay in the final image.</summary>
    private bool _expectOverlay;
    /// <summary>Reuses the URP offscreen render request in the hidden test Player.</summary>
    private readonly RenderPipeline.StandardRequest _renderRequest = new RenderPipeline.StandardRequest();

    /// <summary>Starts this fixture only when camera validation is requested.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FFmpegPlayerSmoke.Argument("-cameraCapture") == "true")
        {
            new GameObject("FFmpeg Camera validation").AddComponent<FFmpegCameraSmoke>();
        }
    }

    private void Start()
    {
        _report = FFmpegPlayerSmoke.Argument("-videoReport");
        MajDebug.SetLogWriter(new StreamWriter(_report + ".diagnostics.log", false));
        Application.runInBackground = true;
        StartCoroutine(RunSafely());
    }

    /// <summary>Contains coroutine failures and always writes a final report.</summary>
    /// <returns>The sequence of asynchronous fixture waits.</returns>
    private IEnumerator RunSafely()
    {
        var routine = Run();
        while (true)
        {
            object? current = null;
            var next = false;
            try { next = routine.MoveNext(); if (next) { current = routine.Current; } }
            catch (Exception error) { _failure = error; }
            if (!next || _failure != null) { break; }
            yield return current;
        }
        if (_capturer != null) { _capturer.StopRecording(); }
        var result = _failure == null ? "PASS: " + _checks + " Camera capture checks" : "FAIL: " + _failure;
        File.WriteAllText(_report, result);
        Debug.Log(result);
        Application.Quit(_failure == null ? 0 : 1);
    }

    /// <summary>Checks display preservation, encoder metadata, pixels, finalization, restart, and disable.</summary>
    /// <returns>The sequence of asynchronous fixture waits.</returns>
    private IEnumerator Run()
    {
        var camera = GameObject.Find("Camera").GetComponent<Camera>();
        Check(camera != null, "fixture camera exists");
        _expectOverlay = GraphicsSettings.currentRenderPipeline != null;
        _capturer = gameObject.AddComponent<FFmpegCameraCapturer>();
        _capturer.TargetCamera = camera;
        var hardware = FFmpegPlayerSmoke.Argument("-captureHardware") == "true";
        var format = hardware ? VideoEncodingFormat.H264 : VideoEncodingFormat.MPEG4;
        var mode = hardware ? VideoRateControlMode.CBR : VideoRateControlMode.VBR;
        if (!hardware && Enum.TryParse<VideoEncodingFormat>(FFmpegPlayerSmoke.Argument("-captureFormat"), out var softwareFormat))
        {
            format = softwareFormat;
        }
        if (!hardware && Enum.TryParse<VideoRateControlMode>(FFmpegPlayerSmoke.Argument("-captureRateControl"), out var softwareMode))
        {
            mode = softwareMode;
        }
        _capturer.Options = new EncoderOptions
        {
            Width = 128, Height = 96, FrameRate = 30, MaximumSoftwareThreads = 1,
            Format = format,
            PreferredEncoderType = hardware ? VideoEncoderType.Hardware : VideoEncoderType.Software,
            RateControlMode = mode,
            BitRate = 400000, MaximumBitRate = 800000
        };
        var target = camera!.targetTexture;
        var initialTexture = new RenderTexture(160, 120, 16);
        initialTexture.Create();
        camera.targetTexture = initialTexture;
        var viewport = camera.rect;
        var path = Path.Combine(Path.GetDirectoryName(_report)!, "camera-" + Guid.NewGuid().ToString("N") + ".mp4");
        var start = _capturer.StartRecordingAsync(path);
        var deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted) { CheckTimeout(deadline); yield return null; }
        start.GetAwaiter().GetResult();
        Check(_capturer.IsRecording && _capturer.EncoderName != null, "recording starts with an actual encoder");
        Check(_capturer.EncoderType == (hardware ? VideoEncoderType.Hardware : VideoEncoderType.Software), "actual encoder backend");
        Check(_capturer.EncodingFormat == format, "actual codec format matches the software or hardware request");
        Check(_capturer.RateControlMode == mode, "actual rate-control mode");
        Check(_capturer.MaximumBitRate == (mode == VideoRateControlMode.CBR ? 400000 : 800000), "effective maximum follows actual CBR or VBR mode");
        _capturer.Options.BitRate = 123000;
        Check(_capturer.BitRate == 400000, "active settings remain a snapshot");
        var begin = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - begin < 1.3f) { RenderFrame(camera); yield return null; }
        Check(camera.targetTexture == initialTexture && camera.rect == viewport, "recording preserves camera output routing");
        Check(_capturer.CurrentBitRate > 0 && _capturer.EncodedFrames > 10, "live encoding and bitrate: frames="
            + _capturer.EncodedFrames + ", bit/s=" + _capturer.CurrentBitRate + ", dropped=" + _capturer.DroppedFrames
            + ", state=" + _capturer.State + ", error=" + _capturer.LastError);
        var stop = _capturer.StopRecordingAsync();
        deadline = Time.realtimeSinceStartup + 30;
        while (!stop.IsCompleted) { CheckTimeout(deadline); yield return null; }
        stop.GetAwaiter().GetResult();
        Check(_capturer.State == CameraCaptureState.Stopped && _capturer.LastError == null, "graceful stop finalizes");
        Check(File.Exists(path) && new FileInfo(path).Length > 0, "recording contains a real file");
        var decode = Task.Run(() => CheckVideo(path, _capturer.EncodedFrames));
        while (!decode.IsCompleted) { CheckTimeout(deadline); yield return null; }
        decode.GetAwaiter().GetResult();

        var restart = CheckFailedStartupRestart(camera, path);
        while (restart.MoveNext()) { yield return restart.Current; }
        var pending = CheckPendingReadbackRestart(camera);
        while (pending.MoveNext()) { yield return pending.Current; }

        var pause = CheckPauseResume(camera);
        while (pause.MoveNext())
        {
            yield return pause.Current;
        }
        var pausedShutdown = CheckPausedShutdown(camera);
        while (pausedShutdown.MoveNext())
        {
            yield return pausedShutdown.Current;
        }
        if (_expectOverlay)
        {
            var pausedTarget = CheckPausedUrpTargetChange(camera);
            while (pausedTarget.MoveNext())
            {
                yield return pausedTarget.Current;
            }
        }
        foreach (var captureRate in new[] { 120, 5 })
        {
            var rates = CheckFrameRateMismatch(camera, captureRate, captureRate == 120 ? 15 : 30);
            while (rates.MoveNext())
            {
                yield return rates.Current;
            }
        }

        // Existing target textures are caller-owned; shutdown must not destroy or replace them.
        var external = new RenderTexture(160, 120, 16);
        external.Create();
        camera.targetTexture = external;
        var second = Path.Combine(Path.GetDirectoryName(_report)!, "camera-restart-" + Guid.NewGuid().ToString("N") + ".mp4");
        start = _capturer.StartRecordingAsync(second);
        deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted) { CheckTimeout(deadline); yield return null; }
        start.GetAwaiter().GetResult();
        begin = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - begin < 0.3f) { RenderFrame(camera); yield return null; }
        _capturer.enabled = false;
        stop = _capturer.StopRecordingAsync();
        while (!stop.IsCompleted) { CheckTimeout(deadline); yield return null; }
        stop.GetAwaiter().GetResult();
        Check(camera.targetTexture == external && external.IsCreated(), "disable preserves caller-owned render texture");
        Check(_capturer.State == CameraCaptureState.Stopped && _capturer.EncodedFrames > 0, "disable drains capture");
        decode = Task.Run(() => CheckVideo(second, _capturer.EncodedFrames));
        while (!decode.IsCompleted) { CheckTimeout(deadline); yield return null; }
        decode.GetAwaiter().GetResult();
        camera.targetTexture = target;
        external.Release();
        Destroy(external);
        initialTexture.Release();
        Destroy(initialTexture);
        _capturer.enabled = true;
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            start = _capturer.StartRecordingAsync(Path.Combine(Path.GetDirectoryName(_report)!, "canceled-" + Guid.NewGuid().ToString("N") + ".mp4"), cancel.Token);
            Check(start.IsCanceled, "pre-canceled recording does not begin");
        }
        File.WriteAllText(_report + ".metadata.txt", "Encoder=" + _capturer.EncoderName + "; type=" + _capturer.EncoderType
            + "; mode=" + _capturer.RateControlMode + "; bit/s=" + _capturer.CurrentBitRate + "; frames=" + _capturer.EncodedFrames
            + "; dropped=" + _capturer.DroppedFrames + "; graphics=" + SystemInfo.graphicsDeviceType);
    }

    /// <summary>Checks that a paused session drains accepted work and resumes without advancing media time.</summary>
    /// <param name="camera">The fixture camera used for real render and readback commands.</param>
    /// <returns>The asynchronous capture, pause, resume, and decode sequence.</returns>
    /// <exception cref="Exception">A recording operation or fixture assertion fails.</exception>
    private IEnumerator CheckPauseResume(Camera camera)
    {
        var capturer = _capturer!;
        var path = NewRecordingPath("pause");
        var start = capturer.StartRecordingAsync(path);
        var deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        start.GetAwaiter().GetResult();
        var sessionField = typeof(FFmpegCameraCapturer).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var session = sessionField.GetValue(capturer);
        var encoder = capturer.EncoderName;
        var began = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - began < 0.4f)
        {
            RenderFrame(camera);
            yield return null;
        }
        capturer.PauseRecording();
        capturer.PauseRecording();
        var frozen = capturer.TimeSeconds;
        var dropped = capturer.DroppedFrames;
        var accepted = capturer.EncodedFrames;
        Check(capturer.IsPaused && capturer.State == CameraCaptureState.Paused && !capturer.CanStartRecording,
            "pause retains a live recording and prevents replacement startup");
        began = Time.realtimeSinceStartup;
        while (!AreCaptureBuffersFree(session!))
        {
            CheckTimeout(deadline);
            RenderFrame(camera);
            yield return null;
        }
        Check(capturer.EncodedFrames >= accepted, "already accepted GPU and encoder work may drain while paused");
        var drained = capturer.EncodedFrames;
        var drainedAt = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - began < 0.75f || Time.realtimeSinceStartup - drainedAt < 0.4f)
        {
            RenderFrame(camera);
            yield return null;
        }
        Check(capturer.EncodedFrames == drained, "rendering while paused does not enqueue new video frames");
        Check(capturer.TimeSeconds == frozen && capturer.DroppedFrames == dropped,
            "paused recording time and omitted-interval counter remain frozen");
        Check(capturer.OutputPath == path && capturer.EncoderName == encoder
            && ReferenceEquals(session, sessionField.GetValue(capturer)), "pause keeps the same file and native encoder session");
        capturer.ResumeRecording();
        capturer.PauseRecording();
        capturer.ResumeRecording();
        capturer.ResumeRecording();
        Check(!capturer.IsPaused && capturer.IsRecording, "resume and repeated controls retain the recording state");
        began = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - began < 0.4f)
        {
            RenderFrame(camera);
            yield return null;
        }
        Check(capturer.EncodedFrames > drained, "resumed camera renders produce new encoded frames");
        var stop = capturer.StopRecordingAsync();
        while (!stop.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        stop.GetAwaiter().GetResult();
        var active = capturer.TimeSeconds;
        Check(active >= 0.7 && active < 1.15, "recording clock excludes the long pause");
        var decode = Task.Run(() => CheckVideo(path, capturer.EncodedFrames));
        while (!decode.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        var timing = decode.GetAwaiter().GetResult();
        Check(timing.Last - timing.First > active - 0.2 && timing.Last <= active + 0.04,
            "decoded media span excludes the paused wall-clock interval");
        Check(timing.MaximumGap < 0.25, "pause and immediate resume do not insert a large PTS gap");
        WriteTiming("pause-resume", capturer.FrameRate, active, timing, 0);
    }

    /// <summary>Checks graceful stop and caller cancellation from the paused state.</summary>
    /// <param name="camera">The camera supplying accepted GPU frames before pausing.</param>
    /// <returns>The asynchronous paused finalization and cancellation sequence.</returns>
    /// <exception cref="Exception">A recording operation or fixture assertion fails.</exception>
    private IEnumerator CheckPausedShutdown(Camera camera)
    {
        var capturer = _capturer!;
        var path = NewRecordingPath("paused-stop");
        var start = capturer.StartRecordingAsync(path);
        var deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        start.GetAwaiter().GetResult();
        var begin = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - begin < 0.25f)
        {
            RenderFrame(camera);
            yield return null;
        }
        capturer.PauseRecording();
        var stop = capturer.StopRecordingAsync();
        while (!stop.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        stop.GetAwaiter().GetResult();
        Check(capturer.State == CameraCaptureState.Stopped && !capturer.IsPaused && capturer.CanStartRecording,
            "stop while paused drains and releases the recording for reuse");
        var decode = Task.Run(() => CheckVideo(path, capturer.EncodedFrames));
        while (!decode.IsCompleted)
        {
            CheckTimeout(deadline);
            yield return null;
        }
        Check(decode.GetAwaiter().GetResult().Count > 0, "paused stop finalizes accepted frames");
        using (var cancel = new CancellationTokenSource())
        {
            path = NewRecordingPath("paused-cancel");
            start = capturer.StartRecordingAsync(path, cancel.Token);
            deadline = Time.realtimeSinceStartup + 30;
            while (!start.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            start.GetAwaiter().GetResult();
            RenderFrame(camera);
            capturer.PauseRecording();
            cancel.Cancel();
            capturer.ResumeRecording();
            Check(!capturer.IsRecording, "immediate resume cannot revive a canceled paused worker");
            stop = capturer.StopRecordingAsync();
            while (!stop.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            Check(stop.IsCanceled && capturer.State == CameraCaptureState.Stopped && capturer.CanStartRecording,
                "paused cancellation waits for GPU release and permits safe reuse");
            capturer.ResumeRecording();
            Check(!capturer.IsRecording && !capturer.IsPaused, "resume after shutdown is a no-op");
            using (var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(released.Length >= 0, "paused cancellation releases the native file handle");
            }
        }

        var temporary = new GameObject("Paused camera destruction fixture");
        var temporaryCamera = temporary.AddComponent<Camera>();
        temporaryCamera.CopyFrom(camera);
        temporaryCamera.enabled = false;
        capturer.TargetCamera = temporaryCamera;
        try
        {
            path = NewRecordingPath("paused-destroyed-camera");
            start = capturer.StartRecordingAsync(path);
            deadline = Time.realtimeSinceStartup + 30;
            while (!start.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            start.GetAwaiter().GetResult();
            capturer.PauseRecording();
            Destroy(temporaryCamera);
            yield return null;
            yield return null;
            Check(!capturer.IsPaused && !capturer.IsRecording, "destroying a paused source camera starts shutdown");
            stop = capturer.StopRecordingAsync();
            while (!stop.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            stop.GetAwaiter().GetResult();
            Check(capturer.State == CameraCaptureState.Stopped && capturer.CanStartRecording,
                "destroyed paused camera releases recording resources");
        }
        finally
        {
            capturer.TargetCamera = camera;
            Destroy(temporary);
        }
    }

    /// <summary>Checks that pausing unregisters a URP display capture before changing to an explicit target.</summary>
    /// <param name="camera">The URP base camera with the fixture overlay stack.</param>
    /// <returns>The asynchronous source change and final decoded recording sequence.</returns>
    /// <exception cref="Exception">A recording operation or fixture assertion fails.</exception>
    private IEnumerator CheckPausedUrpTargetChange(Camera camera)
    {
        var capturer = _capturer!;
        var target = camera.targetTexture;
        var enabled = camera.enabled;
        string? renderError = null;
        Application.LogCallback checkRenderLog = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                renderError = message + Environment.NewLine + stack;
            }
        };
        Application.logMessageReceived += checkRenderLog;
        try
        {
            camera.enabled = false;
            camera.targetTexture = null;
            var path = NewRecordingPath("paused-urp-target");
            var start = capturer.StartRecordingAsync(path);
            var deadline = Time.realtimeSinceStartup + 30;
            while (!start.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            start.GetAwaiter().GetResult();
            capturer.PauseRecording();
            camera.targetTexture = target;
            var frozen = capturer.TimeSeconds;
            RenderFrame(camera);
            yield return null;
            Check(capturer.IsPaused && capturer.TimeSeconds == frozen && capturer.LastError == null,
                "paused URP target change leaves display capture unregistered");
            Check(renderError == null, "paused URP render reports no internal pipeline error: " + renderError);
            capturer.ResumeRecording();
            var begin = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - begin < 0.3f)
            {
                RenderFrame(camera);
                yield return null;
            }
            var stop = capturer.StopRecordingAsync();
            while (!stop.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            stop.GetAwaiter().GetResult();
            Check(capturer.EncodedFrames > 0 && capturer.LastError == null, "URP resumes capture from the current explicit target");
            Check(renderError == null, "resumed URP render reports no internal pipeline error: " + renderError);
            var decode = Task.Run(() => CheckVideo(path, capturer.EncodedFrames));
            while (!decode.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            decode.GetAwaiter().GetResult();
        }
        finally
        {
            Application.logMessageReceived -= checkRenderLog;
            camera.targetTexture = target;
            camera.enabled = enabled;
        }
    }

    /// <summary>Inspects the bounded session pool under its ownership lock.</summary>
    /// <param name="session">The current recording session retained during pause.</param>
    /// <returns>True when every GPU, queued, and encoding reservation has been released.</returns>
    private static bool AreCaptureBuffersFree(object session)
    {
        var type = session.GetType();
        var gate = type.GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var frames = (Array)type.GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        lock (gate)
        {
            foreach (var frame in frames)
            {
                var state = frame!.GetType().GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (Convert.ToInt32(state.GetValue(frame)) != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Checks sampling and real PTS when capture and controlled camera render rates differ.</summary>
    /// <param name="camera">The camera driven only through explicit offscreen renders.</param>
    /// <param name="captureRate">The video time base and maximum sampling rate.</param>
    /// <param name="renderRate">The requested rate of explicit source camera renders.</param>
    /// <returns>The asynchronous rate-limited capture and decoded timing sequence.</returns>
    /// <exception cref="Exception">A recording operation or fixture assertion fails.</exception>
    private IEnumerator CheckFrameRateMismatch(Camera camera, int captureRate, int renderRate)
    {
        var capturer = _capturer!;
        var originalRate = capturer.Options.FrameRate;
        var enabled = camera.enabled;
        var sync = QualitySettings.vSyncCount;
        var targetRate = Application.targetFrameRate;
        try
        {
            camera.enabled = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 90;
            capturer.Options.FrameRate = captureRate;
            var path = NewRecordingPath("fps-" + captureRate);
            var start = capturer.StartRecordingAsync(path);
            var deadline = Time.realtimeSinceStartup + 30;
            while (!start.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            start.GetAwaiter().GetResult();
            var rendered = 0;
            var begin = Time.realtimeSinceStartup;
            var wait = new WaitForSecondsRealtime(1.0f / renderRate);
            while (Time.realtimeSinceStartup - begin < 1.5f)
            {
                RenderFrame(camera);
                rendered++;
                yield return wait;
            }
            var stop = capturer.StopRecordingAsync();
            while (!stop.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            stop.GetAwaiter().GetResult();
            var active = capturer.TimeSeconds;
            var frames = capturer.EncodedFrames;
            Check(frames > 3 && frames <= rendered, "mismatched FPS records only actual source renders");
            if (captureRate > renderRate)
            {
                Check(frames < active * captureRate / 2 && capturer.DroppedFrames > 0,
                    "slow source renders preserve omitted intervals without synthetic CFR duplicates");
            }
            else
            {
                Check(frames <= Math.Ceiling(active * captureRate) + 1 && frames < rendered / 2,
                    "fast source renders are sampled at the configured lower capture rate");
            }
            var decode = Task.Run(() => CheckVideo(path, frames));
            while (!decode.IsCompleted)
            {
                CheckTimeout(deadline);
                yield return null;
            }
            var timing = decode.GetAwaiter().GetResult();
            Check(timing.Last - timing.First > 1.15 && timing.Last <= active + 0.04,
                "decoded duration retains wall-clock time when source and capture rates differ");
            if (captureRate > renderRate)
            {
                Check(timing.MaximumGap > 2.0 / captureRate, "high capture FPS preserves source-render PTS gaps");
            }
            WriteTiming("capture-" + captureRate + "-render-" + renderRate, captureRate, active, timing, rendered);
        }
        finally
        {
            capturer.Options.FrameRate = originalRate;
            camera.enabled = enabled;
            QualitySettings.vSyncCount = sync;
            Application.targetFrameRate = targetRate;
        }
    }

    /// <summary>Returns a unique local recording path for one fixture phase.</summary>
    /// <param name="phase">The phase label included in the filename.</param>
    /// <returns>A fresh MP4 path beside the fixture report.</returns>
    private string NewRecordingPath(string phase)
    {
        return Path.Combine(Path.GetDirectoryName(_report)!, "camera-" + phase + "-" + Guid.NewGuid().ToString("N") + ".mp4");
    }

    /// <summary>Records measured timing evidence separately from the concise PASS report.</summary>
    /// <param name="phase">The recording phase being described.</param>
    /// <param name="frameRate">The configured recording time base.</param>
    /// <param name="active">The component clock after excluding pauses and finalization.</param>
    /// <param name="timing">The actual decoded timestamps and count.</param>
    /// <param name="renders">The controlled source render count, or zero for unrestricted rendering.</param>
    /// <exception cref="IOException">The isolated timing evidence file cannot be written.</exception>
    private void WriteTiming(string phase, int frameRate, double active, VideoTiming timing, int renders)
    {
        File.AppendAllText(_report + ".timing.txt", phase + ": captureFPS=" + frameRate + ", renders=" + renders
            + ", frames=" + timing.Count + ", active=" + active + ", PTS=" + timing.First + ".." + timing.Last
            + ", maxGap=" + timing.MaximumGap + Environment.NewLine);
    }

    /// <summary>Stores measured timing from one complete native video decode.</summary>
    private readonly struct VideoTiming
    {
        /// <summary>Gets the first decoded presentation timestamp.</summary>
        public double First { get; }
        /// <summary>Gets the last decoded presentation timestamp.</summary>
        public double Last { get; }
        /// <summary>Gets the largest interval between decoded frames.</summary>
        public double MaximumGap { get; }
        /// <summary>Gets the actual decoded frame count.</summary>
        public long Count { get; }

        /// <summary>Creates the timing measurements for a completed recording.</summary>
        /// <param name="first">The first decoded timestamp.</param>
        /// <param name="last">The last decoded timestamp.</param>
        /// <param name="maximumGap">The largest decoded timestamp interval.</param>
        /// <param name="count">The number of decoded frames.</param>
        public VideoTiming(double first, double last, double maximumGap, long count)
        {
            First = first;
            Last = last;
            MaximumGap = maximumGap;
            Count = count;
        }
    }

    /// <summary>Checks that cancellation cannot accumulate old GPU pools across immediate restarts.</summary>
    /// <param name="camera">The camera retaining the caller-owned output texture.</param>
    /// <returns>The asynchronous cancellation and pending readback release sequence.</returns>
    private IEnumerator CheckPendingReadbackRestart(Camera camera)
    {
        var capturer = _capturer!;
        using (var cancel = new CancellationTokenSource())
        {
            var path = Path.Combine(Path.GetDirectoryName(_report)!, "camera-pending-" + Guid.NewGuid().ToString("N") + ".mp4");
            var start = capturer.StartRecordingAsync(path, cancel.Token);
            var deadline = Time.realtimeSinceStartup + 30;
            while (!start.IsCompleted) { CheckTimeout(deadline); yield return null; }
            start.GetAwaiter().GetResult();
            RenderFrame(camera);
            cancel.Cancel();
            var session = typeof(FFmpegCameraCapturer).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capturer)!;
            var finished = session.GetType().GetProperty("Finished")!;
            Check(SpinWait.SpinUntil(() => (bool)finished.GetValue(session)!, 5000), "canceled worker releases its native output");
            var rejectedPath = Path.Combine(Path.GetDirectoryName(_report)!, "camera-rejected-" + Guid.NewGuid().ToString("N") + ".mp4");
            var rejected = capturer.StartRecordingAsync(rejectedPath);
            Check(rejected.IsFaulted && rejected.Exception!.InnerException is InvalidOperationException,
                "restart waits for outstanding GPU readbacks instead of creating another pool");
            Check(!File.Exists(rejectedPath), "rejected restart does not create output");
            var stop = capturer.StopRecordingAsync();
            while (!stop.IsCompleted) { CheckTimeout(deadline); yield return null; }
            Check(stop.IsCanceled, "stop waits for pending GPU release and reports lifetime cancellation");
            Check(capturer.State == CameraCaptureState.Stopped, "canceled recording is fully stopped before reuse");
        }
    }

    /// <summary>Checks that a failed startup continuation cannot stop its replacement session.</summary>
    /// <param name="camera">The camera to render for the replacement recording.</param>
    /// <param name="existingPath">An existing output that must cause the old startup to fail.</param>
    /// <returns>The asynchronous startup, capture, and shutdown sequence.</returns>
    private IEnumerator CheckFailedStartupRestart(Camera camera, string existingPath)
    {
        var capturer = _capturer!;
        var field = typeof(FFmpegCameraCapturer).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task? failed = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            failed = capturer.StartRecordingAsync(existingPath);
            var session = field.GetValue(capturer)!;
            var finished = session.GetType().GetProperty("Finished")!;
            Check(SpinWait.SpinUntil(() => (bool)finished.GetValue(session)!, 5000), "failed worker releases its native output");
            // Keep the Unity synchronization-context continuation pending while replacing the session.
            if (capturer.State == CameraCaptureState.Preparing) { break; }
            _ = failed.Exception;
        }
        Check(capturer.State == CameraCaptureState.Preparing, "old startup continuation is deferred");
        var path = Path.Combine(Path.GetDirectoryName(_report)!, "camera-race-" + Guid.NewGuid().ToString("N") + ".mp4");
        var start = capturer.StartRecordingAsync(path);
        var deadline = Time.realtimeSinceStartup + 30;
        while (!start.IsCompleted || !failed!.IsCompleted) { CheckTimeout(deadline); yield return null; }
        Check(failed!.IsFaulted, "old startup failure still reaches its caller");
        _ = failed.Exception;
        start.GetAwaiter().GetResult();
        Check(capturer.IsRecording && capturer.LastError == null, "old failure does not stop or contaminate replacement recording");
        var begin = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - begin < 0.2f) { RenderFrame(camera); yield return null; }
        var stop = capturer.StopRecordingAsync();
        while (!stop.IsCompleted) { CheckTimeout(deadline); yield return null; }
        stop.GetAwaiter().GetResult();
        Check(capturer.State == CameraCaptureState.Stopped && capturer.EncodedFrames > 0, "replacement records and finalizes normally");
        var decode = Task.Run(() => CheckVideo(path, capturer.EncodedFrames));
        while (!decode.IsCompleted) { CheckTimeout(deadline); yield return null; }
        decode.GetAwaiter().GetResult();
    }

    /// <summary>Drives a real offscreen camera render when the hidden Player display is occluded.</summary>
    /// <param name="camera">The camera under test, retaining its caller-owned target texture.</param>
    private void RenderFrame(Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            camera.Render();
        }
        else
        {
            _renderRequest.destination = camera.targetTexture;
            RenderPipeline.SubmitRenderRequest(camera, _renderRequest);
        }
    }

    /// <summary>Decodes every frame and checks orientation, timestamps, and delayed packet draining.</summary>
    /// <param name="path">The finalized recording to decode.</param>
    /// <param name="expectedFrames">The encoder's submitted frame count.</param>
    /// <returns>The measured decoded presentation timestamps and frame count.</returns>
    private VideoTiming CheckVideo(string path, long expectedFrames)
    {
        using (var decoder = new FFmpegVideoDecoder(new DecoderOptions()))
        {
            decoder.Open(path, CancellationToken.None);
            long count = 0;
            var previous = -1.0;
            var first = 0.0;
            var maximumGap = 0.0;
            while (true)
            {
                using (var frame = decoder.ReadFrame())
                {
                    if (frame == null) { break; }
                    Check(frame.PresentationTime > previous, "monotonic capture timestamps");
                    if (count == 0)
                    {
                        first = frame.PresentationTime;
                    }
                    else
                    {
                        maximumGap = Math.Max(maximumGap, frame.PresentationTime - previous);
                    }
                    previous = frame.PresentationTime;
                    var bottom = (12 * frame.Width + frame.Width / 2) * 4;
                    var top = ((frame.Height - 12) * frame.Width + frame.Width / 2) * 4;
                    Check(Marshal.ReadByte(frame.Data, bottom + 2) > 180 && Marshal.ReadByte(frame.Data, bottom) < 60, "blue image bottom");
                    Check(Marshal.ReadByte(frame.Data, top) > 180 && Marshal.ReadByte(frame.Data, top + 2) < 60, "red image top");
                    if (_expectOverlay)
                    {
                        var marker = (72 * frame.Width + 100) * 4;
                        Check(Marshal.ReadByte(frame.Data, marker + 1) > 180 && Marshal.ReadByte(frame.Data, marker) < 60,
                            "camera stack overlay appears in final recording");
                    }
                    count++;
                }
            }
            Check(count == expectedFrames, "all delayed encoded frames were drained");
            return new VideoTiming(first, previous, maximumGap, count);
        }
    }

    /// <summary>Fails stalled asynchronous operations after the supplied deadline.</summary>
    /// <param name="deadline">The unscaled realtime deadline in seconds.</param>
    /// <exception cref="TimeoutException">The asynchronous test exceeded its deadline.</exception>
    private static void CheckTimeout(float deadline)
    {
        if (Time.realtimeSinceStartup > deadline) { throw new TimeoutException("Camera validation timed out."); }
    }

    /// <summary>Counts a successful assertion or raises a descriptive failure.</summary>
    /// <param name="condition">Whether the behavior matched the fixture's expectation.</param>
    /// <param name="description">The behavior being verified.</param>
    /// <exception cref="InvalidOperationException">The assertion failed.</exception>
    private void Check(bool condition, string description)
    {
        if (!condition) { throw new InvalidOperationException(description); }
        _checks++;
    }
}
