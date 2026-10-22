using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Touch;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Notes
{
    /// <summary>Checks the shared judgement of grouped touches in <see cref="TouchGroup"/> and <see cref="TouchHoldGroup"/>.</summary>
    public class TouchGroupTests
    {
        /// <summary>Checks that an empty group reports no progress.</summary>
        [Test]
        public void TouchGroup_EmptyGroupHasNoProgress()
        {
            var group = new TouchGroup();

            Assert.That(group.Members, Is.Empty);
            Assert.That(group.Percent, Is.EqualTo(0f));
            Assert.That(group.JudgeResult, Is.Null);
            Assert.That(group.JudgeDiff, Is.EqualTo(0f));
        }

        /// <summary>Checks that only non-miss results count towards the progress.</summary>
        [Test]
        public void TouchGroup_RegisterResultIgnoresMisses()
        {
            var group = new TouchGroup { Members = new object[4] };

            group.RegisterResult(JudgeGrade.Miss);
            group.RegisterResult(JudgeGrade.TooFast);
            Assert.That(group.Percent, Is.EqualTo(0f));

            group.RegisterResult(JudgeGrade.Perfect);
            group.RegisterResult(JudgeGrade.LateGood);
            Assert.That(group.Percent, Is.EqualTo(0.5f));
        }

        /// <summary>Checks that the shared result can be set until more than half of the members were judged.</summary>
        [Test]
        public void TouchGroup_JudgeResultLocksAfterHalfOfTheMembers()
        {
            var group = new TouchGroup { Members = new object[2], JudgeDiff = 12f };

            group.JudgeResult = JudgeGrade.FastGreat;
            group.RegisterResult(JudgeGrade.FastGreat);
            group.JudgeResult = JudgeGrade.Perfect;
            Assert.That(group.JudgeResult, Is.EqualTo(JudgeGrade.Perfect), "Half of the members still allows changes");

            group.RegisterResult(JudgeGrade.Perfect);
            group.JudgeResult = JudgeGrade.Miss;

            Assert.That(group.JudgeResult, Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(group.JudgeDiff, Is.EqualTo(12f));
        }

        /// <summary>Checks the trigger counting of a touch hold group, including duplicates and unknown members.</summary>
        [Test]
        public void TouchHoldGroup_CountsDistinctTriggers()
        {
            var group = new TouchHoldGroup { Members = new object[4] };

            group.RegisterTrigger(1);
            group.RegisterTrigger(1);
            group.RegisterTrigger(2);
            Assert.That(group.Percent, Is.EqualTo(0.5f));

            group.UnregisterTrigger(1);
            group.UnregisterTrigger(99);
            Assert.That(group.Percent, Is.EqualTo(0.25f));
        }

        /// <summary>Checks that exiting members shrinks the denominator and that an exhausted group reports no progress.</summary>
        [Test]
        public void TouchHoldGroup_ExitShrinksTheGroup()
        {
            var group = new TouchHoldGroup { Members = new object[2] };
            group.RegisterTrigger(7);

            group.Exit();
            Assert.That(group.Percent, Is.EqualTo(1f));

            group.Exit();
            Assert.That(group.Percent, Is.EqualTo(0f));
        }

        /// <summary>Checks that assigning members resets the triggers and that null becomes an empty member list.</summary>
        [Test]
        public void TouchHoldGroup_AssigningMembersResetsTheTriggers()
        {
            var group = new TouchHoldGroup { Members = new object[2] };
            group.RegisterTrigger(1);

            group.Members = new object[3];
            Assert.That(group.Percent, Is.EqualTo(0f));
            Assert.That(group.Members, Has.Length.EqualTo(3));

            group.Members = null!;
            Assert.That(group.Members, Is.Empty);
            Assert.That(group.Percent, Is.EqualTo(0f));
        }
    }
}
