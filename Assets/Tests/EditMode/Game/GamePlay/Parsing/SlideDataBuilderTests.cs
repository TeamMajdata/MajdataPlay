using System;
using System.Linq;
using System.Numerics;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks the arrow placement and judge area detection of <see cref="SlideDataBuilder"/>.</summary>
    public class SlideDataBuilderTests
    {
        /// <summary>The tolerance used for path lengths in judge areas.</summary>
        private const double AreaTolerance = 1e-3;

        /// <summary>The path length around a segment end within which an arrow counts as the segment end.</summary>
        private const double JointTolerance = 1e-4;

        /// <summary>Builds the judge area lookup table used by every test.</summary>
        [SetUp]
        public void SetUp()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();
        }

        /// <summary>Checks the size of the lookup table and a few representative entries.</summary>
        [Test]
        public void InitializeSlideAreaLookup_FillsTheTransitionTable()
        {
            var lookup = SlideDataBuilder.SlideAreaLookup;

            Assert.That(lookup, Has.Count.EqualTo(176));
            var a1ToA2 = lookup[(0 << 5) | 1];
            Assert.That(a1ToA2.Select(x => x.SensorA), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(a1ToA2[0].LengthAfterPush, Is.EqualTo(0.32));
            var a1ToA3 = lookup[(0 << 5) | 2];
            Assert.That(a1ToA3[1].SensorA, Is.EqualTo(1));
            Assert.That(a1ToA3[1].SensorB, Is.EqualTo(9));
            var centerToB1 = lookup[(16 << 5) | 8];
            Assert.That(centerToB1.Select(x => x.SensorA), Is.EqualTo(new[] { 16, 8 }));
            Assert.That(lookup.ContainsKey((0 << 5) | 4), Is.False, "Opposite A areas are never directly adjacent");
        }

        /// <summary>Checks that re-initialization rebuilds the same table instead of accumulating entries.</summary>
        [Test]
        public void InitializeSlideAreaLookup_IsIdempotent()
        {
            SlideDataBuilder.SlideAreaLookup[-1] = Array.Empty<SlideAreaRawData>();

            SlideDataBuilder.InitializeSlideAreaLookup();

            Assert.That(SlideDataBuilder.SlideAreaLookup, Has.Count.EqualTo(176));
            Assert.That(SlideDataBuilder.SlideAreaLookup.ContainsKey(-1), Is.False);
        }

        /// <summary>Checks the arrows of a straight slide: evenly spaced, from start to end, with the end appended.</summary>
        [Test]
        public void BuildArrowData_SpacesArrowsEvenlyOnAStraightPath()
        {
            var arrows = SlideDataBuilder.BuildArrowData(SlideCodeParser.Parse("1CK5"));

            Assert.That(arrows, Has.Length.EqualTo(22));
            Assert.That(arrows[0].PathLength, Is.EqualTo(0));
            Assert.That(arrows[0].Point, Is.EqualTo(SlideGeo.PointGroupA(1)));
            for (var i = 1; i < arrows.Length - 1; i++)
            {
                Assert.That(arrows[i].PathLength - arrows[i - 1].PathLength, Is.EqualTo(SlideGeo.DefaultDistance).Within(1e-9));
                Assert.That(arrows[i].Direction.Magnitude, Is.EqualTo(1).Within(1e-9));
            }
            Assert.That(arrows[^1].PathLength, Is.EqualTo(9.6).Within(1e-9));
            Assert.That(arrows[^1].Point.Real, Is.EqualTo(SlideGeo.PointGroupA(5).Real).Within(1e-9));
            Assert.That(arrows[^1].Point.Imaginary, Is.EqualTo(SlideGeo.PointGroupA(5).Imaginary).Within(1e-9));
        }

        /// <summary>Checks that arrows on a curve point from the previous arrow to the current one.</summary>
        [Test]
        public void BuildArrowData_CurvedArrowsFollowTheChord()
        {
            var arrows = SlideDataBuilder.BuildArrowData(SlideCodeParser.Parse("1Q9K3"));
            var chord = arrows[1].Point - arrows[0].Point;
            chord /= chord.Magnitude;

            Assert.That(arrows, Has.Length.EqualTo(18));
            Assert.That(arrows[1].Direction.Real, Is.EqualTo(chord.Real).Within(1e-9));
            Assert.That(arrows[1].Direction.Imaginary, Is.EqualTo(chord.Imaginary).Within(1e-9));
        }

        /// <summary>Checks that a force-aligned segment restarts the arrow spacing at its end point.</summary>
        [Test]
        public void BuildArrowData_ForceAlignRestartsAtTheSegmentEnd()
        {
            var path = SlideCodeParser.Parse("1CA2Q9K4");
            var arrows = SlideDataBuilder.BuildArrowData(path);
            var expected = path.AccumulatedLengths[1] + SlideGeo.DefaultDistance;

            Assert.That(arrows, Has.Length.EqualTo(38));
            Assert.That(arrows.Any(x => Math.Abs(x.PathLength - expected) < 1e-9), Is.True);
        }

        /// <summary>Checks that a smooth-aligned transfer adjusts the spacing so the arrows land on the segment end.</summary>
        [Test]
        public void BuildArrowData_SmoothAlignAdjustsTheSpacing()
        {
            var path = SlideCodeParser.Parse("1Q1Q9K3");
            var arrows = SlideDataBuilder.BuildArrowData(path);
            var transfer = path.Segments[2];

            Assert.That(arrows, Has.Length.EqualTo(49));
            Assert.That(transfer.ArrowDistance, Is.EqualTo(0.463558).Within(1e-6));
            Assert.That(arrows.Any(x => Math.Abs(x.PathLength - path.AccumulatedLengths[2]) < 1e-6), Is.True);
        }

        /// <summary>Checks that a last arrow at most half an arrow distance from the path end is conditional.</summary>
        [Test]
        public void IsLastArrowConditional_UsesHalfTheDefaultDistance()
        {
            var end = new SlideArrowRawData(Complex.Zero, Complex.One, 10);
            var half = SlideGeo.DefaultDistance / 2;

            Assert.That(SlideDataBuilder.IsLastArrowConditional(new SlideArrowRawData(Complex.Zero, Complex.One, 10 - half), end), Is.True);
            Assert.That(SlideDataBuilder.IsLastArrowConditional(new SlideArrowRawData(Complex.Zero, Complex.One, 10 - half - 1e-6), end), Is.False);
        }

        /// <summary>
        /// Checks that every segment of a connection slide gets exactly the arrows of the standalone slide of its shape, so
        /// that the 1q8 part of 2^1q8, 3^1q8, 4^1q8 and 5-1q8 overlaps 1q8, whether arc joints are filled or not.
        /// </summary>
        /// <param name="chain">The simai segments joined with <c>|</c>.</param>
        /// <param name="isArcJointFilled">Whether arc joints are filled.</param>
        [Test]
        public void BuildSegmentedArrowData_EverySegmentOverlapsItsStandaloneSlide(
            [Values("2^1|1q8", "3^1|1q8", "4^1|1q8", "5-1|1q8", "8>1|1q8", "1^2|2^3|3q1", "1q8|8-4")] string chain,
            [Values] bool isArcJointFilled)
        {
            var segments = chain.Split('|');

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments(segments), isArcJointFilled, out _);

            AssertEverySegmentOverlapsItsStandaloneSlide(arrows, segments);
        }

        /// <summary>
        /// Checks that the joint of two arcs of the judgment circle gets no arrow but is passed by the star, which leaves
        /// a gap of two arrow distances, at every rotation, including those where rounding places an arrow on the end of
        /// the first arc.
        /// </summary>
        /// <param name="startKey">The start key of <c>1^2^3</c> rotated.</param>
        [Test]
        public void BuildSegmentedArrowData_LeavesAGapWhereTwoArcsMeet([Range(1, 8)] int startKey)
        {
            var segments = RotateChain(startKey, "1^2", "2^3");
            var joint = SlideGeo.PointGroupA(WrapKey(startKey + 1));

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments(segments), false, out var stars);

            Assert.That(arrows, Has.Length.EqualTo(16), "The path start, 7 + 7 arrows and the path end");
            Assert.That(arrows.Count(x => IsAt(x, joint)), Is.Zero, "No arrow at the joint");
            Assert.That(stars, Has.Length.EqualTo(17));
            Assert.That(stars.Count(x => IsAt(x, joint)), Is.EqualTo(1), "The star passes the joint");
            Assert.That(GetGaps(arrows).Max(), Is.EqualTo(2 * SlideGeo.DefaultDistance).Within(1e-6));
            Assert.That(GetGaps(stars), Has.All.EqualTo(SlideGeo.DefaultDistance).Within(1e-6));
        }

        /// <summary>
        /// Checks that a filled joint of two arcs gets an arrow that keeps the arrow distance and points from the previous
        /// arrow, like every other arrow on an arc.
        /// </summary>
        /// <param name="startKey">The start key of <c>1^2^3</c> rotated.</param>
        [Test]
        public void BuildSegmentedArrowData_FillsTheJointOfTwoArcs([Range(1, 8)] int startKey)
        {
            var segments = RotateChain(startKey, "1^2", "2^3");
            var joint = SlideGeo.PointGroupA(WrapKey(startKey + 1));

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments(segments), true, out var stars);

            Assert.That(arrows, Has.Length.EqualTo(17));
            Assert.That(stars, Is.EqualTo(arrows), "The star passes every arrow");
            Assert.That(GetGaps(arrows), Has.All.EqualTo(SlideGeo.DefaultDistance).Within(1e-6));
            var jointIndex = Array.FindIndex(arrows, x => IsAt(x, joint));
            var chord = arrows[jointIndex].Point - arrows[jointIndex - 1].Point;
            chord /= chord.Magnitude;
            Assert.That((arrows[jointIndex].Direction - chord).Magnitude, Is.LessThan(1e-9));
        }

        /// <summary>Checks that a filled arc joint also covers an arc that turns back.</summary>
        [Test]
        public void BuildSegmentedArrowData_FillsTheJointOfArcsTurningBack()
        {
            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments("1>2", "2<1"), true, out _);

            Assert.That(arrows.Count(x => IsAt(x, SlideGeo.PointGroupA(2))), Is.EqualTo(1));
        }

        /// <summary>Checks that joints other than two arcs get no arrow even when arc joints are filled.</summary>
        /// <param name="first">The first simai segment.</param>
        /// <param name="second">The second simai segment.</param>
        /// <param name="jointKey">The key where the segments meet.</param>
        [TestCase("1-4", "4-7", 4)]
        [TestCase("2^1", "1q8", 1)]
        [TestCase("1q8", "8^7", 8)]
        public void BuildSegmentedArrowData_FillsOnlyArcJoints(string first, string second, int jointKey)
        {
            var joint = SlideGeo.PointGroupA(jointKey);

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments(first, second), true, out var stars);

            Assert.That(arrows.Count(x => IsAt(x, joint)), Is.Zero);
            Assert.That(stars.Count(x => IsAt(x, joint)), Is.EqualTo(1));
            Assert.That(stars, Has.Length.EqualTo(arrows.Length + 1 - (IsLastArrowConditional(arrows) ? 1 : 0)));
        }

        /// <summary>
        /// Checks that, as in the official game, the conditional last arrow of a segment followed by another one is shown
        /// and passed by the star, while that of the final segment is only kept for the metadata to hide.
        /// </summary>
        [Test]
        public void BuildSegmentedArrowData_ShowsTheConditionalArrowOfAnEarlierSegment()
        {
            var standalone = SlideDataBuilder.BuildArrowData(ParseSegments("1q8")[0]);
            Assume.That(SlideDataBuilder.IsLastArrowConditional(standalone[^2], standalone[^1]), Is.True, "1q8 ends with a conditional arrow");

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments("1q8", "8-4"), false, out var stars);

            Assert.That(arrows.Count(x => IsAt(x, standalone[^2].Point)), Is.EqualTo(1));
            Assert.That(stars.Count(x => IsAt(x, standalone[^2].Point)), Is.EqualTo(1));
            Assert.That(IsLastArrowConditional(arrows), Is.True, "8-4 ends with a conditional arrow");
            Assert.That(stars.Count(x => IsAt(x, arrows[^2].Point)), Is.Zero);
        }

        /// <summary>Checks that a single segment keeps the arrows of <see cref="SlideDataBuilder.BuildArrowData"/>.</summary>
        [Test]
        public void BuildSegmentedArrowData_KeepsTheArrowsOfASingleSegment()
        {
            var expected = SlideDataBuilder.BuildArrowData(ParseSegments("1q8")[0]);

            var arrows = SlideDataBuilder.BuildSegmentedArrowData(ParseSegments("1q8"), false, out var stars);

            Assert.That(arrows, Is.EqualTo(expected));
            Assert.That(stars, Is.EqualTo(expected.Where((_, i) => i != expected.Length - 2)), "The star skips the conditional arrow");
        }

        /// <summary>Checks the arguments of <see cref="SlideDataBuilder.BuildSegmentedArrowData"/>.</summary>
        [Test]
        public void BuildSegmentedArrowData_RejectsMissingSegments()
        {
            Assert.Throws<ArgumentNullException>(() => SlideDataBuilder.BuildSegmentedArrowData(null!, false, out _));
            Assert.Throws<ArgumentException>(() => SlideDataBuilder.BuildSegmentedArrowData(Array.Empty<ParametricSlidePath>(), false, out _));
        }

        /// <summary>Checks the judge areas of a straight slide through the centre, ending with the long tail distance.</summary>
        [Test]
        public void BuildSlideAreas_StraightSlideThroughTheCentre()
        {
            var areas = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse("1CK5"));

            Assert.That(areas.Select(x => x.SensorA), Is.EqualTo(new[] { 0, 8, 16, 12, 4 }));
            Assert.That(areas.All(x => x.SensorB == -1), Is.True);
            Assert.That(areas[0].LengthAfterPush, Is.EqualTo(1.375).Within(AreaTolerance));
            Assert.That(areas[0].LengthAfterFinish, Is.EqualTo(2.2).Within(AreaTolerance));
            Assert.That(areas[^2].LengthAfterFinish, Is.EqualTo(9.6 - SlideDataBuilder.LastDistanceLong).Within(AreaTolerance));
            Assert.That(areas[^1].LengthAfterPush, Is.EqualTo(9.6).Within(1e-9));
            Assert.That(areas[^1].LengthAfterFinish, Is.EqualTo(9.6).Within(1e-9));
        }

        /// <summary>Checks that a straight slide between neighbouring A areas uses the short tail distance.</summary>
        [Test]
        public void BuildSlideAreas_AToAUsesTheShortTailDistance()
        {
            var path = SlideCodeParser.Parse("1A2K3");
            var areas = SlideDataBuilder.BuildSlideAreas(path);

            Assert.That(areas.Select(x => x.SensorA), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(areas[^2].LengthAfterFinish, Is.EqualTo(path.GetPathLength() - SlideDataBuilder.LastDistanceShort).Within(AreaTolerance));
        }

        /// <summary>Checks that a slide ending on an arc uses the circle tail distance.</summary>
        [Test]
        public void BuildSlideAreas_ArcUsesTheCircleTailDistance()
        {
            var path = SlideCodeParser.Parse("1Q9K3");
            var areas = SlideDataBuilder.BuildSlideAreas(path);

            Assert.That(areas.Select(x => x.SensorA), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(areas[^2].LengthAfterFinish, Is.EqualTo(path.GetPathLength() - SlideDataBuilder.LastDistanceCircle).Within(AreaTolerance));
        }

        /// <summary>Checks that paths crossing two sensors at once produce OR areas from the lookup table.</summary>
        [Test]
        public void BuildSlideAreas_ProducesOrAreas()
        {
            var skip = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse("1K3"));
            var ppqq = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse("1P2K5"));

            Assert.That(skip.Select(x => (x.SensorA, x.SensorB)), Is.EqualTo(new[] { (0, -1), (1, 9), (2, -1) }));
            Assert.That(ppqq.Select(x => (x.SensorA, x.SensorB)), Is.EqualTo(new[] { (0, -1), (15, -1), (14, 16), (12, 13), (4, -1) }));
        }

        /// <summary>
        /// Checks that the slide code equivalent of every prefab slide shape produces the same judge areas,
        /// including the sensors of OR areas, as the predefined queue of <see cref="SlideTables"/>.
        /// </summary>
        /// <param name="tableName">The predefined table name of the prefab shape.</param>
        /// <param name="code">The slide code that draws the same path as the prefab, starting at key 1.</param>
        [TestCase("circle1", "1Q9K1")]
        [TestCase("circle2", "1Q9K2")]
        [TestCase("circle3", "1Q9K3")]
        [TestCase("circle4", "1Q9K4")]
        [TestCase("circle5", "1Q9K5")]
        [TestCase("circle6", "1Q9K6")]
        [TestCase("circle7", "1Q9K7")]
        [TestCase("circle8", "1Q9K8")]
        [TestCase("line3", "1K3")]
        [TestCase("line4", "1K4")]
        [TestCase("line5", "1K5")]
        [TestCase("line6", "1K6")]
        [TestCase("line7", "1K7")]
        [TestCase("v1", "1CK1")]
        [TestCase("v2", "1CK2")]
        [TestCase("v3", "1CK3")]
        [TestCase("v4", "1CK4")]
        [TestCase("v6", "1CK6")]
        [TestCase("v7", "1CK7")]
        [TestCase("v8", "1CK8")]
        [TestCase("ppqq1", "1P3K1")]
        [TestCase("ppqq2", "1P3K2")]
        [TestCase("ppqq3", "1P3K3")]
        [TestCase("ppqq4", "1P3P3K4")]
        [TestCase("ppqq5", "1P3P3K5")]
        [TestCase("ppqq6", "1P3K6")]
        [TestCase("ppqq7", "1P3K7")]
        [TestCase("ppqq8", "1P3K8")]
        [TestCase("L2", "1A7K2")]
        [TestCase("L3", "1A7K3")]
        [TestCase("L4", "1A7K4")]
        [TestCase("L5", "1A7K5")]
        [TestCase("s", "1B7B3K5")]
        [TestCase("pq1", "1P0K1")]
        [TestCase("pq2", "1P0K2")]
        [TestCase("pq3", "1P0K3")]
        [TestCase("pq4", "1P0K4")]
        [TestCase("pq5", "1P0K5")]
        [TestCase("pq6", "1P0K6")]
        [TestCase("pq7", "1P0K7")]
        [TestCase("pq8", "1P0K8")]
        public void BuildSlideAreas_MatchesThePredefinedTableOfEachPrefabShape(string tableName, string code)
        {
            using var table = SlideTables.FindTableByName(tableName);
            Assert.That(table, Is.Not.Null, tableName);
            var queue = table!.JudgeQueue.Span;
            var expected = new string[queue.Length];
            for (var i = 0; i < queue.Length; i++)
            {
                expected[i] = DescribeArea(queue[i].IncludedAreas.ToArray());
            }

            var areas = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse(code));
            var actual = areas.Select(DescribeArea).ToArray();

            Assert.That(actual, Is.EqualTo(expected), $"{tableName} = {code}");
        }

        /// <summary>Checks that the push and finish lengths increase along the slide.</summary>
        /// <param name="code">A valid slide code.</param>
        [TestCase("1CK5")]
        [TestCase("1P9K3")]
        [TestCase("1P0K5")]
        [TestCase("1Q1Q9K3")]
        [TestCase("1P0P1K5")]
        public void BuildSlideAreas_LengthsIncreaseMonotonically(string code)
        {
            var path = SlideCodeParser.Parse(code);
            var areas = SlideDataBuilder.BuildSlideAreas(path);
            var previous = 0.0;

            foreach (var area in areas)
            {
                Assert.That(area.LengthAfterPush, Is.GreaterThanOrEqualTo(previous));
                Assert.That(area.LengthAfterFinish, Is.GreaterThanOrEqualTo(area.LengthAfterPush - 1e-9));
                previous = area.LengthAfterPush;
            }
            Assert.That(areas[0].SensorA, Is.EqualTo(0));
            Assert.That(areas[^1].LengthAfterPush, Is.EqualTo(path.GetPathLength()).Within(1e-9));
        }

        /// <summary>Checks the raw data value types keep their constructor arguments.</summary>
        [Test]
        public void RawDataTypes_StoreTheirValues()
        {
            var area = new SlideAreaRawData(0.5, 0.75, 3);
            var orArea = new SlideAreaRawData(0.1, 0.2, 3, 16);
            var arrow = new SlideArrowRawData(new Complex(1, 2), new Complex(0, 1), 4.5);

            Assert.That(area.LengthAfterPush, Is.EqualTo(0.5));
            Assert.That(area.LengthAfterFinish, Is.EqualTo(0.75));
            Assert.That(area.SensorA, Is.EqualTo(3));
            Assert.That(area.SensorB, Is.EqualTo(-1));
            Assert.That(orArea.SensorB, Is.EqualTo(16));
            Assert.That(arrow.Point, Is.EqualTo(new Complex(1, 2)));
            Assert.That(arrow.Direction, Is.EqualTo(new Complex(0, 1)));
            Assert.That(arrow.PathLength, Is.EqualTo(4.5));
        }

        /// <summary>Checks the judge area detection constants derived from the main radius.</summary>
        [Test]
        public void DetectionConstants_AreScaledFromTheMainRadius()
        {
            Assert.That(SlideDataBuilder.HitAreaCalcStep, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(SlideDataBuilder.LastDistanceShort, Is.EqualTo(1.3).Within(1e-12));
            Assert.That(SlideDataBuilder.LastDistanceLong, Is.EqualTo(1.59).Within(1e-12));
            Assert.That(SlideDataBuilder.LastDistanceCircle, Is.EqualTo(1.75).Within(1e-12));
            Assert.That(SlideDataBuilder.HitAreaARadius, Is.EqualTo(0.8).Within(1e-12));
        }

        /// <summary>
        /// Checks that, inside the path range of every segment, a connection slide has the arrows of the standalone slide
        /// of that segment: the same count, points and directions, with path lengths moved by the segments before it.
        /// </summary>
        /// <param name="chainArrows">The arrows of the connection slide.</param>
        /// <param name="simaiSegments">The simai segments of the connection slide.</param>
        private static void AssertEverySegmentOverlapsItsStandaloneSlide(SlideArrowRawData[] chainArrows, string[] simaiSegments)
        {
            var startLength = 0.0;
            foreach (var simaiSegment in simaiSegments)
            {
                var standalone = SlideDataBuilder.BuildArrowData(ParseSegments(simaiSegment)[0]);
                var length = standalone[^1].PathLength;
                var expected = standalone.Where(x => x.PathLength > JointTolerance && x.PathLength < length - JointTolerance).ToArray();
                var actual = chainArrows.Where(x => x.PathLength > startLength + JointTolerance &&
                                                    x.PathLength < startLength + length - JointTolerance).ToArray();

                Assert.That(actual, Has.Length.EqualTo(expected.Length), simaiSegment);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.That((actual[i].Point - expected[i].Point).Magnitude, Is.LessThan(1e-9), $"{simaiSegment} arrow {i}");
                    Assert.That((actual[i].Direction - expected[i].Direction).Magnitude, Is.LessThan(1e-9), $"{simaiSegment} arrow {i}");
                    Assert.That(actual[i].PathLength, Is.EqualTo(startLength + expected[i].PathLength).Within(1e-9), $"{simaiSegment} arrow {i}");
                }
                startLength += length;
            }
        }

        /// <summary>Parses simai segments the way the note loader parses the segments of a connection slide.</summary>
        /// <param name="simaiSegments">The simai segments, such as <c>1-4</c>.</param>
        /// <returns>The path of every segment.</returns>
        private static ParametricSlidePath[] ParseSegments(params string[] simaiSegments)
        {
            return simaiSegments.Select(x => SlideCodeParser.Parse(SimaiSlideTranslator.Translate(x))).ToArray();
        }

        /// <summary>Rotates simai segments written from key 1 so that they start at another key.</summary>
        /// <param name="startKey">The key the first segment starts at.</param>
        /// <param name="simaiSegments">The simai segments starting at key 1.</param>
        /// <returns>The rotated segments.</returns>
        private static string[] RotateChain(int startKey, params string[] simaiSegments)
        {
            return simaiSegments.Select(x => new string(x.Select(c => char.IsDigit(c) ? (char)('0' + WrapKey(c - '0' + startKey - 1)) : c).ToArray())).ToArray();
        }

        /// <summary>Wraps a key that may have left the range 1 to 8.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The key between 1 and 8.</returns>
        private static int WrapKey(int key)
        {
            return ((key - 1) % 8 + 8) % 8 + 1;
        }

        /// <summary>Checks whether an arrow lies on a point.</summary>
        /// <param name="arrow">The arrow.</param>
        /// <param name="point">The point.</param>
        /// <returns><see langword="true"/> when the arrow is within 1e-6 of the point.</returns>
        private static bool IsAt(SlideArrowRawData arrow, Complex point)
        {
            return (arrow.Point - point).Magnitude < 1e-6;
        }

        /// <summary>Gets the path length between every two consecutive arrows.</summary>
        /// <param name="arrows">The arrows, including the path start and end.</param>
        /// <returns>The gaps, without the gap before the path end, which is not an arrow distance.</returns>
        private static double[] GetGaps(SlideArrowRawData[] arrows)
        {
            return arrows.Skip(1).Take(arrows.Length - 2).Select((x, i) => x.PathLength - arrows[i].PathLength).ToArray();
        }

        /// <summary>Checks whether the last arrow of an arrow layout is conditional.</summary>
        /// <param name="arrows">The arrows, including the path start and end.</param>
        /// <returns>The result of <see cref="SlideDataBuilder.IsLastArrowConditional"/> for the last arrow.</returns>
        private static bool IsLastArrowConditional(SlideArrowRawData[] arrows)
        {
            return SlideDataBuilder.IsLastArrowConditional(arrows[^2], arrows[^1]);
        }

        /// <summary>Formats the sensors of a generated judge area independently of their order.</summary>
        /// <param name="area">The generated judge area.</param>
        /// <returns>The sensor names in enum order, joined with <c>|</c>.</returns>
        private static string DescribeArea(SlideAreaRawData area)
        {
            if (area.SensorB == -1)
            {
                return DescribeArea(new[] { (SensorArea)area.SensorA });
            }
            return DescribeArea(new[] { (SensorArea)area.SensorA, (SensorArea)area.SensorB });
        }

        /// <summary>Formats the sensors of a judge area independently of their order.</summary>
        /// <param name="sensors">The sensors that satisfy the area.</param>
        /// <returns>The sensor names in enum order, joined with <c>|</c>.</returns>
        private static string DescribeArea(SensorArea[] sensors)
        {
            return string.Join("|", sensors.OrderBy(x => x).Select(x => x.ToString()));
        }
    }
}
