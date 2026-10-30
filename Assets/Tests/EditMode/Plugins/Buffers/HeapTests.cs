using System;
using System.Linq;
using MajdataPlay.Buffers;
using MajdataPlay.Syscall;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers allocating, wrapping, indexing, slicing, copying and disposing <see cref="Heap{T}"/>.</summary>
    public unsafe class HeapTests
    {
        /// <summary>Checks that an allocation has the requested length and keeps values written through the indexer.</summary>
        [Test]
        public void Alloc_ReturnsWritableMemoryOfRequestedLength()
        {
            var heap = Heap<int>.Alloc(16);
            try
            {
                for (var i = 0L; i < heap.Length; i++)
                {
                    heap[i] = (int)i * 3;
                }
                heap[5] += 1;

                Assert.That(heap.Length, Is.EqualTo(16));
                Assert.That(heap.IsEmpty, Is.False);
                Assert.That(heap.IsDisposed, Is.False);
                Assert.That(heap[5], Is.EqualTo(16));
                Assert.That(heap[15], Is.EqualTo(45));
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Checks that a zero-length allocation returns a heap without memory that is never disposed.</summary>
        [Test]
        public void Alloc_Zero_ReturnsHeapWithoutMemory()
        {
            var heap = Heap<int>.Alloc(0);

            heap.Dispose();

            Assert.That(heap.Length, Is.EqualTo(0));
            Assert.That(heap.IsEmpty, Is.True);
            Assert.That(heap.IsDisposed, Is.False);
            Assert.That(heap.Slice(0).IsEmpty, Is.True);
            Assert.That(heap.AsSpan().IsEmpty, Is.True);
        }

        /// <summary>Checks that negative lengths and lengths beyond the address space are rejected before allocating.</summary>
        [Test]
        public void Alloc_InvalidLength_Throws()
        {
            var negative = Assert.Throws<ArgumentOutOfRangeException>(() => Heap<int>.Alloc(-1));
            // long.MaxValue elements of eight bytes exceed every address space.
            var tooLong = Assert.Throws<ArgumentOutOfRangeException>(() => Heap<long>.Alloc(long.MaxValue));

            Assert.That(negative!.ParamName, Is.EqualTo("length"));
            Assert.That(tooLong!.ParamName, Is.EqualTo("length"));
        }

        /// <summary>Checks that a size malloc cannot provide surfaces as an out-of-memory exception.</summary>
        [Test]
        public void Alloc_UnsatisfiableSize_ThrowsOutOfMemory()
        {
            // long.MaxValue bytes pass the 64-bit length check, but no C runtime heap can provide them.
            Assume.That(IntPtr.Size, Is.EqualTo(sizeof(long)));

            Assert.Throws<OutOfMemoryException>(() => Heap<byte>.Alloc(long.MaxValue));
        }

        /// <summary>Checks that indexes outside the heap are rejected.</summary>
        [Test]
        public void Indexer_OutOfRange_Throws()
        {
            var heap = Heap<int>.Alloc(4);
            try
            {
                Assert.Throws<IndexOutOfRangeException>(() => _ = heap[-1]);
                Assert.Throws<IndexOutOfRangeException>(() => _ = heap[4]);
                Assert.Throws<IndexOutOfRangeException>(() => _ = heap[long.MinValue]);
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Checks that indexes and slice positions beyond the 32-bit range address the right elements.</summary>
        [Test]
        public void Indexer_PositionsBeyondInt32Range_AddressTheRightElements()
        {
            Assume.That(IntPtr.Size, Is.EqualTo(sizeof(long)));
            // The view starts int.MaxValue + 9 elements before a real buffer and is only ever accessed inside that buffer.
            const long offset = (long)int.MaxValue + 9;
            var buffer = new byte[16];
            fixed (byte* pointer = buffer)
            {
                var heap = new Heap<byte>(pointer - offset, offset + buffer.Length, false);

                heap[offset] = 1;
                heap[offset + 15] = 2;
                var tail = heap.Slice(offset);
                var window = heap.Slice(offset + 4, 8);
                window[0] = 3;

                Assert.That(heap.Length, Is.EqualTo(offset + 16));
                Assert.That(tail.Length, Is.EqualTo(16));
                Assert.That(tail[15], Is.EqualTo(2));
                Assert.That(window.AsSpan().Length, Is.EqualTo(8));
                Assert.Throws<InvalidOperationException>(() => heap.AsSpan());
                heap.Dispose();
            }

            Assert.That(buffer[0], Is.EqualTo(1));
            Assert.That(buffer[4], Is.EqualTo(3));
            Assert.That(buffer[15], Is.EqualTo(2));
        }

        /// <summary>Checks that the constructor rejects a null pointer with elements and a negative length.</summary>
        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            var nullPointer = Assert.Throws<ArgumentNullException>(() => new Heap<int>((int*)null, 1, false));
            var negative = Assert.Throws<ArgumentOutOfRangeException>(() => new Heap<int>((int*)null, -1, false));
            var tooLong = Assert.Throws<ArgumentOutOfRangeException>(() => new Heap<long>((long*)sizeof(long), long.MaxValue, false));

            Assert.That(nullPointer!.ParamName, Is.EqualTo("pointer"));
            Assert.That(negative!.ParamName, Is.EqualTo("length"));
            Assert.That(tooLong!.ParamName, Is.EqualTo("length"));
        }

        /// <summary>Checks that a heap over memory it does not own ends its lease on disposal but leaves the memory intact.</summary>
        [Test]
        public void Constructor_NotOwned_DisposeKeepsMemory()
        {
            var buffer = new[] { 1, 2, 3, 4 };
            fixed (int* pointer = buffer)
            {
                var heap = new Heap<int>(pointer, buffer.Length, false);

                heap[1] = 20;
                heap.Dispose();

                Assert.That(heap.IsDisposed, Is.True);
                Assert.That(heap.Length, Is.EqualTo(0));
            }

            Assert.That(buffer, Is.EqualTo(new[] { 1, 20, 3, 4 }));
        }

        /// <summary>Checks that an owning heap uses malloc memory in place and that a rejected constructor leaves ownership with the caller.</summary>
        [Test]
        public void Constructor_Owned_UsesMemoryAndDisposes()
        {
            var address = CRuntime.Malloc((UIntPtr)(sizeof(int) * 4));
            var pointer = (int*)address;
            Assert.That(address, Is.Not.EqualTo(IntPtr.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Heap<int>((int*)address, -1, true));
            // The rejected constructor did not take ownership, so the memory is still usable.
            pointer[0] = 1;

            var heap = new Heap<int>(pointer, 4, true);
            heap[3] = 7;

            Assert.That(heap[0], Is.EqualTo(1));
            Assert.That(pointer[3], Is.EqualTo(7));
            heap.Dispose();
            Assert.That(heap.IsDisposed, Is.True);
        }

        /// <summary>Checks that default heaps and heaps over a null pointer are empty and never disposed.</summary>
        [Test]
        public void Default_IsEmptyAndNeverDisposed()
        {
            var heaps = new[] { default(Heap<int>), new Heap<int>(null, 0, true) };
            foreach (var heap in heaps)
            {
                heap.Dispose();
                heap.Clear();

                Assert.That(heap.Length, Is.EqualTo(0));
                Assert.That(heap.IsEmpty, Is.True);
                Assert.That(heap.IsDisposed, Is.False);
                Assert.That(heap.Slice(0, 0).IsEmpty, Is.True);
                Assert.That(heap.AsSpan().IsEmpty, Is.True);
                Assert.That(heap.TryCopyTo(default), Is.True);
                Assert.Throws<IndexOutOfRangeException>(() => _ = heap[0]);
                fixed (int* pointer = heap)
                {
                    Assert.That((IntPtr)pointer, Is.EqualTo(IntPtr.Zero));
                }
            }
        }

        /// <summary>Checks that slices share memory with their heap and validate their bounds.</summary>
        [Test]
        public void Slice_SharesMemoryAndValidatesBounds()
        {
            var heap = Heap<int>.Alloc(6);
            try
            {
                for (var i = 0; i < 6; i++)
                {
                    heap[i] = i;
                }
                var tail = heap.Slice(2);
                var middle = heap.Slice(1, 3);

                tail[0] = 20;

                Assert.That(tail.Length, Is.EqualTo(4));
                Assert.That(middle.Length, Is.EqualTo(3));
                Assert.That(middle[1], Is.EqualTo(20));
                Assert.That(heap[2], Is.EqualTo(20));
                Assert.That(heap.Slice(6).IsEmpty, Is.True);
                Assert.That(heap.Slice(6, 0).IsEmpty, Is.True);
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => heap.Slice(7))!.ParamName, Is.EqualTo("start"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => heap.Slice(-1))!.ParamName, Is.EqualTo("start"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => heap.Slice(7, 0))!.ParamName, Is.EqualTo("start"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => heap.Slice(2, 5))!.ParamName, Is.EqualTo("length"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => heap.Slice(2, -1))!.ParamName, Is.EqualTo("length"));
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Checks that disposing a slice ends the lease of the heap it was taken from.</summary>
        [Test]
        public void Slice_Dispose_EndsWholeLease()
        {
            var heap = Heap<int>.Alloc(4);
            var slice = heap.Slice(1, 2);

            slice.Dispose();

            Assert.That(slice.IsDisposed, Is.True);
            Assert.That(heap.IsDisposed, Is.True);
            Assert.That(heap.Length, Is.EqualTo(0));
            Assert.Throws<ObjectDisposedException>(() => _ = heap[0]);
            // The allocation has already been freed through the slice; this must not free it again.
            heap.Dispose();
        }

        /// <summary>Checks that disposal ends every copy, is idempotent and makes every accessor throw.</summary>
        [Test]
        public void Dispose_EndsEveryCopyAndRejectsAccess()
        {
            var heap = Heap<int>.Alloc(4);
            var copy = heap;
            var live = Heap<int>.Alloc(4);
            try
            {
                copy.Dispose();
                copy.Dispose();
                heap.Dispose();

                Assert.That(heap.IsDisposed, Is.True);
                Assert.That(copy.IsDisposed, Is.True);
                Assert.That(heap.Length, Is.EqualTo(0));
                Assert.That(heap.IsEmpty, Is.True);
                var exception = Assert.Throws<ObjectDisposedException>(() => _ = heap[0]);
                Assert.That(exception!.ObjectName, Is.EqualTo("Heap"));
                Assert.Throws<ObjectDisposedException>(() => heap.Slice(0));
                Assert.Throws<ObjectDisposedException>(() => heap.Slice(0, 1));
                Assert.Throws<ObjectDisposedException>(() => heap.CopyTo(live));
                Assert.Throws<ObjectDisposedException>(() => heap.TryCopyTo(live));
                Assert.Throws<ObjectDisposedException>(() => live.CopyTo(heap));
                Assert.Throws<ObjectDisposedException>(() => heap.Clear());
                Assert.Throws<ObjectDisposedException>(() => heap.AsSpan());
                Assert.Throws<ObjectDisposedException>(() => heap.GetPinnableReference());
            }
            finally
            {
                live.Dispose();
            }
        }

        /// <summary>Checks that a disposed copy stays disposed when a later allocation reuses its pooled lease state.</summary>
        [Test]
        public void Dispose_StaleCopyStaysDisposedAfterLeaseReuse()
        {
            var first = Heap<int>.Alloc(4);
            first.Dispose();
            var second = Heap<int>.Alloc(4);
            try
            {
                second[0] = 1;

                Assert.That(first.IsDisposed, Is.True);
                Assert.That(second.IsDisposed, Is.False);
                Assert.That(second[0], Is.EqualTo(1));
                Assert.Throws<ObjectDisposedException>(() => _ = first[0]);
            }
            finally
            {
                second.Dispose();
            }
        }

        /// <summary>Checks that copying fills the start of a longer destination and leaves the rest unchanged.</summary>
        [Test]
        public void CopyTo_LongerDestination_CopiesToItsStart()
        {
            var source = Heap<int>.Alloc(3);
            var destination = Heap<int>.Alloc(5);
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    source[i] = i + 1;
                }
                destination.Clear();

                source.CopyTo(destination);

                Assert.That(destination.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 2, 3, 0, 0 }));
                Assert.That(source.TryCopyTo(destination.Slice(2)), Is.True);
                Assert.That(destination.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 2, 1, 2, 3 }));
            }
            finally
            {
                source.Dispose();
                destination.Dispose();
            }
        }

        /// <summary>Checks that a shorter destination is rejected and left unchanged.</summary>
        [Test]
        public void CopyTo_ShorterDestination_ThrowsAndLeavesItUnchanged()
        {
            var source = Heap<int>.Alloc(3);
            var destination = Heap<int>.Alloc(2);
            try
            {
                source.AsSpan().Fill(1);
                destination.AsSpan().Fill(9);

                Assert.That(source.TryCopyTo(destination), Is.False);
                var exception = Assert.Throws<ArgumentException>(() => source.CopyTo(destination));

                Assert.That(exception!.ParamName, Is.EqualTo("destination"));
                Assert.That(destination.AsSpan().ToArray(), Is.EqualTo(new[] { 9, 9 }));
            }
            finally
            {
                source.Dispose();
                destination.Dispose();
            }
        }

        /// <summary>Checks that copying between overlapping slices of one allocation behaves like memmove in both directions.</summary>
        [Test]
        public void CopyTo_OverlappingSlices_CopiesLikeMemmove()
        {
            var heap = Heap<int>.Alloc(6);
            try
            {
                Fill(heap);
                heap.Slice(0, 4).CopyTo(heap.Slice(2));
                Assert.That(heap.AsSpan().ToArray(), Is.EqualTo(new[] { 0, 1, 0, 1, 2, 3 }));

                Fill(heap);
                heap.Slice(2).CopyTo(heap);
                Assert.That(heap.AsSpan().ToArray(), Is.EqualTo(new[] { 2, 3, 4, 5, 4, 5 }));
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Checks that clearing a slice zeroes only its elements.</summary>
        [Test]
        public void Clear_ZeroesOnlyTheSlice()
        {
            var heap = Heap<int>.Alloc(5);
            try
            {
                heap.AsSpan().Fill(7);

                heap.Slice(1, 3).Clear();

                Assert.That(heap.AsSpan().ToArray(), Is.EqualTo(new[] { 7, 0, 0, 0, 7 }));
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Checks that spans and fixed pointers expose the same memory as the indexer.</summary>
        [Test]
        public void AsSpanAndFixed_ExposeTheSameMemory()
        {
            var heap = Heap<int>.Alloc(4);
            try
            {
                heap.AsSpan().Fill(5);
                heap.AsSpan()[2] = 9;

                fixed (int* pointer = heap)
                {
                    Assert.That(pointer[2], Is.EqualTo(9));
                    pointer[3] = 11;
                }
                fixed (int* pointer = heap.Slice(1))
                {
                    Assert.That(pointer[1], Is.EqualTo(9));
                }
                fixed (int* pointer = heap.Slice(4))
                {
                    Assert.That((IntPtr)pointer, Is.EqualTo(IntPtr.Zero));
                }

                Assert.That(heap.AsSpan().ToArray(), Is.EqualTo(new[] { 5, 5, 9, 11 }));
            }
            finally
            {
                heap.Dispose();
            }
        }

        /// <summary>Writes each element's index into it.</summary>
        /// <param name="heap">The heap to fill.</param>
        private static void Fill(Heap<int> heap)
        {
            var values = Enumerable.Range(0, (int)heap.Length).ToArray();
            values.AsSpan().CopyTo(heap.AsSpan());
        }
    }
}
