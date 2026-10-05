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
        _capturer.Options = new EncoderOptions
        {
            Width = 128, Height = 96, FrameRate = 30, MaximumSoftwareThreads = 1,
            Format = hardware ? VideoEncodingFormat.H264 : VideoEncodingFormat.MPEG4,
            PreferredEncoderType = hardware ? VideoEncoderType.Hardware : VideoEncoderType.Software,
            RateControlMode = hardware ? VideoRateControlMode.CBR : VideoRateControlMode.VBR,
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
        Check(_capturer.RateControlMode == (hardware ? VideoRateControlMode.CBR : VideoRateControlMode.VBR), "actual rate-control mode");
        Check(_capturer.MaximumBitRate == (hardware ? 400000 : 800000), "effective maximum follows actual CBR or VBR mode");
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
    private void CheckVideo(string path, long expectedFrames)
    {
        using (var decoder = new FFmpegVideoDecoder(new DecoderOptions()))
        {
            decoder.Open(path, CancellationToken.None);
            long count = 0;
            var previous = -1.0;
            while (true)
            {
                using (var frame = decoder.ReadFrame())
                {
                    if (frame == null) { break; }
                    Check(frame.PresentationTime > previous, "monotonic capture timestamps");
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
