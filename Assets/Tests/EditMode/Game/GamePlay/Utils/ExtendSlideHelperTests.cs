using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
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
            // 星星按点匀速移动：去掉条件箭头后剩 21 个星星点，距终点 1.59 处约是第 16.998 个点
            Assert.That(metadata.SlideConst, Is.EqualTo(0.150112f).Within(Tolerance));
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

        /// <summary>Checks that the star of a slide built from one path passes every arrow pose except the conditional one.</summary>
        [Test]
        public void CreateSlideEntry_StarPosesSkipOnlyTheConditionalLastArrow()
        {
            var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse("1CK5"));
            var expected = metadata.ArrowPoses.Where((_, i) => i != metadata.ArrowPoses.Length - 2);

            Assert.That(metadata.ConditionalLastArrow, Is.True);
            Assert.That(ToTuples(metadata.StarPoses), Is.EqualTo(ToTuples(expected)));
        }

        /// <summary>
        /// Checks that in the classic and medium styles the 1q8 part of 2^1q8, 3^1q8 and 4^1q8 shows exactly the arrows of
        /// 1q8, and the arc before it shows exactly the arrows of the arc alone; the joint at key 1 is only passed by the star.
        /// </summary>
        /// <param name="style">The style of the connection slide.</param>
        /// <param name="arc">The arc before 1q8.</param>
        [Test]
        public void CreateConnSlideEntry_EverySegmentOverlapsItsStandaloneSlide(
            [Values(SlidePathStyle.Classic, SlidePathStyle.Medium)] SlidePathStyle style,
            [Values("2^1", "3^1", "4^1")] string arc)
        {
            var arcArrows = GetShownArrows(ExtendSlideHelper.CreateSlideEntry(ParseSimai(arc)));
            var tailArrows = GetShownArrows(ExtendSlideHelper.CreateSlideEntry(ParseSimai("1q8")));

            var metadata = CreateConnSlideEntry(style, arc, "1q8");

            var shown = GetShownArrows(metadata);
            Assert.That(shown, Has.Length.EqualTo(arcArrows.Length + tailArrows.Length));
            AssertSamePoses(shown.Take(arcArrows.Length), arcArrows);
            AssertSamePoses(shown.Skip(arcArrows.Length), tailArrows);
            var joint = SlideGeo.PointGroupA(1);
            Assert.That(metadata.ArrowPoses.Count(x => IsAt(x, joint)), Is.Zero, "No arrow at the joint");
            Assert.That(metadata.StarPoses.Count(x => IsAt(x, joint)), Is.EqualTo(1), "The star passes the joint");
        }

        /// <summary>
        /// Checks the joint of the two arcs of 1^2^3: the classic style leaves it without an arrow, the medium style fills
        /// it, and the star passes it in both.
        /// </summary>
        [Test]
        public void CreateConnSlideEntry_ArcJointsHaveAGapOnlyInTheClassicStyle()
        {
            var joint = SlideGeo.PointGroupA(2);

            var classic = CreateConnSlideEntry(SlidePathStyle.Classic, "1^2", "2^3");
            var medium = CreateConnSlideEntry(SlidePathStyle.Medium, "1^2", "2^3");

            Assert.That(GetShownArrows(classic), Has.Length.EqualTo(14));
            Assert.That(classic.ArrowPoses.Count(x => IsAt(x, joint)), Is.Zero);
            Assert.That(classic.StarPoses, Has.Length.EqualTo(17));
            Assert.That(classic.StarPoses.Count(x => IsAt(x, joint)), Is.EqualTo(1));
            Assert.That(GetShownArrows(medium), Has.Length.EqualTo(15));
            Assert.That(GetShownArrows(medium).Count(x => IsAt(x, joint)), Is.EqualTo(1));
            Assert.That(medium.StarPoses, Has.Length.EqualTo(17));
        }

        /// <summary>Checks that the perfect style builds the metadata of the whole chain path, as before the styles existed.</summary>
        /// <param name="first">The first simai segment.</param>
        /// <param name="second">The second simai segment.</param>
        [TestCase("1^2", "2^3")]
        [TestCase("2^1", "1q8")]
        [TestCase("1-4", "4q7")]
        public void CreateConnSlideEntry_PerfectStyleFollowsTheChainPath(string first, string second)
        {
            var expected = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse(SimaiSlideTranslator.TranslateChain(new[] { first, second })));

            var metadata = CreateConnSlideEntry(SlidePathStyle.Perfect, first, second);

            Assert.That(ToTuples(metadata.ArrowPoses), Is.EqualTo(ToTuples(expected.ArrowPoses)));
            Assert.That(ToTuples(metadata.StarPoses), Is.EqualTo(ToTuples(expected.StarPoses)));
            Assert.That(metadata.SlideConst, Is.EqualTo(expected.SlideConst));
            Assert.That(metadata.JudgeAreaQueue, Is.EqualTo(expected.JudgeAreaQueue));
        }

        /// <summary>
        /// Checks that every style judges the areas of the whole chain path, with arrow progress that grows along the slide
        /// and ends with all shown arrows, and keeps the slide result and length of the chain.
        /// </summary>
        /// <param name="style">The style of the connection slide.</param>
        [Test]
        public void CreateConnSlideEntry_JudgesTheAreasOfTheChainPath([Values] SlidePathStyle style)
        {
            var chainPath = SlideCodeParser.Parse("1A4Q0K7");
            var expectedAreas = SlideDataBuilder.BuildSlideAreas(chainPath).Select(x => (SensorArea)x.SensorA);

            var metadata = CreateConnSlideEntry(style, "1-4", "4q7");

            Assert.That(metadata.JudgeAreaQueue.Select(x => x.SensorA), Is.EqualTo(expectedAreas));
            var previous = 0;
            foreach (var area in metadata.JudgeAreaQueue)
            {
                Assert.That(area.ArrowProgressPush, Is.GreaterThanOrEqualTo(previous));
                Assert.That(area.ArrowProgressFinish, Is.GreaterThanOrEqualTo(area.ArrowProgressPush));
                previous = area.ArrowProgressFinish;
            }
            Assert.That(metadata.JudgeAreaQueue[^1].ArrowProgressPush, Is.EqualTo(metadata.ArrowPoses.Length));
            Assert.That(metadata.SlideLength, Is.EqualTo((float)chainPath.GetPathLength()).Within(Tolerance));
            Assert.That(metadata.OkType, Is.EqualTo(SlideOkType.StraightL));
        }

        /// <summary>Checks that an undefined style is rejected.</summary>
        [Test]
        public void CreateConnSlideEntry_RejectsAnUnknownStyle()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateConnSlideEntry((SlidePathStyle)99, "1-4", "4q7"));
        }

        /// <summary>Checks the metadata value types keep their constructor arguments.</summary>
        [Test]
        public void MetadataTypes_StoreTheirValues()
        {
            var pose = new SlidePose(1, 2, 3, 4);
            var area = new ParsingSlideArea(5, 6, SensorArea.B3, SensorArea.C);
            var metadata = new ExtendSlideMetadata(new[] { area }, 0.5f, 7f, new[] { pose }, true, pose, SlideOkType.WifiU, SlideFlag.SpecialV);
            var starPose = new SlidePose(5, 6, 7, 8);
            var explicitStars = new ExtendSlideMetadata(new[] { area }, 0.5f, 7f, new[] { pose }, new[] { pose, starPose }, false, pose, SlideOkType.WifiU, SlideFlag.None);

            Assert.That(metadata.StarPoses, Has.Length.EqualTo(1), "The path start is always a star pose");
            Assert.That(explicitStars.StarPoses, Is.EqualTo(new[] { pose, starPose }));

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

        /// <summary>Builds the metadata of a simai connection slide as the note loader does.</summary>
        /// <param name="style">The style of the connection slide.</param>
        /// <param name="simaiSegments">The simai segments, such as <c>1-4</c> and <c>4q7</c>.</param>
        /// <returns>The metadata.</returns>
        private static ExtendSlideMetadata CreateConnSlideEntry(SlidePathStyle style, params string[] simaiSegments)
        {
            var chainPath = SlideCodeParser.Parse(SimaiSlideTranslator.TranslateChain(simaiSegments));
            var segmentPaths = simaiSegments.Select(ParseSimai).ToArray();
            return ExtendSlideHelper.CreateConnSlideEntry(chainPath, segmentPaths, style);
        }

        /// <summary>Parses one simai segment.</summary>
        /// <param name="simaiSegment">The segment, such as <c>1q8</c>.</param>
        /// <returns>The path of the segment.</returns>
        private static ParametricSlidePath ParseSimai(string simaiSegment)
        {
            return SlideCodeParser.Parse(SimaiSlideTranslator.Translate(simaiSegment));
        }

        /// <summary>Gets the arrows an extend slide shows: the arrow poses without the path ends and the conditional arrow.</summary>
        /// <param name="metadata">The metadata.</param>
        /// <returns>The shown arrow poses.</returns>
        private static SlidePose[] GetShownArrows(ExtendSlideMetadata metadata)
        {
            var count = metadata.ArrowPoses.Length - 2 - (metadata.ConditionalLastArrow ? 1 : 0);
            return metadata.ArrowPoses.Skip(1).Take(count).ToArray();
        }

        /// <summary>Checks that two pose sequences have the same positions and rotations.</summary>
        /// <param name="actual">The poses to check.</param>
        /// <param name="expected">The expected poses.</param>
        private static void AssertSamePoses(IEnumerable<SlidePose> actual, IEnumerable<SlidePose> expected)
        {
            var actualPoses = actual.ToArray();
            var expectedPoses = expected.ToArray();
            Assert.That(actualPoses, Has.Length.EqualTo(expectedPoses.Length));
            for (var i = 0; i < expectedPoses.Length; i++)
            {
                Assert.That(actualPoses[i].X, Is.EqualTo(expectedPoses[i].X).Within(Tolerance), $"Arrow {i}");
                Assert.That(actualPoses[i].Y, Is.EqualTo(expectedPoses[i].Y).Within(Tolerance), $"Arrow {i}");
                Assert.That(actualPoses[i].RotZ, Is.EqualTo(expectedPoses[i].RotZ).Within(Tolerance), $"Arrow {i}");
            }
        }

        /// <summary>Checks whether a pose lies on a point.</summary>
        /// <param name="pose">The pose.</param>
        /// <param name="point">The point.</param>
        /// <returns><see langword="true"/> when the pose is within the pose tolerance of the point.</returns>
        private static bool IsAt(SlidePose pose, Complex point)
        {
            return Math.Abs(pose.X - point.Real) < Tolerance && Math.Abs(pose.Y - point.Imaginary) < Tolerance;
        }

        /// <summary>Converts poses into comparable tuples.</summary>
        /// <param name="poses">The poses.</param>
        /// <returns>The position, rotation and path length of every pose.</returns>
        private static (float X, float Y, float RotZ, float L)[] ToTuples(IEnumerable<SlidePose> poses)
        {
            return poses.Select(x => (x.X, x.Y, x.RotZ, x.L)).ToArray();
        }
    }
}
