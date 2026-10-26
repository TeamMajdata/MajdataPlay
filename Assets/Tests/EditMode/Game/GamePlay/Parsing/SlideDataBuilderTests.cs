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
