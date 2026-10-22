#nullable enable
using System;
using MajdataPlay.Net;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.Net
{
    /// <summary>Covers the factory methods, validation, formatting and equality of byte ranges.</summary>
    public class HttpRangeTests
    {
        /// <summary>Checks that a resume range starts at an offset and runs to the end of the file.</summary>
        [Test]
        public void From_CreatesOpenEndedRange()
        {
            var range = HttpRange.From(1000);

            Assert.That(range.Start, Is.EqualTo(1000));
            Assert.That(range.End, Is.Null);
            Assert.That(range.ToString(), Is.EqualTo("bytes=1000-"));
        }

        /// <summary>Checks that a closed range keeps both inclusive bounds, including a single-byte range.</summary>
        [Test]
        public void Between_CreatesClosedRange()
        {
            var range = HttpRange.Between(1000, 2000);
            var single = HttpRange.Between(5, 5);

            Assert.That(range.ToString(), Is.EqualTo("bytes=1000-2000"));
            Assert.That(single.Start, Is.EqualTo(5));
            Assert.That(single.End, Is.EqualTo(5));
        }

        /// <summary>Checks that a suffix range requests the last bytes of the file.</summary>
        [Test]
        public void Tail_CreatesSuffixRange()
        {
            var range = HttpRange.Tail(500);

            Assert.That(range.Start, Is.Null);
            Assert.That(range.End, Is.EqualTo(500));
            Assert.That(range.ToString(), Is.EqualTo("bytes=-500"));
        }

        /// <summary>Checks that negative positions and reversed bounds are rejected.</summary>
        [Test]
        public void Factories_RejectInvalidBounds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HttpRange.From(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => HttpRange.Between(-1, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => HttpRange.Between(0, -5));
            Assert.Throws<ArgumentException>(() => HttpRange.Between(10, 5));
        }

        /// <summary>Checks that a suffix length must be positive.</summary>
        /// <param name="length">A non-positive suffix length.</param>
        [TestCase(0)]
        [TestCase(-3)]
        public void Tail_RejectsNonPositiveLength(long length)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HttpRange.Tail(length));
        }

        /// <summary>Checks that the header value produced for HttpClient carries the same bounds.</summary>
        [Test]
        public void ToRangeHeaderValue_CopiesBounds()
        {
            var header = HttpRange.Between(10, 19).ToRangeHeaderValue();
            var tail = HttpRange.Tail(7).ToRangeHeaderValue();

            Assert.That(header.Unit, Is.EqualTo("bytes"));
            Assert.That(header.ToString(), Is.EqualTo("bytes=10-19"));
            Assert.That(tail.ToString(), Is.EqualTo("bytes=-7"));
        }

        /// <summary>Checks value equality, hashing and the equality operators.</summary>
        [Test]
        public void Equality_ComparesBounds()
        {
            var first = HttpRange.Between(1, 2);
            var same = HttpRange.Between(1, 2);
            var other = HttpRange.From(1);

            Assert.That(first.Equals(same), Is.True);
            Assert.That(first.Equals((object)same), Is.True);
            Assert.That(first.Equals((object)"bytes=1-2"), Is.False);
            Assert.That(first == same, Is.True);
            Assert.That(first != other, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        }
    }
}
