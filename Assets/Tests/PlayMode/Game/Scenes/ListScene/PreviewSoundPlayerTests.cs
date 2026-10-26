using System;
using System.Collections;
using System.Threading;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the song preview player: demo range commands, seeking, supersession and cancellation.</summary>
    public class PreviewSoundPlayerTests : ListSceneTestBase
    {
        /// <summary>The player under test.</summary>
        private PreviewSoundPlayer _player = null!;

        /// <summary>Cancels every preview started by the test.</summary>
        private CancellationTokenSource _source = null!;

        /// <summary>Creates the player and the cancellation source.</summary>
        [SetUp]
        public void SetUp()
        {
            _player = Objects.Add<PreviewSoundPlayer>("Preview", Canvas);
            _source = new CancellationTokenSource();
            var source = _source;
            AddCleanup(() =>
            {
                source.Cancel();
                source.Dispose();
            });
        }

        /// <summary>Creates a song with a preview track and maidata commands.</summary>
        /// <param name="hash">The song hash.</param>
        /// <param name="sample">The preview track.</param>
        /// <param name="commands">Pairs of command prefix and value.</param>
        /// <returns>The song.</returns>
        private static FakeSongDetail Song(string hash, FakeAudioSample sample, params (string Prefix, string Value)[] commands)
        {
            var simaiCommands = new SimaiCommand[commands.Length];
            for (var i = 0; i < commands.Length; i++)
            {
                simaiCommands[i] = new SimaiCommand(commands[i].Prefix, commands[i].Value);
            }
            return new FakeSongDetail(hash, "Preview", "1")
            {
                PreviewAudio = sample,
                Maidata = Maidata(ChartLevel.Easy, SimaiChart.Empty, simaiCommands)
            };
        }

        /// <summary>Starts a preview and waits until its track plays.</summary>
        /// <param name="song">The song.</param>
        /// <param name="sample">The preview track.</param>
        /// <returns>The coroutine.</returns>
        private IEnumerator PlayAndWait(FakeSongDetail song, FakeAudioSample sample)
        {
            _player.PlayPreviewSound(song, 0, _source.Token);
            yield return WaitUntil(() => sample.PlayCount > 0, 5f);
        }

        /// <summary>Checks that the demo range is applied and the track loops at full speed with the BGM volume.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AppliesDemoRange()
        {
            MajEnv.Settings.Audio.Volume.BGM = 0.6f;
            var sample = new FakeAudioSample("preview", TimeSpan.FromSeconds(60));
            var song = Song("majtest-preview-range", sample, ("demo_seek", "5"), ("demo_len", "10"));

            yield return PlayAndWait(song, sample);

            Assert.That(sample.PlayCount, Is.EqualTo(1));
            Assert.That(sample.CurrentSec, Is.EqualTo(5d).Within(1e-4));
            Assert.That(sample.IsLoop, Is.True);
            Assert.That(sample.Speed, Is.EqualTo(1f));
            Assert.That(sample.LastSetVolume, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(_player.IsPreviewPending(song), Is.True);
            Assert.That(_player.IsPreviewPlaying(song), Is.True);
            Assert.That(Sfx("bgm_select.mp3").LastSetVolume, Is.EqualTo(0.6f).Within(1e-4f));
        }

        /// <summary>Checks that invalid, negative and repeated demo commands are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_IgnoresInvalidAndRepeatedCommands()
        {
            var sample = new FakeAudioSample("preview", TimeSpan.FromSeconds(60));
            var song = Song(
                "majtest-preview-invalid",
                sample,
                ("demo_seek", "abc"),
                ("demo_seek", "-1"),
                ("demo_seek", "3"),
                ("demo_seek", "4"),
                ("demo_len", "x"),
                ("demo_len", "0"),
                ("demo_len", "2"),
                ("demo_len", "9"),
                ("title", "ignored"));

            yield return PlayAndWait(song, sample);

            Assert.That(sample.CurrentSec, Is.EqualTo(3d).Within(1e-4));
        }

        /// <summary>Checks that an offset beyond the track restarts from the beginning.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_OffsetBeyondTrackStartsAtZero()
        {
            var sample = new FakeAudioSample("preview", TimeSpan.FromSeconds(10)) { CurrentSec = 7 };
            var song = Song("majtest-preview-beyond", sample, ("demo_seek", "30"), ("demo_len", "100"));

            yield return PlayAndWait(song, sample);

            Assert.That(sample.CurrentSec, Is.EqualTo(0d));
        }

        /// <summary>Checks that a track that cannot seek is stopped instead of positioned.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_NonSeekableTrackIsStopped()
        {
            var sample = new FakeAudioSample("preview", TimeSpan.FromSeconds(10), canSeek: false);
            var song = Song("majtest-preview-noseek", sample, ("demo_seek", "2"));

            yield return PlayAndWait(song, sample);

            Assert.That(sample.StopCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(sample.CurrentSec, Is.EqualTo(0d));
            Assert.That(sample.PlayCount, Is.EqualTo(1));
        }

        /// <summary>Checks that cancelling pauses the track, stops looping and clears the preview state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancel_PausesAndClearsState()
        {
            var sample = new FakeAudioSample("preview", TimeSpan.FromSeconds(60));
            var song = Song("majtest-preview-cancel", sample);
            yield return PlayAndWait(song, sample);

            _source.Cancel();
            yield return WaitUntil(() => !_player.IsPreviewPending(song), 5f);

            Assert.That(_player.IsPreviewPlaying(song), Is.False);
            Assert.That(sample.PauseCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(sample.IsLoop, Is.False);
        }

        /// <summary>Checks that an empty preview track ends the preview without playing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_EmptyTrackEndsPreview()
        {
            var sample = new FakeAudioSample("preview") { Empty = true };
            var song = Song("majtest-preview-empty", sample);

            _player.PlayPreviewSound(song, 0, _source.Token);
            yield return WaitUntil(() => !_player.IsPreviewPending(song), 5f);

            Assert.That(_player.IsPreviewPending(song), Is.False);
            Assert.That(sample.PlayCount, Is.EqualTo(0));
        }

        /// <summary>Checks that a newer preview supersedes a pending one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_NewerSongSupersedesPendingOne()
        {
            var firstSample = new FakeAudioSample("first", TimeSpan.FromSeconds(60));
            var secondSample = new FakeAudioSample("second", TimeSpan.FromSeconds(60));
            var first = Song("majtest-preview-first", firstSample);
            var second = Song("majtest-preview-second", secondSample);

            _player.PlayPreviewSound(first, 10000, _source.Token);
            yield return PlayAndWait(second, secondSample);

            Assert.That(_player.IsPreviewPending(first), Is.False);
            Assert.That(_player.IsPreviewPlaying(second), Is.True);
            Assert.That(firstSample.PlayCount, Is.EqualTo(0));
        }

        /// <summary>Checks that destroying the player restores the list music volume.</summary>
        [Test]
        public void Destroy_RestoresListMusicVolume()
        {
            MajEnv.Settings.Audio.Volume.BGM = 0.3f;

            UnityEngine.Object.DestroyImmediate(_player.gameObject);

            Assert.That(Sfx("bgm_select.mp3").LastSetVolume, Is.EqualTo(0.3f).Within(1e-4f));
        }
    }
}
