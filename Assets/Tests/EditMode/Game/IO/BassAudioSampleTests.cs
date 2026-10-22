using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using ManagedBass;
using ManagedBass.Mix;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the BASS backed samples <see cref="BassSimpleAudioSample"/> and <see cref="BassAudioSample"/> on the silent
    /// BASS device, so no audio hardware is opened.
    /// </summary>
    public class BassAudioSampleTests
    {
        /// <summary>The temporary directory holding generated WAV files.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The decoding mixer standing in for the global output mixer.</summary>
        private int _mixer;

        /// <summary>The mixing matrix before the test.</summary>
        private float[,] _savedMatrix = new float[0, 0];

        /// <summary>Initializes BASS on the silent device once for the fixture.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            BassTestSupport.InitNoSoundDevice();
        }

        /// <summary>Frees BASS after the fixture.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            BassTestSupport.FreeNoSoundDevice();
        }

        /// <summary>Creates the temporary directory, settings and the decoding mixer.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _settings = new SettingsScope();
            _settings.Settings.Audio.Volume.Global = 0.5f;
            _savedMatrix = AudioManager.MixingMatrix;
            PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), new float[,] { { 1f, 0f }, { 0f, 1f } });
            _mixer = BassMix.CreateMixerStream(BassTestSupport.SampleRate, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
            Assert.That(_mixer, Is.Not.Zero, Bass.LastError.ToString());
        }

        /// <summary>Frees the mixer and restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            Bass.StreamFree(_mixer);
            PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), _savedMatrix);
            _settings.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Checks the length, seeking and looping of a simple sample.</summary>
        [Test]
        public void Simple_ReportsLengthSeekAndLoop()
        {
            using var sample = BassSimpleAudioSample.Create(WriteWave("simple.wav", 22050, 0.5f), normalize: false);

            Assert.That(sample.IsEmpty, Is.False);
            Assert.That(sample.CanSeek, Is.True);
            Assert.That(sample.Length.TotalSeconds, Is.EqualTo(0.5).Within(0.01));
            Assert.That(sample.IsPlaying, Is.False);

            sample.CurrentSec = 0.25;
            Assert.That(sample.CurrentSec, Is.EqualTo(0.25).Within(0.01));

            sample.IsLoop = true;
            Assert.That(sample.IsLoop, Is.True);
            sample.IsLoop = false;
            Assert.That(sample.IsLoop, Is.False);
        }

        /// <summary>Checks the transport controls of a simple sample on the silent device.</summary>
        [Test]
        public void Simple_PlayPauseStop()
        {
            using var sample = BassSimpleAudioSample.Create(WriteWave("transport.wav", 44100, 0.5f), normalize: false);

            sample.Play();
            Assert.That(sample.IsPlaying, Is.True);
            sample.Pause();
            Assert.That(sample.IsPlaying, Is.False);
            sample.CurrentSec = 0.5;
            sample.PlayOneShot();
            Assert.That(sample.IsPlaying, Is.True);
            sample.Stop();
            Assert.That(sample.IsPlaying, Is.False);
            Assert.That(sample.CurrentSec, Is.EqualTo(0).Within(0.01));
        }

        /// <summary>Checks that volume is clamped and scaled by the gain and the global volume.</summary>
        [Test]
        public void Simple_VolumeUsesGainAndGlobalVolume()
        {
            using var plain = BassSimpleAudioSample.Create(WriteWave("plain.wav", 4410, 0.5f), normalize: false);
            using var normalized = BassSimpleAudioSample.Create(WriteWave("normalized.wav", 4410, 0.5f), normalize: true);

            plain.Volume = 0.8f;
            Assert.That(plain.Volume, Is.EqualTo(0.4f).Within(1e-4f));
            plain.SetVolume(5f);
            Assert.That(plain.Volume, Is.EqualTo(1f).Within(1e-4f), "clamped to two, then halved");
            normalized.SetVolume(1f);
            Assert.That(normalized.Volume, Is.EqualTo(1f).Within(0.05f), "a half amplitude source doubles its gain");
        }

        /// <summary>Checks that speed changes only apply to samples created for them.</summary>
        [Test]
        public void Simple_SpeedChangeNeedsTempoSupport()
        {
            using var fixedSpeed = BassSimpleAudioSample.Create(WriteWave("fixed.wav", 4410, 0.5f), false, false);
            using var variable = BassSimpleAudioSample.Create(WriteWave("variable.wav", 4410, 0.5f), false, true);

            fixedSpeed.Speed = 2f;
            variable.Speed = 1.5f;

            Assert.That(fixedSpeed.Speed, Is.EqualTo(1f));
            Assert.That(variable.Speed, Is.EqualTo(1.5f).Within(1e-3f));
        }

        /// <summary>Checks that a disposed simple sample rejects further use and tolerates repeated disposal.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Simple_DisposedSample_ThrowsAndDisposesOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var sample = await BassSimpleAudioSample.CreateAsync(WriteWave("async.wav", 4410, 0.5f), normalize: false);

                sample.Dispose();
                await sample.DisposeAsync();

                Assert.Throws<ObjectDisposedException>(() => _ = sample.IsPlaying);
                Assert.Throws<ObjectDisposedException>(() => sample.Play());
                Assert.Throws<ObjectDisposedException>(() => sample.Pause());
                Assert.Throws<ObjectDisposedException>(() => sample.Stop());
                Assert.Throws<ObjectDisposedException>(() => sample.PlayOneShot());
                Assert.Throws<ObjectDisposedException>(() => _ = sample.Volume);
                Assert.Throws<ObjectDisposedException>(() => sample.Speed = 1f);
                Assert.Throws<ObjectDisposedException>(() => _ = sample.Speed);
                Assert.Throws<ObjectDisposedException>(() => sample.IsLoop = true);
                Assert.Throws<ObjectDisposedException>(() => _ = sample.IsLoop);
                Assert.Throws<ObjectDisposedException>(() => _ = sample.CurrentSec);
                Assert.Throws<ObjectDisposedException>(() => sample.CurrentSec = 1);
                Assert.Throws<ObjectDisposedException>(() => sample.Volume = 1);
            });
        }

        /// <summary>Checks that an undecodable file and an invalid handle are rejected.</summary>
        [Test]
        public void Simple_InvalidInput_Throws()
        {
            var garbage = _temporary.Directory.CreateFile("garbage.wav");
            garbage.WriteAllBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

            Assert.That(() => BassSimpleAudioSample.Create(garbage.Location),
                Throws.InstanceOf<ArgumentOutOfRangeException>().Or.InstanceOf<BassException>());
            Assert.Throws<ArgumentException>(() => new BassSimpleAudioSample(0, 1));
        }

        /// <summary>Checks that a simple sample can wrap a stream created elsewhere.</summary>
        [Test]
        public void Simple_PublicConstructor_WrapsAStream()
        {
            var wav = BassTestSupport.CreateSquareWave(2, 22050, 0.5f);
            var stream = Bass.CreateStream(wav, 0, wav.Length, BassFlags.Default);

            using var sample = new BassSimpleAudioSample(stream, 1);

            Assert.That(sample.Length.TotalSeconds, Is.EqualTo(0.5).Within(0.01));
            Assert.That(sample.CanSeek, Is.False);
            Assert.Throws<NotSupportedException>(() => _ = sample.CurrentSec);
        }

        /// <summary>Checks the length, seeking, looping and volume of a mixer sample.</summary>
        [Test]
        public void Mixer_ReportsLengthSeekLoopAndVolume()
        {
            using var sample = BassAudioSample.Create(WriteWave("mixer.wav", 22050, 0.5f), _mixer, normalize: false);

            Assert.That(sample.IsEmpty, Is.False);
            Assert.That(sample.Length.TotalSeconds, Is.EqualTo(0.5).Within(0.01));
            sample.CurrentSec = 0.2;
            Assert.That(sample.CurrentSec, Is.EqualTo(0.2).Within(0.02));
            sample.IsLoop = true;
            sample.IsLoop = true;
            Assert.That(sample.IsLoop, Is.True);
            sample.IsLoop = false;
            sample.IsLoop = false;
            Assert.That(sample.IsLoop, Is.False);
            sample.SetVolume(0.6f);
            Assert.That(sample.Volume, Is.EqualTo(0.3f).Within(1e-3f));
        }

        /// <summary>Checks that the mixer pause flag drives the playing state of a mixer sample.</summary>
        [Test]
        public void Mixer_PlayPauseStop()
        {
            using var sample = BassAudioSample.Create(WriteWave("mixer-transport.wav", 22050, 0.5f), _mixer, normalize: true);

            var decode = PrivateMember.GetField<int>(sample, "_decode");
            Assert.That(sample.IsPlaying, Is.False);
            sample.Play();
            Assert.That(BassMix.ChannelHasFlag(decode, BassFlags.MixerChanPause), Is.False);
            sample.Pause();
            Assert.That(BassMix.ChannelHasFlag(decode, BassFlags.MixerChanPause), Is.True);
            Assert.That(sample.IsPlaying, Is.False);
            sample.CurrentSec = 0.2;
            sample.PlayOneShot();
            Assert.That(BassMix.ChannelHasFlag(decode, BassFlags.MixerChanPause), Is.False);
            Assert.That(sample.CurrentSec, Is.EqualTo(0).Within(0.02));
            sample.Stop();
            Assert.That(BassMix.ChannelHasFlag(decode, BassFlags.MixerChanPause), Is.True);
            Assert.That(sample.IsPlaying, Is.False);
        }

        /// <summary>Checks the speed control and disposal of mixer samples.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mixer_SpeedAndDisposal()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var variable = await BassAudioSample.CreateAsync(WriteWave("mixer-speed.wav", 4410, 0.5f), _mixer, false, true);
                using var fixedSpeed = BassAudioSample.Create(WriteWave("mixer-fixed.wav", 4410, 0.5f), _mixer, false, false);

                variable.Speed = 0.75f;
                fixedSpeed.Speed = 3f;
                Assert.That(variable.Speed, Is.EqualTo(0.75f).Within(1e-3f));
                Assert.That(fixedSpeed.Speed, Is.EqualTo(1f));

                await variable.DisposeAsync();
                variable.Dispose();
                Assert.Throws<ObjectDisposedException>(() => _ = variable.IsPlaying);
                Assert.Throws<ObjectDisposedException>(() => variable.Play());
                Assert.Throws<ObjectDisposedException>(() => _ = variable.Speed);
            });
        }

        /// <summary>Checks that a mixer sample rejects missing streams and mixers.</summary>
        [Test]
        public void Mixer_InvalidHandles_Throw()
        {
            Assert.Throws<ArgumentException>(() => new BassAudioSample(0, _mixer, 1));
            Assert.Throws<ArgumentException>(() => new BassAudioSample(1, 0, 1));
        }

        /// <summary>Checks that the empty sample is inert.</summary>
        [Test]
        public void EmptySample_IsInert()
        {
            var empty = AudioSampleWrap.Empty;

            empty.Play();
            empty.Pause();
            empty.Stop();
            empty.PlayOneShot();
            empty.SetVolume(0.1f);
            empty.Volume = 0.2f;
            empty.Speed = 2f;
            empty.CurrentSec = 3;
            empty.IsLoop = true;
            empty.Dispose();

            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.IsPlaying, Is.True);
            Assert.That(empty.Volume, Is.EqualTo(1f));
            Assert.That(empty.Speed, Is.EqualTo(1f));
            Assert.That(empty.CurrentSec, Is.EqualTo(0));
            Assert.That(empty.IsLoop, Is.False);
            Assert.That(empty.Length, Is.EqualTo(TimeSpan.Zero));
            Assert.That(empty.CanSeek, Is.True);
            Assert.That(empty.DisposeAsync().IsCompletedSuccessfully, Is.True);
        }

        /// <summary>Writes a square wave WAV file into the temporary directory.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="frames">The number of stereo frames.</param>
        /// <param name="amplitude">The peak amplitude.</param>
        /// <returns>The file location.</returns>
        private string WriteWave(string name, int frames, float amplitude)
        {
            var file = _temporary.Directory.CreateFile(name);
            file.WriteAllBytes(BassTestSupport.CreateSquareWave(2, frames, amplitude));
            return file.Location;
        }
    }
}
