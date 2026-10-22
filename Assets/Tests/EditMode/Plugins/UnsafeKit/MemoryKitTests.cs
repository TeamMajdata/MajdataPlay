using System;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>Covers the native memory copy, fill and clear helpers in <see cref="MemoryKit"/>.</summary>
    public class MemoryKitTests
    {
        /// <summary>Checks that MemCpy copies every source element into the destination.</summary>
        [Test]
        public void MemCpy_CopiesElements()
        {
            var source = new long[] { 1, -2, 3 };
            var destination = new long[4];

            MemoryKit.MemCpy<long>(source, destination);

            Assert.That(destination, Is.EqualTo(new long[] { 1, -2, 3, 0 }));
        }

        /// <summary>Checks that a destination shorter than the source is rejected.</summary>
        [Test]
        public void MemCpy_DestinationTooShort_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => MemoryKit.MemCpy<int>(new int[3], new int[2]));

            Assert.That(exception!.ParamName, Is.EqualTo("dst"));
        }

        /// <summary>Checks that copying an empty source leaves the destination untouched.</summary>
        [Test]
        public void MemCpy_EmptySource_DoesNothing()
        {
            var destination = new[] { 5, 5 };

            MemoryKit.MemCpy(ReadOnlySpan<int>.Empty, destination.AsSpan());

            Assert.That(destination, Is.EqualTo(new[] { 5, 5 }));
        }

        /// <summary>Checks that copying a span onto itself is allowed.</summary>
        [Test]
        public void MemCpy_SameStart_IsAllowed()
        {
            var buffer = new[] { 1, 2, 3 };

            Assert.DoesNotThrow(() => MemoryKit.MemCpy<int>(buffer, buffer));
            Assert.That(buffer, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        /// <summary>Checks that adjacent, non-overlapping regions of one buffer can be copied.</summary>
        [Test]
        public void MemCpy_AdjacentRegions_AreAllowed()
        {
            var buffer = new[] { 1, 2, 3, 4, 0, 0, 0, 0 };

            MemoryKit.MemCpy<int>(buffer.AsSpan(0, 4), buffer.AsSpan(4));
            MemoryKit.MemCpy<int>(buffer.AsSpan(4, 2), buffer.AsSpan(0, 2));

            Assert.That(buffer, Is.EqualTo(new[] { 1, 2, 3, 4, 1, 2, 3, 4 }));
        }

        /// <summary>Checks that a destination starting inside the source is rejected.</summary>
        [Test]
        public void MemCpy_DestinationInsideSource_Throws()
        {
            var buffer = new int[8];

            Assert.Throws<ArgumentException>(() => MemoryKit.MemCpy<int>(buffer.AsSpan(0, 4), buffer.AsSpan(2)));
        }

        /// <summary>Checks that a destination ending inside the source is rejected.</summary>
        [Test]
        public void MemCpy_SourceInsideDestination_Throws()
        {
            var buffer = new int[8];

            Assert.Throws<ArgumentException>(() => MemoryKit.MemCpy<int>(buffer.AsSpan(2, 4), buffer.AsSpan(0, 4)));
        }

        /// <summary>Checks that MemSet fills every byte and ignores an empty span.</summary>
        [Test]
        public void MemSet_FillsBytes()
        {
            var buffer = new byte[5];

            MemoryKit.MemSet(buffer.AsSpan(1, 3), 0xAB);
            MemoryKit.MemSet(Span<byte>.Empty, 0xFF);

            Assert.That(buffer, Is.EqualTo(new byte[] { 0, 0xAB, 0xAB, 0xAB, 0 }));
        }

        /// <summary>Checks that MemClear zeroes every element and ignores an empty span.</summary>
        [Test]
        public void MemClear_ZeroesElements()
        {
            var buffer = new[] { 1.5, 2.5, 3.5 };

            MemoryKit.MemClear(buffer.AsSpan(0, 2));
            MemoryKit.MemClear(Span<double>.Empty);

            Assert.That(buffer, Is.EqualTo(new[] { 0.0, 0.0, 3.5 }));
        }
    }
}
