using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            var copy = array;

            Assert.That(array.Length, Is.EqualTo(0));
            Assert.That(array.IsEmpty, Is.True);
            Assert.That(array.AsArray(), Is.Empty);
            Assert.That(array.AsSpan().Length, Is.EqualTo(0));
            Assert.DoesNotThrow(() => array.Dispose());
            AssertDisposedAccessors(array);
            Assert.That(copy.AsArray(), Is.Empty);
            copy.Dispose();
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
            var copy = array;

            array.EnsureLength(8);
            array.EnsureLength(1);

            Assert.That(array.AsArray(), Is.SameAs(before));
            Assert.That(copy.AsArray(), Is.SameAs(before));
            Assert.That(pool.RentedLengths.Count, Is.EqualTo(1));
            copy.Dispose();
            Assert.That(array.Length, Is.Zero);
            array.Dispose();
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that EnsureLength grows a short array and preserves its content.</summary>
        [Test]
        public void EnsureLength_TooShort_Grows()
        {
            var array = new PooledArray<int>(2, new RecordingArrayPool<int>(), false);
            array[0] = 5;
            array[1] = 6;
            var copy = array;

            array.EnsureLength(3);

            Assert.That(array.Length, Is.EqualTo(4));
            Assert.That(array[0], Is.EqualTo(5));
            Assert.That(array[1], Is.EqualTo(6));
            AssertDisposedAccessors(copy);
            copy.Dispose();
            Assert.That(array[0], Is.EqualTo(5));
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

            AssertDisposedAccessors(array);
        }

        /// <summary>Checks that all construction paths share their lifetime with subsequent struct copies.</summary>
        /// <param name="construction">The constructor or pool entry point to exercise.</param>
        [TestCase("ExplicitPool")]
        [TestCase("SharedPool")]
        [TestCase("ExistingArray")]
        [TestCase("Pool")]
        public void Constructors_CopiesShareDisposal(string construction)
        {
            var pool = new RecordingArrayPool<int>();
            var array = construction switch
            {
                "ExplicitPool" => new PooledArray<int>(4, pool, false),
                "SharedPool" => new PooledArray<int>(4, false),
                "ExistingArray" => new PooledArray<int>(pool.Rent(4), pool, false),
                "Pool" => Pool<int>.Rent(4),
                _ => throw new ArgumentOutOfRangeException(nameof(construction))
            };
            var copy = array;
            var rented = array.AsArray();
            try
            {
                copy.Dispose();

                AssertDisposedAccessors(array);
                array.Dispose();
                copy.Dispose();
                if (construction == "ExplicitPool" || construction == "ExistingArray")
                {
                    Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
                    Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));
                }
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that a boxed disposer invalidates all copies and returns the array exactly once.</summary>
        [Test]
        public void Dispose_BoxedCopy_InvalidatesAllCopiesAndReturnsOnce()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(4, pool, true);
            var copy = array;
            var boxed = (IDisposable)array;
            var rented = array.AsArray();
            array[0] = 42;

            boxed.Dispose();
            copy.Dispose();
            array.Dispose();
            boxed.Dispose();

            AssertDisposedAccessors(array);
            AssertDisposedAccessors(copy);
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));
            Assert.That(pool.ReturnedClearFlags, Is.EqualTo(new[] { true }));
            Assert.That(rented[0], Is.Zero);
        }

        /// <summary>Checks that concurrent disposers of distinct struct copies return the shared array once.</summary>
        [Test]
        public void Dispose_ConcurrentCopies_ReturnsOnce()
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var pool = new RecordingArrayPool<int>();
                var array = new PooledArray<int>(4, pool, false);
                var rented = array.AsArray();

                Parallel.For(0, 64, _ =>
                {
                    var copy = array;
                    copy.Dispose();
                });

                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
                Assert.That(pool.ReturnedArrays[0], Is.SameAs(rented));
                Assert.That(array.Length, Is.Zero);
                Assert.That(array.IsEmpty, Is.True);
                array.Dispose();
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            }
        }

        /// <summary>Checks that an ended lease invalidates existing enumerators, including cached interface values.</summary>
        /// <param name="resize">Whether to end the lease by resizing instead of disposing a copy.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Enumerator_EndedLease_RejectsCurrentMoveNextAndReset(bool resize)
        {
            var array = new PooledArray<int>(4, new RecordingArrayPool<int>(), false);
            var copy = array;
            array[0] = 42;
            var enumerator = array.GetEnumerator();
            var generic = ((IEnumerable<int>)array).GetEnumerator();
            var nonGeneric = ((IEnumerable)array).GetEnumerator();
            try
            {
                Assert.That(enumerator.MoveNext(), Is.True);
                Assert.That(generic.MoveNext(), Is.True);
                Assert.That(nonGeneric.MoveNext(), Is.True);
                if (resize)
                {
                    array.Resize(8);
                }
                else
                {
                    copy.Dispose();
                }

                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = enumerator.Current;
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    enumerator.MoveNext();
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    ((IEnumerator)enumerator).Reset();
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = generic.Current;
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    generic.MoveNext();
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    generic.Reset();
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = nonGeneric.Current;
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    nonGeneric.MoveNext();
                });
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    nonGeneric.Reset();
                });
            }
            finally
            {
                enumerator.Dispose();
                generic.Dispose();
                ((IDisposable)nonGeneric).Dispose();
                array.Dispose();
            }
        }

        /// <summary>Checks that resizing replaces the shared lease, preserving content and the configured clear policy.</summary>
        /// <param name="newSize">The requested replacement length.</param>
        /// <param name="clearArray">Whether returned arrays must be cleared.</param>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(5, false)]
        [TestCase(5, true)]
        public void Resize_EndsOldCopiesAndCreatesIndependentLease(int newSize, bool clearArray)
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(4, pool, clearArray);
            var copy = array;
            var oldArray = array.AsArray();
            array[0] = 42;
            try
            {
                array.Resize(newSize);
                var replacement = array.AsArray();
                var replacementCopy = array;

                AssertDisposedAccessors(copy);
                copy.Dispose();
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
                Assert.That(pool.ReturnedArrays[0], Is.SameAs(oldArray));
                Assert.That(pool.ReturnedClearFlags[0], Is.EqualTo(clearArray));
                Assert.That(oldArray[0], Is.EqualTo(clearArray ? 0 : 42));
                Assert.That(array.AsArray(), Is.SameAs(replacement));
                if (newSize > 0)
                {
                    Assert.That(array[0], Is.EqualTo(42));
                }
                else
                {
                    Assert.That(array.AsArray(), Is.Empty);
                }

                replacementCopy.Dispose();
                AssertDisposedAccessors(array);
                array.Dispose();
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(newSize == 0 ? 1 : 2));
                if (newSize > 0)
                {
                    Assert.That(pool.ReturnedArrays[1], Is.SameAs(replacement));
                    Assert.That(pool.ReturnedClearFlags[1], Is.EqualTo(clearArray));
                }
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that a live empty lease can grow and that its copies then remain invalid.</summary>
        [Test]
        public void Resize_FromEmptyLease_InvalidatesItsCopies()
        {
            var pool = new RecordingArrayPool<int>();
            var array = new PooledArray<int>(4, pool, false);
            try
            {
                array.Resize(0);
                var emptyCopy = array;
                Assert.That(emptyCopy.AsArray(), Is.Empty);

                array.Resize(4);

                AssertDisposedAccessors(emptyCopy);
                emptyCopy.Dispose();
                Assert.That(array.Length, Is.EqualTo(4));
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
                array.Dispose();
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(2));
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that the default empty value can acquire a live lease through either growth API.</summary>
        /// <param name="ensureLength">Whether to grow through EnsureLength instead of Resize.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Default_GrowthCreatesSharedLease(bool ensureLength)
        {
            var array = default(PooledArray<int>);
            var emptyCopy = array;
            try
            {
                if (ensureLength)
                {
                    array.EnsureLength(4);
                }
                else
                {
                    array.Resize(4);
                }
                var copy = array;

                Assert.That(array.Length, Is.GreaterThanOrEqualTo(4));
                Assert.That(emptyCopy.AsArray(), Is.Empty);
                emptyCopy.Dispose();
                array[0] = 42;
                Assert.That(copy[0], Is.EqualTo(42));
                copy.Dispose();
                AssertDisposedAccessors(array);
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that stale array copies cannot revive or dispose a later rental of their pooled state.</summary>
        [Test]
        public void Dispose_ReusedLifetime_StaleCopiesCannotReturnNewArray()
        {
            var pool = new RecordingArrayPool<int>();
            var previous = new PooledArray<int>(4, pool, false);
            var stale = previous;
            var previousLifetime = PrivateMember.GetField<PooledDisposable>(previous, "_lifetime");
            var state = PrivateMember.GetField<object>(previousLifetime, "_state");
            previous.Dispose();
            var current = new PooledArray<int>(4, pool, false);
            try
            {
                var currentLifetime = PrivateMember.GetField<PooledDisposable>(current, "_lifetime");
                Assert.That(PrivateMember.GetField<object>(currentLifetime, "_state"), Is.SameAs(state));

                stale.Dispose();
                AssertDisposedAccessors(stale);
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
                current[0] = 42;
                Assert.That(current[0], Is.EqualTo(42));
                current.Dispose();
                Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(2));
            }
            finally
            {
                current.Dispose();
            }
        }

        /// <summary>Checks that failing to rent a replacement preserves the original lease and its copies.</summary>
        [Test]
        public void Resize_RentThrows_PreservesOriginalLease()
        {
            var pool = new FaultingArrayPool();
            var array = new PooledArray<int>(4, pool, false);
            var copy = array;
            var rented = array.AsArray();
            array[0] = 42;
            pool.ThrowOnNextRent = true;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    array.Resize(8);
                });

                Assert.That(array.AsArray(), Is.SameAs(rented));
                Assert.That(copy.AsArray(), Is.SameAs(rented));
                Assert.That(copy[0], Is.EqualTo(42));
                Assert.That(pool.Recording.ReturnedArrays, Is.Empty);
                copy.Dispose();
                AssertDisposedAccessors(array);
                Assert.That(pool.Recording.ReturnedArrays.Count, Is.EqualTo(1));
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <summary>Checks that a failed return still ends the lease and is never retried by another copy.</summary>
        [Test]
        public void Dispose_ReturnThrows_InvalidatesCopiesWithoutRetry()
        {
            var pool = new FaultingArrayPool();
            var array = new PooledArray<int>(4, pool, false);
            var copy = array;
            pool.ThrowOnNextReturn = true;

            Assert.Throws<InvalidOperationException>(() =>
            {
                array.Dispose();
            });

            AssertDisposedAccessors(array);
            AssertDisposedAccessors(copy);
            copy.Dispose();
            array.Dispose();
            Assert.That(pool.Recording.ReturnedArrays.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that a failed return during resize cleans up the replacement and invalidates old copies.</summary>
        [Test]
        public void Resize_ReturnThrows_ReturnsReplacementAndInvalidatesCopies()
        {
            var pool = new FaultingArrayPool();
            var array = new PooledArray<int>(4, pool, true);
            var copy = array;
            var original = array.AsArray();
            pool.ThrowOnNextReturn = true;

            Assert.Throws<InvalidOperationException>(() =>
            {
                array.Resize(8);
            });

            AssertDisposedAccessors(array);
            AssertDisposedAccessors(copy);
            copy.Dispose();
            array.Dispose();
            Assert.That(pool.Recording.RentedLengths, Is.EqualTo(new[] { 4, 8 }));
            Assert.That(pool.Recording.ReturnedArrays.Count, Is.EqualTo(2));
            Assert.That(pool.Recording.ReturnedArrays[0], Is.SameAs(original));
            Assert.That(pool.Recording.ReturnedArrays[1].Length, Is.EqualTo(8));
            Assert.That(pool.Recording.ReturnedClearFlags, Is.EqualTo(new[] { true, true }));
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

        /// <summary>Checks empty metadata and rejection of every operation requiring a live array lease.</summary>
        /// <param name="array">A copy whose shared lease must already be disposed.</param>
        private static void AssertDisposedAccessors(PooledArray<int> array)
        {
            Assert.That(array.Length, Is.Zero);
            Assert.That(array.IsEmpty, Is.True);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = array[0];
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsArray();
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsMemory();
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsMemory(0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsMemory(0, 0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsMemory(0..0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsSpan();
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsSpan(0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsSpan(0, 0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.AsSpan(0..0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.Resize(2);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.EnsureLength(0);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.EnsureLength(2);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                array.GetEnumerator();
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                ((IEnumerable<int>)array).GetEnumerator();
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                ((IEnumerable)array).GetEnumerator();
            });
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

        /// <summary>Records pool operations and injects a single failure to exercise ownership cleanup.</summary>
        private sealed class FaultingArrayPool : ArrayPool<int>
        {
            /// <summary>Gets the pool recording successful rents and all attempted returns.</summary>
            public RecordingArrayPool<int> Recording { get; } = new();

            /// <summary>Gets or sets whether the next rent must fail before allocating an array.</summary>
            public bool ThrowOnNextRent { get; set; }

            /// <summary>Gets or sets whether the next return must fail after recording the attempt.</summary>
            public bool ThrowOnNextReturn { get; set; }

            /// <summary>Rents an exact-size array unless a failure has been requested.</summary>
            /// <param name="minimumLength">The required array length.</param>
            /// <returns>A newly allocated array of the requested length.</returns>
            /// <exception cref="InvalidOperationException">An injected rent failure was requested.</exception>
            public override int[] Rent(int minimumLength)
            {
                if (ThrowOnNextRent)
                {
                    ThrowOnNextRent = false;
                    throw new InvalidOperationException("Injected rent failure.");
                }
                return Recording.Rent(minimumLength);
            }

            /// <summary>Records a return attempt and then throws if a failure has been requested.</summary>
            /// <param name="array">The array being returned.</param>
            /// <param name="clearArray">Whether to clear the returned array.</param>
            /// <exception cref="InvalidOperationException">An injected return failure was requested.</exception>
            public override void Return(int[] array, bool clearArray = false)
            {
                Recording.Return(array, clearArray);
                if (ThrowOnNextReturn)
                {
                    ThrowOnNextReturn = false;
                    throw new InvalidOperationException("Injected return failure.");
                }
            }
        }
    }
}
