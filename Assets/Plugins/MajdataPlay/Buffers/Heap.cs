using System;
using System.Runtime.CompilerServices;
using MajdataPlay.Syscall;

#nullable enable
namespace MajdataPlay.Buffers
{
    /// <summary>
    /// A contiguous region of native memory with 64-bit indexes, whose copies and slices share one disposal lifetime.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <remarks>
    /// <para>
    /// A heap works like <see cref="Span{T}"/>, but it is an ordinary struct that can be stored in fields, and its
    /// <see cref="Length"/>, indexes and slice positions are <see cref="long"/> values, so it can address more than
    /// <see cref="int.MaxValue"/> elements. <see cref="Alloc"/> allocates memory with C <c>malloc</c>; the constructor
    /// views memory that already exists and optionally takes ownership of it.
    /// </para>
    /// <para>
    /// Every copy, slice and <see cref="ReadOnlyHeap{T}"/> view of a heap shares one <see cref="PooledDisposable"/>
    /// lease. Disposing any of them ends the lease for all of them and, when the heap owns its memory, frees the whole
    /// allocation exactly once with C <c>free</c>. Afterwards every copy reports a zero <see cref="Length"/> and throws
    /// <see cref="ObjectDisposedException"/> on access instead of touching freed memory. Spans, references and
    /// pointers obtained before disposal are not tracked and must not be used afterwards.
    /// </para>
    /// <para>
    /// Disposal is thread-safe, but element access is not synchronized with it: dispose a heap only when no other
    /// thread can still access it. A default value is an empty heap without memory or lifetime; disposing it has no
    /// effect.
    /// </para>
    /// </remarks>
    public readonly unsafe struct Heap<T> : IDisposable where T : unmanaged
    {
        /// <summary>Gets a writable reference to an element.</summary>
        /// <param name="index">The zero-based element index.</param>
        /// <returns>A reference to the element, valid until the heap is disposed.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="IndexOutOfRangeException">The index is negative or not less than <see cref="Length"/>.</exception>
        public ref T this[long index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                if ((ulong)index >= (ulong)_length)
                {
                    throw new IndexOutOfRangeException();
                }
                return ref _pointer[index];
            }
        }
        /// <summary>Gets the number of elements, or zero when the heap is disposed.</summary>
        public long Length
        {
            get
            {
                return IsDisposed ? 0 : _length;
            }
        }
        /// <summary>Gets whether the heap has no elements or is disposed.</summary>
        public bool IsEmpty
        {
            get
            {
                return Length == 0;
            }
        }
        /// <summary>Gets whether the shared lease of this heap has ended.</summary>
        /// <remarks>A heap without memory, such as a default value, has no lease and is never disposed.</remarks>
        public bool IsDisposed
        {
            get
            {
                return _pointer != null && _lifetime.IsDisposed;
            }
        }

        /// <summary>Gets the largest element count whose size in bytes fits in the native address space.</summary>
        private static long MaxLength
        {
            get
            {
                var maxByteCount = IntPtr.Size == sizeof(long) ? long.MaxValue : uint.MaxValue;
                return maxByteCount / sizeof(T);
            }
        }

