#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using DeviceActivity = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Ffmpegplayer.Validation.AndroidContentDeviceActivity;
#endif

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Checks real SAF input through production playback without a storage facade or media cache.</summary>
    [DisallowMultipleComponent]
    public sealed class AndroidContentDeviceSmoke : MonoBehaviour
    {
        /// <summary>Cancels the observed run when the scene component is destroyed.</summary>
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        /// <summary>Accumulates URI-free runtime and per-stage evidence.</summary>
        private readonly StringBuilder _evidence = new StringBuilder();
        /// <summary>Owns the production player used for both decoder preferences.</summary>
        private FFmpegVideoPlayer? _player;
        /// <summary>Retains the selected URI only for input and exact equality, never diagnostics.</summary>
        private string? _selectedUri;
        /// <summary>Identifies the result file in this application's private files directory.</summary>
        private string? _reportPath;
        /// <summary>Stores the first report line, changed to PASS or FAIL only when the run terminates.</summary>
        private string _result = "WAITING: select the staged test video in the Android document picker.";
        /// <summary>Identifies the current stage without storing document identity.</summary>
        private string _phase = "startup";
        /// <summary>Stores controlled assertion text, never provider exception messages.</summary>
        private string _failureReason = "Runtime operation failed; exception message redacted.";
        /// <summary>Counts successful assertions.</summary>
        private int _checks;
        /// <summary>Counts actual FrameReady notifications across stages.</summary>
        private long _presentedFrames;
        /// <summary>Stores the actual event frame number rather than treating it as a timestamp.</summary>
        private long _lastFrameNumber = -1;
        /// <summary>Counts production end-of-stream events for the bounded loop check.</summary>
        private int _endReached;
        /// <summary>Records playback error without retaining its potentially sensitive message.</summary>
        private bool _playerFailed;
        /// <summary>Prevents resumed tasks from accessing destroyed Unity objects.</summary>
        private bool _destroyed;
        /// <summary>Indicates that the run has completed and disposed its cancellation source.</summary>
        private bool _finished;
        /// <summary>Separates actual hardware decoding from software fallback.</summary>
        private string _hardwareResult = "NOT_RUN";
        /// <summary>Records whether the real provider exposes working seek.</summary>
        private string _seekResult = "NOT_RUN";
        /// <summary>Records whether the near-EOF loop regression ran successfully.</summary>
        private string _loopResult = "NOT_RUN";

        /// <summary>Uses Unity's void message boundary while awaiting every task and catching all terminal failures.</summary>
        private async void Start()
        {
            try
            {
                _reportPath = Path.Combine(Application.persistentDataPath, "ffmpeg-content-smoke.txt");
                Application.runInBackground = true;
                WriteReport();
#if UNITY_ANDROID && !UNITY_EDITOR
                await RunAsync(_lifetime.Token);
                _result = "PASS: " + _checks.ToString(CultureInfo.InvariantCulture)
                    + " assertions; SOFTWARE=PASS; HARDWARE_CPU=" + _hardwareResult
                    + "; SEEK=" + _seekResult + "; LOOP=" + _loopResult
                    + "; real SAF/native content; URI omitted.";
#else
                throw new PlatformNotSupportedException("Device content validation requires an Android player.");
#endif
            }
            catch (Exception exception)
            {
                // Provider/JNI messages and stack traces may contain private document URIs. Do not print either.
                _result = "FAIL: phase=" + _phase + "; exception=" + exception.GetType().Name
                    + "; reason=" + _failureReason + "; URI and exception message omitted.";
            }
            finally
            {
                if (!_destroyed)
                {
                    try
                    {
                        ClosePlayer();
                        WriteReport();
                        UnityEngine.Debug.Log(_result);
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogError("FAIL: report/cleanup; exception=" + exception.GetType().Name
                            + "; exception message omitted.");
                    }
                }
                _finished = true;
                _lifetime.Dispose();
            }
            // Keep the process alive for ADB PID/log collection and the next force-stop/install-r cycle.
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Runs picker, native protocol, software and hardware-CPU checks on Unity's captured main-thread context.</summary>
        /// <param name="cancellationToken">The scene-destruction token propagated through all waits.</param>
        /// <returns>A task completing after both decoder preference stages have produced their evidence.</returns>
        /// <exception cref="InvalidOperationException">A picker or playback invariant fails.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        /// <exception cref="TimeoutException">A bounded picker or playback wait expires.</exception>
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            _phase = "environment";
#if ENABLE_IL2CPP
            const string ScriptingBackend = "IL2CPP";
#else
            const string ScriptingBackend = "Mono";
#endif
            Evidence("ENV processABI=" + (IntPtr.Size == 8 ? "arm64-v8a" : "armeabi-v7a")
                + "; processBits=" + (IntPtr.Size * 8).ToString(CultureInfo.InvariantCulture)
                + "; IntPtr.Size=" + IntPtr.Size.ToString(CultureInfo.InvariantCulture)
                + "; api=" + DeviceActivity.GetAndroidApiLevel().ToString(CultureInfo.InvariantCulture)
                + "; unity=" + Application.unityVersion + "; backend=" + ScriptingBackend + "; scripting=" + ScriptingBackend
                + "; ENABLE_IL2CPP=" + (ScriptingBackend == "IL2CPP" ? "true" : "false")
                + "; graphics=" + SystemInfo.graphicsDeviceType + "; model=" + SystemInfo.deviceModel);
            var contentAvailable = HasContentInputProtocol(out var protocolCount);
            Evidence("PROTOCOL avio_enum_protocols(input): count=" + protocolCount.ToString(CultureInfo.InvariantCulture)
                + "; contentProtocol=" + (contentAvailable ? "True" : "False") + "; content=" + (contentAvailable ? "true" : "false"));
            Check(contentAvailable, "The running Android libavformat must expose the content input protocol.");
            _phase = "picker";
            Evidence("PICKER select Download/MajdataFFmpegContentValidation-<uuid>/test.mp4; no broad storage permission is requested.");
            DeviceActivity.RequestVideo();
            await WaitUntilAsync(() => !string.Equals(DeviceActivity.GetRequestState(), "pending", StringComparison.Ordinal)
                && !string.Equals(DeviceActivity.GetRequestState(), "idle", StringComparison.Ordinal), 300000,
                "The document picker did not finish within five minutes.", cancellationToken);
            Evidence("PICKER state=" + DeviceActivity.GetRequestState() + "; failureCode=" + DeviceActivity.GetFailure());
            Check(string.Equals(DeviceActivity.GetRequestState(), "selected", StringComparison.Ordinal),
                "The picker was canceled or failed; a selected document is required.");
            _selectedUri = DeviceActivity.GetSelectedUri();
            Check(!string.IsNullOrEmpty(_selectedUri) && _selectedUri.StartsWith("content:", StringComparison.OrdinalIgnoreCase),
                "The picker must return an authorized opaque content URI.");
            Evidence("PICKER selected=true; persistedReadGrant=" + (DeviceActivity.GetHasPersistedReadGrant() ? "true" : "false")
                + "; URI omitted.");
            _player = gameObject.AddComponent<FFmpegVideoPlayer>();
            _player.PreferNativeTextures = false;
            _player.RequireHardwareDecoding = false;
            _player.PlayOnAwake = false;
            _player.FrameReady += OnFrameReady;
            _player.EndReached += OnEndReached;
            _player.ErrorReceived += OnPlayerError;
            await TestSoftwareAsync(cancellationToken);
            await TestHardwareCpuAsync(cancellationToken);
        }

        /// <summary>Checks software preparation first, real display and controls, bounded loop, and close without hardware warm-up.</summary>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing after the software session is closed and stale presentation is rejected.</returns>
        /// <exception cref="InvalidOperationException">A software or control invariant fails.</exception>
        /// <exception cref="TimeoutException">An operation does not make bounded progress.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private async Task TestSoftwareAsync(CancellationToken cancellationToken)
        {
            var player = RequirePlayer();
            _phase = "software.prepare";
            player.PreferredDecoderType = VideoDecoderType.Software;
            var beforePrepare = _presentedFrames;
            await WaitTaskAsync(player.PreloadAsync(RequireSelectedUri(), cancellationToken), 45000, cancellationToken);
            Check(player.DecoderType == VideoDecoderType.Software, "The first session must actually use software decoding.");
            CheckPrepared(beforePrepare);
            RecordPlayer("SOFTWARE FIRST_FRAME");
            await CheckPlayAndPauseAsync(cancellationToken);
            Check(player.DecoderType == VideoDecoderType.Software, "Software playback must not switch to a hardware decoder.");
            if (player.IsSeekable)
            {
                _phase = "software.seek";
                Check(player.LengthSeconds > 1, "The seekable fixture must contain more than one second of video.");
                var target = Math.Min(2, player.LengthSeconds / 3);
                var beforeSeek = _presentedFrames;
                await WaitTaskAsync(player.SeekAsync(target), 30000, cancellationToken);
                Check(_presentedFrames > beforeSeek && Math.Abs(player.TimeSeconds - target) < 0.25,
                    "Seek must present a new frame at the requested timeline position.");
                AssertSourceUnchanged();
                await WaitTaskAsync(player.SeekAsync(0), 30000, cancellationToken);
                Check(player.TimeSeconds < 0.25, "Backward seek must return to the beginning.");
                _seekResult = "PASS";
                RecordPlayer("SOFTWARE SEEK_BACK");
                await CheckLoopAsync(cancellationToken);
                _phase = "software.stop";
                player.Stop();
                await WaitUntilAsync(() => player.State == VideoPlaybackState.Stopped && !player.IsBuffering,
                    30000, "Stop did not finish its asynchronous rewind.", cancellationToken);
                Check(player.IsPrepared && !player.IsPlaying && player.TimeSeconds < 0.25,
                    "Stop must retain seekable media and rewind its timeline.");
                AssertSourceUnchanged();
                await WaitTaskAsync(player.SeekAsync(target), 30000, cancellationToken);
                await WaitTaskAsync(player.SeekAsync(0), 30000, cancellationToken);
                Check(player.TimeSeconds < 0.25, "Seek back must remain functional after Stop.");
            }
            else
            {
                _seekResult = "SKIP_NONSEEKABLE_PROVIDER";
                _loopResult = "SKIP_NONSEEKABLE_PROVIDER";
                Evidence("SKIP: SOFTWARE seek/rewind/loop; provider is nonseekable; not counted as seek/loop passing.");
                player.Stop();
                Check(!player.IsPrepared && !player.IsPlaying, "Stop must close a nonseekable input.");
                AssertSourceUnchanged();
            }
            _phase = "software.close";
            player.Close();
            Check(!player.IsPrepared && !player.IsPlaying, "Close must release the prepared software session.");
            AssertSourceUnchanged();
            var afterClose = _presentedFrames;
            await WaitWallClockAsync(150, cancellationToken);
            Check(_presentedFrames == afterClose, "Close must prevent stale software frames from being presented.");
            Evidence("STAGE SOFTWARE=PASS; seek=" + _seekResult + "; loop=" + _loopResult);
        }

        /// <summary>Checks the hardware preference with CPU textures and distinguishes actual hardware use from fallback.</summary>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing after the second session plays, records its actual backend, and closes.</returns>
        /// <exception cref="InvalidOperationException">Preparation, playback, transport or URI preservation fails.</exception>
        /// <exception cref="TimeoutException">Preparation or playback does not make bounded progress.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private async Task TestHardwareCpuAsync(CancellationToken cancellationToken)
        {
            var player = RequirePlayer();
            _phase = "hardware_cpu.prepare";
            player.PreferNativeTextures = false;
            player.RequireHardwareDecoding = false;
            player.PreferredDecoderType = VideoDecoderType.Hardware;
            var beforePrepare = _presentedFrames;
            await WaitTaskAsync(player.PreloadAsync(RequireSelectedUri(), cancellationToken), 45000, cancellationToken);
            CheckPrepared(beforePrepare);
            Check(!player.PreferNativeTextures, "Hardware validation must not depend on native GPU texture sharing.");
            RecordPlayer("HARDWARE_CPU FIRST_FRAME");
            await CheckPlayAndPauseAsync(cancellationToken);
            RecordPlayer("HARDWARE_CPU PLAYED");
            if (player.DecoderType == VideoDecoderType.Hardware)
            {
                _hardwareResult = "PASS";
                Check(player.DecoderType == VideoDecoderType.Hardware, "Hardware passing requires an actual hardware decoder.");
            }
            else
            {
                _hardwareResult = "SKIP_SOFTWARE_FALLBACK";
                Evidence("SKIP: HARDWARE_CPU unavailable; actual backend is software fallback; not counted as hardware passing.");
            }
            _phase = "hardware_cpu.close";
            player.Close();
            Check(!player.IsPrepared && !player.IsPlaying, "Close must release the hardware-preference session.");
            AssertSourceUnchanged();
            Evidence("STAGE HARDWARE_CPU=" + _hardwareResult);
        }

        /// <summary>Checks presented-frame and timeline progress followed by a frozen paused timeline.</summary>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing when real playback and pause invariants hold.</returns>
        /// <exception cref="InvalidOperationException">Playback or pause invariants fail.</exception>
        /// <exception cref="TimeoutException">No decoded-frame and timeline progress are observed.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private async Task CheckPlayAndPauseAsync(CancellationToken cancellationToken)
        {
            var player = RequirePlayer();
            var before = _presentedFrames;
            var position = player.TimeSeconds;
            var stage = player.PreferredDecoderType == VideoDecoderType.Hardware ? "hardware_cpu" : "software";
            _phase = stage + ".play";
            player.Play();
            await WaitUntilAsync(() => _presentedFrames >= before + 3 && player.TimeSeconds > position + 0.08,
                15000, "Playback did not present new decoded frames and advance the timeline.", cancellationToken);
            Check(player.IsPlaying, "The player must report Playing after observed frame progress.");
            AssertSourceUnchanged();
            RecordPlayer("PLAY PROGRESS");
            _phase = stage + ".pause";
            player.Pause();
            var paused = player.TimeSeconds;
            await WaitWallClockAsync(150, cancellationToken);
            Check(!player.IsPlaying && Math.Abs(player.TimeSeconds - paused) < 0.001,
                "Pause must stop playback and freeze its timeline.");
            AssertSourceUnchanged();
        }

        /// <summary>Seeks near EOF and requires an end event followed by newly displayed loop-start frames.</summary>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing without waiting for the entire media duration.</returns>
        /// <exception cref="InvalidOperationException">Loop restart or source preservation fails.</exception>
        /// <exception cref="TimeoutException">No bounded EOF and loop restart are observed.</exception>
        /// <exception cref="OperationCanceledException">The component is destroyed.</exception>
        private async Task CheckLoopAsync(CancellationToken cancellationToken)
        {
            var player = RequirePlayer();
            _phase = "software.loop";
            player.Loop = true;
            await WaitTaskAsync(player.SeekAsync(Math.Max(0, player.LengthSeconds - 0.6)), 30000, cancellationToken);
            var beforeEnds = _endReached;
            var beforeFrames = _presentedFrames;
            player.Play();
            await WaitUntilAsync(() => _endReached > beforeEnds && _presentedFrames >= beforeFrames + 2
                && player.IsPlaying && player.TimeSeconds < Math.Min(3, player.LengthSeconds / 2),
                15000, "Loop did not present a beginning frame after its near-EOF end event.", cancellationToken);
            Check(_endReached > beforeEnds && player.IsPlaying, "Loop must cross EOF and resume actual playback.");
            AssertSourceUnchanged();
            _loopResult = "PASS";
            RecordPlayer("SOFTWARE LOOP_RESTART");
            player.Pause();
            player.Loop = false;
        }

        /// <summary>Checks decoded first-frame presentation and metadata after preparation.</summary>
        /// <param name="beforePrepare">The displayed-frame count before this preparation.</param>
        /// <exception cref="InvalidOperationException">Preparation lacks decoded output, metadata or source identity.</exception>
        private void CheckPrepared(long beforePrepare)
        {
            var player = RequirePlayer();
            Check(player.IsPrepared && !player.IsPlaying, "Preload must complete with prepared, paused media.");
            Check(player.Texture != null && player.Width > 0 && player.Height > 0 && player.FrameRate > 0,
                "Preload must display a decoded first frame and expose video dimensions and frame rate.");
            Check(_presentedFrames > beforePrepare && _lastFrameNumber >= 0,
                "Preload must emit a real FrameReady notification and frame number.");
            Check(!string.IsNullOrEmpty(player.CodecName) && !string.IsNullOrEmpty(player.DecoderName),
                "Prepared media must identify its video codec and actual decoder.");
            AssertSourceUnchanged();
        }

        /// <summary>Compares player input and Java picker state without exposing either URI.</summary>
        /// <exception cref="InvalidOperationException">A control or preparation operation changes the original URI.</exception>
        private void AssertSourceUnchanged()
        {
            Check(string.Equals(RequirePlayer().Url, RequireSelectedUri(), StringComparison.Ordinal)
                && string.Equals(DeviceActivity.GetSelectedUri(), RequireSelectedUri(), StringComparison.Ordinal),
                "The production player and picker must preserve the original opaque URI across controls.");
        }
#endif

        /// <summary>Enumerates actual native input protocols without using an internal decoder helper.</summary>
        /// <param name="protocolCount">Receives the number of input protocols exposed by the loaded library.</param>
        /// <returns>True when the native input table contains content.</returns>
        /// <exception cref="DllNotFoundException">The native library cannot be loaded.</exception>
        /// <exception cref="EntryPointNotFoundException">The library does not export protocol enumeration.</exception>
        private static unsafe bool HasContentInputProtocol(out int protocolCount)
        {
            void* opaque = null;
            protocolCount = 0;
            var content = false;
            while (ffmpeg.avio_enum_protocols(&opaque, 0) is { } name)
            {
                protocolCount++;
                content |= string.Equals(name, "content", StringComparison.Ordinal);
            }
            return content;
        }

        /// <summary>Observes a production task using main-thread yields and a finite deadline.</summary>
        /// <param name="task">The production preparation or seek task.</param>
        /// <param name="timeoutMilliseconds">The operation's wall-clock deadline.</param>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing after successful production completion.</returns>
        /// <exception cref="TimeoutException">The deadline expires.</exception>
        /// <exception cref="OperationCanceledException">The scene or production task is canceled.</exception>
        /// <exception cref="InvalidOperationException">The production player reports failure.</exception>
        private async Task WaitTaskAsync(Task task, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested || watch.ElapsedMilliseconds >= timeoutMilliseconds || _playerFailed)
                {
                    if (!_destroyed && _player != null)
                    {
                        _player.Close();
                    }
                    try
                    {
                        await task;
                    }
                    catch (Exception)
                    {
                        // Observe completion without retaining or printing provider exception messages.
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfPlaybackFailed();
                    _failureReason = "A production task exceeded its finite validation deadline.";
                    throw new TimeoutException();
                }
                await Task.Yield();
            }
            await task;
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfPlaybackFailed();
        }

        /// <summary>Waits for a main-thread predicate without background Unity access.</summary>
        /// <param name="ready">The playback or picker completion predicate.</param>
        /// <param name="timeoutMilliseconds">The finite deadline.</param>
        /// <param name="failure">A controlled, URI-free deadline diagnostic.</param>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing when the predicate holds.</returns>
        /// <exception cref="TimeoutException">The predicate never becomes true before the deadline.</exception>
        /// <exception cref="OperationCanceledException">The scene is destroyed.</exception>
        /// <exception cref="InvalidOperationException">The production player reports failure.</exception>
        private async Task WaitUntilAsync(Func<bool> ready, int timeoutMilliseconds, string failure, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfPlaybackFailed();
                if (ready())
                {
                    return;
                }
                if (watch.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    _failureReason = failure;
                    throw new TimeoutException();
                }
                await Task.Yield();
            }
        }

        /// <summary>Waits a short interval using main-thread frame yields rather than sleeping.</summary>
        /// <param name="milliseconds">The pause or close observation interval.</param>
        /// <param name="cancellationToken">The scene-destruction token.</param>
        /// <returns>A task completing after the interval.</returns>
        /// <exception cref="OperationCanceledException">The scene is destroyed.</exception>
        /// <exception cref="InvalidOperationException">The production player reports failure.</exception>
        private async Task WaitWallClockAsync(int milliseconds, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfPlaybackFailed();
                await Task.Yield();
            }
        }

        /// <summary>Fails polling without counting per-frame polling as successful assertions.</summary>
        /// <exception cref="InvalidOperationException">The production player reported a playback error.</exception>
        private void ThrowIfPlaybackFailed()
        {
            if (_playerFailed)
            {
                _failureReason = "The production player reported an error; message redacted.";
                throw new InvalidOperationException(_failureReason);
            }
        }

        /// <summary>Returns the live player owned by this component.</summary>
        /// <returns>The production player.</returns>
        /// <exception cref="InvalidOperationException">No live player is available.</exception>
        private FFmpegVideoPlayer RequirePlayer()
        {
            if (_player == null)
            {
                throw new InvalidOperationException("The validation player is unavailable.");
            }
            return _player;
        }

        /// <summary>Returns the private original selection without logging it.</summary>
        /// <returns>The selected opaque URI.</returns>
        /// <exception cref="InvalidOperationException">No document has been selected.</exception>
        private string RequireSelectedUri()
        {
            return _selectedUri ?? throw new InvalidOperationException("No document was selected.");
        }

        /// <summary>Records actual backend, transport, metadata and displayed-frame evidence without URI or exception text.</summary>
        /// <param name="stage">The controlled snapshot stage name.</param>
        /// <exception cref="IOException">Report writing fails.</exception>
        private void RecordPlayer(string stage)
        {
            var player = RequirePlayer();
            Evidence(stage + "; actualDecoder=" + player.DecoderType + "/" + player.DecoderName
                + "; TransferMode=" + player.TransferMode + "; FrameNumber=" + _lastFrameNumber.ToString(CultureInfo.InvariantCulture)
                + "; presentedFrames=" + _presentedFrames.ToString(CultureInfo.InvariantCulture)
                + "; IsSeekable=" + (player.IsSeekable ? "true" : "false")
                + "; size=" + player.Width.ToString(CultureInfo.InvariantCulture) + "x" + player.Height.ToString(CultureInfo.InvariantCulture)
                + "; FrameRate=" + player.FrameRate.ToString("G17", CultureInfo.InvariantCulture)
                + "; LengthSeconds=" + player.LengthSeconds.ToString("G17", CultureInfo.InvariantCulture)
                + "; TimeSeconds=" + player.TimeSeconds.ToString("G17", CultureInfo.InvariantCulture));
        }

        /// <summary>Flushes safe evidence for the external ADB runner.</summary>
        /// <param name="message">A controlled diagnostic with no URI or provider exception message.</param>
        /// <exception cref="IOException">Report writing fails.</exception>
        private void Evidence(string message)
        {
            _evidence.AppendLine(message);
            WriteReport();
            UnityEngine.Debug.Log("FFmpeg Content Device: " + message);
        }

        /// <summary>Writes status and evidence to the fixed application-private report file.</summary>
        /// <exception cref="IOException">Report writing fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The report directory is inaccessible.</exception>
        private void WriteReport()
        {
            if (_reportPath != null)
            {
                File.WriteAllText(_reportPath, _result + Environment.NewLine + _evidence.ToString());
            }
        }

        /// <summary>Counts one real invariant or records a controlled failure.</summary>
        /// <param name="condition">Whether the invariant holds.</param>
        /// <param name="message">The controlled, URI-free failure reason.</param>
        /// <exception cref="InvalidOperationException">The invariant is false.</exception>
        private void Check(bool condition, string message)
        {
            if (!condition)
            {
                _failureReason = message;
                throw new InvalidOperationException(message);
            }
            _checks++;
        }

        /// <summary>Records genuinely presented frames.</summary>
        /// <param name="player">The event source.</param>
        /// <param name="frameNumber">The actual decoder frame number, not a PTS.</param>
        private void OnFrameReady(FFmpegVideoPlayer player, long frameNumber)
        {
            _presentedFrames++;
            _lastFrameNumber = frameNumber;
        }

        /// <summary>Records production EOF events used by the loop check.</summary>
        /// <param name="player">The event source.</param>
        private void OnEndReached(FFmpegVideoPlayer player)
        {
            _endReached++;
        }

        /// <summary>Discards potentially sensitive provider diagnostics while recording failure.</summary>
        /// <param name="player">The event source.</param>
        /// <param name="message">The intentionally discarded error message.</param>
        private void OnPlayerError(FFmpegVideoPlayer player, string message)
        {
            _playerFailed = true;
        }

        /// <summary>Unsubscribes before closing on Unity's main thread.</summary>
        private void ClosePlayer()
        {
            if (_player != null)
            {
                _player.FrameReady -= OnFrameReady;
                _player.EndReached -= OnEndReached;
                _player.ErrorReceived -= OnPlayerError;
                _player.Close();
            }
        }

        private void OnDestroy()
        {
            _destroyed = true;
            if (!_finished)
            {
                _lifetime.Cancel();
            }
            ClosePlayer();
        }
    }
}
