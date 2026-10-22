using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Base
{
    /// <summary>Checks the conversion of mod options into <see cref="GameModInfo"/>.</summary>
    public class GameModInfoTests
    {
        /// <summary>Checks that every option is copied from the mod settings, explicitly and implicitly.</summary>
        [Test]
        public void FromModOptions_CopiesEveryOption()
        {
            var options = new ModOptions
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
                ButtonRingForTouch = true,
                NoteMask = "Inner",
            };

            var explicitInfo = new GameModInfo(options);
            GameModInfo implicitInfo = options;

            foreach (var info in new[] { explicitInfo, implicitInfo })
            {
                Assert.That(info.PlaybackSpeed, Is.EqualTo(1.25f));
                Assert.That(info.AutoPlay, Is.EqualTo(AutoplayModeOption.Enable));
                Assert.That(info.JudgeStyle, Is.EqualTo(JudgeStyleOption.GACHI));
                Assert.That(info.SubdivideSlideJudgeGrade, Is.True);
                Assert.That(info.AllBreak, Is.True);
                Assert.That(info.AllEx, Is.True);
                Assert.That(info.AllTouch, Is.True);
                Assert.That(info.SlideNoHead, Is.True);
                Assert.That(info.SlideNoTrack, Is.True);
                Assert.That(info.ButtonRingForTouch, Is.True, "The editor is a standalone platform");
                Assert.That(info.NoteMask, Is.EqualTo("Inner"));
            }
        }

        /// <summary>Checks that the copy constructor copies every option and allows overriding some of them.</summary>
        [Test]
        public void CopyConstructor_CopiesAndAllowsOverrides()
        {
            var source = new GameModInfo(new ModOptions { PlaybackSpeed = 0.5f, AllEx = true, NoteMask = "Outer", ButtonRingForTouch = true });

            var copy = new GameModInfo(source);
            var overridden = new GameModInfo(source) { PlaybackSpeed = 2f, AllEx = false };

            Assert.That(copy, Is.EqualTo(source));
            Assert.That(overridden.PlaybackSpeed, Is.EqualTo(2f));
            Assert.That(overridden.AllEx, Is.False);
            Assert.That(overridden.NoteMask, Is.EqualTo("Outer"));
            Assert.That(overridden.ButtonRingForTouch, Is.True);
        }

        /// <summary>Checks the defaults of the mod settings.</summary>
        [Test]
        public void DefaultOptions_DisableEveryMod()
        {
            GameModInfo info = new ModOptions();

            Assert.That(info.PlaybackSpeed, Is.EqualTo(1f));
            Assert.That(info.AutoPlay, Is.EqualTo(AutoplayModeOption.Disable));
            Assert.That(info.AllBreak || info.AllEx || info.AllTouch || info.SlideNoHead || info.SlideNoTrack, Is.False);
            Assert.That(info.NoteMask, Is.EqualTo("Disable"));
        }
    }
}
