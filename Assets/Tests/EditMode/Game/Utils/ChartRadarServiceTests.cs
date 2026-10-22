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
