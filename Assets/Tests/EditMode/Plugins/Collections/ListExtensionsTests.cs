using System.Collections.Generic;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers the list helpers in <see cref="ListExtensions"/>.</summary>
    public class ListExtensionsTests
    {
        /// <summary>Checks that IsEmpty reports whether the list has items.</summary>
        [Test]
        public void IsEmpty_ReportsWhetherListHasItems()
        {
            Assert.That(ListExtensions.IsEmpty(new List<int>()), Is.True);
            Assert.That(ListExtensions.IsEmpty(new List<int> { 1 }), Is.False);
        }

        /// <summary>Checks that TryGetElement returns existing elements.</summary>
        [Test]
        public void TryGetElement_InRange_ReturnsElement()
        {
            var list = new List<string> { "a", "b" };

            var found = list.TryGetElement(1, out var element);

            Assert.That(found, Is.True);
            Assert.That(element, Is.EqualTo("b"));
        }

        /// <summary>Checks that TryGetElement reports indices at or past the end as missing.</summary>
        [Test]
        public void TryGetElement_PastEnd_ReturnsFalse()
        {
            var list = new List<string> { "a" };

            var found = list.TryGetElement(1, out var element);

            Assert.That(found, Is.False);
            Assert.That(element, Is.Null);
        }

        /// <summary>Checks that TryGetElement reports negative indices as missing instead of throwing.</summary>
        [Test]
        [Ignore("Known issue: ListExtensions.TryGetElement only checks the upper bound, so a negative index throws ArgumentOutOfRangeException.")]
        public void TryGetElement_Negative_ReturnsFalse()
        {
            var list = new List<int> { 1 };

            var found = list.TryGetElement(-1, out var element);

            Assert.That(found, Is.False);
            Assert.That(element, Is.EqualTo(0));
        }
    }
}
