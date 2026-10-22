using System;
using System.Collections.Generic;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers the search, aggregation and counting helpers in MajdataPlay.Collections.ReadOnlySpanExtensions.</summary>
    public class ReadOnlySpanExtensionsTests
    {
        /// <summary>Checks that IsEmpty reports whether the span has elements.</summary>
        [Test]
        public void IsEmpty_ReportsLength()
        {
            Assert.That(ReadOnlySpanExtensions.IsEmpty(ReadOnlySpan<int>.Empty), Is.True);
            Assert.That(ReadOnlySpanExtensions.IsEmpty(new ReadOnlySpan<int>(new int[1])), Is.False);
        }

        /// <summary>Checks Max for empty, single-element and multi-element spans.</summary>
        [Test]
        public void Max_FindsLargestElement()
        {
            Assert.Throws<InvalidOperationException>(() => ReadOnlySpanExtensions.Max(ReadOnlySpan<int>.Empty));
            Assert.That(ReadOnlySpanExtensions.Max(new ReadOnlySpan<int>(new[] { 4 })), Is.EqualTo(4));
            Assert.That(ReadOnlySpanExtensions.Max(new ReadOnlySpan<int>(new[] { 3, 9, -2, 9, 1 })), Is.EqualTo(9));
        }

        /// <summary>Checks Min for empty, single-element and multi-element spans.</summary>
        [Test]
        public void Min_FindsSmallestElement()
        {
            Assert.Throws<InvalidOperationException>(() => ReadOnlySpanExtensions.Min(ReadOnlySpan<int>.Empty));
            Assert.That(ReadOnlySpanExtensions.Min(new ReadOnlySpan<int>(new[] { 4 })), Is.EqualTo(4));
            Assert.That(ReadOnlySpanExtensions.Min(new ReadOnlySpan<int>(new[] { 3, 9, -2, 9, 1 })), Is.EqualTo(-2));
        }

        /// <summary>Checks that WithIndex enumerates every element with its position.</summary>
        [Test]
        public void WithIndex_EnumeratesPositions()
        {
            var pairs = new List<(int, char)>();

            foreach (var pair in ReadOnlySpanExtensions.WithIndex("ab".AsSpan()))
            {
                pairs.Add(pair);
            }

            Assert.That(pairs, Is.EqualTo(new[] { (0, 'a'), (1, 'b') }));
        }

        /// <summary>Checks that Find returns the first match or the default value.</summary>
        [Test]
        public void Find_ReturnsFirstMatchOrDefault()
        {
            var span = new ReadOnlySpan<int>(new[] { 1, 5, 7 });

            Assert.That(ReadOnlySpanExtensions.Find(span, x => x > 2), Is.EqualTo(5));
            Assert.That(ReadOnlySpanExtensions.Find(span, x => x > 20), Is.EqualTo(0));
        }

        /// <summary>Checks that FindAll returns the matches in order.</summary>
        [Test]
        public void FindAll_ReturnsMatches()
        {
            var span = new ReadOnlySpan<int>(new[] { 1, 2, 3, 4 });

            Assert.That(ReadOnlySpanExtensions.FindAll(span, x => (x % 2) == 0).ToArray(), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(ReadOnlySpanExtensions.FindAll(span, x => x > 10).Length, Is.EqualTo(0));
        }

        /// <summary>Checks that FindIndex returns the first matching position or minus one.</summary>
        [Test]
        public void FindIndex_ReturnsPosition()
        {
            var span = new ReadOnlySpan<int>(new[] { 9, 8, 8 });

            Assert.That(ReadOnlySpanExtensions.FindIndex(span, x => x == 8), Is.EqualTo(1));
            Assert.That(ReadOnlySpanExtensions.FindIndex(span, x => x == 0), Is.EqualTo(-1));
        }

        /// <summary>Checks that Contains uses element equality.</summary>
        [Test]
        public void Contains_UsesEquality()
        {
            var span = new ReadOnlySpan<string>(new[] { "x", "y" });

            Assert.That(ReadOnlySpanExtensions.Contains(span, "y"), Is.True);
            Assert.That(ReadOnlySpanExtensions.Contains(span, "z"), Is.False);
        }

        /// <summary>Checks that Any reports whether at least one element matches.</summary>
        [Test]
        public void Any_ReportsMatches()
        {
            var span = new ReadOnlySpan<int>(new[] { 1, 2, 3 });

            Assert.That(ReadOnlySpanExtensions.Any(span, x => x == 3), Is.True);
            Assert.That(ReadOnlySpanExtensions.Any(span, x => x == 4), Is.False);
        }

        /// <summary>Checks that counting a value finds every occurrence, including adjacent ones.</summary>
        [Test]
        public void CountValue_CountsOccurrences()
        {
            var span = new ReadOnlySpan<int>(new[] { 1, 2, 2, 3, 2 });

            Assert.That(ReadOnlySpanExtensions.Count(span, 2), Is.EqualTo(3));
            Assert.That(ReadOnlySpanExtensions.Count(span, 9), Is.EqualTo(0));
            Assert.That(ReadOnlySpanExtensions.Count(ReadOnlySpan<int>.Empty, 1), Is.EqualTo(0));
        }

        /// <summary>Checks that counting with a predicate counts matches and rejects a null predicate.</summary>
        [Test]
        public void CountPredicate_CountsMatches()
        {
            var values = new[] { 1, 2, 3, 4 };
            Func<int, bool> isEven = x => (x % 2) == 0;

            Assert.That(ReadOnlySpanExtensions.Count(new ReadOnlySpan<int>(values), isEven), Is.EqualTo(2));
            var exception = Assert.Throws<ArgumentNullException>(() => ReadOnlySpanExtensions.Count(new ReadOnlySpan<int>(values), (Func<int, bool>)null!));
            Assert.That(exception!.ParamName, Is.EqualTo("predicate"));
        }
    }
}
