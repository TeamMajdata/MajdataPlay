using System;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Slide;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Notes
{
    /// <summary>Checks the connection slide bookkeeping of <see cref="ConnSlideInfo"/>.</summary>
    public class ConnSlideInfoTests
    {
        /// <summary>Checks the defaults of a slide that is not part of a group.</summary>
        [Test]
        public void Defaults_DescribeAStandaloneSlide()
        {
            var info = new ConnSlideInfo();

            Assert.That(info.IsGroupPart, Is.False);
            Assert.That(info.IsConnSlide, Is.False);
            Assert.That(info.IsGroupPartHead, Is.False);
            Assert.That(info.IsGroupPartEnd, Is.False);
            Assert.That(info.DestroyAfterJudge, Is.False);
            Assert.That(info.Parent, Is.Null);
            Assert.That(info.TotalLength, Is.EqualTo(0f));
            Assert.That(info.TotalJudgeQueueLen, Is.EqualTo(0));
            Assert.That(info.StartTiming, Is.EqualTo(0f));
        }

        /// <summary>Checks that head and end flags only apply to slides that are part of a group.</summary>
        [Test]
        public void HeadAndEndFlags_RequireGroupMembership()
        {
            var info = new ConnSlideInfo
            {
                IsGroupPartHead = true,
                IsGroupPartEnd = true,
                TotalLength = 1.5f,
                TotalJudgeQueueLen = 9,
                StartTiming = 2.5f,
            };

            Assert.That(info.IsGroupPartHead, Is.False);
            Assert.That(info.IsGroupPartEnd, Is.False);

            info.IsGroupPart = true;

            Assert.That(info.IsConnSlide, Is.True);
            Assert.That(info.IsGroupPartHead, Is.True);
            Assert.That(info.IsGroupPartEnd, Is.True);
            Assert.That(info.DestroyAfterJudge, Is.True);
            Assert.That(info.TotalLength, Is.EqualTo(1.5f));
            Assert.That(info.TotalJudgeQueueLen, Is.EqualTo(9));
            Assert.That(info.StartTiming, Is.EqualTo(2.5f));
        }

        /// <summary>Checks that the parent state is read from the parent slide.</summary>
        [Test]
        public void ParentState_IsReadFromTheParent()
        {
            var parent = new FakeConnectableSlide { IsFinished = true, IsPendingFinish = false };
            var info = new ConnSlideInfo { Parent = parent };

            Assert.That(info.ParentFinished, Is.True);
            Assert.That(info.ParentPendingFinish, Is.False);

            parent.IsFinished = false;
            parent.IsPendingFinish = true;

            Assert.That(info.ParentFinished, Is.False);
            Assert.That(info.ParentPendingFinish, Is.True);
        }

        /// <summary>Checks that reading the parent state without a parent fails.</summary>
        [Test]
        public void ParentState_WithoutParentThrows()
        {
            var info = new ConnSlideInfo();

            Assert.Throws<NullReferenceException>(() => _ = info.ParentFinished);
            Assert.Throws<NullReferenceException>(() => _ = info.ParentPendingFinish);
        }

        /// <summary>A connectable slide whose state is set by the test.</summary>
        private sealed class FakeConnectableSlide : IConnectableSlide
        {
            /// <summary>Gets the connection info; a fresh instance.</summary>
            public ConnSlideInfo ConnectInfo { get; } = new();

            /// <summary>Gets the game object; always null.</summary>
            public GameObject GameObject => null!;

            /// <summary>Gets a value indicating whether the slide ended.</summary>
            public bool IsEnded { get; set; }

            /// <summary>Gets or sets a value indicating whether every judge area finished.</summary>
            public bool IsFinished { get; set; }

            /// <summary>Gets or sets a value indicating whether one judge area remains.</summary>
            public bool IsPendingFinish { get; set; }

            /// <summary>Gets the remaining judge areas.</summary>
            public int QueueRemaining { get; set; }

            /// <summary>Gets the final star angle.</summary>
            public Quaternion FinalStarAngle => Quaternion.identity;

            /// <summary>Gets the note state.</summary>
            public NoteStatus State { get; set; }

            /// <summary>Marks the slide as finished.</summary>
            public void ForceFinish()
            {
                IsFinished = true;
            }

            /// <summary>Marks the slide as ended.</summary>
            public void End()
            {
                IsEnded = true;
            }
        }
    }
}
