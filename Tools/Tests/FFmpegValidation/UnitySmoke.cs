#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using UnityEngine;

public sealed class FFmpegPlayerSmoke : MonoBehaviour
{
    /// <summary>Owns the production player used by the isolated validation scene.</summary>
    private FFmpegVideoPlayer _player = null!;
    /// <summary>Counts completed assertions.</summary>
    private int _checks;
    /// <summary>Counts frames actually presented through the production FrameReady event.</summary>
    private int _frames;
    /// <summary>Stores the optional result and evidence file prefix.</summary>
    private string? _report;
    /// <summary>Stores the first observed validation or playback failure.</summary>
    private Exception? _failure;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (Argument("-cameraCapture") != "true") new GameObject("FFmpeg Player validation").AddComponent<FFmpegPlayerSmoke>(); }
    void Start()
    {
        _report = Argument("-videoReport");
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(_report)) _report = Path.Combine(Application.persistentDataPath, "ffmpeg-smoke.txt");
#endif
        if (!string.IsNullOrEmpty(_report))
            MajDebug.SetLogWriter(new StreamWriter(_report + ".diagnostics.log", false));
        Application.runInBackground = true;
        StartCoroutine(RunSafely());
    }
    IEnumerator RunSafely()
    {
        var test = Run();
        while (true)
        {
            object? current = null;
            bool next = false;
            try { next = test.MoveNext(); if (next) current = test.Current; }
            catch (Exception error) { _failure = error; }
            if (!next || _failure != null) break;
            yield return current;
        }
        string result = _failure == null
            ? "PASS: " + _checks + " assertions; " + SystemInfo.graphicsDeviceType + "; " + _player.TransferMode + "; frames=" + _frames + "; fallback=" + _player.HardwareFallbackReason
            : "FAIL: " + _failure;
        Debug.Log(result);
        if (!string.IsNullOrEmpty(_report)) File.WriteAllText(_report, result);
        if (_player != null) _player.Close();
        MajDebug.FlushLog();
        Application.Quit(_failure == null ? 0 : 1);
    }
    IEnumerator Run()
    {
        _player = gameObject.AddComponent<FFmpegVideoPlayer>();
        _player.PreferredDecoderType = Argument("-videoHardware") == "true" ? VideoDecoderType.Hardware : VideoDecoderType.Software;
        _player.RequireHardwareDecoding = Argument("-videoRequireHardware") == "true" && Argument("-videoTestRecovery") != "true";
        _player.PreferNativeTextures = Argument("-videoHardwareCpuUpload") != "true";
        if (Argument("-videoGraphics") == "vulkan")
        {
            Check(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                "the Vulkan regression uses an actual Vulkan renderer");
        }
        _player.FrameReady += (_, __) => _frames++;
        _player.ErrorReceived += (_, error) => _failure = new Exception(error);
        var externalMedia = Argument("-videoMedia");
        var path = string.IsNullOrEmpty(externalMedia) ? Path.Combine(Application.streamingAssetsPath, "test.mp4") : externalMedia!;
#if UNITY_ANDROID && !UNITY_EDITOR
        // FFmpeg cannot open an APK's jar: URL. Extract the encoded fixture once;
        // this is input I/O, not a decoded video-pixel readback or GPU upload.
        if (string.IsNullOrEmpty(externalMedia))
        {
            string extracted = Path.Combine(Application.temporaryCachePath, "ffmpeg-smoke.mp4");
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(path))
            {
                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(extracted);
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                    throw new IOException("Cannot extract the Android video fixture: " + request.error);
            }
            path = extracted;
        }
