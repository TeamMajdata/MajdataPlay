using System;
using MajdataPlay.Numerics;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Numerics
{
    /// <summary>Covers construction, bound flags and membership tests of <see cref="Range{T}"/>.</summary>
    public class RangeTests
    {
        /// <summary>Checks that a lower bound above the upper bound is rejected.</summary>
        [Test]
        public void Constructor_InvertedBounds_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Range<int>(5, 4, ContainsType.Closed));
        }

        /// <summary>Checks that an undefined containment type is rejected.</summary>
        [Test]
        public void Constructor_UndefinedType_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => new Range<int>(1, 2, (ContainsType)99));

            Assert.That(exception!.ParamName, Is.EqualTo("type"));
        }

        /// <summary>Checks that the two-argument constructor builds a closed interval.</summary>
        [Test]
        public void Constructor_WithoutType_IsClosed()
        {
            var range = new Range<int>(1, 3);

            Assert.That(range.Start, Is.EqualTo(1));
            Assert.That(range.End, Is.EqualTo(3));
            Assert.That(range.Type, Is.EqualTo(ContainsType.Closed));
            Assert.That(range.IsStartInclusive, Is.True);
            Assert.That(range.IsEndInclusive, Is.True);
        }

        /// <summary>Checks the inclusivity flags derived from each containment type.</summary>
        /// <param name="type">The containment type.</param>
        /// <param name="startInclusive">The expected start inclusivity.</param>
        /// <param name="endInclusive">The expected end inclusivity.</param>
        [TestCase(ContainsType.Closed, true, true)]
        [TestCase(ContainsType.LeftOpen, false, true)]
        [TestCase(ContainsType.RightOpen, true, false)]
        [TestCase(ContainsType.Open, false, false)]
        public void Constructor_SetsInclusivity(ContainsType type, bool startInclusive, bool endInclusive)
        {
            var range = new Range<int>(0, 10, type);

            Assert.That(range.Type, Is.EqualTo(type));
            Assert.That(range.IsStartInclusive, Is.EqualTo(startInclusive));
            Assert.That(range.IsEndInclusive, Is.EqualTo(endInclusive));
        }

        /// <summary>Checks membership at, inside and outside the bounds for every containment type.</summary>
        /// <param name="type">The containment type of the interval between 0 and 10.</param>
        /// <param name="value">The value to test.</param>
        /// <param name="expected">Whether the value belongs to the interval.</param>
        [TestCase(ContainsType.Closed, -1, false)]
        [TestCase(ContainsType.Closed, 0, true)]
        [TestCase(ContainsType.Closed, 5, true)]
        [TestCase(ContainsType.Closed, 10, true)]
        [TestCase(ContainsType.Closed, 11, false)]
        [TestCase(ContainsType.LeftOpen, 0, false)]
        [TestCase(ContainsType.LeftOpen, 5, true)]
        [TestCase(ContainsType.LeftOpen, 10, true)]
        [TestCase(ContainsType.LeftOpen, 11, false)]
        [TestCase(ContainsType.RightOpen, -1, false)]
        [TestCase(ContainsType.RightOpen, 0, true)]
        [TestCase(ContainsType.RightOpen, 5, true)]
        [TestCase(ContainsType.RightOpen, 10, false)]
        [TestCase(ContainsType.Open, 0, false)]
        [TestCase(ContainsType.Open, 5, true)]
        [TestCase(ContainsType.Open, 10, false)]
        [TestCase(ContainsType.Open, -3, false)]
        public void InRange_RespectsContainmentType(ContainsType type, int value, bool expected)
        {
            var range = new Range<int>(0, 10, type);

            Assert.That(range.InRange(value), Is.EqualTo(expected));
        }

        /// <summary>Checks that a degenerate closed interval contains only its single point.</summary>
        [Test]
        public void InRange_DegenerateClosedInterval_ContainsSinglePoint()
        {
            var range = new Range<double>(2.5, 2.5);

            Assert.That(range.InRange(2.5), Is.True);
            Assert.That(range.InRange(2.4), Is.False);
        }

        /// <summary>Checks that a range whose type was corrupted to an undefined value contains nothing.</summary>
        [Test]
        public void InRange_UndefinedType_ReturnsFalse()
        {
            object boxed = new Range<int>(0, 10);
            PrivateMember.SetProperty(boxed, nameof(Range<int>.Type), (ContainsType)99);
            var range = (Range<int>)boxed;

            Assert.That(range.InRange(5), Is.False);
        }

        /// <summary>Checks that a System.Range converts to a right-open integer interval.</summary>
        [Test]
        public void FromRange_BuildsRightOpenInterval()
        {
            var range = Range<int>.FromRange(2..5);

            Assert.That(range.Start, Is.EqualTo(2));
            Assert.That(range.End, Is.EqualTo(5));
            Assert.That(range.Type, Is.EqualTo(ContainsType.RightOpen));
            Assert.That(range.InRange(4), Is.True);
            Assert.That(range.InRange(5), Is.False);
        }
    }
}
