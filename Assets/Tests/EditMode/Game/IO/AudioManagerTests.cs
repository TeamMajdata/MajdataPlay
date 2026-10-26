using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Verifies the sound effect registry, sample loading, mixing matrix and recorder factory of <see cref="AudioManager"/>
    /// without initializing an audio output; BASS runs on its silent device.
    /// </summary>
    public class AudioManagerTests
    {
        /// <summary>The value of the global mixer field before any mixer is created.</summary>
        private const int NoMixer = -114514;

        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The temporary directory for generated audio files.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The manager under test.</summary>
        private AudioManager _manager = null!;

        /// <summary>The manager's sound effect list.</summary>
        private List<AudioSampleWrap> _samples = null!;

        /// <summary>The mixing matrix before the test.</summary>
        private float[,] _savedMatrix = new float[0, 0];

        /// <summary>A decoding mixer standing in for the global mixer.</summary>
        private int _mixer;

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

        /// <summary>Creates the manager without running its lifecycle.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new SettingsScope();
            _temporary = new TemporaryDirectory();
            _savedMatrix = AudioManager.MixingMatrix;
            _manager = _objects.Add<AudioManager>();
            _samples = PrivateMember.GetField<List<AudioSampleWrap>>(_manager, "SFXSamples");
            _mixer = BassMix.CreateMixerStream(BassTestSupport.SampleRate, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        }

        /// <summary>Restores the static audio state and destroys the manager.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(AudioManager), "BassGlobalMixer", NoMixer);
            PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), _savedMatrix);
            Bass.StreamFree(_mixer);
            if (ReferenceEquals(MajdataPlay.Majdata<AudioManager>.Instance, _manager))
            {
                MajdataPlay.Majdata<AudioManager>.Free();
            }
            _objects.Dispose();
            _temporary.Dispose();
            _settings.Dispose();
        }

#if UNITY_STANDALONE
        /// <summary>Checks the stereo routing generated for every supported speaker layout.</summary>
        /// <param name="channels">The output channel count.</param>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(8)]
        public void MixingMatrix_RoutesStereoToEachLayout(int channels)
        {
            var volumes = _settings.Settings.Audio.Channel;
            volumes.FrontVolume = 0.9f;
            volumes.CenterAndLFEVolume = 0.8f;
            volumes.SideVolume = 0.7f;
            volumes.RearVolume = 0.6f;

            PrivateMember.InvokeStatic(typeof(AudioManager), "GenerateMixingMatrix", channels);
            var matrix = AudioManager.MixingMatrix;

            var expected = channels switch
            {
                2 => new float[,] { { 0.9f, 0f }, { 0f, 0.9f } },
                3 => new float[,] { { 0.9f, 0f }, { 0.4f, 0.4f }, { 0f, 0.9f } },
                4 => new float[,] { { 0.9f, 0f }, { 0f, 0.9f }, { 0.6f, 0f }, { 0f, 0.6f } },
                6 => new float[,] { { 0.9f, 0f }, { 0f, 0.9f }, { 0.8f, 0f }, { 0f, 0.8f }, { 0.7f, 0f }, { 0f, 0.7f } },
                8 => new float[,] { { 0.9f, 0f }, { 0f, 0.9f }, { 0.8f, 0f }, { 0f, 0.8f }, { 0.7f, 0f }, { 0f, 0.7f }, { 0.6f, 0f }, { 0f, 0.6f } },
                _ => new float[,] { { 0.5f, 0.5f } },
            };
            Assert.That(matrix, Is.EqualTo(expected));
        }