#endif
        var prepare = _player.PreloadAsync(path);
        var start = UnityEngine.Time.realtimeSinceStartup;
        while (!prepare.IsCompleted) { CheckTimeout(start); yield return null; }
        prepare.GetAwaiter().GetResult();
        Check(_player.IsPrepared && !_player.IsPlaying, "preload is paused");
        CheckHardwarePath();

        CheckDecoderIdentity();
        var extendedRegression = int.TryParse(Argument("-videoDoubleRateSeconds"), out var requestedSeconds) && requestedSeconds > 3;
        Check(_player.Texture != null && _player.Width > 0 && _player.Height > 0, "preload presents first texture");
        Check(_player.Length > 0 && _player.IsSeekable, "timeline metadata");
        var first = _player.Time;
        long firstBitRate = _player.CurrentBitRate;
        Check(firstBitRate > 0, "first displayed frame reports its video bitrate");
        yield return new WaitForSecondsRealtime(0.12f);
        Check(_player.Time == first, "preload clock frozen");
        Check(_player.CurrentBitRate == firstBitRate, "decode read-ahead does not change displayed bitrate while preloaded");
        _player.PlaybackRate = 2;
        Check(_player.PlaybackRate == 2, "rate accepted");
        Check(_player.CurrentBitRate == firstBitRate, "playback rate does not scale the media bitrate");
        var rejectedReverse = false;
        try
        {
            _player.PlaybackRate = -1;
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedReverse = true;
        }

        Check(rejectedReverse && _player.PlaybackRate == 2, "reverse rate rejected");
        if (Argument("-videoTestDecodeOverload") == "true")
        {
            var overload = TestDecodeOverload();
            while (overload.MoveNext())
            {
                yield return overload.Current;
            }
        }

        if (Argument("-videoTestPlaybackThroughput") == "true")
        {
            var throughput = TestPlaybackThroughput();
            while (throughput.MoveNext())
            {
                yield return throughput.Current;
            }
        }
        var doubleRatePlayback = TestDoubleRatePlayback();
        while (doubleRatePlayback.MoveNext())
        {
            yield return doubleRatePlayback.Current;
        }
        var paused = _player.Time;
        long pausedBitRate = _player.CurrentBitRate;
        yield return new WaitForSecondsRealtime(0.12f);
        Check(Math.Abs(_player.Time - paused) < 2, "pause freezes clock");
        Check(_player.CurrentBitRate == pausedBitRate, "pause retains bitrate of the displayed frame");
        var seek = _player.SeekAsync(Math.Min(1, _player.LengthSeconds / 2));
        Check(_player.CurrentBitRate == 0, "seek clears stale bitrate before replacement frame");
        start = UnityEngine.Time.realtimeSinceStartup;
        while (!seek.IsCompleted) { CheckTimeout(start); yield return null; }
        seek.GetAwaiter().GetResult();
        Check(!_player.IsPlaying && _player.State == VideoPlaybackState.Paused, "seek retains pause");
        Check(_player.CurrentBitRate > 0, "seek updates bitrate from the replacement frame");
        // Exercise actual GPU contents, not just frame notifications. The selected fixture
        // contains a non-uniform image at this timestamp; use equivalent media if overriding it.
        yield return null;
        CheckTextureContents();
        bool releasedTargets = ReleaseHardwareTargets();
        var frameCount = _frames;
        _player.NextFrame();
        start = UnityEngine.Time.realtimeSinceStartup;
        while (_frames == frameCount) { CheckTimeout(start); yield return null; }
        Check(!_player.IsPlaying, "single frame step");
        if (releasedTargets)
        {
            yield return null;
            CheckHardwarePath();
            Check(_player.Texture is RenderTexture restored && restored.IsCreated(), "hardware output recovers after render-target loss");
            CheckTextureContents();
        }
        _player.Stop();
        start = UnityEngine.Time.realtimeSinceStartup;
        while (_player.State == VideoPlaybackState.Seeking) { CheckTimeout(start); yield return null; }
        Check(_player.Time == 0 && _player.State == VideoPlaybackState.Stopped, "stop rewinds");
        _player.Play();
        var displayedEnd = typeof(FFmpegVideoPlayer).GetField("_lastFrameEnd", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(FFmpegVideoPlayer).FullName, "_lastFrameEnd");
        var seekAttempts = extendedRegression ? 8 : 1;
        for (var index = 0; index < seekAttempts; index++)
        {
            var target = Math.Min(0.4 + (index % 4) * 0.5, _player.LengthSeconds / 2);
            var older = _player.SeekAsync(Math.Min(0.2 + (index % 2) * 0.5, _player.LengthSeconds / 2));
            var newer = _player.SeekAsync(target);
            start = UnityEngine.Time.realtimeSinceStartup;
            while (!newer.IsCompleted)
            {
                CheckTimeout(start);
                yield return null;
            }

            newer.GetAwaiter().GetResult();
            Check(older.IsCanceled && _player.IsPlaying && _player.PlaybackRate == 2,
                "seek supersedes the older request while preserving 2x play intent: " + index);
            if (extendedRegression)
            {
                var seekFrames = _frames;
                start = UnityEngine.Time.realtimeSinceStartup;
                while (_frames == seekFrames)
                {
                    CheckTimeout(start);
                    yield return null;
                }

                var presentedEnd = (double)displayedEnd.GetValue(_player)!;
                Check(_player.IsPlaying && presentedEnd >= target && Math.Abs(presentedEnd - _player.TimeSeconds) <= 0.3,
                    "repeated seek continues presenting current 2x frames rather than stale pre-seek frames: " + index);
            }
        }

        if (extendedRegression)
        {
            var resumed = CheckDoubleRateResumption("final repeated seek");
            while (resumed.MoveNext())
            {
                yield return resumed.Current;
            }

            CheckTextureContents();
        }

        int ended = 0;
        _player.EndReached += _ => ended++;
        _player.Loop = true;
        seek = _player.SeekAsync(Math.Max(0, _player.LengthSeconds - 0.15));
        start = UnityEngine.Time.realtimeSinceStartup;
        while (!seek.IsCompleted)
        {
            CheckTimeout(start);
            yield return null;
        }

        seek.GetAwaiter().GetResult();
        while (ended == 0)
        {
            CheckTimeout(start);
            yield return null;
        }

        while (!_player.IsPlaying)
        {
            CheckTimeout(start);
            yield return null;
        }

        Check(_player.Time < 1000 && _player.PlaybackRate == 2, "loop returns to the beginning at 2x");
        if (extendedRegression)
        {
            var resumed = CheckDoubleRateResumption("loop");
            while (resumed.MoveNext())
            {
                yield return resumed.Current;
            }
        }

        CheckHardwarePath();
        CheckDecoderIdentity();
        _player.Close();
        Check(!_player.IsPrepared && _player.Texture == null, "close releases texture and session");
        Check(_player.CurrentBitRate == 0, "close clears bitrate");
        if (extendedRegression)
        {
            var closedFrames = _frames;
            yield return new WaitForSecondsRealtime(0.15f);
            Check(_frames == closedFrames && _player.BufferedFrames == 0 && _player.Texture == null && _player.State == VideoPlaybackState.Idle,
                "close during 2x playback rejects late frames and keeps the player closed");
            var reopened = _player.PreloadAsync(path);
            start = UnityEngine.Time.realtimeSinceStartup;
            while (!reopened.IsCompleted)
            {
                CheckTimeout(start);
                yield return null;
            }

            reopened.GetAwaiter().GetResult();

            CheckDecoderIdentity();
            _player.Play();
            var reopenedFrames = _frames;
            start = UnityEngine.Time.realtimeSinceStartup;
            while (_frames == reopenedFrames)
            {
                CheckTimeout(start);
                yield return null;
            }

            Check(_player.IsPlaying && _player.PlaybackRate == 2, "reopening continues actual 2x presentation after close");
            var interruptedSeek = _player.SeekAsync(Math.Min(1, _player.LengthSeconds / 2));
            _player.Close();
            Check(interruptedSeek.IsCanceled, "close cancels an in-flight seek during 2x playback");
            closedFrames = _frames;
            yield return new WaitForSecondsRealtime(0.15f);
            Check(_frames == closedFrames && !_player.IsPrepared && _player.Texture == null && _player.BufferedFrames == 0,
                "a cancelled 2x seek cannot revive or present through the closed session");
        }

        var interrupted = _player.PreloadAsync(path);
        _player.Close();
        Check(interrupted.IsCanceled, "close cancels preparation");
        if (Argument("-videoTestRecovery") == "true")
        {
            // Move the nested iterator here so RunSafely also catches its exceptions.
            var recovery = TestRecovery(path);
            while (recovery.MoveNext()) yield return recovery.Current;
        }
        if (Argument("-videoStress") == "true")
        {
            var stress = TestSustainedPlayback(path);
            while (stress.MoveNext()) yield return stress.Current;
        }
        if (Argument("-videoTestDecoderPreference") == "true")
        {
            var preference = TestDecoderPreference(path);
            while (preference.MoveNext()) yield return preference.Current;
        }
        if (!string.IsNullOrEmpty(_report))
        {
            MajDebug.FlushLog();
            string diagnostics;
            using (var reader = new StreamReader(new FileStream(_report + ".diagnostics.log", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                diagnostics = reader.ReadToEnd();
            Check(diagnostics.Contains("[FFmpeg]") && diagnostics.Contains("decoder=") && diagnostics.Contains("device="),
                "real MajDebug log records selected decoder and device");
            Check(diagnostics.Contains("transport="), "real MajDebug log records frame transport selection");
        }
    }
    /// <summary>Checks one second of stable double-speed presentation after a seek or a loop.</summary>
    /// <param name="phase">The control transition to identify in assertion failures and logs.</param>
    /// <returns>The coroutine that observes continuous video and clock progress without changing playback controls.</returns>
    /// <exception cref="MissingFieldException">The displayed frame timestamp is unavailable.</exception>
    /// <exception cref="Exception">Playback stops, timestamps regress, a frame stalls, or progress does not remain near 2x.</exception>
    private IEnumerator CheckDoubleRateResumption(string phase)
    {
        var displayedEnd = typeof(FFmpegVideoPlayer).GetField("_lastFrameEnd", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(FFmpegVideoPlayer).FullName, "_lastFrameEnd");
        yield return new WaitForSecondsRealtime(0.2f);
        var initialTime = _player.TimeSeconds;
        var initialVideo = (double)displayedEnd.GetValue(_player)!;
        var initialFrames = _frames;
        var observedFrames = _frames;
        var lastTime = initialTime;
        var lastVideo = initialVideo;
        var lastPresentation = 0d;
        var maximumGap = 0d;
        var maximumError = 0d;
        var wentBackwards = false;
        var wallClock = System.Diagnostics.Stopwatch.StartNew();
        while (wallClock.Elapsed.TotalSeconds < 1)
        {
            yield return null;
            var elapsed = wallClock.Elapsed.TotalSeconds;
            var time = _player.TimeSeconds;
            var video = (double)displayedEnd.GetValue(_player)!;
            wentBackwards |= time < lastTime - 0.000001 || video < lastVideo - 0.000001;
            lastTime = time;
            lastVideo = video;
            maximumGap = Math.Max(maximumGap, elapsed - lastPresentation);
            maximumError = Math.Max(maximumError, Math.Abs(time - video) / 2);
            if (_frames != observedFrames)
            {
                observedFrames = _frames;
                lastPresentation = elapsed;
            }
        }

        wallClock.Stop();
        var seconds = wallClock.Elapsed.TotalSeconds;
        var clockRate = (lastTime - initialTime) / seconds;
        var videoRate = (lastVideo - initialVideo) / seconds;
        Debug.Log("2x resumption: " + phase + "; clock=" + clockRate.ToString("F3") + "x; video="
            + videoRate.ToString("F3") + "x; frames=" + (_frames - initialFrames) + "; maximum gap="
            + maximumGap.ToString("F3") + " s; maximum wall error=" + maximumError.ToString("F3") + " s");
        Check(_player.IsPlaying && _player.PlaybackRate == 2 && !wentBackwards && _frames > initialFrames,
            phase + ": 2x playback and presentation continue without timestamp regression");
        Check(Math.Abs(clockRate - 2) <= 0.2 && Math.Abs(videoRate - 2) <= 0.3,
            phase + ": clock and presented PTS sustain 2x instead of freezing after a single frame");
        Check(maximumGap < 0.1 && maximumError <= 0.15,
            phase + ": no 100ms presentation freeze or 150ms wall-clock error after resuming");
        CheckDecoderIdentity();
    }

    /// <summary>Measures continuous double-speed presentation, including frame skipping when the source exceeds the render cadence.</summary>
    /// <returns>The coroutine that measures actual video progress and restores paused playback and rendering settings.</returns>
    /// <exception cref="Exception">The fixture is too short, the requested duration is invalid, or presentation stalls or regresses.</exception>
    /// <exception cref="MissingFieldException">The displayed frame timestamp is unavailable.</exception>
    /// <exception cref="TimeoutException">The initial seek does not complete within the smoke timeout.</exception>
    /// <exception cref="IOException">The timing evidence cannot be written.</exception>
    /// <exception cref="TargetInvocationException">The decoder diagnostics fail while timing results are read.</exception>
    private IEnumerator TestDoubleRatePlayback()
    {
        var displayedEnd = typeof(FFmpegVideoPlayer).GetField("_lastFrameEnd", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(FFmpegVideoPlayer).FullName, "_lastFrameEnd");
        var durationArgument = Argument("-videoDoubleRateSeconds");
        var requestedSeconds = 3d;
        if (!string.IsNullOrEmpty(durationArgument))
        {
            Check(double.TryParse(durationArgument, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out requestedSeconds), "double-speed duration is a valid number");
        }
        Check(!double.IsNaN(requestedSeconds) && !double.IsInfinity(requestedSeconds) && requestedSeconds >= 1 && requestedSeconds <= 60,
            "double-speed duration is between one and 60 wall-clock seconds");
        var availableSeconds = ((_player.LengthSeconds - 0.25) / 2) - 0.25;
        // Preserve the short smoke for small fixtures. Extended regressions must
        // measure the full requested interval, never silently shorten at EOF.
        var measuredSeconds = requestedSeconds <= 3 ? Math.Min(requestedSeconds, availableSeconds) : requestedSeconds;
        Check(measuredSeconds >= 0.7 && availableSeconds >= measuredSeconds,
            "fixture leaves enough media for the complete double-speed measurement: " + measuredSeconds + " wall-clock seconds");
        var previousVSync = QualitySettings.vSyncCount;
        var previousFrameRate = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        try
        {
            _player.Pause();
            _player.PlaybackRate = 2;
            var seek = _player.SeekAsync(0);
            var start = UnityEngine.Time.realtimeSinceStartup;
            while (!seek.IsCompleted)
            {
                CheckTimeout(start);
                yield return null;
            }

            seek.GetAwaiter().GetResult();
            _player.Play();
            yield return new WaitForSecondsRealtime(0.25f);
            if (Argument("-videoHardware") == "true" && Argument("-videoAv1Software") != "true" && requestedSeconds > 3)
            {
                Check(_player.DecoderType == VideoDecoderType.Hardware,
                    "extended 2x Vulkan regression uses an actual hardware decoder: " + _player.DecoderName
                        + "; device=" + _player.DecoderDevice + "; fallback=" + _player.HardwareFallbackReason);
            }

            var initialTime = _player.TimeSeconds;
            var initialVideo = (double)displayedEnd.GetValue(_player)!;
            var initialFrames = _frames;
            var initialUpdates = UnityEngine.Time.frameCount;
            var observedFrames = _frames;
            var lastTime = initialTime;
            var lastVideo = initialVideo;
            var lastPresentation = 0d;
            var maximumGap = 0d;
            var maximumLag = 0d;
            var maximumLead = 0d;
            var maximumBufferedFrames = 0;
            var clockWentBackwards = false;
            var videoWentBackwards = false;
            var bufferingUpdates = 0;
            var remainedHardware = _player.DecoderType == VideoDecoderType.Hardware;
            var wallClock = System.Diagnostics.Stopwatch.StartNew();
            while (wallClock.Elapsed.TotalSeconds < measuredSeconds)
            {
                yield return null;
                var elapsed = wallClock.Elapsed.TotalSeconds;
                var time = _player.TimeSeconds;
                var video = (double)displayedEnd.GetValue(_player)!;
                clockWentBackwards |= time < lastTime - 0.000001;
                videoWentBackwards |= video < lastVideo - 0.000001;
                lastTime = time;
                lastVideo = video;
                maximumGap = Math.Max(maximumGap, elapsed - lastPresentation);
                if (_frames != observedFrames)
                {
                    lastPresentation = elapsed;
                    observedFrames = _frames;
                }

                var wallError = (time - video) / 2;
                maximumLag = Math.Max(maximumLag, wallError);
                maximumLead = Math.Max(maximumLead, -wallError);
                maximumBufferedFrames = Math.Max(maximumBufferedFrames, _player.BufferedFrames);
                remainedHardware &= _player.DecoderType == VideoDecoderType.Hardware;
                if (_player.IsBuffering)
                {
                    bufferingUpdates++;
                }
            }

            var finalTime = _player.TimeSeconds;
            var finalVideo = (double)displayedEnd.GetValue(_player)!;
            _player.Pause();
            wallClock.Stop();
            var seconds = wallClock.Elapsed.TotalSeconds;
            var clockRate = (finalTime - initialTime) / seconds;
            var videoRate = (finalVideo - initialVideo) / seconds;
            var presentedFrames = _frames - initialFrames;
            var presentationRate = presentedFrames / seconds;
            var updateRate = (UnityEngine.Time.frameCount - initialUpdates) / seconds;
            var sourceFrames = (finalVideo - initialVideo) * _player.FrameRate;
            var estimatedSkippedFrames = Math.Max(0, sourceFrames - presentedFrames);
            var discardedFrames = ReadSessionCounter("DiscardedFrames");
            var catchUpSeeks = ReadSessionCounter("CatchUpSeeks");
            Debug.Log("Double-speed playback: wall seconds=" + seconds.ToString("F3")
                + "; clock=" + clockRate.ToString("F3") + "x; video=" + videoRate.ToString("F3")
                + "x; presentations=" + presentationRate.ToString("F1") + " FPS; Unity updates=" + updateRate.ToString("F1")
                + " FPS; maximum gap=" + maximumGap.ToString("F3") + " s; wall lag/lead=" + maximumLag.ToString("F3")
                + "/" + maximumLead.ToString("F3") + " s; estimated skipped source frames=" + estimatedSkippedFrames.ToString("F1")
                + "; discarded before conversion=" + discardedFrames + "; catch-up seeks=" + catchUpSeeks
                + "; buffering updates=" + bufferingUpdates + "; decoder=" + _player.DecoderName + "/" + _player.DecoderDevice
                + "; transport=" + _player.TransferMode + "; source=" + _player.Width + "x" + _player.Height + "@" + _player.FrameRate
                + "; graphics=" + SystemInfo.graphicsDeviceType + "; GPU=" + SystemInfo.graphicsDeviceName
                + "; driver=" + SystemInfo.graphicsDeviceVersion + "; fallback=" + _player.HardwareFallbackReason);
            if (!string.IsNullOrEmpty(_report))
            {
                var header = "wall_seconds\tclock_rate\tvideo_rate\tpresented_frames\tpresentation_fps\tupdate_fps\tpresentation_max_gap_ms\twall_lag_max_ms\twall_lead_max_ms\testimated_skipped_source_frames\tdiscarded_before_conversion\tcatch_up_seeks\tbuffering_updates\tmaximum_buffered_frames\tdecoder\tdevice\ttransport\tgraphics_api\tgpu\tdriver\tfallback";
                var row = FormattableString.Invariant($"{seconds}\t{clockRate}\t{videoRate}\t{presentedFrames}\t{presentationRate}\t{updateRate}\t{maximumGap * 1000}\t{maximumLag * 1000}\t{maximumLead * 1000}\t{estimatedSkippedFrames}\t{discardedFrames}\t{catchUpSeeks}\t{bufferingUpdates}\t{maximumBufferedFrames}\t{_player.DecoderName}\t{_player.DecoderDevice}\t{_player.TransferMode}\t{SystemInfo.graphicsDeviceType}\t{SystemInfo.graphicsDeviceName}\t{SystemInfo.graphicsDeviceVersion}\t{_player.HardwareFallbackReason}");
                File.WriteAllText(_report + ".double-rate.tsv", header + Environment.NewLine + row + Environment.NewLine);
            }

            if (requestedSeconds > 3 && Argument("-videoHardware") == "true" && Argument("-videoAv1Software") != "true")
            {
                Check(remainedHardware, "extended 2x playback never substitutes software decoding for the requested hardware regression");
            }

            CheckDecoderIdentity();
            Check(seconds >= measuredSeconds, "double-speed playback measures the entire requested wall-clock interval");
            Check(!clockWentBackwards && Math.Abs(clockRate - 2) <= 0.2, "double-speed clock advances continuously at 2x within 10%: " + clockRate);
            Check(!videoWentBackwards && presentedFrames > 0 && Math.Abs(videoRate - 2) <= 0.2,
                "double-speed presented PTS advance continuously at 2x within 10%: " + videoRate);
            Check(maximumGap < 0.1, "no 100ms presentation freeze during the continuous 2x interval: " + maximumGap);
            Check(maximumLag <= 0.15 && maximumLead <= 0.15, "displayed 2x frames remain within 150ms of wall-clock time");
            Check(maximumBufferedFrames <= Math.Max(1, Math.Min(8, _player.BufferedFrameLimit)), "double-speed decoder queue remains bounded");
            if (_player.FrameRate * 2 > 60 * 1.2)
            {
                Check(estimatedSkippedFrames > 0, "high-frame-rate 2x playback skips source frames instead of delaying the video timeline");
            }
        }
        finally
        {
            _player.Pause();
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousFrameRate;
        }
    }
    /// <summary>Measures actual video progress and presentation cadence at 1x, 2x, and 3x, not just Unity update FPS.</summary>
    /// <returns>The coroutine that tests each speed and restores the paused playback settings.</returns>
    /// <exception cref="Exception">The fixture is too short or hardware playback cannot sustain the requested speed.</exception>
    /// <exception cref="MissingFieldException">The displayed frame timestamp is unavailable.</exception>
    private IEnumerator TestPlaybackThroughput()
    {
        Check(_player.LengthSeconds >= 8, "throughput fixture contains at least eight seconds of video");
        var previousVSync = QualitySettings.vSyncCount;
        var previousFrameRate = Application.targetFrameRate;
        var previousRate = _player.PlaybackRate;
        var rates = new[] { 1f, 2f, 3f };
        var clockRates = new double[rates.Length];
        var videoRates = new double[rates.Length];
        var presentationRates = new double[rates.Length];
        var maximumGaps = new double[rates.Length];
        var displayedEnd = typeof(FFmpegVideoPlayer).GetField("_lastFrameEnd", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(FFmpegVideoPlayer).FullName, "_lastFrameEnd");
        var latestPresentation = 0d;
        // FrameReady carries a sequence number, not a presentation timestamp.
        Action<FFmpegVideoPlayer, long> onFrame = (player, _) =>
        {
            latestPresentation = (double)displayedEnd.GetValue(player)!;
        };
        _player.FrameReady += onFrame;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        try
        {
            for (var index = 0; index < rates.Length; index++)
            {
                _player.Pause();
                _player.PlaybackRate = rates[index];
                var seek = _player.SeekAsync(0);
                var start = UnityEngine.Time.realtimeSinceStartup;
                while (!seek.IsCompleted)
                {
                    CheckTimeout(start);
                    yield return null;
                }

                seek.GetAwaiter().GetResult();
                _player.Play();
                yield return new WaitForSecondsRealtime(0.25f);
                var initialTime = _player.TimeSeconds;
                var initialPresentation = latestPresentation;
                var initialFrames = _frames;
                var observedFrames = _frames;
                var initialUpdates = UnityEngine.Time.frameCount;
                var bufferingUpdates = 0;
                var maximumLag = 0d;
                var lastPresentation = 0d;
                var maximumGap = 0d;
                var wallClock = System.Diagnostics.Stopwatch.StartNew();
                while (wallClock.Elapsed.TotalSeconds < 2)
                {
                    yield return null;
                    var elapsed = wallClock.Elapsed.TotalSeconds;
                    maximumGap = Math.Max(maximumGap, elapsed - lastPresentation);
                    if (_frames != observedFrames)
                    {
                        lastPresentation = elapsed;
                        observedFrames = _frames;
                    }

                    if (_player.IsBuffering)
                    {
                        bufferingUpdates++;
                    }

                    maximumLag = Math.Max(maximumLag, (_player.TimeSeconds - latestPresentation) / rates[index]);
                }

                _player.Pause();
                wallClock.Stop();
                var seconds = wallClock.Elapsed.TotalSeconds;
                clockRates[index] = (_player.TimeSeconds - initialTime) / seconds;
                videoRates[index] = (latestPresentation - initialPresentation) / seconds;
                presentationRates[index] = (_frames - initialFrames) / seconds;
                maximumGaps[index] = maximumGap;
                Debug.Log("Playback throughput: requested=" + rates[index] + "x; clock=" + clockRates[index].ToString("F3")
                    + "x; video=" + videoRates[index].ToString("F3") + "x; presentations=" + presentationRates[index].ToString("F1")
                    + " FPS; Unity updates=" + ((UnityEngine.Time.frameCount - initialUpdates) / seconds).ToString("F1")
                    + " FPS; maximum presentation gap=" + maximumGap.ToString("F3") + " s; maximum wall-clock lag="
                    + maximumLag.ToString("F3") + " s; buffering updates=" + bufferingUpdates + "; decoder=" + _player.DecoderDevice
                    + "; transport=" + _player.TransferMode + "; video=" + _player.Width + "x" + _player.Height
                    + "@" + _player.FrameRate + "; GPU=" + SystemInfo.graphicsDeviceName);
            }

            for (var index = 0; index < rates.Length; index++)
            {
                Check(Math.Abs(clockRates[index] - rates[index]) <= rates[index] * 0.02,
                    "playback clock sustains " + rates[index] + "x within 2%: " + clockRates[index]);
                Check(Math.Abs(videoRates[index] - rates[index]) <= rates[index] * 0.1,
                    "displayed video sustains " + rates[index] + "x within 10%: " + videoRates[index]);
                Check(presentationRates[index] >= Math.Min(60, _player.FrameRate * rates[index]) * 0.8,
                    "presentation cadence sustains at least 80% of the expected FPS at " + rates[index] + "x: " + presentationRates[index]);
                Check(maximumGaps[index] < 0.1, "no 100ms presentation freeze at " + rates[index] + "x: " + maximumGaps[index]);
            }
        }
        finally
        {
            _player.Pause();
            _player.PlaybackRate = previousRate;
            _player.FrameReady -= onFrame;
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousFrameRate;
        }
    }
    /// <summary>Compares frame timing and timeline progress at 1x, 2x, 3x, and 16x against paired paused baselines.</summary>
    /// <returns>The coroutine that measures each rate and restores paused playback and rendering settings.</returns>
    /// <exception cref="Exception">The fixture is too short, playback stalls, or frame timing exceeds the baseline tolerances.</exception>
    /// <exception cref="MissingFieldException">The displayed frame timestamp is unavailable.</exception>
    /// <exception cref="IOException">The timing evidence cannot be written.</exception>
    /// <exception cref="TargetInvocationException">The decoder diagnostics fail while timing results are read.</exception>
    private IEnumerator TestDecodeOverload()
    {
        Check(_player.LengthSeconds >= 20, "decode overload fixture contains at least 20 seconds of video");
        var previousVSync = QualitySettings.vSyncCount;
        var previousFrameRate = Application.targetFrameRate;
        var previousRate = _player.PlaybackRate;
        var displayedEnd = typeof(FFmpegVideoPlayer).GetField("_lastFrameEnd", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(FFmpegVideoPlayer).FullName, "_lastFrameEnd");
        var targetFrameRate = int.TryParse(Argument("-videoTargetFps"), out var requestedFrameRate) ? requestedFrameRate : 60;
        Check(targetFrameRate >= 30 && targetFrameRate <= 1000, "decode overload target FPS is between 30 and 1000");
        var timings = new FrameTiming[1];
        var evidence = new System.Text.StringBuilder();
        var failures = new System.Collections.Generic.List<string>();
        evidence.AppendLine("phase\trate\ttarget_fps\tupdates_fps\tfps_vs_paused_percent\tupdate_p95_ms\tupdate_p99_ms\tupdate_max_ms\tmain_p99_ms\trender_p99_ms\trender_max_ms\tgpu_p99_ms\tmain_timing_samples\trender_timing_samples\tgpu_timing_samples\tclock_rate\tvideo_rate\tpresentations_fps\tpresentation_max_gap_ms\twall_lag_max_ms\twall_lead_max_ms\tbuffering_updates\tcatch_up_seeks\tdiscarded_frames");
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;
        try
        {
            foreach (var rate in new[] { 1f, 2f, 3f, 16f })
            {
                _player.Pause();
                _player.PlaybackRate = rate;
                var seekSeconds = Math.Min(5, _player.LengthSeconds * 0.1);
                var measuredSeconds = Math.Min(3, ((_player.LengthSeconds - seekSeconds - 2) / rate) - 0.4);
                Check(measuredSeconds >= 0.5, "fixture leaves at least half a second for the " + rate + "x measurement before EOF");
                var seek = _player.SeekAsync(seekSeconds);
                var start = UnityEngine.Time.realtimeSinceStartup;
                while (!seek.IsCompleted)
                {
                    CheckTimeout(start);
                    yield return null;
                }

                seek.GetAwaiter().GetResult();
                var baselineFps = 0d;
                var baselineP95 = 0d;
                var baselineP99 = 0d;
                var baselineMaximum = 0d;
                var baselineRenderP99 = 0d;
                for (var phase = 0; phase < 2; phase++)
                {
                    if (phase == 1)
                    {
                        _player.Play();
                    }

                    yield return new WaitForSecondsRealtime(0.4f);
                    var updates = new FrameTimeDistribution();
                    var mainThread = new FrameTimeDistribution();
                    var renderThread = new FrameTimeDistribution();
                    var gpu = new FrameTimeDistribution();
                    var initialFrames = _frames;
                    var observedFrames = _frames;
                    var initialUpdates = UnityEngine.Time.frameCount;
                    var initialTime = _player.TimeSeconds;
                    var initialVideo = (double)displayedEnd.GetValue(_player)!;
                    var lastTime = initialTime;
                    var lastUpdate = 0d;
                    var lastPresentation = 0d;
                    var maximumPresentationGap = 0d;
                    var maximumLag = 0d;
                    var maximumLead = 0d;
                    var lagPeakElapsed = 0d;
                    var lagPeakClock = 0d;
                    var lagPeakFrameEnd = 0d;
                    var lagPeakBufferedFrames = 0;
                    var bufferingUpdates = 0;
                    var clockWentBackwards = false;
                    var lastFrameStart = 0UL;
                    var wallClock = System.Diagnostics.Stopwatch.StartNew();
                    while (wallClock.Elapsed.TotalSeconds < measuredSeconds)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        var elapsed = wallClock.Elapsed.TotalSeconds;
                        updates.Record((elapsed - lastUpdate) * 1000);
                        lastUpdate = elapsed;
                        if (FrameTimingManager.GetLatestTimings(1, timings) != 0 && timings[0].frameStartTimestamp != lastFrameStart)
                        {
                            lastFrameStart = timings[0].frameStartTimestamp;
                            mainThread.Record(timings[0].cpuMainThreadFrameTime);
                            renderThread.Record(timings[0].cpuRenderThreadFrameTime);
                            gpu.Record(timings[0].gpuFrameTime);
                        }

                        if (phase == 1)
                        {
                            var time = _player.TimeSeconds;
                            clockWentBackwards |= time < lastTime - 0.000001;
                            lastTime = time;
                            maximumPresentationGap = Math.Max(maximumPresentationGap, elapsed - lastPresentation);
                            if (_frames != observedFrames)
                            {
                                lastPresentation = elapsed;
                                observedFrames = _frames;
                            }

                            var frameEnd = (double)displayedEnd.GetValue(_player)!;
                            var presentationError = (time - frameEnd) / rate;
                            if (presentationError > maximumLag)
                            {
                                maximumLag = presentationError;
                                lagPeakElapsed = elapsed;
                                lagPeakClock = time;
                                lagPeakFrameEnd = frameEnd;
                                lagPeakBufferedFrames = _player.BufferedFrames;
                            }

                            maximumLead = Math.Max(maximumLead, -presentationError);
                            if (_player.IsBuffering)
                            {
                                bufferingUpdates++;
                            }
                        }
                    }

                    var finalTime = _player.TimeSeconds;
                    var finalVideo = (double)displayedEnd.GetValue(_player)!;
                    wallClock.Stop();
                    _player.Pause();
                    var seconds = wallClock.Elapsed.TotalSeconds;
                    var updateRate = (UnityEngine.Time.frameCount - initialUpdates) / seconds;
                    var clockRate = (finalTime - initialTime) / seconds;
                    var videoRate = (finalVideo - initialVideo) / seconds;
                    var presentationRate = (_frames - initialFrames) / seconds;
                    var phaseName = phase == 0 ? "paused" : "playing";
                    var fpsChange = phase == 0 ? 0 : ((updateRate / baselineFps) - 1) * 100;
                    var catchUpSeeks = ReadSessionCounter("CatchUpSeeks");
                    var discardedFrames = ReadSessionCounter("DiscardedFrames");
                    Debug.Log("Decode overload: phase=" + phaseName + "; playback=" + rate + "x; Unity updates=" + updateRate.ToString("F1")
                        + " FPS; change from paused=" + fpsChange.ToString("F3") + "%; target=" + targetFrameRate + " FPS; update=" + updates.Describe() + "; main=" + mainThread.Describe() + "; render=" + renderThread.Describe()
                        + "; GPU timing=" + gpu.Describe() + "; frame timing enabled=" + FrameTimingManager.IsFeatureEnabled()
                        + "; clock=" + clockRate.ToString("F3") + "x; video=" + videoRate.ToString("F3")
                        + "x; presentations=" + presentationRate.ToString("F1") + " FPS; maximum presentation gap="
                        + (maximumPresentationGap * 1000).ToString("F1") + " ms; maximum wall-clock lag=" + (maximumLag * 1000).ToString("F1")
                        + " ms; maximum wall-clock lead=" + (maximumLead * 1000).ToString("F1") + " ms; buffering updates=" + bufferingUpdates
                        + "; catch-up seeks=" + catchUpSeeks + "; discarded frames=" + discardedFrames + "; decoder=" + _player.DecoderName + "/" + _player.DecoderDevice
                        + "; transport=" + _player.TransferMode + "; video=" + _player.Width + "x" + _player.Height
                        + "@" + _player.FrameRate + "; GPU=" + SystemInfo.graphicsDeviceName);
                    if (maximumLag > 0)
                    {
                        Debug.Log("Presentation lag peak: rate=" + rate + "x; measured wall time=" + lagPeakElapsed.ToString("F6")
                            + " s; clock=" + lagPeakClock.ToString("F6") + " s; displayed frame end=" + lagPeakFrameEnd.ToString("F6")
                            + " s; wall lag=" + maximumLag.ToString("F6") + " s; buffered frames=" + lagPeakBufferedFrames);
                    }
                    evidence.AppendLine(string.Join("\t", phaseName, rate, targetFrameRate, updateRate, fpsChange, updates.Percentile(0.95), updates.Percentile(0.99),
                        updates.Percentile(1), mainThread.Percentile(0.99), renderThread.Percentile(0.99), renderThread.Percentile(1),
                        gpu.Percentile(0.99), mainThread.Count, renderThread.Count, gpu.Count, clockRate, videoRate, presentationRate,
                        maximumPresentationGap * 1000, maximumLag * 1000, maximumLead * 1000, bufferingUpdates, catchUpSeeks, discardedFrames));
                    if (!string.IsNullOrEmpty(_report))
                    {
                        File.WriteAllText(_report + ".timing.tsv", evidence.ToString());
                    }

                    if (phase == 0)
                    {
                        baselineFps = updateRate;
                        baselineP95 = updates.Percentile(0.95);
                        baselineP99 = updates.Percentile(0.99);
                        baselineMaximum = updates.Percentile(1);
                        baselineRenderP99 = renderThread.Percentile(0.99);
                        Check(baselineFps >= targetFrameRate * 0.9, "paused render baseline reaches at least 90% of the requested " + targetFrameRate + " FPS: " + baselineFps);
                        continue;
                    }

                    CheckPlaybackTiming(failures, updateRate >= baselineFps * 0.95, rate + "x retains at least 95% of paired paused Unity FPS: " + updateRate + " / " + baselineFps);
                    CheckPlaybackTiming(failures, updates.Percentile(0.95) <= baselineP95 + 2, rate + "x update p95 stays within 2ms of the paused baseline: " + updates.Percentile(0.95));
                    CheckPlaybackTiming(failures, updates.Percentile(0.99) <= baselineP99 + 4, rate + "x update p99 stays within 4ms of the paused baseline: " + updates.Percentile(0.99));
                    CheckPlaybackTiming(failures, updates.Percentile(1) <= Math.Max(50, baselineMaximum + 5), rate + "x introduces no update spike above the baseline or 50ms: " + updates.Percentile(1));
                    if (renderThread.Count > 0)
                    {
                        CheckPlaybackTiming(failures, renderThread.Percentile(0.99) <= Math.Max(500d / targetFrameRate, baselineRenderP99 + 4),
                            rate + "x render thread p99 stays within the half-frame budget or paused baseline plus 4ms: " + renderThread.Percentile(0.99));
                    }

                    CheckPlaybackTiming(failures, !clockWentBackwards && Math.Abs(clockRate - rate) <= rate * 0.02, rate + "x clock remains continuous and advances within 2%: " + clockRate);
                    CheckPlaybackTiming(failures, _frames > initialFrames && Math.Abs(videoRate - rate) <= rate * 0.1, rate + "x displayed timeline advances within 10%: " + videoRate);
                    CheckPlaybackTiming(failures, maximumLag <= 0.15, rate + "x displayed timeline remains within 150ms of wall-clock time: " + maximumLag);
                    CheckPlaybackTiming(failures, maximumLead <= 0.15, rate + "x displayed timeline leads wall-clock time by at most 150ms: " + maximumLead);
                    CheckPlaybackTiming(failures, maximumPresentationGap < 0.1, rate + "x introduces no 100ms presentation freeze: " + maximumPresentationGap);
                }
            }

            Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            _player.Pause();
            _player.PlaybackRate = previousRate;
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousFrameRate;
        }
    }

    /// <summary>Reads a worker-published diagnostic counter after the timing measurement has ended.</summary>
    /// <param name="name">The name of the internal decode session's counter property.</param>
    /// <returns>The published counter value, or minus one when the session or diagnostic property is unavailable.</returns>
    /// <exception cref="TargetInvocationException">The counter getter fails while the diagnostic is read.</exception>
    private long ReadSessionCounter(string name)
    {
        var sessionField = typeof(FFmpegVideoPlayer).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
        var session = sessionField?.GetValue(_player);
        var value = session?.GetType().GetProperty(name)?.GetValue(session);
        return value is long count ? count : -1;
    }

    /// <summary>Records a timing assertion without preventing measurement of the remaining playback rates.</summary>
    /// <param name="failures">The collection that receives failed timing assertions.</param>
    /// <param name="value">Whether the observed timing satisfies the assertion.</param>
    /// <param name="message">The diagnostic evidence to report when the assertion fails.</param>
    private void CheckPlaybackTiming(System.Collections.Generic.List<string> failures, bool value, string message)
    {
        _checks++;
        if (!value)
        {
            failures.Add(message);
        }
    }

    /// <summary>Collects finite positive frame times without allocating inside the measurement loop.</summary>
    private sealed class FrameTimeDistribution
    {
        /// <summary>Stores one three-second phase at the maximum supported target of 1000 FPS.</summary>
        private readonly double[] _samples = new double[4096];

        /// <summary>Records whether the active samples have been ordered for percentile queries.</summary>
        private bool _sorted;

        /// <summary>Gets the number of available timing samples; zero means the timing source is unavailable.</summary>
        public int Count { get; private set; }

        /// <summary>Adds a finite positive frame time when capacity remains.</summary>
        /// <param name="milliseconds">The elapsed frame time, measured in milliseconds.</param>
        public void Record(double milliseconds)
        {
            if (milliseconds <= 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || Count == _samples.Length)
            {
                return;
            }

            _samples[Count++] = milliseconds;
            _sorted = false;
        }

        /// <summary>Returns a nearest-rank percentile from the collected samples.</summary>
        /// <param name="fraction">The percentile fraction between zero and one.</param>
        /// <returns>The selected frame time in milliseconds, or zero when no measurements are available.</returns>
        public double Percentile(double fraction)
        {
            if (Count == 0)
            {
                return 0;
            }

            if (!_sorted)
            {
                Array.Sort(_samples, 0, Count);
                _sorted = true;
            }

            var index = Math.Max(0, Math.Min(Count - 1, (int)Math.Ceiling(fraction * Count) - 1));
            return _samples[index];
        }

        /// <summary>Formats percentile timing evidence after the measurement phase has finished.</summary>
        /// <returns>The sample count and p95, p99, and maximum timing, or an explicit unavailable marker.</returns>
        public string Describe()
        {
            if (Count == 0)
            {
                return "unavailable";
            }

            return "p95/p99/max=" + Percentile(0.95).ToString("F3") + "/" + Percentile(0.99).ToString("F3")
                + "/" + Percentile(1).ToString("F3") + " ms (n=" + Count + ")";
        }
    }

    /// <summary>Checks decoder identity, CPU transport, and bounded recovery through available hardware backends.</summary>
    /// <param name="path">The seekable encoded video fixture.</param>
    /// <returns>The coroutine that exercises decoder preference and restores a closed player.</returns>
    /// <exception cref="Exception">Decoder identity, pixels, or hardware recovery do not match the requested behavior.</exception>
    /// <exception cref="TargetInvocationException">The recovery fault-injection entry point fails.</exception>
    IEnumerator TestDecoderPreference(string path)
    {
        _player.Loop = false;
        _player.PlaybackRate = 1;
        _player.RequireHardwareDecoding = false;
        foreach (var preference in new[] { VideoDecoderType.Software, VideoDecoderType.Hardware })
        {
            _player.PreferredDecoderType = preference;
            _player.PreferNativeTextures = false;
            int beforePreload = _frames, pauseCallbacks = 0;
            Action<FFmpegVideoPlayer, Texture> pauseFirstTexture = (player, texture) => {
                if (texture != null && pauseCallbacks++ == 0) player.Pause();
            };
            _player.TextureChanged += pauseFirstTexture;
            var prepare = _player.PreloadAsync(path);
            var start = UnityEngine.Time.realtimeSinceStartup;
            while (!prepare.IsCompleted) { CheckTimeout(start); yield return null; }
            _player.TextureChanged -= pauseFirstTexture;
            prepare.GetAwaiter().GetResult();
            Check(_player.IsPrepared && !_player.IsPlaying && _frames == beforePreload + 1 && pauseCallbacks == 1,
                "Pause from the first texture callback still completes exactly one prepared frame");
            Check(_player.DecoderType == preference, "actual decoder follows " + preference + " preference: " + _player.DecoderName);
            Check(!string.IsNullOrEmpty(_player.DecoderName) && !string.IsNullOrEmpty(_player.DecoderDevice), "actual decoder name and device are populated");
            Check(_player.TransferMode == (preference == VideoDecoderType.Hardware ? "Hardware decode + CPU RGBA upload" : "Software RGBA upload"),
                "decoder identity is independent of CPU texture transport: " + _player.TransferMode);
            var seek = _player.SeekAsync(Math.Min(1, _player.LengthSeconds / 2));
            start = UnityEngine.Time.realtimeSinceStartup;
            while (!seek.IsCompleted) { CheckTimeout(start); yield return null; }
            seek.GetAwaiter().GetResult();
            yield return null;
            CheckTextureContents();
            _player.PreferredDecoderType = preference == VideoDecoderType.Hardware ? VideoDecoderType.Software : VideoDecoderType.Hardware;
            Check(_player.DecoderType == preference, "preference changes do not mislabel the already-open decoder");
            _player.Close();
        }

        _player.PreferredDecoderType = VideoDecoderType.Hardware;
        _player.PreferNativeTextures = true;
        var nativePrepare = _player.PreloadAsync(path);
        var waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!nativePrepare.IsCompleted) { CheckTimeout(waitStart); yield return null; }
        nativePrepare.GetAwaiter().GetResult();
        Check(_player.DecoderType == VideoDecoderType.Hardware && _player.TransferMode != "Hardware decode + CPU RGBA upload" &&
            !_player.TransferMode.StartsWith("Software", StringComparison.Ordinal), "fallback scenario starts on a real native texture path");
        _player.Play();
        var recover = typeof(FFmpegVideoPlayer).GetMethod("RecoverHardwarePlayback", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(recover != null, "native presentation recovery entry point exists");
        for (var attempt = 0; attempt < 3 && _player.TransferMode != "Hardware decode + CPU RGBA upload"; attempt++)
        {
            var failedDevice = _player.DecoderDevice;
            var failedTransport = _player.TransferMode;
            recover!.Invoke(_player, new object[] { new NotSupportedException("Injected GPU texture failure for hardware CPU fallback, attempt " + (attempt + 1)) });
            waitStart = UnityEngine.Time.realtimeSinceStartup;
            while (!_player.IsPrepared || _player.TransferMode.StartsWith("Reopening", StringComparison.Ordinal))
            {
                CheckTimeout(waitStart);
                yield return null;
            }

            Check(_player.DecoderType == VideoDecoderType.Hardware, "GPU transport recovery retains hardware decoding on attempt " + (attempt + 1));
            Debug.Log("Hardware recovery: " + failedDevice + "/" + failedTransport + " -> " + _player.DecoderDevice + "/" + _player.TransferMode);
            if (_player.TransferMode != "Hardware decode + CPU RGBA upload")
            {
                Check(_player.DecoderDevice != failedDevice, "GPU failure tries a different hardware API before CPU transport");
            }
        }
        Check(_player.IsPrepared && _player.TransferMode == "Hardware decode + CPU RGBA upload",
            "GPU presentation failures reach hardware CPU transport within three attempts");
        Check(_player.DecoderType == VideoDecoderType.Hardware, "native presentation failure retains hardware decoding through CPU transport");
        Check(_player.IsPlaying && !string.IsNullOrEmpty(_player.HardwareFallbackReason), "recovery retains play intent and reports the failed native path");
        var recoveredSeek = _player.SeekAsync(Math.Min(1, _player.LengthSeconds / 2));
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!recoveredSeek.IsCompleted) { CheckTimeout(waitStart); yield return null; }
        recoveredSeek.GetAwaiter().GetResult();
        yield return null;
        CheckTextureContents();
        Check(_player.IsPlaying && _player.DecoderType == VideoDecoderType.Hardware, "hardware CPU fallback remains seekable and playing");
        _player.Close();
        // Reopening restores the preferred native path; keep the final report's
        // transport representative of normal hardware playback on device runners.
        var restored = _player.PreloadAsync(path);
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!restored.IsCompleted) { CheckTimeout(waitStart); yield return null; }
        restored.GetAwaiter().GetResult();
        Check(_player.DecoderType == VideoDecoderType.Hardware && _player.TransferMode != "Hardware decode + CPU RGBA upload" &&
            !_player.TransferMode.StartsWith("Software", StringComparison.Ordinal), "a new open restores preferred native texture transport");
        _player.Close();
    }
    IEnumerator TestSustainedPlayback(string path)
    {
        _player.PlaybackRate = 1;
        _player.Loop = true;
        var prepare = _player.PreloadAsync(path);
        var start = UnityEngine.Time.realtimeSinceStartup;
        while (!prepare.IsCompleted) { CheckTimeout(start); yield return null; }
        prepare.GetAwaiter().GetResult();
        Check(_player.IsPrepared, "sustained playback prepared");
        int before = _frames;
        _player.Play();
        start = UnityEngine.Time.realtimeSinceStartup;
        while (_frames - before < 300) { CheckTimeout(start); yield return null; }
        Check(_player.IsPlaying && _player.TimeSeconds >= 8, "300 presented frames advance the playback clock");
        Check(_player.BufferedFrames <= 8, "sustained decoder queue stays bounded");
        for (int index = 0; index < 10; index++)
        {
            double target = Math.Min(0.25 + (index % 4) * 0.5, _player.LengthSeconds / 2);
            var seek = _player.SeekAsync(target);
            start = UnityEngine.Time.realtimeSinceStartup;
            while (!seek.IsCompleted) { CheckTimeout(start); yield return null; }
            seek.GetAwaiter().GetResult();
            Check(_player.IsPlaying && Math.Abs(_player.TimeSeconds - target) < 0.25,
                "repeated hardware seek preserves playback and rejects stale frames");
        }
        CheckHardwarePath();
        _player.Close();
        Check(_player.Texture == null && !_player.IsPrepared, "sustained playback releases its final image");
    }
    IEnumerator TestRecovery(string path)
    {
        var recover = typeof(FFmpegVideoPlayer).GetMethod("RecoverInSoftware", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(recover != null, "recovery fault injection entry point exists");
        _player.Loop = false;
        _player.PlaybackRate = 1;
        foreach (var control in new[] { "Pause", "SeekAsync", "Close" })
        {
            var prepare = _player.PreloadAsync(path);
            var start = UnityEngine.Time.realtimeSinceStartup;
            while (!prepare.IsCompleted) { CheckTimeout(start); yield return null; }
            prepare.GetAwaiter().GetResult();
            _player.Play();
            int before = _frames;
            start = UnityEngine.Time.realtimeSinceStartup;
            while (_frames == before) { CheckTimeout(start); yield return null; }
            CheckHardwarePath();
            Check(!_player.TransferMode.StartsWith("Software", StringComparison.Ordinal), control + ": fault starts from real hardware playback");
            Check(_player.Texture != null && _player.IsPlaying, control + ": hardware frame is visible before fault");
            int callbacks = 0;
            Task? seek = null;
            double seekTarget = Math.Min(0.75, _player.LengthSeconds / 2);
            Action<FFmpegVideoPlayer, Texture>? callback = null;
            callback = (player, texture) =>
            {
                if (texture != null) return;
                Check(player.CurrentBitRate == 0, control + ": recovery clears bitrate before the texture callback");
                player.TextureChanged -= callback;
                callbacks++;
                if (control == "Pause") player.Pause();
                else if (control == "SeekAsync") seek = player.SeekAsync(seekTarget);
                else player.Close();
            };
            _player.TextureChanged += callback;
            try
            {
                // Reproduce the managed hand-off from a render-thread failure without
                // corrupting the GPU resource or making a driver-dependent failure.
                recover!.Invoke(_player, new object[] { new NotSupportedException("Injected asynchronous GPU presentation failure: " + control) });
            }
            finally { _player.TextureChanged -= callback; }
            Check(callbacks == 1, control + ": null texture callback fired exactly once");
            if (control == "Close")
            {
                Check(_player.State == VideoPlaybackState.Idle && !_player.IsPrepared && _player.Texture == null,
                    "Close callback keeps player closed after recovery returns");
                before = _frames;
                yield return new WaitForSecondsRealtime(0.15f);
                Check(_frames == before && _player.BufferedFrames == 0 && _player.Texture == null,
                    "Close callback prevents stale replacement frames");
                prepare = _player.PreloadAsync(path);
                start = UnityEngine.Time.realtimeSinceStartup;
                while (!prepare.IsCompleted) { CheckTimeout(start); yield return null; }
                prepare.GetAwaiter().GetResult();
                Check(_player.IsPrepared && _player.Texture != null, "Close callback permits a fresh preload");
                CheckHardwarePath();
            }
            else
            {
                start = UnityEngine.Time.realtimeSinceStartup;
                while (_player.State == VideoPlaybackState.Seeking || (seek != null && !seek.IsCompleted))
                { CheckTimeout(start); yield return null; }
                if (seek != null) seek.GetAwaiter().GetResult();
                Check(_player.IsPrepared && _player.Texture != null && _player.TransferMode.StartsWith("Software", StringComparison.Ordinal),
                    control + ": replacement software session presents a frame");
                if (control == "Pause")
                {
                    Check(_player.State == VideoPlaybackState.Paused, "Pause callback survives the replacement seek");
                    long paused = _player.Time;
                    yield return new WaitForSecondsRealtime(0.12f);
                    Check(Math.Abs(_player.Time - paused) < 2, "Pause callback freezes recovered clock");
                    _player.Play();
                }
                else
                    Check(seek != null && seek.Status == TaskStatus.RanToCompletion && _player.IsPlaying && Math.Abs(_player.TimeSeconds - seekTarget) < 0.2,
                        "SeekAsync callback completes at requested position and retains play intent");
                before = _frames;
                start = UnityEngine.Time.realtimeSinceStartup;
                while (_frames == before) { CheckTimeout(start); yield return null; }
                Check(_player.IsPlaying, control + ": recovered decoder continues producing frames");
            }
            _player.Close();
        }
        var fail = typeof(FFmpegVideoPlayer).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(fail != null, "terminal failure fault injection entry point exists");
        var initial = _player.PreloadAsync(path);
        var waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!initial.IsCompleted) { CheckTimeout(waitStart); yield return null; }
        initial.GetAwaiter().GetResult();
        _player.Play();
        int initialFrames = _frames;
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (_frames == initialFrames) { CheckTimeout(waitStart); yield return null; }
        CheckHardwarePath();
        var failedSeek = _player.SeekAsync(0.2);
        Task? restarted = null;
        int restarts = 0;
        Action<FFmpegVideoPlayer, Texture>? restart = null;
        restart = (player, texture) =>
        {
            if (texture != null) return;
            player.TextureChanged -= restart;
            restarts++;
            player.Play(path);
            restarted = player.PrepareAsync();
        };
        _player.TextureChanged += restart;
        try { fail!.Invoke(_player, new object[] { new InvalidOperationException("Expected recovery test terminal failure") }); }
        finally { _player.TextureChanged -= restart; }
        Check(restarts == 1 && restarted != null, "terminal failure callback starts replacement media once");
        Check(failedSeek.IsFaulted && failedSeek.Exception != null, "terminal failure completes old seek with its error");
        Check(_player.State != VideoPlaybackState.Error, "terminal failure does not overwrite replacement state");
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!restarted!.IsCompleted) { CheckTimeout(waitStart); yield return null; }
        restarted.GetAwaiter().GetResult();
        Check(_player.IsPrepared && _player.IsPlaying && _player.Texture != null, "replacement preload completes after terminal failure callback");
        initialFrames = _frames;
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (_frames == initialFrames) { CheckTimeout(waitStart); yield return null; }
        CheckHardwarePath();
        Check(string.IsNullOrEmpty(_player.LastError), "replacement media has no stale error");
        _player.Close();
    }
    void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    void CheckHardwarePath()
    {
        if (Argument("-videoRequireHardware") == "true")
            Check(_player.DecoderType == VideoDecoderType.Hardware && !_player.TransferMode.StartsWith("Software", StringComparison.Ordinal) &&
                _player.TransferMode != "Hardware decode + CPU RGBA upload",
                "hardware presentation required; actual=" + _player.TransferMode + "; fallback=" + _player.HardwareFallbackReason);
    }
    void CheckDecoderIdentity()
    {
        if (Argument("-videoAv1Software") == "true")
            Check(_player.CodecName == "av1" && _player.DecoderName == "libdav1d" &&
                _player.DecoderType == VideoDecoderType.Software && _player.TransferMode == "Software RGBA upload",
                "AV1 fixture uses libdav1d software decoding and CPU texture upload");
        if (int.TryParse(Argument("-videoDoubleRateSeconds"), out var seconds) && seconds > 3
            && Argument("-videoHardware") == "true" && Argument("-videoAv1Software") != "true")
        {
            Check(_player.DecoderType == VideoDecoderType.Hardware,
                "extended 2x regression keeps an actual hardware decoder; device=" + _player.DecoderDevice
                    + "; fallback=" + _player.HardwareFallbackReason);
        }

        var native = Argument("-videoNativeDecoder");
        if (native == "d3d12" || native == "vulkan")
        {
            string expected = native == "d3d12" ? "D3D12VA" : "Vulkan Video";
            Check(_player.DecoderType == VideoDecoderType.Hardware && _player.DecoderDevice.Contains(expected),
                "required native decoder is active: " + expected + "; actual=" + _player.DecoderDevice);
            Check(_player.TransferMode.StartsWith(expected + " native decode", StringComparison.Ordinal),
                "native decoder frames reach Unity without CPU upload: " + _player.TransferMode);
        }
        if (Argument("-videoHardwareCpuUpload") == "true")
        {
            Check(_player.DecoderType == VideoDecoderType.Hardware, "CPU upload still uses an actual hardware decoder: " + _player.DecoderName);
            Check(_player.TransferMode == "Hardware decode + CPU RGBA upload", "explicit CPU texture transport is reported accurately");
            Check(!string.IsNullOrEmpty(_player.DecoderName) && !string.IsNullOrEmpty(_player.DecoderDevice), "hardware decoder diagnostics are available");
        }
        else if (Argument("-videoHardware") != "true")
            Check(_player.DecoderType == VideoDecoderType.Software, "software preference uses a software decoder");
    }
    bool ReleaseHardwareTargets()
    {
        if (!(_player.Texture is RenderTexture output)) return false;
        // The preceding pixel readback has completed earlier GPU submissions.
        // Exercise target recreation so cached native handles cannot stay stale.
        var field = typeof(FFmpegVideoPlayer).GetField("_hardwarePresenter", BindingFlags.Instance | BindingFlags.NonPublic);
        var presenter = field?.GetValue(_player);
        if (presenter == null) return false;
        var copyField = presenter.GetType().GetField("_copyTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        var copy = copyField?.GetValue(presenter) as RenderTexture;
        if (copy != null)
        {
            copy.Release();
            Check(!copy.IsCreated(), "hardware copy target released before next frame");
        }
        output.Release();
        Check(!output.IsCreated(), "hardware output released before next frame");
        return true;
    }
    void CheckTextureContents()
    {
        var target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(_player.Texture, target);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            pixels.Apply();
            var colors = pixels.GetPixels32();
            int low = 765, high = 0;
            foreach (var color in colors)
            {
                int brightness = color.r + color.g + color.b;
                low = Math.Min(low, brightness); high = Math.Max(high, brightness);
            }
            Check(high - low > 15, "decoded pixels are visible and non-uniform");
            if (!string.IsNullOrEmpty(_report)) File.WriteAllBytes(_report + ".png", pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(pixels); }
    }
    static void CheckTimeout(float start) { if (UnityEngine.Time.realtimeSinceStartup - start > 30) throw new TimeoutException("Unity video smoke timeout"); }
    /// <summary>Reads a smoke argument from the command line or Android activity extras.</summary>
    /// <param name="name">The argument name, including its leading hyphen.</param>
    /// <returns>The supplied value or Android smoke default, or null when no value is supplied.</returns>
    public static string? Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
#if UNITY_ANDROID && !UNITY_EDITOR
        // The test runner can select software validation and swap fixtures without
        // rebuilding the APK. Missing extras preserve the original hardware stress test.
        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
        using var intent = activity.Call<AndroidJavaObject>("getIntent");
        var extra = intent.Call<string>("getStringExtra", name.TrimStart('-'));
        if (extra != null) return extra;
        if (intent.Call<string>("getStringExtra", "videoAv1Software") == "true" &&
            (name == "-videoHardware" || name == "-videoRequireHardware" || name == "-videoStress" || name == "-videoTestDecoderPreference")) return "false";
        if (name == "-videoHardware" || name == "-videoRequireHardware" || name == "-videoStress" || name == "-videoTestDecoderPreference") return "true";
#endif
        return null;
    }
}
