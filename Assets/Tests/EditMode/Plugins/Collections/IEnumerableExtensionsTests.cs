using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers the sequence helpers in <see cref="IEnumerableExtensions"/>.</summary>
    public class IEnumerableExtensionsTests
    {
        /// <summary>Checks that IsEmpty distinguishes empty and non-empty sequences.</summary>
        [Test]
        public void IsEmpty_ReportsWhetherSequenceHasItems()
        {
            Assert.That(IEnumerableExtensions.IsEmpty(Enumerable.Empty<int>()), Is.True);
            Assert.That(IEnumerableExtensions.IsEmpty(Enumerable.Range(0, 1)), Is.False);
        }

        /// <summary>Checks that WithIndex pairs each item with its zero-based position.</summary>
        [Test]
        public void WithIndex_PairsItemsWithPositions()
        {
            var pairs = new[] { "a", "b", "c" }.AsEnumerable().WithIndex().ToArray();

            Assert.That(pairs, Is.EqualTo(new[] { (0, "a"), (1, "b"), (2, "c") }));
        }

        /// <summary>Checks that Find returns the first match or the default value.</summary>
        [Test]
        public void Find_ReturnsFirstMatchOrDefault()
        {
            IEnumerable<int> source = new List<int> { 1, 4, 6, 8 };

            Assert.That(IEnumerableExtensions.Find(source, x => x > 3), Is.EqualTo(4));
            Assert.That(IEnumerableExtensions.Find(source, x => x > 100), Is.EqualTo(0));
        }

        /// <summary>Checks that FindAll returns every match in order and an empty array when nothing matches.</summary>
        [Test]
        public void FindAll_ReturnsEveryMatch()
        {
            IEnumerable<int> source = new List<int> { 1, 2, 3, 4, 5 };

            Assert.That(IEnumerableExtensions.FindAll(source, x => (x % 2) == 1), Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(IEnumerableExtensions.FindAll(source, x => x > 10), Is.Empty);
        }

        /// <summary>Checks that FindIndex returns the first matching position or minus one.</summary>
        [Test]
        public void FindIndex_ReturnsFirstMatchingPosition()
        {
            IEnumerable<string> source = new List<string> { "x", "y", "y" };

            Assert.That(IEnumerableExtensions.FindIndex(source, s => s == "y"), Is.EqualTo(1));
            Assert.That(IEnumerableExtensions.FindIndex(source, s => s == "z"), Is.EqualTo(-1));
        }
    }
}
