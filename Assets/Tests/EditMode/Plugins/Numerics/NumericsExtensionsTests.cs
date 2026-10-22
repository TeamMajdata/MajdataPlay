using MajdataPlay.Numerics;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Numerics
{
    /// <summary>Covers <see cref="NumericsExtensions.Clamp{T}"/> and <see cref="NumericsExtensions.InRange{T}"/>.</summary>
    public class NumericsExtensionsTests
    {
        /// <summary>Checks that Clamp returns the bound for values outside the interval and the value otherwise.</summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="expected">The expected result for the interval [0, 10].</param>
        [TestCase(-5, 0)]
        [TestCase(0, 0)]
        [TestCase(4, 4)]
        [TestCase(10, 10)]
        [TestCase(11, 10)]
        public void Clamp_KeepsValueInsideBounds(int value, int expected)
        {
            Assert.That(value.Clamp(0, 10), Is.EqualTo(expected));
        }

        /// <summary>Checks that Clamp works for reference types that implement IComparable.</summary>
        [Test]
        public void Clamp_Strings_UsesOrdinalComparison()
        {
            Assert.That("a".Clamp("b", "d"), Is.EqualTo("b"));
            Assert.That("z".Clamp("b", "d"), Is.EqualTo("d"));
            Assert.That("c".Clamp("b", "d"), Is.EqualTo("c"));
        }

        /// <summary>Checks that InRange treats both bounds as inclusive.</summary>
        /// <param name="value">The value to test.</param>
        /// <param name="expected">Whether the value is in [1.5, 2.5].</param>
        [TestCase(1.4f, false)]
        [TestCase(1.5f, true)]
        [TestCase(2.0f, true)]
        [TestCase(2.5f, true)]
        [TestCase(2.6f, false)]
        public void InRange_IsInclusive(float value, bool expected)
        {
            Assert.That(value.InRange(1.5f, 2.5f), Is.EqualTo(expected));
        }
    }
}
