using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Base
{
    /// <summary>Checks the conversion of mod and gameplay options into <see cref="GameModInfo"/>.</summary>
    public class GameModInfoTests
    {
        /// <summary>Checks that every mod option and the button ring option of the gameplay settings are copied.</summary>
        [Test]
        public void FromOptions_CopiesEveryOption()
        {
            var modOptions = new ModOptions
            {
                PlaybackSpeed = 1.25f,
                AutoPlay = AutoplayModeOption.Enable,
                JudgeStyle = JudgeStyleOption.GACHI,
                SubdivideSlideJudgeGrade = true,
                AllBreak = true,
                AllEx = true,
                AllTouch = true,
                SlideNoHead = true,
                SlideNoTrack = true,
                NoteMask = "Inner",
            };
            var gameOptions = new GameOptions { ButtonRingForTouch = true };

            var info = new GameModInfo(modOptions, gameOptions);

            Assert.That(info.PlaybackSpeed, Is.EqualTo(1.25f));
            Assert.That(info.AutoPlay, Is.EqualTo(AutoplayModeOption.Enable));
            Assert.That(info.JudgeStyle, Is.EqualTo(JudgeStyleOption.GACHI));
            Assert.That(info.SubdivideSlideJudgeGrade, Is.True);
            Assert.That(info.AllBreak, Is.True);
            Assert.That(info.AllEx, Is.True);
            Assert.That(info.AllTouch, Is.True);
            Assert.That(info.SlideNoHead, Is.True);
            Assert.That(info.SlideNoTrack, Is.True);
            Assert.That(info.ButtonRingForTouch, Is.True);
            Assert.That(info.NoteMask, Is.EqualTo("Inner"));
        }

        /// <summary>Checks that the copy constructor copies every option and allows overriding some of them.</summary>
        [Test]
        public void CopyConstructor_CopiesAndAllowsOverrides()
        {
            var source = new GameModInfo(new ModOptions { PlaybackSpeed = 0.5f, AllEx = true, NoteMask = "Outer" }, new GameOptions()) { ButtonRingForTouch = true };

            var copy = new GameModInfo(source);
            var overridden = new GameModInfo(source) { PlaybackSpeed = 2f, AllEx = false };

            Assert.That(copy, Is.EqualTo(source));
            Assert.That(overridden.PlaybackSpeed, Is.EqualTo(2f));
            Assert.That(overridden.AllEx, Is.False);
            Assert.That(overridden.NoteMask, Is.EqualTo("Outer"));
            Assert.That(overridden.ButtonRingForTouch, Is.True);
        }

        /// <summary>Checks the defaults of the mod and gameplay settings.</summary>
        [Test]
        public void DefaultOptions_DisableEveryMod()
        {
            var info = new GameModInfo(new ModOptions(), new GameOptions());

            Assert.That(info.PlaybackSpeed, Is.EqualTo(1f));
            Assert.That(info.AutoPlay, Is.EqualTo(AutoplayModeOption.Disable));
            Assert.That(info.AllBreak || info.AllEx || info.AllTouch || info.SlideNoHead || info.SlideNoTrack, Is.False);
            Assert.That(info.NoteMask, Is.EqualTo("Disable"));
#if UNITY_ANDROID || UNITY_IOS
            Assert.That(info.ButtonRingForTouch, Is.True, "Mobile players use the outer touch area as buttons by default");
#else
            Assert.That(info.ButtonRingForTouch, Is.False, "The editor is a standalone platform");
#endif
        }
    }
}
