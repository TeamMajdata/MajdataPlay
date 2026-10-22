using System;
using MajdataPlay.Utils;
using MajSimai;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies chart length, BPM range and density graph analysis in <see cref="ChartAnalyzer"/>.</summary>
    public class ChartAnalyzerTests
    {
        /// <summary>Tracks the textures created by the graph analysis.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the created textures.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that an empty chart produces the default result.</summary>
        [Test]
        public void AnalyzeMaidata_EmptyChart_ReturnsDefault()
        {
            var result = ChartAnalyzer.AnalyzeMaidata(SimaiChart.Empty);

            Assert.That(result.Length, Is.EqualTo(TimeSpan.Zero));
            Assert.That(result.MinBPM, Is.EqualTo(0f));
            Assert.That(result.MaxBPM, Is.EqualTo(0f));
        }

        /// <summary>Checks that the length is the timing of the last note and the BPM range of a constant chart collapses.</summary>
        [Test]
        public void AnalyzeMaidata_ConstantBpm_UsesLastNoteTiming()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,2,3,4,E".AsSpan());

            var result = ChartAnalyzer.AnalyzeMaidata(chart);

            Assert.That(result.Length.TotalSeconds, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(result.MinBPM, Is.EqualTo(120f));
            Assert.That(result.MaxBPM, Is.EqualTo(120f));
        }

        /// <summary>Checks that BPM changes widen the reported range.</summary>
        [Test]
        public void AnalyzeMaidata_BpmChanges_ReportsMinimumAndMaximum()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,(180)2,(90)3,E".AsSpan());

            var result = ChartAnalyzer.AnalyzeMaidata(chart);

            Assert.That(result.MinBPM, Is.EqualTo(90f));
            Assert.That(result.MaxBPM, Is.EqualTo(180f));
        }

        /// <summary>Checks that an explicit chart length overrides the last note timing.</summary>
        [Test]
        public void AnalyzeMaidata_ExplicitLength_OverridesNoteTiming()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,2,E".AsSpan());

            var result = ChartAnalyzer.AnalyzeMaidata(chart, 42.5f);

            Assert.That(result.Length.TotalSeconds, Is.EqualTo(42.5).Within(1e-6));
        }

        /// <summary>Checks that an empty chart produces no graph.</summary>
        [Test]
        public void AnalyzeMaidataWithGraph_EmptyChart_ReturnsDefault()
        {
            var result = ChartAnalyzer.AnalyzeMaidataWithGraph(SimaiChart.Empty, 32, 64);

            Assert.That(result.LineGraph, Is.Null);
            Assert.That(result.Length, Is.EqualTo(TimeSpan.Zero));
        }

        /// <summary>Checks that the graph has the requested size and draws bars only where the chart has notes.</summary>
        [Test]
        public void AnalyzeMaidataWithGraph_DrawsBarsWhereNotesAre()
        {
            var chart = SimaiParser.ParseChart("(120){4}1,,,,,,,,,,,,,,,2,E".AsSpan());

            var result = ChartAnalyzer.AnalyzeMaidataWithGraph(chart, 100, 640);
            var texture = (Texture2D)_objects.Track(result.LineGraph);

            Assert.That(texture.width, Is.EqualTo(640));
            Assert.That(texture.height, Is.EqualTo(100));
            Assert.That(result.Length.TotalSeconds, Is.EqualTo(7.5).Within(1e-6));
            Assert.That(MaxAlphaInColumn(texture, 5), Is.GreaterThan(0.5f), "first bar");
            Assert.That(MaxAlphaInColumn(texture, 635), Is.GreaterThan(0.5f), "last bar");
            Assert.That(MaxAlphaInColumn(texture, 325), Is.EqualTo(0f), "empty bar");
        }

        /// <summary>Checks that every note type contributes to the graph and repeated calls reuse the cached drawing state.</summary>
        [Test]
        public void AnalyzeMaidataWithGraph_AllNoteTypes_DrawRepeatedly()
        {
            var chart = SimaiParser.ParseChart("(120){4}1/C,1-5[4:1]/E1,2h[4:1]/Ch[4:1],3,E".AsSpan());

            var first = ChartAnalyzer.AnalyzeMaidataWithGraph(chart, 50, 320, 2f);
            var second = ChartAnalyzer.AnalyzeMaidataWithGraph(chart, 50, 320, 2f);
            var firstTexture = (Texture2D)_objects.Track(first.LineGraph);
            var secondTexture = (Texture2D)_objects.Track(second.LineGraph);

            Assert.That(first.MinBPM, Is.EqualTo(120f));
            Assert.That(MaxAlphaInColumn(firstTexture, 2), Is.GreaterThan(0.5f));
            Assert.That(secondTexture.GetPixels32(), Is.EqualTo(firstTexture.GetPixels32()));
        }

        /// <summary>Finds the largest alpha value in one pixel column.</summary>
        /// <param name="texture">The readable texture.</param>
        /// <param name="x">The column.</param>
        /// <returns>The maximum alpha in the column.</returns>
        private static float MaxAlphaInColumn(Texture2D texture, int x)
        {
            var max = 0f;
            for (var y = 0; y < texture.height; y++)
            {
                max = Mathf.Max(max, texture.GetPixel(x, y).a);
            }
            return max;
        }
    }
}