        /// <summary>The first element of this view, or null for a heap without memory.</summary>
        private readonly T* _pointer;
        /// <summary>The number of elements in this view.</summary>
        private readonly long _length;
        /// <summary>The start of the owned allocation to free on disposal, or null when the memory is not owned.</summary>
        private readonly void* _allocation;
        /// <summary>The disposal lifetime shared by all copies, slices and read-only views of this heap.</summary>
        private readonly PooledDisposable _lifetime;

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
        public Heap(T* pointer, long length, bool ownsMemory)
        {
            if (length < 0 || length > MaxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (pointer == null && length != 0)
            {
                throw new ArgumentNullException(nameof(pointer));
            }
            _pointer = pointer;
            _length = length;
            _allocation = ownsMemory ? pointer : null;
            // Without memory there is nothing to protect or free, so no pooled lease is rented.
            _lifetime = pointer == null ? default : PooledDisposable.Rent();
        }
        /// <summary>Creates a view that shares an existing lease.</summary>
        /// <param name="pointer">The first element of the view.</param>
        /// <param name="length">The number of elements in the view.</param>
        /// <param name="allocation">The owned allocation to free on disposal, or null when it is not owned.</param>
        /// <param name="lifetime">The lease shared with the heap the view was taken from.</param>
        private Heap(T* pointer, long length, void* allocation, PooledDisposable lifetime)
        {
            _pointer = pointer;
            _length = length;
            _allocation = allocation;
            _lifetime = lifetime;
        }

        /// <summary>Allocates uninitialized native memory with C <c>malloc</c>.</summary>
        /// <param name="length">The number of elements to allocate.</param>
        /// <returns>
        /// A heap that owns the memory and frees it with C <c>free</c> when any of its copies is disposed, or a heap
        /// without memory when <paramref name="length"/> is zero. Use <see cref="Clear"/> to zero the elements.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="length"/> is negative or too large for the native address space.
        /// </exception>
        /// <exception cref="OutOfMemoryException">C <c>malloc</c> cannot provide the memory.</exception>
        /// <exception cref="DllNotFoundException">The C runtime library cannot be loaded.</exception>
        public static Heap<T> Alloc(long length)
        {
            if (length < 0 || length > MaxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (length == 0)
            {
                return default;
            }

            var pointer = (T*)CRuntime.Malloc((nuint)(length * sizeof(T)));
            if (pointer == null)
            {
                throw new OutOfMemoryException($"malloc failed to allocate {length} elements of {typeof(T).Name}.");
            }
            try
            {
                return new Heap<T>(pointer, length, true);
            }
            catch
            {
                // Renting the lease can fail; nothing owns the memory yet.
                CRuntime.Free((IntPtr)pointer);
                throw;
            }
        }
        /// <summary>Forms a slice from an index to the end that shares this heap's memory and lease.</summary>
        /// <param name="start">The index of the first element of the slice.</param>
        /// <returns>A heap viewing the elements from <paramref name="start"/>; disposing it disposes this heap.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="start"/> is negative or greater than <see cref="Length"/>.
        /// </exception>
        public Heap<T> Slice(long start)
        {
            ThrowIfDisposed();
            if ((ulong)start > (ulong)_length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            return new Heap<T>(_pointer + start, _length - start, _allocation, _lifetime);
        }
        /// <summary>Forms a slice of a given length that shares this heap's memory and lease.</summary>
        /// <param name="start">The index of the first element of the slice.</param>
        /// <param name="length">The number of elements in the slice.</param>
        /// <returns>A heap viewing the requested elements; disposing it disposes this heap.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="start"/> is negative or greater than <see cref="Length"/>, or <paramref name="length"/> is
        /// negative or reaches past the end of this heap.
        /// </exception>
        public Heap<T> Slice(long start, long length)
        {
            ThrowIfDisposed();
            if ((ulong)start > (ulong)_length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            if ((ulong)length > (ulong)(_length - start))
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            return new Heap<T>(_pointer + start, length, _allocation, _lifetime);
        }
        /// <summary>Copies all elements to the start of a destination heap.</summary>
        /// <param name="destination">The heap that receives the elements; it may overlap this heap.</param>
        /// <exception cref="ObjectDisposedException">The lease of this heap or of the destination has ended.</exception>
        /// <exception cref="ArgumentException">The destination is shorter than this heap.</exception>
        public void CopyTo(Heap<T> destination)
        {
            if (!TryCopyTo(destination))
            {
                throw new ArgumentException("Destination is too short.", nameof(destination));
            }
        }
        /// <summary>Copies all elements to the start of a destination heap if it is long enough.</summary>
        /// <param name="destination">The heap that receives the elements; it may overlap this heap.</param>
        /// <returns>True if the elements were copied; false if the destination is shorter and was left unchanged.</returns>
        /// <exception cref="ObjectDisposedException">The lease of this heap or of the destination has ended.</exception>
        public bool TryCopyTo(Heap<T> destination)
        {
            ThrowIfDisposed();
            destination.ThrowIfDisposed();
            if (_length > destination._length)
            {
                return false;
            }
            if (_length != 0)
            {
                // Buffer.MemoryCopy behaves like memmove, so overlapping slices of one allocation copy correctly.
                var byteCount = _length * sizeof(T);
                Buffer.MemoryCopy(_pointer, destination._pointer, byteCount, byteCount);
            }
            return true;
        }
        /// <summary>Sets every element to its default value.</summary>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public void Clear()
        {
            ThrowIfDisposed();
            var pointer = _pointer;
            var remaining = _length;
            // A span holds at most int.MaxValue elements, so longer heaps are cleared in chunks.
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, int.MaxValue);
                new Span<T>(pointer, count).Clear();
                pointer += count;
                remaining -= count;
            }
        }
        /// <summary>Gets a span over all elements.</summary>
        /// <returns>A span valid until the heap is disposed; the span itself does not observe disposal.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Length"/> exceeds <see cref="int.MaxValue"/>; slice the heap first.
        /// </exception>
        public Span<T> AsSpan()
        {
            ThrowIfDisposed();
            if (_length > int.MaxValue)
            {
                throw new InvalidOperationException($"A heap of {_length} elements is too long for a span; slice it first.");
            }
            return new Span<T>(_pointer, (int)_length);
        }
        /// <summary>Gets a reference to the first element, so that a <c>fixed</c> statement can take the heap's address.</summary>
        /// <returns>A reference to the first element, or a null reference when the heap is empty.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public ref T GetPinnableReference()
        {
            ThrowIfDisposed();
            if (_length == 0)
            {
                return ref *(T*)null;
            }
            return ref *_pointer;
        }
        /// <summary>Ends the shared lease and frees the owned allocation at most once.</summary>
        /// <remarks>
        /// Disposing any copy, slice or read-only view frees the whole allocation. Repeated calls, and calls on a heap
        /// without memory, have no effect.
        /// </remarks>
        public void Dispose()
        {
            if (_lifetime.TryDispose() && _allocation != null)
            {
                CRuntime.Free((IntPtr)_allocation);
            }
        }

        /// <summary>Rejects an ended lease while allowing a heap without memory.</summary>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        private void ThrowIfDisposed()
        {
            if (_pointer != null)
            {
                _lifetime.ThrowIfDisposed(nameof(Heap<T>));
            }
        }
    }
}