#endif

        /// <summary>Checks that forced mono sends both inputs equally to every speaker.</summary>
        /// <param name="channels">The output channel count.</param>
        /// <param name="rows">The expected number of matrix rows.</param>
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(5, 1)]
        [TestCase(6, 6)]
        [TestCase(8, 8)]
        public void MixingMatrix_ForceMono_UsesEqualWeights(int channels, int rows)
        {
            _settings.Settings.Audio.ForceMono = true;

            PrivateMember.InvokeStatic(typeof(AudioManager), "GenerateMixingMatrix", channels);
            var matrix = AudioManager.MixingMatrix;

            Assert.That(matrix.GetLength(0), Is.EqualTo(rows));
            Assert.That(matrix.GetLength(1), Is.EqualTo(2));
            foreach (var value in matrix)
            {
                Assert.That(value, Is.EqualTo(0.5f));
            }
        }

        /// <summary>Checks that playing a sound effect restarts it and applies the loop flag.</summary>
        [Test]
        public void PlaySFX_PlaysTheNamedSample()
        {
            var tap = AddSample("tap.wav", SFXSampleType.Tap);

            var played = _manager.PlaySFX("tap.wav", true);

            Assert.That(played, Is.SameAs(tap));
            Assert.That(tap.OneShots, Is.EqualTo(1));
            Assert.That(tap.IsLoop, Is.True);
            Assert.That(_manager.GetSFX("tap.wav"), Is.SameAs(tap));
        }

        /// <summary>Checks that a voice stops every other voice before playing.</summary>
        [Test]
        public void PlaySFX_VoiceStopsOtherVoices()
        {
            var first = AddSample("voice_a.wav", SFXSampleType.Voice);
            var second = AddSample("voice_b.wav", SFXSampleType.Voice);
            var tap = AddSample("tap.wav", SFXSampleType.Tap);

            _manager.PlaySFX("voice_b.wav");

            Assert.That(first.Stops, Is.EqualTo(1));
            Assert.That(second.Stops, Is.EqualTo(1));
            Assert.That(second.OneShots, Is.EqualTo(1));
            Assert.That(tap.Stops, Is.EqualTo(0));
        }

        /// <summary>Checks that unknown names return the empty sample and stopping them is harmless.</summary>
        [Test]
        public void UnknownSFX_ReturnsEmpty()
        {
            AddSample("tap.wav", SFXSampleType.Tap);

            Assert.That(_manager.PlaySFX("missing.wav"), Is.SameAs(AudioSampleWrap.Empty));
            Assert.That(_manager.GetSFX("missing.wav"), Is.SameAs(AudioSampleWrap.Empty));
            Assert.DoesNotThrow(() => _manager.StopSFX("missing.wav"));
        }

        /// <summary>Checks that stopping a named sound effect stops it.</summary>
        [Test]
        public void StopSFX_StopsTheNamedSample()
        {
            var bgm = AddSample("bgm.wav", SFXSampleType.BGM);

            _manager.StopSFX("bgm.wav");

            Assert.That(bgm.Stops, Is.EqualTo(1));
        }

        /// <summary>Checks that every sound effect group receives its configured volume and empty samples are skipped.</summary>
        [Test]
        public void ReadVolumeFromSettings_AppliesGroupVolumes()
        {
            var volume = _settings.Settings.Audio.Volume;
            volume.Answer = 0.11f;
            volume.Tap = 0.12f;
            volume.Ex = 0.13f;
            volume.Break = 0.14f;
            volume.Touch = 0.15f;
            volume.Hanabi = 0.16f;
            volume.BGM = 0.17f;
            volume.Slide = 0.18f;
            volume.Voice = 0.19f;
            var types = new[]
            {
                SFXSampleType.Answer, SFXSampleType.Tap, SFXSampleType.Ex, SFXSampleType.Break, SFXSampleType.Touch,
                SFXSampleType.Hanabi, SFXSampleType.BGM, SFXSampleType.Slide, SFXSampleType.Voice, (SFXSampleType)99,
            };
            var samples = new List<FakeAudioSample>();
            foreach (var type in types)
            {
                samples.Add(AddSample(type.ToString(), type));
            }
            var empty = AddSample("empty", SFXSampleType.Tap, true);
            _samples.Add(null!);

            _manager.ReadVolumeFromSettings();

            var expected = new[] { 0.11f, 0.12f, 0.13f, 0.14f, 0.15f, 0.16f, 0.17f, 0.18f, 0.19f, 1f };
            for (var i = 0; i < samples.Count; i++)
            {
                Assert.That(samples[i].LastSetVolume, Is.EqualTo(expected[i]), types[i].ToString());
            }
            Assert.That(empty.LastSetVolume, Is.EqualTo(-1f));
        }

        /// <summary>Checks that the debug input handler plays the answer sound for buttons and the touch sound for sensors.</summary>
        [Test]
        public void OnAnyAreaDown_PlaysFeedbackForPresses()
        {
            var answer = AddSample("answer.wav", SFXSampleType.Answer);
            var touch = AddSample("touch.wav", SFXSampleType.Touch);

            Invoke("OnAnyAreaDown", new InputEventArgs { IsButton = true, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On });
            Invoke("OnAnyAreaDown", new InputEventArgs { IsButton = false, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On });
            Invoke("OnAnyAreaDown", new InputEventArgs { IsButton = true, OldStatus = SwitchStatus.On, Status = SwitchStatus.Off });

            Assert.That(answer.OneShots, Is.EqualTo(1));
            Assert.That(touch.OneShots, Is.EqualTo(1));
        }

        /// <summary>Checks that loading a missing file or using an unknown backend returns the empty sample.</summary>
        [Test]
        public void LoadMusic_MissingFileOrUnknownBackend_ReturnsEmpty()
        {
            var wav = WriteWave("track.wav");

            Assert.That(_manager.LoadMusic(_temporary.Location + "/missing.wav"), Is.SameAs(AudioSampleWrap.Empty));
            _settings.Settings.Audio.Backend = (SoundBackendOption)99;
            Assert.That(_manager.LoadMusic(wav), Is.SameAs(AudioSampleWrap.Empty));
            Assert.That(_manager.LoadMusicFromUri(new Uri("https://example.invalid/a.wav")), Is.SameAs(AudioSampleWrap.Empty));
        }

        /// <summary>Checks that the BASS backends create the matching sample types.</summary>
        [Test]
        public void LoadMusic_BassBackends_CreateMatchingSamples()
        {
            var wav = WriteWave("track.wav");
            PrivateMember.SetStaticField(typeof(AudioManager), "BassGlobalMixer", _mixer);
            PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), new float[,] { { 1f, 0f }, { 0f, 1f } });

            _settings.Settings.Audio.Backend = SoundBackendOption.BassSimple;
            using var simple = _manager.LoadMusic(wav, false);
            _settings.Settings.Audio.Backend = SoundBackendOption.Wasapi;
            using var mixed = _manager.LoadMusic(wav, false);
            _settings.Settings.Audio.Backend = SoundBackendOption.Asio;
            using var asio = _manager.LoadMusic(wav, true, true);

            Assert.That(simple, Is.TypeOf<BassSimpleAudioSample>());
            Assert.That(mixed, Is.TypeOf<BassAudioSample>());
            Assert.That(asio, Is.TypeOf<BassAudioSample>());
            Assert.That(simple.Length.TotalSeconds, Is.EqualTo(0.25).Within(0.01));
        }

        /// <summary>Checks the asynchronous loaders on every backend outcome that needs no network.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadMusicAsync_ReturnsSamplesOrEmpty()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var wav = WriteWave("async.wav");
                PrivateMember.SetStaticField(typeof(AudioManager), "BassGlobalMixer", _mixer);
                PrivateMember.SetStaticProperty(typeof(AudioManager), nameof(AudioManager.MixingMatrix), new float[,] { { 1f, 0f }, { 0f, 1f } });

                _settings.Settings.Audio.Backend = SoundBackendOption.BassSimple;
                var simple = await _manager.LoadMusicAsync(wav, false);
                _settings.Settings.Audio.Backend = SoundBackendOption.Wasapi;
                var mixed = await _manager.LoadMusicAsync(wav, false);
                var missing = await _manager.LoadMusicAsync(_temporary.Location + "/missing.wav");
                _settings.Settings.Audio.Backend = (SoundBackendOption)99;
                var unknown = await _manager.LoadMusicAsync(wav);
                var unknownUri = await _manager.LoadMusicFromUriAsync(new Uri("https://example.invalid/a.wav"));

                Assert.That(simple, Is.TypeOf<BassSimpleAudioSample>());
                Assert.That(mixed, Is.TypeOf<BassAudioSample>());
                Assert.That(missing, Is.SameAs(AudioSampleWrap.Empty));
                Assert.That(unknown, Is.SameAs(AudioSampleWrap.Empty));
                Assert.That(unknownUri, Is.SameAs(AudioSampleWrap.Empty));
                simple.Dispose();
                mixed.Dispose();
            });
        }

        /// <summary>Checks which backends can create a recorder without an initialized output.</summary>
        [Test]
        public void CreateAudioRecorder_DependsOnTheBackend()
        {
            _settings.Settings.Audio.Backend = SoundBackendOption.Unity;
            Assert.Throws<NotSupportedException>(() => _manager.CreateAudioRecorder());

            _settings.Settings.Audio.Backend = SoundBackendOption.Wasapi;
            Assert.Throws<InvalidOperationException>(() => _manager.CreateAudioRecorder(), "no global mixer yet");

            PrivateMember.SetStaticField(typeof(AudioManager), "BassGlobalMixer", _mixer);
            Assert.Throws<InvalidOperationException>(() => _manager.CreateAudioRecorder(), "WASAPI is not initialized");

            _settings.Settings.Audio.Backend = SoundBackendOption.Asio;
            using var recorder = _manager.CreateAudioRecorder();
            Assert.That(recorder.Channels, Is.EqualTo(2));
            Assert.That(recorder.SampleRate, Is.EqualTo(BassTestSupport.SampleRate));
        }

        /// <summary>Checks that the ASIO control panel is only opened for the ASIO backend and pause notifications are accepted.</summary>
        [Test]
        public void AsioPanelAndPause_AreSafeForOtherBackends()
        {
            _settings.Settings.Audio.Backend = SoundBackendOption.Unity;

            Assert.DoesNotThrow(() => _manager.OpenAsioPannel());
            Assert.DoesNotThrow(() => PrivateMember.Invoke(_manager, "OnAppPause", null, true));
        }

        /// <summary>Checks that Awake registers the manager as the singleton.</summary>
        [Test]
        public void Awake_RegistersSingleton()
        {
            UnityMessages.Send(_manager, "Awake");

            Assert.That(MajdataPlay.Majdata<AudioManager>.Instance, Is.SameAs(_manager));
        }

        /// <summary>Checks that destruction with a BASS backend disposes every sample and frees BASS.</summary>
        [Test]
        public void OnDestroy_BassBackend_DisposesSamplesAndFreesBass()
        {
            var tap = AddSample("tap.wav", SFXSampleType.Tap);
            _samples.Add(null!);
            _settings.Settings.Audio.Backend = SoundBackendOption.BassSimple;
            try
            {
                UnityMessages.Send(_manager, "OnDestroy");

                Assert.That(tap.Disposals, Is.EqualTo(1));
                Assert.That(PrivateMember.GetStaticField<int>(typeof(AudioManager), "s_bassDeviceOutputStream"), Is.EqualTo(0));
            }
            finally
            {
                if (BassTestSupport.OwnsDevice)
                {
                    BassTestSupport.FreeNoSoundDevice();
                    BassTestSupport.InitNoSoundDevice();
                    _mixer = BassMix.CreateMixerStream(BassTestSupport.SampleRate, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
                }
            }
        }

        /// <summary>Checks that destruction with the Unity backend leaves the samples alone.</summary>
        [Test]
        public void OnDestroy_UnityBackend_KeepsSamples()
        {
            var tap = AddSample("tap.wav", SFXSampleType.Tap);
            _settings.Settings.Audio.Backend = SoundBackendOption.Unity;

            UnityMessages.Send(_manager, "OnDestroy");

            Assert.That(tap.Disposals, Is.EqualTo(0));
        }

        /// <summary>Adds a fake sound effect to the manager.</summary>
        /// <param name="name">The sample name.</param>
        /// <param name="type">The sound effect group.</param>
        /// <param name="isEmpty">Whether the sample is empty.</param>
        /// <returns>The added sample.</returns>
        private FakeAudioSample AddSample(string name, SFXSampleType type, bool isEmpty = false)
        {
            var sample = new FakeAudioSample(name, type, isEmpty);
            _samples.Add(sample);
            return sample;
        }

        /// <summary>Invokes a private input handler of the manager.</summary>
        /// <param name="name">The method name.</param>
        /// <param name="args">The event.</param>
        private void Invoke(string name, InputEventArgs args)
        {
            PrivateMember.Invoke(_manager, name, this, args);
        }

        /// <summary>Writes a quarter second WAV file.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The file location.</returns>
        private string WriteWave(string name)
        {
            var file = _temporary.Directory.CreateFile(name);
            file.WriteAllBytes(BassTestSupport.CreateSquareWave(2, BassTestSupport.SampleRate / 4, 0.5f));
            return file.Location;
        }
    }
}
