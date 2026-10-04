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
    FFmpegVideoPlayer _player;
    int _checks, _frames;
    string _report;
    Exception _failure;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { new GameObject("FFmpeg Player validation").AddComponent<FFmpegPlayerSmoke>(); }
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
            object current = null;
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
        _player.FrameReady += (_, __) => _frames++;
        _player.ErrorReceived += (_, error) => _failure = new Exception(error);
        var externalMedia = Argument("-videoMedia");
        var path = string.IsNullOrEmpty(externalMedia) ? Path.Combine(Application.streamingAssetsPath, "test.mp4") : externalMedia;
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
        Check(_player.Texture != null && _player.Width > 0 && _player.Height > 0, "preload presents first texture");
        Check(_player.Length > 0 && _player.IsSeekable, "timeline metadata");
        var first = _player.Time;
        long firstBitRate = _player.CurrentBitRate;
        Check(firstBitRate > 0, "first displayed frame reports its video bitrate");
        yield return new WaitForSecondsRealtime(0.12f);
        Check(_player.Time == first, "preload clock frozen");
        Check(_player.CurrentBitRate == firstBitRate, "decode read-ahead does not change displayed bitrate while preloaded");
        Check(_player.SetRate(2), "rate accepted");
        Check(_player.CurrentBitRate == firstBitRate, "playback rate does not scale the media bitrate");
        Check(!_player.SetRate(-1), "reverse rate rejected");
        _player.Play();
        yield return new WaitForSecondsRealtime(0.35f);
        Check(_player.Time > first + 150 && _frames > 1, "play advances time and frames");
        _player.Pause();
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
        var older = _player.SeekAsync(0.2);
        var newer = _player.SeekAsync(0.4);
        start = UnityEngine.Time.realtimeSinceStartup;
        while (!newer.IsCompleted) { CheckTimeout(start); yield return null; }
        newer.GetAwaiter().GetResult();
        Check(older.IsCanceled && _player.IsPlaying, "new seek supersedes old seek while keeping play intent");
        int ended = 0;
        _player.EndReached += _ => ended++;
        _player.Loop = true;
        seek = _player.SeekAsync(Math.Max(0, _player.LengthSeconds - 0.15));
        start = UnityEngine.Time.realtimeSinceStartup;
        while (ended == 0) { CheckTimeout(start); yield return null; }
        while (!_player.IsPlaying) { CheckTimeout(start); yield return null; }
        Check(_player.Time < 1000, "loop returns to beginning");
        CheckHardwarePath();
        CheckDecoderIdentity();
        _player.Close();
        Check(!_player.IsPrepared && _player.Texture == null, "close releases texture and session");
        Check(_player.CurrentBitRate == 0, "close clears bitrate");
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
    IEnumerator TestDecoderPreference(string path)
    {
        _player.Loop = false;
        _player.Rate = 1;
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
        bool nativeDecoder = _player.DecoderDevice.Contains("D3D12VA") || _player.DecoderDevice.Contains("Vulkan Video");
        recover.Invoke(_player, new object[] { new NotSupportedException("Injected native texture import failure for hardware CPU fallback") });
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        if (nativeDecoder)
        {
            while (!_player.IsPrepared || _player.TransferMode.StartsWith("Reopening", StringComparison.Ordinal)) { CheckTimeout(waitStart); yield return null; }
            Check(_player.DecoderType == VideoDecoderType.Hardware && _player.TransferMode != "Hardware decode + CPU RGBA upload",
                "native API failure first recovers through the platform GPU backend");
            Check(!_player.DecoderDevice.Contains("D3D12VA") && !_player.DecoderDevice.Contains("Vulkan Video"),
                "recovery selects a different hardware API");
            recover.Invoke(_player, new object[] { new NotSupportedException("Injected platform texture import failure for hardware CPU fallback") });
            waitStart = UnityEngine.Time.realtimeSinceStartup;
        }
        while (!_player.IsPrepared || _player.TransferMode != "Hardware decode + CPU RGBA upload") { CheckTimeout(waitStart); yield return null; }
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
        _player.Rate = 1;
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
        _player.Rate = 1;
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
            Task seek = null;
            double seekTarget = Math.Min(0.75, _player.LengthSeconds / 2);
            Action<FFmpegVideoPlayer, Texture> callback = null;
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
                recover.Invoke(_player, new object[] { new NotSupportedException("Injected asynchronous GPU presentation failure: " + control) });
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
        Task restarted = null;
        int restarts = 0;
        Action<FFmpegVideoPlayer, Texture> restart = null;
        restart = (player, texture) =>
        {
            if (texture != null) return;
            player.TextureChanged -= restart;
            restarts++;
            player.Play(path);
            restarted = player.PrepareAsync();
        };
        _player.TextureChanged += restart;
        try { fail.Invoke(_player, new object[] { new InvalidOperationException("Expected recovery test terminal failure") }); }
        finally { _player.TextureChanged -= restart; }
        Check(restarts == 1 && restarted != null, "terminal failure callback starts replacement media once");
        Check(failedSeek.IsFaulted && failedSeek.Exception != null, "terminal failure completes old seek with its error");
        Check(_player.State != VideoPlaybackState.Error, "terminal failure does not overwrite replacement state");
        waitStart = UnityEngine.Time.realtimeSinceStartup;
        while (!restarted.IsCompleted) { CheckTimeout(waitStart); yield return null; }
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
    public static string Argument(string name)
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
