using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the FFmpeg video player component while Unity drives its lifecycle and Update loop in Play Mode.</summary>
    public class FFmpegVideoPlayerPlayModeTests
    {
        /// <summary>Owns the encoded test media.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores a one-second, 30 fps MPEG-4 clip.</summary>
        private string _clip = "";
        /// <summary>Tracks the objects created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Encodes the test media once.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
            _clip = FFmpegTestMedia.CreateVideo(_directory, "player.mkv");
        }

        /// <summary>Deletes the test media.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object, which also closes the players.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Adds a software-decoding player to a new game object.</summary>
        /// <returns>The player.</returns>
        private FFmpegVideoPlayer CreatePlayer()
        {
            var player = _objects.Add<FFmpegVideoPlayer>();
            player.PreferredDecoderType = VideoDecoderType.Software;
            return player;
        }

        /// <summary>Yields until a task completes or the time limit elapses.</summary>
        /// <param name="task">The task to wait for.</param>
        /// <returns>The waiting coroutine.</returns>
        private static IEnumerator WaitFor(Task task)
        {
            return PlayModeWait.Until(() => task.IsCompleted);
        }

        /// <summary>Checks preparation: the first frame, media information, events and the idle-to-prepared transition.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreloadAsync_PresentsFirstFrameAndMetadata()
        {
            var player = CreatePlayer();
            var prepared = 0;
            var frames = 0L;
            Texture? published = null;
            player.Prepared += _ => prepared++;
            player.FrameReady += (_, frame) => frames = frame;
            player.TextureChanged += (_, texture) => published = texture;
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
            Assert.That(player.Texture, Is.Null);
            Assert.That(player.Width, Is.EqualTo(0));

            var task = player.PreloadAsync(_clip);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Preparing));
            Assert.That(player.IsBuffering, Is.True);
            Assert.That(player.PrepareAsync(), Is.SameAs(task));
            yield return WaitFor(task);

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Prepared));
            Assert.That(player.IsPrepared, Is.True);
            Assert.That(player.IsPlaying, Is.False);
            Assert.That(player.IsSeekable, Is.True);
            Assert.That(player.Texture, Is.InstanceOf<Texture2D>());
            Assert.That(published, Is.SameAs(player.Texture));
            Assert.That(prepared, Is.EqualTo(1));
            Assert.That(frames, Is.EqualTo(1));
            Assert.That(player.Width, Is.EqualTo((uint)FFmpegTestMedia.DefaultWidth));
            Assert.That(player.Height, Is.EqualTo((uint)FFmpegTestMedia.DefaultHeight));
            Assert.That(player.FrameRate, Is.EqualTo(30).Within(0.01));
            Assert.That(player.LengthSeconds, Is.EqualTo(1).Within(0.05));
            Assert.That(player.Length, Is.InRange(950, 1050));
            Assert.That(player.CodecName, Is.EqualTo("mpeg4"));
            Assert.That(player.DecoderName, Is.EqualTo("mpeg4"));
            Assert.That(player.DecoderDevice, Is.EqualTo("Software"));
            Assert.That(player.DecoderType, Is.EqualTo(VideoDecoderType.Software));
            Assert.That(player.DecoderThreadCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(player.DecoderThreadingMode, Is.Not.Empty);
            Assert.That(player.TransferMode, Is.EqualTo("Software RGBA upload"));
            Assert.That(player.TimeSeconds, Is.EqualTo(0));
            Assert.That(player.Position, Is.EqualTo(0));
            Assert.That(player.BitRate, Is.GreaterThanOrEqualTo(0));
            Assert.That(player.CurrentBitRate, Is.GreaterThanOrEqualTo(0));
            Assert.That(player.BufferedFrames, Is.InRange(0, 3));
            Assert.That(player.LastError, Is.Null);
            Assert.That(player.HardwareFallbackReason, Is.Null);
            Assert.That(player.PrepareAsync().IsCompleted, Is.True);
        }

        /// <summary>Checks argument and state validation of the control API.</summary>
        [Test]
        public void Controls_ValidateArguments()
        {
            var player = CreatePlayer();

            Assert.Throws<InvalidOperationException>(() => player.PrepareAsync());
            player.Url = _clip;
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() => player.PrepareAsync(source.Token));
            Assert.Throws<InvalidOperationException>(() => player.SeekAsync(0));
            Assert.Throws<InvalidOperationException>(() => player.Position = 0.5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => player.SeekAsync(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.PlaybackRate = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => player.PlaybackRate = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => player.PreferredDecoderType = (VideoDecoderType)5);
            player.NextFrame();
            player.Pause();
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>Checks the configuration properties that apply to the next media open.</summary>
        [Test]
        public void Configuration_RoundTrips()
        {
            var player = CreatePlayer();
            var target = _objects.Track(new RenderTexture(8, 8, 0));

            player.PreferredDecoderType = VideoDecoderType.Hardware;
            player.PreferNativeTextures = false;
            player.RequireHardwareDecoding = true;
            player.TargetTexture = target;
            player.Loop = true;
            player.PlayOnAwake = true;
            player.BufferedFrameLimit = 5;
            player.IOTimeoutSeconds = 3;
            player.PlaybackRate = 2;
            player.Url = null;

            Assert.That(player.PreferredDecoderType, Is.EqualTo(VideoDecoderType.Hardware));
            Assert.That(player.PreferNativeTextures, Is.False);
            Assert.That(player.RequireHardwareDecoding, Is.True);
            Assert.That(player.TargetTexture, Is.SameAs(target));
            Assert.That(player.Loop, Is.True);
            Assert.That(player.PlayOnAwake, Is.True);
            Assert.That(player.BufferedFrameLimit, Is.EqualTo(5));
            Assert.That(player.IOTimeoutSeconds, Is.EqualTo(3));
            Assert.That(player.PlaybackRate, Is.EqualTo(2));
            Assert.That(player.Url, Is.EqualTo(""));
        }

        /// <summary>Checks playback to the end, the end event and restarting ended media.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ReachesEndAndRestarts()
        {
            var player = CreatePlayer();
            var started = 0;
            var ended = 0;
            long lastTime = -1;
            player.Started += _ => started++;
            player.EndReached += _ => ended++;
            player.TimeChanged += (_, time) => lastTime = time;
            player.PlaybackRate = 8;

            player.Play(_clip);
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Ended);

            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Ended));
            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(player.TimeSeconds, Is.EqualTo(player.LengthSeconds).Within(1e-6));
            Assert.That(player.Position, Is.EqualTo(1).Within(1e-6));
            Assert.That(lastTime, Is.EqualTo(player.Time));
            Assert.That(player.IsBuffering, Is.False);

            player.Play();
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Seeking));
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Playing);

            Assert.That(player.IsPlaying, Is.True);
            Assert.That(player.TimeSeconds, Is.LessThan(0.9));
            player.Play();
            Assert.That(player.IsPlaying, Is.True);
        }

        /// <summary>Checks that looping media restarts after reaching the end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Loop_RestartsAtEnd()
        {
            var player = CreatePlayer();
            var ended = 0;
            player.EndReached += _ => ended++;
            player.Loop = true;
            player.PlaybackRate = 16;

            player.Play(_clip);
            yield return PlayModeWait.Until(() => ended >= 2);

            Assert.That(ended, Is.GreaterThanOrEqualTo(2));
            Assert.That(player.State, Is.Not.EqualTo(VideoPlaybackState.Ended));
        }

        /// <summary>Checks pausing, resuming and the pause events.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PauseAndResume_FreezeAndAdvanceTime()
        {
            var player = CreatePlayer();
            var paused = 0;
            player.Paused += _ => paused++;
            player.PlaybackRate = 0.25f;
            yield return WaitFor(player.PreloadAsync(_clip));

            player.SetPause(false);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Playing));
            yield return PlayModeWait.Until(() => player.TimeSeconds > 0.01);
            player.SetPause(true);
            var frozen = player.TimeSeconds;
            yield return PlayModeWait.Frames(5);

            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Paused));
            Assert.That(paused, Is.EqualTo(1));
            Assert.That(player.TimeSeconds, Is.EqualTo(frozen));
            Assert.That(player.IsBuffering, Is.False);
        }

        /// <summary>Checks seeking through every seek API, superseded seeks and seek completion events.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Seek_PresentsTargetFrame()
        {
            var player = CreatePlayer();
            var completed = 0;
            player.SeekCompleted += _ => completed++;
            yield return WaitFor(player.PreloadAsync(_clip));

            var superseded = player.SeekAsync(0.2);
            var seek = player.SeekAsync(0.5);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Seeking));
            Assert.That(player.IsBuffering, Is.True);
            yield return WaitFor(seek);

            Assert.That(superseded.IsCanceled, Is.True);
            Assert.That(seek.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Paused));
            Assert.That(player.TimeSeconds, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(completed, Is.EqualTo(1));

            player.Time = 200;
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Paused);
            Assert.That(player.TimeSeconds, Is.EqualTo(0.2).Within(1e-9));

            player.Position = 0.25f;
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Paused);
            Assert.That(player.TimeSeconds, Is.EqualTo(0.25f * player.LengthSeconds).Within(1e-9));

            player.SeekTo(TimeSpan.FromSeconds(5));
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Paused);
            Assert.That(player.TimeSeconds, Is.EqualTo(player.LengthSeconds).Within(1e-6));

            player.TimeSeconds = -1;
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Paused);
            Assert.That(player.TimeSeconds, Is.EqualTo(0));
            Assert.That(completed, Is.EqualTo(5));
        }

        /// <summary>Checks that a play request during a seek resumes playback when the seek completes, and a pause request keeps it paused.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Seek_DeferredPlayAndPause()
        {
            var player = CreatePlayer();
            player.PlaybackRate = 0.25f;
            yield return WaitFor(player.PreloadAsync(_clip));

            player.SeekAsync(0.3);
            player.Play();
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Seeking));
            yield return PlayModeWait.Until(() => player.State != VideoPlaybackState.Seeking);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Playing));

            player.SeekAsync(0.1);
            player.Pause();
            yield return PlayModeWait.Until(() => player.State != VideoPlaybackState.Seeking);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Paused));
        }

        /// <summary>Checks stepping one frame forward while paused.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NextFrame_StepsOneFrame()
        {
            var player = CreatePlayer();
            var frames = 0L;
            player.FrameReady += (_, frame) => frames = frame;
            yield return WaitFor(player.PreloadAsync(_clip));

            player.NextFrame();
            yield return PlayModeWait.Until(() => frames >= 2);

            Assert.That(frames, Is.EqualTo(2));
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Paused));
            Assert.That(player.TimeSeconds, Is.EqualTo(1 / 30.0).Within(0.002));
        }

        /// <summary>Checks that Stop rewinds seekable media and keeps it prepared.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Stop_RewindsPreparedMedia()
        {
            var player = CreatePlayer();
            var stopped = 0;
            player.Stopped += _ => stopped++;
            player.PlaybackRate = 0.5f;
            player.Play(_clip);
            yield return PlayModeWait.Until(() => player.IsPlaying && player.TimeSeconds > 0.02);

            player.Stop();
            Assert.That(stopped, Is.EqualTo(1));
            yield return PlayModeWait.Until(() => player.State == VideoPlaybackState.Stopped);

            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Stopped));
            Assert.That(player.IsPrepared, Is.True);
            Assert.That(player.TimeSeconds, Is.EqualTo(0));
        }

        /// <summary>Checks that stopping unprepared media closes it and reports the stopped state.</summary>
        [Test]
        public void Stop_UnpreparedMedia_Closes()
        {
            var player = CreatePlayer();
            var stopped = 0;
            player.Stopped += _ => stopped++;
            player.Url = _clip;
            var task = player.PrepareAsync();

            player.Stop();

            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Stopped));
            Assert.That(stopped, Is.EqualTo(1));
            Assert.That(task.IsCanceled, Is.True);
        }

        /// <summary>Checks that Close and Url changes release the media and cancel pending work.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Close_ReleasesMediaAndCancelsWork()
        {
            var player = CreatePlayer();
            var changes = 0;
            Texture? published = null;
            yield return WaitFor(player.PreloadAsync(_clip));
            player.TextureChanged += (_, texture) =>
            {
                changes++;
                published = texture;
            };
            var seek = player.SeekAsync(0.5);

            player.Url = _clip;
            Assert.That(player.IsPrepared, Is.True);
            player.Close();

            Assert.That(seek.IsCanceled, Is.True);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
            Assert.That(player.IsPrepared, Is.False);
            Assert.That(player.Texture, Is.Null);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(published, Is.Null);
            Assert.That(player.TimeSeconds, Is.EqualTo(0));
            Assert.That(player.LengthSeconds, Is.EqualTo(0));
            Assert.That(player.CodecName, Is.Empty);
            Assert.That(player.DecoderName, Is.Empty);
            Assert.That(player.DecoderDevice, Is.Empty);
            Assert.That(player.DecoderThreadingMode, Is.Empty);
            Assert.That(player.DecoderThreadCount, Is.EqualTo(0));
            Assert.That(player.FrameRate, Is.EqualTo(0));
            Assert.That(player.BufferedFrames, Is.EqualTo(0));

            var pending = player.PreloadAsync(_clip);
            player.Url = FFmpegTestMedia.PathIn(_directory, "other.mkv");
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>Checks that canceling the preparation token closes the pending session on the next update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PrepareAsync_TokenCancellation_Closes()
        {
            var player = CreatePlayer();
            using var source = new CancellationTokenSource();
            player.Url = _clip;
            var task = player.PrepareAsync(source.Token);

            source.Cancel();
            yield return WaitFor(task);

            Assert.That(task.IsCanceled, Is.True);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>Checks that Play before preparation prepares the media and then starts playback.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_BeforePreparation_StartsAfterFirstFrame()
        {
            var player = CreatePlayer();
            player.Url = "file:///" + _clip.Replace('\\', '/');
            player.PlaybackRate = 0.25f;

            player.Play();
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Preparing));
            yield return PlayModeWait.Until(() => player.IsPlaying);

            Assert.That(player.IsPlaying, Is.True);
            Assert.That(player.IsPrepared, Is.True);
        }

        /// <summary>Checks that disabling a playing component pauses it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Disable_PausesPlayback()
        {
            var player = CreatePlayer();
            player.PlaybackRate = 0.25f;
            player.Play(_clip);
            yield return PlayModeWait.Until(() => player.IsPlaying);

            player.enabled = false;

            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Paused));
        }

        /// <summary>Checks that PlayOnAwake starts playback from Start.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayOnAwake_StartsFromStart()
        {
            var host = _objects.Create("AutoPlay");
            host.SetActive(false);
            var player = host.AddComponent<FFmpegVideoPlayer>();
            player.PreferredDecoderType = VideoDecoderType.Software;
            player.PlayOnAwake = true;
            player.PlaybackRate = 0.25f;
            player.Url = _clip;

            host.SetActive(true);
            yield return PlayModeWait.Until(() => player.IsPlaying);

            Assert.That(player.IsPlaying, Is.True);
        }

        /// <summary>Checks that destroying the component closes it and cancels pending preparation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_CancelsPreparation()
        {
            var player = CreatePlayer();
            var task = player.PreloadAsync(_clip);

            Object.Destroy(player.gameObject);
            yield return null;

            Assert.That(task.IsCanceled, Is.True);
        }

        /// <summary>Checks that frames are copied into a caller-owned render texture and published to a renderer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Outputs_TargetTextureAndRenderer()
        {
            var player = CreatePlayer();
            var target = _objects.Track(new RenderTexture(FFmpegTestMedia.DefaultWidth, FFmpegTestMedia.DefaultHeight, 0));
            var renderer = player.gameObject.AddComponent<MeshRenderer>();
            PrivateMember.SetField(player, "_targetRenderer", renderer);
            player.TargetTexture = target;

            yield return WaitFor(player.PreloadAsync(_clip));

            Assert.That(player.Texture, Is.SameAs(target));
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetTexture("_MainTex"), Is.SameAs(target));
        }

        /// <summary>Checks that the player rejects control calls from other threads.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Controls_FromAnotherThread_AreRejected()
        {
            var player = CreatePlayer();
            var task = Task.Run(() => player.Pause());
            yield return WaitFor(task);

            Assert.That(task.Exception!.InnerException, Is.InstanceOf<InvalidOperationException>());
        }

        /// <summary>Checks that an unreadable input faults preparation and reports the error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MissingInput_ReportsError()
        {
            PlayModeWait.ExpectErrorLogs();
            var player = CreatePlayer();
            string? message = null;
            player.ErrorReceived += (_, error) => message = error;

            var task = player.PreloadAsync(FFmpegTestMedia.PathIn(_directory, "missing.mkv"));
            yield return WaitFor(task);
            yield return PlayModeWait.FlushLogs();

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception, Is.Not.Null);
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Error));
            Assert.That(player.LastError, Is.Not.Null);
            Assert.That(message, Is.EqualTo(player.LastError));
        }

        /// <summary>Checks the software fallback when native texture sharing is preferred for a codec without hardware support.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HardwarePreference_NativeTextures_FallsBackToSoftware()
        {
            return HardwarePreferenceFallsBackToSoftware(true);
        }

        /// <summary>Checks the software fallback when hardware decoding with CPU upload is requested for a codec without hardware support.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HardwarePreference_CpuUpload_FallsBackToSoftware()
        {
            return HardwarePreferenceFallsBackToSoftware(false);
        }

        /// <summary>Prepares the MPEG-4 clip with hardware preference and checks the reported software fallback.</summary>
        /// <param name="nativeTextures">Whether native texture sharing is preferred.</param>
        /// <returns>The test coroutine.</returns>
        private IEnumerator HardwarePreferenceFallsBackToSoftware(bool nativeTextures)
        {
            var player = _objects.Add<FFmpegVideoPlayer>();
            player.PreferredDecoderType = VideoDecoderType.Hardware;
            player.PreferNativeTextures = nativeTextures;

            var task = player.PreloadAsync(_clip);
            yield return WaitFor(task);

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(player.DecoderType, Is.EqualTo(VideoDecoderType.Software));
            Assert.That(player.HardwareFallbackReason, Is.Not.Null.And.Not.Empty);
            Assert.That(player.TransferMode, Is.EqualTo("Software RGBA upload"));
        }

        /// <summary>Checks that strict GPU decoding fails for a codec without a native hardware path.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RequireHardware_UnsupportedCodec_Fails()
        {
            PlayModeWait.ExpectErrorLogs();
            var player = _objects.Add<FFmpegVideoPlayer>();
            player.RequireHardwareDecoding = true;

            var task = player.PreloadAsync(_clip);
            yield return WaitFor(task);
            yield return PlayModeWait.FlushLogs();

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception!.InnerException, Is.InstanceOf<NotSupportedException>());
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Error));
            Assert.That(player.LastError, Is.Not.Empty);
        }
    }
}
