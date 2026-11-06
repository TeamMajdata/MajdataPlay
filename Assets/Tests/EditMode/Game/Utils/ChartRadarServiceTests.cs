using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Utils.ChartRadar;
using MajRadar.Runtime;
using MajSimai;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies the Play-facing radar facade <see cref="ChartRadarService"/> and its snapshot type.</summary>
    public class ChartRadarServiceTests
    {
        /// <summary>
        /// A chart that mixes headed, same-head-group, headless (<c>?</c> and <c>!</c>) and marked slides with ordinary notes.
        /// </summary>
        private const string MixedSlideChart = "(150){8}1-5[8:1],2,3?-7[8:1],4,5-1[8:1]*-3[8:1]*>8[8:2],6/7," +
                                               "8!-4[8:1]/1b-5[8:1]m,2x,3$-6[4:1],{16}4,5,6,7,8@-3[8:1],1," +
                                               "2-6[8:1]/3-7[8:1],4h[8:1],B1,C,5V31[4:1],1-3-5-7[4:3],,,,E";

        /// <summary>
        /// The tolerance for radar values, which were computed on CoreCLR and are compared on Unity's runtime.
        /// </summary>
        private const double RadarValueTolerance = 1e-9;

        /// <summary>Checks that a small valid chart is analyzed successfully with every score present.</summary>
        [Test]
        public void Analyze_ValidChart_ReturnsSuccessfulSnapshot()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,2,3,4,1-5[4:1],E1,E".AsSpan());

            var snapshot = ChartRadarService.Analyze(chart);

            Assert.That(snapshot.IsSuccess, Is.True, string.Join("; ", snapshot.Errors));
            Assert.That(snapshot.Status, Is.EqualTo("ok"));
            Assert.That(snapshot.IsCancelled, Is.False);
            Assert.That(snapshot.FittedConstant, Is.Not.Null);
            Assert.That(snapshot.ModelVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(snapshot.MappingVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(snapshot.DimensionOrder, Is.EqualTo(RadarOutputDimensions.DefaultOrder));
            foreach (RadarOutputDimension dimension in Enum.GetValues(typeof(RadarOutputDimension)))
            {
                Assert.That(snapshot.GetScore(dimension), Is.Not.Null, dimension.ToString());
                Assert.That(snapshot.GetRawValue(dimension), Is.EqualTo(snapshot.RawValues[RadarOutputDimensions.Key(dimension)]));
            }
        }

        /// <summary>
        /// Checks that slides keep the radar values computed with MajSimai 2.2.2, where a slide with a head was one note.
        /// MajSimai 3 reads that slide as a star head tap followed by its slide, and the radar must not change.
        /// </summary>
        /// <param name="fumen">The chart text.</param>
        /// <param name="fittedConstant">The expected fitted constant.</param>
        /// <param name="note">The expected raw note value.</param>
        /// <param name="peak">The expected raw peak value.</param>
        /// <param name="sweep">The expected raw sweep value.</param>
        /// <param name="slideTricky">The expected raw slide tricky value.</param>
        /// <param name="slideSequence">The expected raw slide sequence value.</param>
        /// <param name="jack">The expected raw jack value.</param>
        [TestCase("(120){4}1-5[4:1],2,3,E",
                  10.90238276166452, 2.6666666666666665, 0.7999999999999999, 0d, 0.3653943384499391, 0d, 0d,
                  TestName = "Analyze_HeadedSlide_KeepsMajSimai2RadarValues")]
        [TestCase("(120){4}1-5[4:1]*-7[4:1],2,3,E",
                  10.91190178804231, 2.6666666666666665, 0.7999999999999999, 0d, 0.38809703203998563, 0.07024237842078739, 0d,
                  TestName = "Analyze_SameHeadGroup_KeepsMajSimai2RadarValues")]
        [TestCase("(120){4}1?-5[4:1],2,3,E",
                  10.751763631179315, 2d, 0.6, 0d, 0d, 0d, 0d,
                  TestName = "Analyze_HeadlessSlideWithQuestionMark_KeepsMajSimai2RadarValues")]
        [TestCase("(120){4}1!-5[4:1],2,3,E",
                  10.751763631179315, 2d, 0.6, 0d, 0d, 0d, 0d,
                  TestName = "Analyze_HeadlessSlideWithExclamationMark_KeepsMajSimai2RadarValues")]
        [TestCase(MixedSlideChart,
                  13.286733692704928, 8.728563302430421, 4.392852617380959, 3.651483716701106, 6.298857332207382,
                  0.9860430530030515, 0d,
                  TestName = "Analyze_MixedSlides_KeepsMajSimai2RadarValues")]
        public void Analyze_Slides_KeepMajSimai2RadarValues(string fumen,
                                                            double fittedConstant,
                                                            double note,
                                                            double peak,
                                                            double sweep,
                                                            double slideTricky,
                                                            double slideSequence,
                                                            double jack)
        {
            var chart = SimaiParser.ParseChart(fumen.AsSpan());

            var snapshot = ChartRadarService.Analyze(chart);

            Assert.That(snapshot.IsSuccess, Is.True, string.Join("; ", snapshot.Errors));
            Assert.That(snapshot.FittedConstant, Is.EqualTo(fittedConstant).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Note), Is.EqualTo(note).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Peak), Is.EqualTo(peak).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Sweep), Is.EqualTo(sweep).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.SlideTricky), Is.EqualTo(slideTricky).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.SlideSequence), Is.EqualTo(slideSequence).Within(RadarValueTolerance));
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Jack), Is.EqualTo(jack).Within(RadarValueTolerance));
        }

        /// <summary>Checks that the asynchronous facade returns the same result as the synchronous one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AnalyzeAsync_ValidChart_MatchesSynchronousResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var chart = SimaiParser.ParseChart("(150){8}1,2,3,4,5,6,7,8,E".AsSpan());

                var asynchronous = await ChartRadarService.AnalyzeAsync(chart);
                var synchronous = ChartRadarService.Analyze(chart);

                Assert.That(asynchronous.IsSuccess, Is.True, string.Join("; ", asynchronous.Errors));
                Assert.That(asynchronous.FittedConstant, Is.EqualTo(synchronous.FittedConstant));
                Assert.That(asynchronous.Scores, Is.EqualTo(synchronous.Scores));
            });
        }

        /// <summary>Checks that a missing chart is reported as a failed analysis.</summary>
        [Test]
        public void Analyze_NullChart_ReportsFailure()
        {
            var snapshot = ChartRadarService.Analyze(null!);

            Assert.That(snapshot.IsSuccess, Is.False);
            Assert.That(snapshot.Status, Is.EqualTo("error"));
            Assert.That(snapshot.Errors, Is.Not.Empty);
        }

        /// <summary>Checks that an already cancelled token yields a cancelled snapshot.</summary>
        [Test]
        public void Analyze_CancelledToken_ReportsCancellation()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,2,E".AsSpan());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var snapshot = ChartRadarService.Analyze(chart, cancellation.Token);

            Assert.That(snapshot.IsCancelled, Is.True);
            Assert.That(snapshot.IsSuccess, Is.False);
            Assert.That(snapshot.Status, Is.EqualTo("cancelled"));
        }

        /// <summary>Checks that a default snapshot reports an error and no values.</summary>
        [Test]
        public void DefaultSnapshot_HasNoValues()
        {
            var snapshot = new ChartRadarSnapshot();

            Assert.That(snapshot.Status, Is.EqualTo("error"));
            Assert.That(snapshot.GetScore(RadarOutputDimension.Note), Is.Null);
            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Jack), Is.Null);
            Assert.That(snapshot.DimensionOrder, Is.Empty);
            Assert.That(snapshot.Errors, Is.Empty);
        }

        /// <summary>Checks that snapshot lookups return the stored value for a dimension key.</summary>
        [Test]
        public void Snapshot_LooksUpValuesByDimensionKey()
        {
            var snapshot = new ChartRadarSnapshot
            {
                RawValues = new Dictionary<string, double?> { [RadarOutputDimensions.Peak] = 3.5 },
                Scores = new Dictionary<string, double?> { [RadarOutputDimensions.Sweep] = 77 },
            };

            Assert.That(snapshot.GetRawValue(RadarOutputDimension.Peak), Is.EqualTo(3.5));
            Assert.That(snapshot.GetScore(RadarOutputDimension.Sweep), Is.EqualTo(77));
            Assert.That(snapshot.GetScore(RadarOutputDimension.Peak), Is.Null);
        }

        /// <summary>Checks that a chart with an extended K slide is analyzed through the gameplay slide geometry.</summary>
        [Test]
        public void Analyze_ExtendedSlide_UsesTheProvider()
        {
            var chart = SimaiParser.ParseChart("(120){4}1P6K7[4:1],2,E".AsSpan());

            var snapshot = ChartRadarService.Analyze(chart);

            Assert.That(snapshot.IsSuccess, Is.True, string.Join("; ", snapshot.Errors));
        }

        /// <summary>Checks that the extended slide provider uses the gameplay slide geometry.</summary>
        /// <param name="slideCode">The slide code.</param>
        [TestCase("1K5")]
        [TestCase("1P6K7")]
        [TestCase("1B3K5")]
        public void ExtendedSlideProvider_MatchesGameplayGeometry(string slideCode)
        {
            var provider = new PlayExtendedSlideBarCountProvider();
            var expected = SlideDataBuilder.BuildArrowData(SlideCodeParser.Parse(slideCode)).Length - 2;

            var count = provider.ResolveBarCount(slideCode);

            Assert.That(count, Is.EqualTo(expected));
            Assert.That(count, Is.GreaterThan(0));
        }
    }
}
