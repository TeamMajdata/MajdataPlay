using System;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Slide;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Notes
{
    /// <summary>Checks the judge area state machine of <see cref="SlideArea"/>.</summary>
    public class SlideAreaTests
    {
        /// <summary>Checks the defaults of an area without sensors: it can never finish.</summary>
        [Test]
        public void EmptyArea_NeverFinishes()
        {
            var area = new SlideArea();

            area.Check(SensorArea.A1, SwitchStatus.On);

            Assert.That(area.IsSkippable, Is.True);
            Assert.That(area.Policy, Is.EqualTo(AreaPolicy.OR));
            Assert.That(area.On, Is.False);
            Assert.That(area.IsFinished, Is.False);
            Assert.That(area.IncludedAreas.Length, Is.EqualTo(0));
            area.Dispose();
        }

        /// <summary>Checks that an empty sensor list behaves like the parameterless constructor.</summary>
        [Test]
        public void Constructor_WithNoSensorsKeepsTheAreaEmpty()
        {
            var area = new SlideArea(ReadOnlySpan<(SensorArea, bool)>.Empty, 4, 5);

            Assert.That(area.IncludedAreas.Length, Is.EqualTo(0));
            Assert.That(area.ArrowProgressWhenOn, Is.EqualTo(0));
            Assert.That(area.IsFinished, Is.False);
        }

        /// <summary>Checks that duplicate sensors are registered once and the arrow progress is stored.</summary>
        [Test]
        public void Constructor_RemovesDuplicateSensors()
        {
            var area = new SlideArea(new[] { (SensorArea.A1, false), (SensorArea.A1, true), (SensorArea.B1, false) }, 3, 7);

            Assert.That(area.IncludedAreas.ToArray(), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B1 }));
            Assert.That(area.ArrowProgressWhenOn, Is.EqualTo(3));
            Assert.That(area.ArrowProgressWhenFinished, Is.EqualTo(7));
            area.Dispose();
        }

        /// <summary>Checks that the single-progress constructor uses the same progress for both thresholds.</summary>
        [Test]
        public void Constructor_SingleProgressAppliesToBothThresholds()
        {
            var area = new SlideArea(new[] { (SensorArea.C, true) }, 6);

            Assert.That(area.ArrowProgressWhenOn, Is.EqualTo(6));
            Assert.That(area.ArrowProgressWhenFinished, Is.EqualTo(6));
            area.Dispose();
        }

        /// <summary>Checks that a non-final area needs a press and a release, and ignores other sensors and early releases.</summary>
        [Test]
        public void Check_NonFinalAreaNeedsPressAndRelease()
        {
            var area = new SlideArea(new[] { (SensorArea.A1, false) }, 1);

            area.Check(SensorArea.A1, SwitchStatus.Off);
            area.Check(SensorArea.A2, SwitchStatus.On);
            Assert.That(area.On, Is.False);
            Assert.That(area.IsFinished, Is.False);

            area.Check(SensorArea.A1, SwitchStatus.On);
            Assert.That(area.On, Is.True);
            Assert.That(area.IsFinished, Is.False);

            area.Check(SensorArea.A1, SwitchStatus.Off);
            Assert.That(area.IsFinished, Is.True);
            area.Dispose();
        }

        /// <summary>Checks that a final area finishes as soon as it is pressed.</summary>
        [Test]
        public void Check_FinalAreaFinishesOnPress()
        {
            var area = new SlideArea(new[] { (SensorArea.A5, true) }, 1);

            area.Check(SensorArea.A5, SwitchStatus.On);

            Assert.That(area.IsFinished, Is.True);
            area.Dispose();
        }

        /// <summary>Checks that an OR area finishes when any sensor finishes and an AND area only when all do.</summary>
        [Test]
        public void Check_AppliesTheAreaPolicy()
        {
            var types = new[] { (SensorArea.A5, true), (SensorArea.B5, true) };
            var or = new SlideArea(types, 1);
            var and = new SlideArea(types, 1)
            {
                Policy = AreaPolicy.AND
            };

            or.Check(SensorArea.B5, SwitchStatus.On);
            and.Check(SensorArea.B5, SwitchStatus.On);

            Assert.That(or.IsFinished, Is.True);
            Assert.That(and.IsFinished, Is.False);
            Assert.That(and.On, Is.True);

            and.Check(SensorArea.A5, SwitchStatus.On);

            Assert.That(and.IsFinished, Is.True);
            or.Dispose();
            and.Dispose();
        }

        /// <summary>Checks that marking all sensors as final or non-final changes the finishing rule.</summary>
        [Test]
        public void SetIsLastAndSetNonLast_ChangeTheFinishingRule()
        {
            var area = new SlideArea(new[] { (SensorArea.A1, false) }, 1);
            area.SetIsLast();
            area.Check(SensorArea.A1, SwitchStatus.On);
            Assert.That(area.IsFinished, Is.True);

            area.SetNonLast();
            area.Check(SensorArea.A1, SwitchStatus.On);
            Assert.That(area.IsFinished, Is.False);
            area.Dispose();
        }

        /// <summary>Checks that mirroring and rotating move the sensors and the checked target.</summary>
        [Test]
        public void MirrorAndDiff_MoveTheSensors()
        {
            var area = new SlideArea(new[] { (SensorArea.A2, true), (SensorArea.B2, true) }, 1);

            area.Mirror(SensorArea.A1);
            Assert.That(area.IncludedAreas.ToArray(), Is.EqualTo(new[] { SensorArea.A8, SensorArea.B8 }));

            area.Diff(2);
            Assert.That(area.IncludedAreas.ToArray(), Is.EqualTo(new[] { SensorArea.A2, SensorArea.B2 }));

            area.Check(SensorArea.A8, SwitchStatus.On);
            Assert.That(area.IsFinished, Is.False);
            area.Check(SensorArea.A2, SwitchStatus.On);
            Assert.That(area.IsFinished, Is.True);
            area.Dispose();
        }

        /// <summary>Checks that a disposed area rejects every operation and that disposing twice is harmless.</summary>
        [Test]
        public void Dispose_RejectsLaterUse()
        {
            var area = new SlideArea(new[] { (SensorArea.A1, true) }, 1);

            area.Dispose();
            area.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = area.IsFinished);
            Assert.Throws<ObjectDisposedException>(() => _ = area.IncludedAreas.Length);
            Assert.Throws<ObjectDisposedException>(() => area.Check(SensorArea.A1, SwitchStatus.On));
            Assert.Throws<ObjectDisposedException>(() => area.Mirror(SensorArea.A1));
            Assert.Throws<ObjectDisposedException>(() => area.Diff(1));
            Assert.Throws<ObjectDisposedException>(() => area.SetIsLast());
            Assert.Throws<ObjectDisposedException>(() => area.SetNonLast());
        }
    }
}
