using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>
    /// Covers <see cref="AudioVolumeEnumerator"/>: a volume option re-applies the mixer volumes and previews its sound
    /// effect only when its value has changed since the last refresh.
    /// </summary>
    public class AudioVolumeEnumeratorTests : SceneTestBase
    {
        /// <summary>The preview sound and sample type of every volume option that has a preview.</summary>
        private static readonly (string Option, string Sfx, SFXSampleType Type)[] s_previews =
        {
            (nameof(SFXVolume.Answer), "answer.wav", SFXSampleType.Answer),
            (nameof(SFXVolume.Tap), "tap_perfect.wav", SFXSampleType.Tap),
            (nameof(SFXVolume.Ex), "tap_ex.wav", SFXSampleType.Ex),
            (nameof(SFXVolume.Break), "break.wav", SFXSampleType.Break),
            (nameof(SFXVolume.Slide), "slide.wav", SFXSampleType.Slide),
            (nameof(SFXVolume.Touch), "touch.wav", SFXSampleType.Touch),
            (nameof(SFXVolume.Hanabi), "touch_hanabi.wav", SFXSampleType.Hanabi),
            (nameof(SFXVolume.Voice), "MajdataPlay.wav", SFXSampleType.Voice),
        };

        /// <summary>The volume settings edited by the enumerators under test.</summary>
        private SFXVolume _volume = null!;

        /// <summary>Installs a stub audio manager holding one fake sample per preview sound.</summary>
        [SetUp]
        public void SetUp()
        {
            _volume = MajEnv.Settings.Audio.Volume;
            CreateAudioManager();
            foreach (var (_, sfx, type) in s_previews)
            {
                AddSfx(sfx).SampleType = type;
            }
        }

        /// <summary>Creates and initializes an enumerator for one volume option.</summary>
        /// <param name="option">The <see cref="SFXVolume"/> property name.</param>
        /// <returns>The initialized enumerator.</returns>
        private AudioVolumeEnumerator Create(string option)
        {
            var enumerator = new AudioVolumeEnumerator();
            enumerator.Init(Property(option), _volume);
            return enumerator;
        }

        /// <summary>Gets a volume option property.</summary>
        /// <param name="option">The property name.</param>
        /// <returns>The property.</returns>
        private static PropertyInfo Property(string option)
        {
            return typeof(SFXVolume).GetProperty(option)!;
        }

        /// <summary>Writes a volume option behind the enumerator's back, as the settings file or another menu would.</summary>
        /// <param name="option">The property name.</param>
        /// <param name="value">The new volume.</param>
        private void SetVolume(string option, float value)
        {
            Property(option).SetValue(_volume, value);
        }

        /// <summary>Counts how many preview samples have been played.</summary>
        /// <returns>The number of samples with at least one one-shot play.</returns>
        private int PlayedPreviewCount()
        {
            var count = 0;
            foreach (var (_, sfx, _) in s_previews)
            {
                if (Sfx(sfx).PlayOneShotCount > 0)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Checks that refreshing an unchanged option neither previews a sound nor touches the sample volumes.</summary>
        [Test]
        public void Refresh_UnchangedValue_IsSilent()
        {
            var enumerator = Create(nameof(SFXVolume.Answer));

            enumerator.Refresh();
            enumerator.Refresh();

            Assert.That(PlayedPreviewCount(), Is.Zero);
            Assert.That(Sfx("answer.wav").LastSetVolume, Is.NaN);
        }

        /// <summary>Checks that a changed option plays only its own preview, applies the new volume, and does not replay on the next refresh.</summary>
        /// <param name="option">The volume option.</param>
        /// <param name="sfx">The preview sound expected for the option.</param>
        [TestCase(nameof(SFXVolume.Answer), "answer.wav")]
        [TestCase(nameof(SFXVolume.Tap), "tap_perfect.wav")]
        [TestCase(nameof(SFXVolume.Ex), "tap_ex.wav")]
        [TestCase(nameof(SFXVolume.Break), "break.wav")]
        [TestCase(nameof(SFXVolume.Slide), "slide.wav")]
        [TestCase(nameof(SFXVolume.Touch), "touch.wav")]
        [TestCase(nameof(SFXVolume.Hanabi), "touch_hanabi.wav")]
        [TestCase(nameof(SFXVolume.Voice), "MajdataPlay.wav")]
        public void Refresh_ChangedValue_PlaysPreviewAndAppliesVolume(string option, string sfx)
        {
            var enumerator = Create(option);
            SetVolume(option, 0.55f);

            enumerator.Refresh();

            Assert.That(Sfx(sfx).PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx(sfx).LastSetVolume, Is.EqualTo(0.55f).Within(1e-6f));
            Assert.That(PlayedPreviewCount(), Is.EqualTo(1));

            enumerator.Refresh();

            Assert.That(Sfx(sfx).PlayOneShotCount, Is.EqualTo(1));
        }

        /// <summary>Checks that options without a preview sound still re-apply every sample volume when they change.</summary>
        /// <param name="option">A volume option that has no preview sound.</param>
        [TestCase(nameof(SFXVolume.Global))]
        [TestCase(nameof(SFXVolume.BGM))]
        [TestCase(nameof(SFXVolume.Track))]
        public void Refresh_OptionWithoutPreview_OnlyAppliesVolumes(string option)
        {
            var enumerator = Create(option);
            SetVolume(option, 0.1f);
            SetVolume(nameof(SFXVolume.Answer), 0.25f);

            enumerator.Refresh();

            Assert.That(PlayedPreviewCount(), Is.Zero);
            Assert.That(Sfx("answer.wav").LastSetVolume, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(Sfx("MajdataPlay.wav").LastSetVolume, Is.EqualTo(_volume.Voice).Within(1e-6f));
        }

        /// <summary>Checks that stepping the option changes the value silently and the following refresh previews it once.</summary>
        [Test]
        public void MoveNext_ThenRefresh_PlaysPreviewOnce()
        {
            var enumerator = Create(nameof(SFXVolume.Tap));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(_volume.Tap, Is.EqualTo(0.35f).Within(1e-6f));
            Assert.That(PlayedPreviewCount(), Is.Zero);

            enumerator.Refresh();

            Assert.That(Sfx("tap_perfect.wav").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx("tap_perfect.wav").LastSetVolume, Is.EqualTo(0.35f).Within(1e-6f));
        }

        /// <summary>Checks that stepping back to the previous value previews again, because the value differs from the last refresh.</summary>
        [Test]
        public void MovePrevious_AfterRefresh_PlaysPreviewAgain()
        {
            var enumerator = Create(nameof(SFXVolume.Break));
            enumerator.MoveNext();
            enumerator.Refresh();

            Assert.That(enumerator.MovePrevious(), Is.True);
            enumerator.Refresh();

            Assert.That(Sfx("break.wav").PlayOneShotCount, Is.EqualTo(2));
            Assert.That(_volume.Break, Is.EqualTo(0.3f).Within(1e-6f));
        }
    }
}
