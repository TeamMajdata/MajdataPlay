using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Buffers;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers renting, resizing, viewing, enumerating and disposing <see cref="PooledArray{T}"/>.</summary>
    public class PooledArrayTests
    {
        /// <summary>Checks that the pool constructor rents from the given pool and that disposal returns the array to it.</summary>
        [Test]
        public void Constructor_WithPool_RentsAndReturnsThroughThatPool()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(5, pool, false);
            var rented = array.AsArray();

            array.Dispose();

            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 5 }));
            Assert.That(rented.Length, Is.EqualTo(5));
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));
            Assert.That(pool.ReturnedClearFlags, Is.EqualTo(new[] { false }));
        }

        /// <summary>Checks that the shared-pool constructor rents an array at least as long as requested.</summary>
        [Test]
        public void Constructor_WithoutPool_RentsFromSharedPool()
        {
            var array = new PooledArray<int>(10, true);
            try
            {
                Assert.That(array.Length, Is.GreaterThanOrEqualTo(10));
                Assert.That(array.IsEmpty, Is.False);
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that a default instance behaves as an empty array and disposes without touching a pool.</summary>
        [Test]
        public void Default_IsEmptyAndDisposesQuietly()
        {
            var array = default(PooledArray<int>);

            Assert.That(array.Length, Is.EqualTo(0));
            Assert.That(array.IsEmpty, Is.True);
            Assert.That(array.AsArray(), Is.Empty);
            Assert.That(array.AsSpan().Length, Is.EqualTo(0));
            Assert.DoesNotThrow(() => array.Dispose());
        }

        /// <summary>Checks that the indexer returns a writable reference into the rented array.</summary>
        [Test]
        public void Indexer_ReturnsWritableReference()
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);

            array[1] = 7;
            array[1] += 1;

            Assert.That(array.AsArray()[1], Is.EqualTo(8));
            Assert.That(array[1], Is.EqualTo(8));
            array.Dispose();
        }

        /// <summary>Checks that a negative size is rejected.</summary>
        [Test]
        public void Resize_Negative_Throws()
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => array.Resize(-1));

            Assert.That(exception!.ParamName, Is.EqualTo("newSize"));
            array.Dispose();
        }

        /// <summary>Checks that resizing to zero returns the array and leaves an empty array that disposes without a second return.</summary>
        [Test]
        public void Resize_Zero_ReturnsArrayAndBecomesEmpty()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(4, pool, true);
            var rented = array.AsArray();

            array.Resize(0);

            Assert.That(array.Length, Is.EqualTo(0));
            Assert.That(array.IsEmpty, Is.True);
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));

            array.Dispose();

            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that growing rounds the size up to a power of two, copies the content and returns the old array.</summary>
        [Test]
        public void Resize_Grow_RoundsUpToPowerOfTwoAndCopies()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(3, pool, false);
            var old = array.AsArray();
            array[0] = 1;
            array[1] = 2;
            array[2] = 3;

            array.Resize(5);

            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 3, 8 }));
            Assert.That(array.Length, Is.EqualTo(8));
            Assert.That(array.AsSpan(0, 3).ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays[0], Is.SameAs(old));
            array.Dispose();
        }

        /// <summary>Checks that shrinking keeps only the leading elements that fit.</summary>
        [Test]
        public void Resize_Shrink_KeepsLeadingElements()
        {
            var array = new PooledArray<int>(8, new RecordingArrayPool<int>(), false);
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = i;
            }

            array.Resize(2);

            Assert.That(array.AsArray(), Is.EqualTo(new[] { 0, 1 }));
            array.Dispose();
        }

        /// <summary>Checks that resizing to an exact power of two keeps that size.</summary>
        [Test]
        public void Resize_PowerOfTwo_KeepsRequestedSize()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(1, pool, false);

            array.Resize(16);

            Assert.That(array.Length, Is.EqualTo(16));
            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 1, 16 }));
            array.Dispose();
        }

        /// <summary>Checks the power-of-two rounding helper used by Resize, including zero.</summary>
        /// <param name="value">The value to round.</param>
        /// <param name="expected">The expected power of two.</param>
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(5, 8)]
        [TestCase(64, 64)]
        public void RoundUpToPowerOf2_RoundsUp(int value, int expected)
        {
            Assert.That(PrivateMember.InvokeStatic<int>(typeof(PooledArray<int>), "RoundUpToPowerOf2", value), Is.EqualTo(expected));
        }

        /// <summary>Checks that EnsureLength keeps the current array when it is already long enough.</summary>
        [Test]
        public void EnsureLength_LongEnough_KeepsArray()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(8, pool, false);
            var before = array.AsArray();

            array.EnsureLength(8);
            array.EnsureLength(1);

            Assert.That(array.AsArray(), Is.SameAs(before));
            Assert.That(pool.RentedLengths.Count, Is.EqualTo(1));
            array.Dispose();
        }

        /// <summary>Checks that EnsureLength grows a short array and preserves its content.</summary>
        [Test]
        public void EnsureLength_TooShort_Grows()
        {
            var array = new PooledArray<int>(2, new RecordingArrayPool<int>(), false);
            array[0] = 5;
            array[1] = 6;

            array.EnsureLength(3);

            Assert.That(array.Length, Is.EqualTo(4));
            Assert.That(array[0], Is.EqualTo(5));
            Assert.That(array[1], Is.EqualTo(6));
            array.Dispose();
        }

        /// <summary>Checks that every memory and span view exposes the expected window of the rented array.</summary>
        [Test]
        public void MemoryAndSpanViews_ExposeUnderlyingArray()
        {
            var array = new PooledArray<int>(8, new RecordingArrayPool<int>(), false);
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = i;
            }

            Assert.That(array.AsMemory().ToArray(), Is.EqualTo(Enumerable.Range(0, 8).ToArray()));
            Assert.That(array.AsMemory(6).ToArray(), Is.EqualTo(new[] { 6, 7 }));
            Assert.That(array.AsMemory(2, 3).ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(array.AsMemory(1..3).ToArray(), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(array.AsSpan().ToArray(), Is.EqualTo(Enumerable.Range(0, 8).ToArray()));
            Assert.That(array.AsSpan(6).ToArray(), Is.EqualTo(new[] { 6, 7 }));
            Assert.That(array.AsSpan(2, 3).ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(array.AsSpan(^2..).ToArray(), Is.EqualTo(new[] { 6, 7 }));
            array.Dispose();
        }

        /// <summary>Checks that the implicit conversions expose the rented array.</summary>
        [Test]
        public void ImplicitConversions_ExposeUnderlyingArray()
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);
            array[3] = 42;

            Span<int> span = array;
            Memory<int> memory = array;
            int[] raw = array;

            Assert.That(span[3], Is.EqualTo(42));
            Assert.That(memory.Span[3], Is.EqualTo(42));
            Assert.That(raw, Is.SameAs(array.AsArray()));
            array.Dispose();
        }

        /// <summary>Checks that disposal returns the array once with the configured clear flag and leaves an empty array.</summary>
        [Test]
        public void Dispose_ReturnsArrayOnceWithClearFlag()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(4, pool, true);
            array[0] = 9;
            var rented = array.AsArray();

            array.Dispose();
            array.Dispose();

            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));
            Assert.That(pool.ReturnedClearFlags, Is.EqualTo(new[] { true }));
            Assert.That(rented[0], Is.EqualTo(0));
            Assert.That(array.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that every accessor rejects a disposed array.</summary>
        [Test]
        public void Disposed_AccessorsThrow()
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);
            array.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = array[0]);
            Assert.Throws<ObjectDisposedException>(() => array.AsArray());
            Assert.Throws<ObjectDisposedException>(() => array.AsMemory());
            Assert.Throws<ObjectDisposedException>(() => array.AsMemory(0));
            Assert.Throws<ObjectDisposedException>(() => array.AsMemory(0, 0));
            Assert.Throws<ObjectDisposedException>(() => array.AsMemory(0..0));
            Assert.Throws<ObjectDisposedException>(() => array.AsSpan());
            Assert.Throws<ObjectDisposedException>(() => array.AsSpan(0));
            Assert.Throws<ObjectDisposedException>(() => array.AsSpan(0, 0));
            Assert.Throws<ObjectDisposedException>(() => array.AsSpan(0..0));
            Assert.Throws<ObjectDisposedException>(() => array.Resize(2));
            Assert.Throws<ObjectDisposedException>(() => array.EnsureLength(2));
            Assert.Throws<ObjectDisposedException>(() =>
            {
                Span<int> span = array;
                _ = span.Length;
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                Memory<int> memory = array;
                _ = memory.Length;
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                int[] raw = array;
                _ = raw.Length;
            });
        }

        /// <summary>Checks that foreach yields every element of the rented array in order.</summary>
        [Test]
        public void Enumerator_YieldsEveryElement()
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = i * 10;
            }
            var items = new List<int>();

            foreach (var item in array)
            {
                items.Add(item);
            }

            Assert.That(items, Is.EqualTo(new[] { 0, 10, 20, 30 }));
            array.Dispose();
        }

        /// <summary>Checks that the enumerator's Current is a reference into the array.</summary>
        [Test]
        public void Enumerator_CurrentIsWritableReference()
        {
            var array = new PooledArray<int>(2, new RecordingArrayPool<int>(), false);
            var enumerator = array.GetEnumerator();

            Assert.That(enumerator.MoveNext(), Is.True);
            enumerator.Current = 9;
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.False);
            enumerator.Dispose();

            Assert.That(array[0], Is.EqualTo(9));
            array.Dispose();
        }

        /// <summary>Checks the generic and non-generic enumerator interfaces, including Reset.</summary>
        [Test]
        public void Enumerator_Interfaces_EnumerateAndReset()
        {
            var array = new PooledArray<int>(3, new RecordingArrayPool<int>(), false);
            array[0] = 1;
            array[1] = 2;
            array[2] = 3;

            IEnumerable<int> generic = array;
            Assert.That(generic.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));

            var nonGeneric = ((IEnumerable)array).GetEnumerator();
            Assert.That(nonGeneric.MoveNext(), Is.True);
            Assert.That(nonGeneric.Current, Is.EqualTo(1));
            Assert.That(nonGeneric.MoveNext(), Is.True);
            nonGeneric.Reset();
            Assert.That(nonGeneric.MoveNext(), Is.True);
            Assert.That(nonGeneric.Current, Is.EqualTo(1));

            var typed = ((IEnumerable<int>)array).GetEnumerator();
            Assert.That(typed.MoveNext(), Is.True);
            Assert.That(typed.Current, Is.EqualTo(1));
            typed.Dispose();
            array.Dispose();
        }
    }
}
