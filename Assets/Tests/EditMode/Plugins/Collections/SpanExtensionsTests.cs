using System;
using System.Collections.Generic;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers the search and aggregation helpers in MajdataPlay.Collections.SpanExtensions.</summary>
    public class SpanExtensionsTests
    {
        /// <summary>Checks that IsEmpty reports whether the span has elements.</summary>
        [Test]
        public void IsEmpty_ReportsLength()
        {
            Assert.That(SpanExtensions.IsEmpty(Span<int>.Empty), Is.True);
            Assert.That(SpanExtensions.IsEmpty(new int[1].AsSpan()), Is.False);
        }

        /// <summary>Checks Max for empty, single-element and multi-element spans.</summary>
        [Test]
        public void Max_FindsLargestElement()
        {
            Assert.Throws<InvalidOperationException>(() => SpanExtensions.Max(Span<int>.Empty));
            Assert.That(SpanExtensions.Max(new[] { 4 }.AsSpan()), Is.EqualTo(4));
            Assert.That(SpanExtensions.Max(new[] { 3, 9, -2, 9, 1 }.AsSpan()), Is.EqualTo(9));
        }

        /// <summary>Checks Min for empty, single-element and multi-element spans.</summary>
        [Test]
        public void Min_FindsSmallestElement()
        {
            Assert.Throws<InvalidOperationException>(() => SpanExtensions.Min(Span<int>.Empty));
            Assert.That(SpanExtensions.Min(new[] { 4 }.AsSpan()), Is.EqualTo(4));
            Assert.That(SpanExtensions.Min(new[] { 3, 9, -2, 9, 1 }.AsSpan()), Is.EqualTo(-2));
        }

        /// <summary>Checks that WithIndex enumerates every element with its position.</summary>
        [Test]
        public void WithIndex_EnumeratesPositions()
        {
            var pairs = new List<(int, string)>();

            foreach (var pair in SpanExtensions.WithIndex(new[] { "a", "b" }.AsSpan()))
            {
                pairs.Add(pair);
            }

            Assert.That(pairs, Is.EqualTo(new[] { (0, "a"), (1, "b") }));
        }

        /// <summary>Checks that Find returns the first match or the default value.</summary>
        [Test]
        public void Find_ReturnsFirstMatchOrDefault()
        {
            var span = new[] { 1, 5, 7 }.AsSpan();

            Assert.That(SpanExtensions.Find(span, x => x > 2), Is.EqualTo(5));
            Assert.That(SpanExtensions.Find(span, x => x > 20), Is.EqualTo(0));
        }

        /// <summary>Checks that FindAll returns the matches in a new buffer.</summary>
        [Test]
        public void FindAll_ReturnsMatches()
        {
            var source = new[] { 1, 2, 3, 4 };

            var matches = SpanExtensions.FindAll(source.AsSpan(), x => (x % 2) == 0);

            Assert.That(matches.ToArray(), Is.EqualTo(new[] { 2, 4 }));
            matches[0] = 100;
            Assert.That(source[1], Is.EqualTo(2));
            Assert.That(SpanExtensions.FindAll(source.AsSpan(), x => x > 10).Length, Is.EqualTo(0));
        }

        /// <summary>Checks that FindIndex returns the first matching position or minus one.</summary>
        [Test]
        public void FindIndex_ReturnsPosition()
        {
            var span = new[] { 9, 8, 8 }.AsSpan();

            Assert.That(SpanExtensions.FindIndex(span, x => x == 8), Is.EqualTo(1));
            Assert.That(SpanExtensions.FindIndex(span, x => x == 0), Is.EqualTo(-1));
        }

        /// <summary>Checks that Contains uses element equality.</summary>
        [Test]
        public void Contains_UsesEquality()
        {
            var span = new[] { "x", "y" }.AsSpan();

            Assert.That(SpanExtensions.Contains(span, "y"), Is.True);
            Assert.That(SpanExtensions.Contains(span, "z"), Is.False);
        }

        /// <summary>Checks that Any reports whether at least one element matches.</summary>
        [Test]
        public void Any_ReportsMatches()
        {
            var span = new[] { 1, 2, 3 }.AsSpan();

            Assert.That(SpanExtensions.Any(span, x => x == 3), Is.True);
            Assert.That(SpanExtensions.Any(span, x => x == 4), Is.False);
        }
    }
}
