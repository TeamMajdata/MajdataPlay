using System;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Notes
{
    /// <summary>Checks the small gameplay value types: judge results, judge grade helpers, input events and exceptions.</summary>
    public class NoteValueTypesTests
    {
        /// <summary>Checks that a negative difference is fast and that the stored flags are returned.</summary>
        [Test]
        public void NoteJudgeResult_DerivesFastFromTheDifference()
        {
            var fast = new NoteJudgeResult
            {
                Grade = JudgeGrade.FastGreat,
                Diff = -20f,
                IsBreak = true,
                IsEX = true,
                IsMine = true,
            };
            var late = new NoteJudgeResult { Grade = JudgeGrade.LateGood, Diff = 30f };
            var exact = new NoteJudgeResult { Grade = JudgeGrade.Perfect, Diff = 0f };

            Assert.That(fast.IsFast, Is.True);
            Assert.That(fast.IsBreak, Is.True);
            Assert.That(fast.IsEX, Is.True);
            Assert.That(fast.IsMine, Is.True);
            Assert.That(fast.Grade, Is.EqualTo(JudgeGrade.FastGreat));
            Assert.That(fast.Diff, Is.EqualTo(-20f));
            Assert.That(late.IsFast, Is.False);
            Assert.That(exact.IsFast, Is.False);
        }

        /// <summary>Checks that only miss and too-fast grades count as missed, for the result and the extension.</summary>
        [Test]
        public void MissOrTooFast_OnlyForMissAndTooFast()
        {
            foreach (JudgeGrade grade in Enum.GetValues(typeof(JudgeGrade)))
            {
                var expected = grade is JudgeGrade.Miss or JudgeGrade.TooFast;
                var result = new NoteJudgeResult { Grade = grade };
                Assert.That(grade.IsMissOrTooFast(), Is.EqualTo(expected), grade.ToString());
                Assert.That(result.IsMissOrTooFast, Is.EqualTo(expected), grade.ToString());
            }
        }

        /// <summary>Checks that only an off-to-on transition is a click.</summary>
        [Test]
        public void GameInputEventArgs_IsClickOnlyOnPress()
        {
            var press = new GameInputEventArgs { Area = SensorArea.B2, OldState = SwitchStatus.Off, State = SwitchStatus.On, IsButton = true };
            var hold = new GameInputEventArgs { OldState = SwitchStatus.On, State = SwitchStatus.On };
            var release = new GameInputEventArgs { OldState = SwitchStatus.On, State = SwitchStatus.Off };

            Assert.That(press.IsClick, Is.True);
            Assert.That(press.Area, Is.EqualTo(SensorArea.B2));
            Assert.That(press.IsButton, Is.True);
            Assert.That(hold.IsClick, Is.False);
            Assert.That(release.IsClick, Is.False);
        }

        /// <summary>Checks that the empty chart exception is an ordinary exception.</summary>
        [Test]
        public void EmptyChartException_IsAnException()
        {
            var exception = new EmptyChartException();

            Assert.That(exception, Is.InstanceOf<Exception>());
            Assert.That(exception.InnerException, Is.Null);
        }
    }
}
