using System;
using System.Linq;
using System.Numerics;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Utils
{
    /// <summary>Checks the conversion of slide paths into extended slide metadata in <see cref="ExtendSlideHelper"/>.</summary>
    public class ExtendSlideHelperTests
    {
        /// <summary>The tolerance used for poses.</summary>
        private const float Tolerance = 1e-4f;

        /// <summary>Builds the judge area lookup table used by the metadata tests.</summary>
        [SetUp]
        public void SetUp()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();
        }

        /// <summary>Checks that an arrow pose keeps the position and converts the direction to a view rotation.</summary>
        [Test]
        public void CalcArrowPose_ConvertsTheDirectionToDegrees()
        {
            var pose = ExtendSlideHelper.CalcArrowPose(new SlideArrowRawData(new Complex(1, 2), new Complex(0, 1), 3));

            Assert.That(pose.X, Is.EqualTo(1f));
            Assert.That(pose.Y, Is.EqualTo(2f));
            Assert.That(pose.RotZ, Is.EqualTo(270f).Within(Tolerance));
            Assert.That(pose.L, Is.EqualTo(3f));
        }

        /// <summary>Checks the circle slide OK poses for both directions at representative buttons.</summary>
        [Test]
        public void CalcCircleOkPose_PlacesTheOkOnTheOuterRing()
        {
            var ccw = ExtendSlideHelper.CalcCircleOkPose(2, true);
            var cw = ExtendSlideHelper.CalcCircleOkPose(1, false);

            Assert.That(ccw.X, Is.EqualTo(4.62f).Within(Tolerance));
            Assert.That(ccw.Y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ccw.RotZ, Is.EqualTo(270f));
            Assert.That(cw.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(cw.Y, Is.EqualTo(4.62f).Within(Tolerance));
            Assert.That(cw.RotZ, Is.EqualTo(360f));
            for (var button = 1; button <= 8; button++)
            {
                var pose = ExtendSlideHelper.CalcCircleOkPose(button, button % 2 == 0);
                Assert.That(Math.Sqrt((pose.X * pose.X) + (pose.Y * pose.Y)), Is.EqualTo(ExtendSlideHelper.CircleOkRadius).Within(1e-4));
                Assert.That(pose.L, Is.EqualTo(0f));
            }
        }

        /// <summary>Checks that a straight OK is pulled back from the final arrow and flipped for left-facing OKs.</summary>
        [Test]
        public void CalcStraightOkPose_PullsBackFromTheFinalArrow()
        {
            var finalArrow = new SlideArrowRawData(new Complex(0, 4.8), new Complex(0, 1), 9.6);

            var right = ExtendSlideHelper.CalcStraightOkPose(finalArrow, false);
            var left = ExtendSlideHelper.CalcStraightOkPose(finalArrow, true);

            Assert.That(right.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(right.Y, Is.EqualTo(2.75f).Within(Tolerance));
            Assert.That(right.RotZ, Is.EqualTo(90f).Within(Tolerance));
            Assert.That(left.RotZ, Is.EqualTo(270f).Within(Tolerance));
            Assert.That(ExtendSlideHelper.StraightOkDistance, Is.EqualTo(2.05).Within(1e-12));
        }

        /// <summary>Checks the metadata of a straight slide through the centre.</summary>
        [Test]
        public void CreateSlideEntry_StraightSlide()
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1CK5"));

            Assert.That(metadata.SlideLength, Is.EqualTo(9.6f).Within(Tolerance));
            Assert.That(metadata.SlideConst, Is.EqualTo(0.165625f).Within(Tolerance));
            Assert.That(metadata.ConditionalLastArrow, Is.True);
            Assert.That(metadata.OkType, Is.EqualTo(SlideOkType.StraightL));
            Assert.That(metadata.Flag, Is.EqualTo(SlideFlag.None));
            Assert.That(metadata.ArrowPoses, Has.Length.EqualTo(22));
            Assert.That(metadata.JudgeAreaQueue.Select(x => x.SensorA), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B1, SensorArea.C, SensorArea.B5, SensorArea.A5 }));
            Assert.That(metadata.JudgeAreaQueue.Select(x => (x.ArrowProgressPush, x.ArrowProgressFinish)), Is.EqualTo(new[] { (2, 3), (5, 7), (9, 12), (14, 15), (22, 22) }));
            Assert.That(metadata.JudgeAreaQueue.All(x => x.SensorB is null), Is.True);
        }

        /// <summary>Checks the metadata of clockwise and counter-clockwise circle slides.</summary>
        [Test]
        public void CreateSlideEntry_CircleSlides()
        {
            var clockwise = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1Q9K3"));
            var counterClockwise = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1P9K3"));

            Assert.That(clockwise.OkType, Is.EqualTo(SlideOkType.CircleR));
            Assert.That(clockwise.OkPose.X, Is.EqualTo(4.62f).Within(Tolerance));
            Assert.That(clockwise.OkPose.RotZ, Is.EqualTo(270f));
            Assert.That(clockwise.JudgeAreaQueue.Select(x => x.SensorA), Is.EqualTo(new[] { SensorArea.A1, SensorArea.A2, SensorArea.A3 }));
            Assert.That(counterClockwise.OkType, Is.EqualTo(SlideOkType.CircleL));
            Assert.That(counterClockwise.OkPose.RotZ, Is.EqualTo(225f));
            Assert.That(counterClockwise.JudgeAreaQueue, Has.Length.EqualTo(7));
        }

        /// <summary>Checks a right-facing straight OK, an OR judge area and the flag passed through.</summary>
        [Test]
        public void CreateSlideEntry_RightFacingSlideWithOrArea()
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1K3"), SlideFlag.NormalV);

            Assert.That(metadata.OkType, Is.EqualTo(SlideOkType.StraightR));
            Assert.That(metadata.Flag, Is.EqualTo(SlideFlag.NormalV));
            Assert.That(metadata.JudgeAreaQueue[1].SensorA, Is.EqualTo(SensorArea.A2));
            Assert.That(metadata.JudgeAreaQueue[1].SensorB, Is.EqualTo(SensorArea.B2));
        }

        /// <summary>Checks that a short slide whose last arrow is far enough from the end shows it unconditionally.</summary>
        [Test]
        public void CreateSlideEntry_KeepsTheLastArrowWhenItIsFarFromTheEnd()
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1K2"));

            Assert.That(metadata.ConditionalLastArrow, Is.False);
            Assert.That(metadata.JudgeAreaQueue.Select(x => (x.ArrowProgressPush, x.ArrowProgressFinish)), Is.EqualTo(new[] { (2, 3), (9, 9) }));
        }

        /// <summary>Checks the overload that takes raw data, including an explicit end shape.</summary>
        [Test]
        public void CreateSlideEntry_AcceptsRawDataAndEndShape()
        {
            var path = SlideCodeParser.Parse("1CK5");
            var arrows = SlideDataBuilder.BuildArrowData(path);
            var areas = SlideDataBuilder.BuildSlideAreas(path);

            var metadata = ExtendSlideHelper.CreateSlideEntry(arrows, areas, SlideEndShape.CircleCCW, SlideFlag.SpecialV);

            Assert.That(metadata.OkType, Is.EqualTo(SlideOkType.CircleL));
            Assert.That(metadata.Flag, Is.EqualTo(SlideFlag.SpecialV));
            Assert.That(metadata.ArrowPoses, Has.Length.EqualTo(arrows.Length));
        }

        /// <summary>Checks that every pose of a slide that ends at its last explicit node stays finite.</summary>
        [Test]
        public void CreateSlideEntry_RepeatedFinalNodeKeepsFinitePoses()
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1A3K3"));

            Assert.That(float.IsNaN(metadata.OkPose.X), Is.False);
            Assert.That(metadata.ArrowPoses.All(x => !float.IsNaN(x.RotZ)), Is.True);
        }

        /// <summary>Checks the metadata value types keep their constructor arguments.</summary>
        [Test]
        public void MetadataTypes_StoreTheirValues()
        {
            var pose = new SlidePose(1, 2, 3, 4);
            var area = new ParsingSlideArea(5, 6, SensorArea.B3, SensorArea.C);
            var metadata = new ExtendSlideMetadata(new[] { area }, 0.5f, 7f, new[] { pose }, true, pose, SlideOkType.WifiU, SlideFlag.SpecialV);

            Assert.That((pose.X, pose.Y, pose.RotZ, pose.L), Is.EqualTo((1f, 2f, 3f, 4f)));
            Assert.That(area.ArrowProgressPush, Is.EqualTo(5));
            Assert.That(area.ArrowProgressFinish, Is.EqualTo(6));
            Assert.That(area.SensorA, Is.EqualTo(SensorArea.B3));
            Assert.That(area.SensorB, Is.EqualTo(SensorArea.C));
            Assert.That(metadata.SlideConst, Is.EqualTo(0.5f));
            Assert.That(metadata.SlideLength, Is.EqualTo(7f));
            Assert.That(metadata.ConditionalLastArrow, Is.True);
            Assert.That(metadata.OkType, Is.EqualTo(SlideOkType.WifiU));
            Assert.That(metadata.Flag, Is.EqualTo(SlideFlag.SpecialV));
            Assert.That(metadata.JudgeAreaQueue, Has.Length.EqualTo(1));
            Assert.That(metadata.ArrowPoses, Has.Length.EqualTo(1));
            Assert.That(metadata.OkPose.RotZ, Is.EqualTo(3f));
        }
    }
}
