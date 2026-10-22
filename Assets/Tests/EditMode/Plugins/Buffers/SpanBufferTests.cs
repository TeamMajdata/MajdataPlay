using System;
using MajdataPlay.Buffers;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers writing, truncation, skipping and clearing of <see cref="SpanBuffer"/>.</summary>
    public class SpanBufferTests
    {
        /// <summary>Checks that a new buffer is empty.</summary>
        [Test]
        public void New_IsEmpty()
        {
            var buffer = new SpanBuffer(new byte[4]);

            Assert.That(buffer.IsEmpty, Is.True);
            Assert.That(buffer.Data.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that writing nothing reports zero bytes and keeps the buffer empty.</summary>
        [Test]
        public void Write_Empty_WritesNothing()
        {
            var buffer = new SpanBuffer(new byte[4]);

            Assert.That(buffer.Write(ReadOnlySpan<byte>.Empty), Is.EqualTo(0));
            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>Checks that consecutive writes append their data.</summary>
        [Test]
        public void Write_AppendsData()
        {
            var buffer = new SpanBuffer(new byte[8]);

            Assert.That(buffer.Write(new byte[] { 1, 2 }), Is.EqualTo(2));
            Assert.That(buffer.Write(new byte[] { 3 }), Is.EqualTo(1));

            Assert.That(buffer.IsEmpty, Is.False);
            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>Checks that writes beyond the capacity are truncated and that a full buffer accepts nothing.</summary>
        [Test]
        public void Write_BeyondCapacity_Truncates()
        {
            var buffer = new SpanBuffer(new byte[3]);

            Assert.That(buffer.Write(new byte[] { 1, 2, 3, 4, 5 }), Is.EqualTo(3));
            Assert.That(buffer.Write(new byte[] { 6 }), Is.EqualTo(0));
            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>Checks that skipping zero or a negative count changes nothing.</summary>
        [Test]
        public void Skip_NotPositive_DoesNothing()
        {
            var buffer = new SpanBuffer(new byte[4]);
            buffer.Write(new byte[] { 1, 2 });

            buffer.Skip(0);
            buffer.Skip(-1);

            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        }

        /// <summary>Checks that skipping part of the data moves the remainder to the front.</summary>
        [Test]
        public void Skip_Partial_ShiftsRemainingData()
        {
            var storage = new byte[6];
            var buffer = new SpanBuffer(storage);
            buffer.Write(new byte[] { 1, 2, 3, 4 });

            buffer.Skip(1);

            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 2, 3, 4 }));
            Assert.That(buffer.Write(new byte[] { 5 }), Is.EqualTo(1));
            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 2, 3, 4, 5 }));
            Assert.That(storage[0], Is.EqualTo(2));
        }

        /// <summary>Checks that skipping all written data empties the buffer.</summary>
        [Test]
        public void Skip_AllData_Clears()
        {
            var buffer = new SpanBuffer(new byte[4]);
            buffer.Write(new byte[] { 1, 2 });

            buffer.Skip(5);

            Assert.That(buffer.IsEmpty, Is.True);
        }

        /// <summary>Checks that Clear empties the buffer so it can be refilled from the start.</summary>
        [Test]
        public void Clear_EmptiesBuffer()
        {
            var buffer = new SpanBuffer(new byte[2]);
            buffer.Write(new byte[] { 1, 2 });

            buffer.Clear();

            Assert.That(buffer.IsEmpty, Is.True);
            Assert.That(buffer.Write(new byte[] { 7, 8 }), Is.EqualTo(2));
            Assert.That(buffer.Data.ToArray(), Is.EqualTo(new byte[] { 7, 8 }));
        }
    }
}
