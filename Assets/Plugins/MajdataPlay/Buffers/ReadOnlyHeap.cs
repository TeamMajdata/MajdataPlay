using System;
using System.Runtime.CompilerServices;
using MajdataPlay.Syscall;

#nullable enable
namespace MajdataPlay.Buffers
{
    /// <summary>
    /// A read-only view of a <see cref="Heap{T}"/>: native memory with 64-bit indexes whose copies and slices share one
    /// disposal lifetime.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <remarks>
    /// A read-only heap relates to <see cref="Heap{T}"/> as <see cref="ReadOnlySpan{T}"/> relates to
    /// <see cref="Span{T}"/>. A heap converts to it implicitly and the view shares the heap's
    /// <see cref="PooledDisposable"/> lease, so disposing either one ends the lease for both and frees owned memory once.
    /// Otherwise it follows the lifetime and threading rules of <see cref="Heap{T}"/>.
    /// </remarks>
    public readonly unsafe struct ReadOnlyHeap<T> : IDisposable where T : unmanaged
    {
        /// <summary>Gets a read-only reference to an element.</summary>
        /// <param name="index">The zero-based element index.</param>
        /// <returns>A reference to the element, valid until the heap is disposed.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="IndexOutOfRangeException">The index is negative or not less than <see cref="Length"/>.</exception>
        public ref readonly T this[long index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref _heap[index];
            }
        }
        /// <summary>Gets the number of elements, or zero when the heap is disposed.</summary>
        public long Length
        {
            get
            {
                return _heap.Length;
            }
        }
        /// <summary>Gets whether the heap has no elements or is disposed.</summary>
        public bool IsEmpty
        {
            get
            {
                return _heap.IsEmpty;
            }
        }
        /// <summary>Gets whether the shared lease of this heap has ended.</summary>
        /// <remarks>A heap without memory, such as a default value, has no lease and is never disposed.</remarks>
        public bool IsDisposed
        {
            get
            {
                return _heap.IsDisposed;
            }
        }

        /// <summary>The writable heap whose memory and lease this view shares.</summary>
        private readonly Heap<T> _heap;

        /// <summary>Views existing native memory with a new shared disposal lifetime.</summary>
        /// <param name="pointer">The first element, or null for an empty heap.</param>
        /// <param name="length">The number of elements at <paramref name="pointer"/>.</param>
        /// <param name="ownsMemory">
        /// Whether disposal frees <paramref name="pointer"/> with C <c>free</c>. Pass true only for memory from
        /// <see cref="CRuntime.Malloc"/>, or from C <c>malloc</c> of the same C runtime, that nothing else frees.
        /// </param>
        /// <remarks>
        /// A null <paramref name="pointer"/> creates a heap without memory that behaves like a default value.
        /// If the constructor throws, ownership of the memory stays with the caller.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="length"/> is negative or too large for the native address space.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="pointer"/> is null and <paramref name="length"/> is not zero.
        /// </exception>
        public ReadOnlyHeap(T* pointer, long length, bool ownsMemory)
        {
            _heap = new Heap<T>(pointer, length, ownsMemory);
        }
        /// <summary>Creates a read-only view that shares a heap's memory and lease.</summary>
        /// <param name="heap">The heap to view.</param>
        private ReadOnlyHeap(Heap<T> heap)
        {
            _heap = heap;
        }

        /// <summary>Forms a slice from an index to the end that shares this heap's memory and lease.</summary>
        /// <param name="start">The index of the first element of the slice.</param>
        /// <returns>A read-only heap viewing the elements from <paramref name="start"/>; disposing it disposes this heap.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="start"/> is negative or greater than <see cref="Length"/>.
        /// </exception>
        public ReadOnlyHeap<T> Slice(long start)
        {
            return new ReadOnlyHeap<T>(_heap.Slice(start));
        }
        /// <summary>Forms a slice of a given length that shares this heap's memory and lease.</summary>
        /// <param name="start">The index of the first element of the slice.</param>
        /// <param name="length">The number of elements in the slice.</param>
        /// <returns>A read-only heap viewing the requested elements; disposing it disposes this heap.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="start"/> is negative or greater than <see cref="Length"/>, or <paramref name="length"/> is
        /// negative or reaches past the end of this heap.
        /// </exception>
        public ReadOnlyHeap<T> Slice(long start, long length)
        {
            return new ReadOnlyHeap<T>(_heap.Slice(start, length));
        }
        /// <summary>Copies all elements to the start of a destination heap.</summary>
        /// <param name="destination">The heap that receives the elements; it may overlap this heap.</param>
        /// <exception cref="ObjectDisposedException">The lease of this heap or of the destination has ended.</exception>
        /// <exception cref="ArgumentException">The destination is shorter than this heap.</exception>
        public void CopyTo(Heap<T> destination)
        {
            _heap.CopyTo(destination);
        }
        /// <summary>Copies all elements to the start of a destination heap if it is long enough.</summary>
        /// <param name="destination">The heap that receives the elements; it may overlap this heap.</param>
        /// <returns>True if the elements were copied; false if the destination is shorter and was left unchanged.</returns>
        /// <exception cref="ObjectDisposedException">The lease of this heap or of the destination has ended.</exception>
        public bool TryCopyTo(Heap<T> destination)
        {
            return _heap.TryCopyTo(destination);
        }
        /// <summary>Gets a read-only span over all elements.</summary>
        /// <returns>A span valid until the heap is disposed; the span itself does not observe disposal.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Length"/> exceeds <see cref="int.MaxValue"/>; slice the heap first.
        /// </exception>
        public ReadOnlySpan<T> AsSpan()
        {
            return _heap.AsSpan();
        }
        /// <summary>Gets a reference to the first element, so that a <c>fixed</c> statement can take the heap's address.</summary>
        /// <returns>A reference to the first element, or a null reference when the heap is empty.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public ref readonly T GetPinnableReference()
        {
            return ref _heap.GetPinnableReference();
        }
        /// <summary>Ends the shared lease and frees the owned allocation at most once.</summary>
        /// <remarks>
        /// Disposing any copy, slice or view, including the writable heap this view came from, frees the whole
        /// allocation. Repeated calls, and calls on a heap without memory, have no effect.
        /// </remarks>
        public void Dispose()
        {
            _heap.Dispose();
        }

        /// <summary>Views a heap as read-only, sharing its memory and lease.</summary>
        /// <param name="heap">The heap to view.</param>
        /// <returns>A read-only heap over the same elements; disposing it disposes <paramref name="heap"/>.</returns>
        public static implicit operator ReadOnlyHeap<T>(Heap<T> heap)
        {
            return new ReadOnlyHeap<T>(heap);
        }
    }
}
