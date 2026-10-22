using System;
using System.Collections.Generic;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>
    /// Covers the allocation-free enumerable wrappers: <see cref="ArrayHelper"/>, <see cref="ValueEnumerable{T}"/>,
    /// <see cref="ValueEnumerator{T}"/>, <see cref="SpanWithIndexEnumerable{T}"/> and <see cref="SpanWithIndexEnumerator{T}"/>.
    /// </summary>
    public class EnumerableWrapperTests
    {
        /// <summary>Checks that ArrayHelper wraps an array whose foreach yields every element in order.</summary>
        [Test]
        public void ArrayHelper_ToEnumerable_YieldsElements()
        {
            var items = new List<int>();

            foreach (var item in ArrayHelper.ToEnumerable(new[] { 3, 1, 2 }))
            {
                items.Add(item);
            }

            Assert.That(items, Is.EqualTo(new[] { 3, 1, 2 }));
        }

        /// <summary>Checks the value enumerator's Current before, during and after enumeration.</summary>
        [Test]
        public void ValueEnumerator_TracksCurrent()
        {
            var enumerator = new ValueEnumerable<string>(new[] { "a" }).GetEnumerator();

            Assert.That(enumerator.Current, Is.Null);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo("a"));
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.Current, Is.EqualTo("a"));
        }

        /// <summary>Checks that an empty array produces no elements.</summary>
        [Test]
        public void ValueEnumerator_EmptyArray_HasNoElements()
        {
            var enumerator = new ValueEnumerator<int>(Array.Empty<int>());

            Assert.That(enumerator.MoveNext(), Is.False);
        }

        /// <summary>Checks that the indexed span enumerator reports positions starting at zero.</summary>
        [Test]
        public void SpanWithIndexEnumerator_ReportsPositions()
        {
            var enumerator = new SpanWithIndexEnumerable<int>(new[] { 10, 20 }).GetEnumerator();

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo((0, 10)));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo((1, 20)));
            Assert.That(enumerator.MoveNext(), Is.False);
        }

        /// <summary>Checks that an empty span produces no indexed elements.</summary>
        [Test]
        public void SpanWithIndexEnumerator_Empty_HasNoElements()
        {
            var enumerator = new SpanWithIndexEnumerator<int>(ReadOnlySpan<int>.Empty);

            Assert.That(enumerator.MoveNext(), Is.False);
        }
    }
}
