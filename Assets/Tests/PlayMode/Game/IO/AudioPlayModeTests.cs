using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.IO.PlayMode
{
    /// <summary>
    /// Verifies the Unity audio backend sample <see cref="UnityAudioSample"/> and the Play Mode lifecycle of
    /// <see cref="AudioManager"/> with the Unity backend, which never initializes BASS.
    /// </summary>
    public class AudioPlayModeTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The settings before the test.</summary>
        private GameSetting _savedSettings = null!;

        /// <summary>The installed default settings.</summary>
        private GameSetting _settings = null!;

        /// <summary>Installs default settings and the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _savedSettings = MajEnv.Settings;
            _settings = new GameSetting();
            _settings.Audio.Volume.Global = 0.5f;
            _settings.Audio.Backend = SoundBackendOption.Unity;
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), _settings);
        }

        /// <summary>Destroys the objects and restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            if (MajdataPlay.Majdata<AudioManager>.IsSingleton)
            {
                MajdataPlay.Majdata<AudioManager>.Free();
            }
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), _savedSettings);
        }

        /// <summary>Checks the clip backed sample properties and transport controls.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UnitySample_ControlsItsAudioSource()
        {
            var host = _objects.Create("AudioHost");
            var clip = _objects.Track(AudioClip.Create("Tone", 44100, 1, 44100, false));
            var sample = new UnityAudioSample(clip, host);
            var source = host.GetComponent<AudioSource>();

            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(clip));
            Assert.That(sample.IsEmpty, Is.False);
            Assert.That(sample.CanSeek, Is.False);
            Assert.That(sample.Length.TotalSeconds, Is.EqualTo(clip.length).Within(0.01));

            sample.IsLoop = true;
            Assert.That(sample.IsLoop, Is.True);
            sample.Volume = 0.8f;
            Assert.That(sample.Volume, Is.EqualTo(0.4f).Within(1e-4f));
            sample.SetVolume(4f);
            Assert.That(sample.Volume, Is.EqualTo(0.5f).Within(1e-4f));
            sample.Speed = 1.5f;
            Assert.That(sample.Speed, Is.EqualTo(1.5f));
            sample.CurrentSec = 0.5;
            Assert.Throws<NotSupportedException>(() => _ = sample.CurrentSec);

            sample.Play();
            yield return null;
            Assert.That(sample.IsPlaying, Is.EqualTo(source.isPlaying));
            sample.Pause();
            Assert.That(sample.IsPlaying, Is.False);
            sample.PlayOneShot();
            sample.Stop();
            Assert.That(sample.IsPlaying, Is.False);
            Assert.That(source.time, Is.EqualTo(0f));

            sample.Dispose();
            sample.Dispose();
            Assert.Throws<ObjectDisposedException>(() => sample.Play());
            Assert.Throws<ObjectDisposedException>(() => _ = sample.IsPlaying);
            Assert.Throws<ObjectDisposedException>(() => sample.Volume = 1f);
            yield return null;
            yield return null;
            Assert.That(source == null, Is.True, "disposal destroys the audio source on the main thread");
        }

        /// <summary>Checks that asynchronous disposal releases the clip and source immediately.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UnitySample_DisposeAsync_ReleasesTheSource()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var host = _objects.Create("AsyncAudioHost");
                var clip = AudioClip.Create("AsyncTone", 4410, 1, 44100, false);
                var sample = new UnityAudioSample(clip, host);

                await sample.DisposeAsync();
                await sample.DisposeAsync();
                await UniTask.Yield();

                Assert.That(host.GetComponent<AudioSource>() == null, Is.True);
                Assert.That(clip == null, Is.True);
                Assert.Throws<ObjectDisposedException>(() => sample.Stop());
            });
        }

        /// <summary>Checks that initialization with the Unity backend registers the manager and tolerates empty sound folders.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AudioManager_InitWithUnityBackend()
        {
            var temporary = new TemporaryDirectory();
            var savedAssets = MajEnv.AssetsDirectory;
            try
            {
                temporary.Directory.CreateDirectory("SFX");
                PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.AssetsDirectory), temporary.Directory);
                PrivateMember.SetStaticField(typeof(AudioManager), "_isInited", false);
                var root = _objects.Create("AudioManager");
                root.SetActive(false);
                var manager = root.AddComponent<AudioManager>();
                manager.PlayDebug = true;
                root.SetActive(true);

                manager.Init();
                manager.Init();
                yield return null;

                Assert.That(MajdataPlay.Majdata<AudioManager>.Instance, Is.SameAs(manager));
                Assert.That(PrivateMember.GetStaticField<bool>(typeof(AudioManager), "_isInited"), Is.True);
                Assert.That(manager.GetSFX("answer.wav"), Is.SameAs(AudioSampleWrap.Empty));
                Assert.That(manager.LoadMusic(Path.Combine(temporary.Location, "missing.wav")), Is.SameAs(AudioSampleWrap.Empty));
            }
            finally
            {
                InputManager.ClearAllSubscriber();
                PrivateMember.SetStaticField(typeof(AudioManager), "_isInited", false);
                PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.AssetsDirectory), savedAssets);
                temporary.Dispose();
            }
        }
    }
}
