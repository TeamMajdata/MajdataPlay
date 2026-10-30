using System;
using MajdataPlay.Buffers;
using MajdataPlay.Syscall;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers viewing, wrapping, slicing, copying and disposing <see cref="ReadOnlyHeap{T}"/>.</summary>
    public unsafe class ReadOnlyHeapTests
    {
        /// <summary>Checks that a converted heap reads the same memory and shares the heap's lease.</summary>
        [Test]
        public void ImplicitConversion_ViewsSameMemoryAndLease()
        {
            var heap = Heap<int>.Alloc(3);
            heap[0] = 1;
            heap[1] = 2;
            heap[2] = 3;
            ReadOnlyHeap<int> view = heap;

            heap[1] = 5;

            Assert.That(view.Length, Is.EqualTo(3));
            Assert.That(view.IsEmpty, Is.False);
            Assert.That(view[1], Is.EqualTo(5));
            Assert.That(view.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 5, 3 }));
            fixed (int* viewPointer = view)
            {
                fixed (int* heapPointer = heap)
                {
                    Assert.That((IntPtr)viewPointer, Is.EqualTo((IntPtr)heapPointer));
                }
            }

            heap.Dispose();

            Assert.That(view.IsDisposed, Is.True);
            Assert.That(view.Length, Is.EqualTo(0));
            Assert.Throws<ObjectDisposedException>(() => _ = view[0]);
        }

        /// <summary>Checks that disposing the read-only view ends the lease of the heap it came from.</summary>
        [Test]
        public void Dispose_ReadOnlyView_EndsHeapLease()
        {
            var heap = Heap<int>.Alloc(2);
            ReadOnlyHeap<int> view = heap;

            view.Dispose();
            view.Dispose();

            Assert.That(heap.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _ = heap[0]);
            Assert.Throws<ObjectDisposedException>(() => view.Slice(0));
            Assert.Throws<ObjectDisposedException>(() => view.AsSpan());
            Assert.Throws<ObjectDisposedException>(() => view.GetPinnableReference());
            // The allocation has already been freed through the view; this must not free it again.
            heap.Dispose();
        }

        /// <summary>Checks that the constructor wraps memory it does not own without freeing it.</summary>
        [Test]
        public void Constructor_NotOwned_DisposeKeepsMemory()
        {
            var buffer = new[] { 4, 5, 6 };
            fixed (int* pointer = buffer)
            {
                var view = new ReadOnlyHeap<int>(pointer, buffer.Length, false);

                Assert.That(view.Length, Is.EqualTo(3));
                Assert.That(view[2], Is.EqualTo(6));
                view.Dispose();
                Assert.That(view.IsDisposed, Is.True);
            }

            Assert.That(buffer, Is.EqualTo(new[] { 4, 5, 6 }));
        }

        /// <summary>Checks that the constructor takes ownership of malloc memory and validates its arguments.</summary>
        [Test]
        public void Constructor_Owned_ReadsMemoryAndValidatesArguments()
        {
            var pointer = (int*)CRuntime.Malloc((UIntPtr)(sizeof(int) * 2));
            Assert.That((IntPtr)pointer, Is.Not.EqualTo(IntPtr.Zero));
            pointer[0] = 8;
            pointer[1] = 9;

            var view = new ReadOnlyHeap<int>(pointer, 2, true);

            Assert.That(view[1], Is.EqualTo(9));
            view.Dispose();
            Assert.That(view.IsDisposed, Is.True);
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ReadOnlyHeap<int>((int*)null, 1, false))!.ParamName, Is.EqualTo("pointer"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOnlyHeap<int>((int*)null, -1, false))!.ParamName, Is.EqualTo("length"));
        }

        /// <summary>Checks that slices share memory and lease with the view and validate their bounds.</summary>
        [Test]
        public void Slice_SharesMemoryAndLease()
        {
            var heap = Heap<int>.Alloc(4);
            for (var i = 0; i < 4; i++)
            {
                heap[i] = i * 10;
            }
            ReadOnlyHeap<int> view = heap;

            var tail = view.Slice(1);
            var middle = view.Slice(1, 2);

            Assert.That(tail.Length, Is.EqualTo(3));
            Assert.That(tail[2], Is.EqualTo(30));
            Assert.That(middle.AsSpan().ToArray(), Is.EqualTo(new[] { 10, 20 }));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => view.Slice(5))!.ParamName, Is.EqualTo("start"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => view.Slice(1, 4))!.ParamName, Is.EqualTo("length"));

            middle.Dispose();

            Assert.That(view.IsDisposed, Is.True);
            Assert.That(heap.IsDisposed, Is.True);
        }

        /// <summary>Checks that the view copies into a long enough heap and rejects a shorter one.</summary>
        [Test]
        public void CopyTo_CopiesIntoHeapAndRejectsShortDestination()
        {
            var source = Heap<int>.Alloc(2);
            var destination = Heap<int>.Alloc(3);
            var shortDestination = Heap<int>.Alloc(1);
            try
            {
                source[0] = 1;
                source[1] = 2;
                destination.Clear();
                shortDestination[0] = 9;
                ReadOnlyHeap<int> view = source;

                view.CopyTo(destination);

                Assert.That(destination.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 2, 0 }));
                Assert.That(view.TryCopyTo(destination.Slice(1)), Is.True);
                Assert.That(destination.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 1, 2 }));
                Assert.That(view.TryCopyTo(shortDestination), Is.False);
                Assert.That(Assert.Throws<ArgumentException>(() => view.CopyTo(shortDestination))!.ParamName, Is.EqualTo("destination"));
                Assert.That(shortDestination[0], Is.EqualTo(9));
            }
            finally
            {
                source.Dispose();
                destination.Dispose();
                shortDestination.Dispose();
            }
        }

        /// <summary>Checks that a default view is empty and never disposed.</summary>
        [Test]
        public void Default_IsEmptyAndNeverDisposed()
        {
            var view = default(ReadOnlyHeap<int>);

            view.Dispose();

            Assert.That(view.Length, Is.EqualTo(0));
            Assert.That(view.IsEmpty, Is.True);
            Assert.That(view.IsDisposed, Is.False);
            Assert.That(view.Slice(0).IsEmpty, Is.True);
            Assert.That(view.AsSpan().IsEmpty, Is.True);
            Assert.Throws<IndexOutOfRangeException>(() => _ = view[0]);
        }
    }
}
