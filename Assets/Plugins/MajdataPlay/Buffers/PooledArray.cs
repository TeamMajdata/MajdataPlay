using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#nullable enable
namespace MajdataPlay.Buffers
{
    /// <summary>Owns a pooled array whose struct copies share one disposal lifetime.</summary>
    /// <typeparam name="T">The element type of the rented array.</typeparam>
    /// <remarks>
    /// Disposing any copy invalidates every copy of the same lease. Resizing replaces the calling
    /// instance's lease and invalidates its previous copies and enumerators. Concurrent disposal is
    /// supported; array access and resizing require external synchronization with disposal.
    /// Previously obtained arrays, spans, memory and references must not be used after disposal or resize.
    /// A default value is an empty, uninitialized container; its copies have no shared lifetime until rented.
    /// </remarks>
    public struct PooledArray<T> : IEnumerable<T>, IEnumerable, IDisposable
    {
        /// <summary>Gets a writable reference to an element of the current array.</summary>
        /// <param name="index">The zero-based element index.</param>
        /// <returns>A reference to the requested element.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="IndexOutOfRangeException">The index is outside the array.</exception>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return ref CurrentArray[index];
            }
        }
        /// <summary>Gets the rented array length, or zero when uninitialized or disposed.</summary>
        public readonly int Length
        {
            get
            {
                return _lifetime.IsDisposed ? 0 : CurrentArray.Length;
            }
        }
        /// <summary>Gets whether this container is empty, uninitialized or disposed.</summary>
        public readonly bool IsEmpty
        {
            get
            {
                return Length == 0;
            }
        }

        /// <summary>Gets the stored array, treating an uninitialized container as empty.</summary>
        private readonly T[] CurrentArray
        {
            get
            {
                return _array ?? Array.Empty<T>();
            }
        }
        /// <summary>Gets the owning pool, using the shared pool for an uninitialized container.</summary>
        private readonly ArrayPool<T> CurrentPool
        {
            get
            {
                return _pool ?? ArrayPool<T>.Shared;
            }
        }

        /// <summary>The disposal lifetime shared by all copies of this lease.</summary>
        private readonly PooledDisposable _lifetime;

        /// <summary>The rented array, or null only while this container is uninitialized.</summary>
        private T[]? _array;
        /// <summary>Whether to clear array contents when returning them to the pool.</summary>
        private readonly bool _clearArrayWhenReturn;
        /// <summary>The owning pool, or null for an uninitialized container.</summary>
        private readonly ArrayPool<T>? _pool;


        /// <summary>Rents an array from the shared array pool.</summary>
        /// <param name="minimumLength">The minimum number of elements required.</param>
        /// <param name="clearArrayWhenReturn">Whether to clear arrays when returning them.</param>
        /// <exception cref="ArgumentOutOfRangeException">The requested length is negative.</exception>
        public PooledArray(int minimumLength, bool clearArrayWhenReturn) : this(minimumLength, ArrayPool<T>.Shared, clearArrayWhenReturn)
        {

        }
        /// <summary>Rents an array from the specified pool with a new shared disposal lifetime.</summary>
        /// <param name="minimumLength">The minimum number of elements required.</param>
        /// <param name="pool">The pool that rents and receives the array.</param>
        /// <param name="clearArrayWhenReturn">Whether to clear arrays when returning them.</param>
        /// <exception cref="ArgumentOutOfRangeException">The pool rejects the requested length.</exception>
        public PooledArray(int minimumLength, ArrayPool<T> pool, bool clearArrayWhenReturn)
        {
            _array = pool.Rent(minimumLength);
            _pool = pool;
            _clearArrayWhenReturn = clearArrayWhenReturn;
            _lifetime = PooledDisposable.Rent();
        }
        /// <summary>Takes ownership of an already rented array with a new shared disposal lifetime.</summary>
        /// <param name="array">The array whose ownership is transferred to this container.</param>
        /// <param name="pool">The pool to which the array will be returned.</param>
        /// <param name="clearArrayWhenReturn">Whether to clear arrays when returning them.</param>
        internal PooledArray(T[] array, ArrayPool<T> pool, bool clearArrayWhenReturn)
        {
            _array = array;
            _pool = pool;
            _clearArrayWhenReturn = clearArrayWhenReturn;
            _lifetime = PooledDisposable.Rent();
        }

        /// <summary>Replaces this lease with a resized array, preserving the elements that fit.</summary>
        /// <param name="newSize">The requested length, rounded up to a power of two when positive.</param>
        /// <remarks>
        /// A successful resize invalidates all copies of the old lease, including when resizing to zero.
        /// Failure to rent the replacement leaves the old lease intact. If returning the old array throws,
        /// the old lease remains disposed and the replacement is disposed before the exception propagates.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The requested size is negative or too large for the pool.</exception>
        public void Resize(int newSize)
        {
            ThrowIfDisposed();
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newSize));
            }
            var replacement = newSize == 0
                ? new PooledArray<T>(Array.Empty<T>(), CurrentPool, _clearArrayWhenReturn)
                : new PooledArray<T>(RoundUpToPowerOf2(newSize), CurrentPool, _clearArrayWhenReturn);
            try
            {
                var copyLength = Math.Min(Length, replacement.Length);
                CurrentArray.AsSpan(0, copyLength).CopyTo(replacement.CurrentArray);
                Dispose();
            }
            catch
            {
                replacement.Dispose();
                throw;
            }
            this = replacement;
        }
        /// <summary>Resizes only when the current lease is shorter than the requested length.</summary>
        /// <param name="length">The minimum number of elements required.</param>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The requested length is too large for the pool.</exception>
        public void EnsureLength(int length)
        {
            ThrowIfDisposed();
            if (length > Length)
            {
                Resize(length);
            }
        }
        /// <summary>Gets the rented array without transferring ownership.</summary>
        /// <returns>The current array, valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public readonly T[] AsArray()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        /// <summary>Gets memory covering the current array.</summary>
        /// <returns>Memory valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public readonly Memory<T> AsMemory()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        /// <summary>Gets memory covering a range of the current array.</summary>
        /// <param name="range">The range of elements to expose.</param>
        /// <returns>Memory valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the array.</exception>
        public readonly Memory<T> AsMemory(Range range)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(range);
        }
        /// <summary>Gets memory from the specified index to the end of the array.</summary>
        /// <param name="start">The first element to expose.</param>
        /// <returns>Memory valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The start is outside the array.</exception>
        public readonly Memory<T> AsMemory(int start)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(start);
        }
        /// <summary>Gets memory covering a slice of the current array.</summary>
        /// <param name="start">The first element to expose.</param>
        /// <param name="length">The number of elements to expose.</param>
        /// <returns>Memory valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The slice is outside the array.</exception>
        public readonly Memory<T> AsMemory(int start, int length)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(start, length);
        }
        /// <summary>Gets a span covering the current array.</summary>
        /// <returns>A span valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public readonly Span<T> AsSpan()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        /// <summary>Gets a span covering a range of the current array.</summary>
        /// <param name="range">The range of elements to expose.</param>
        /// <returns>A span valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the array.</exception>
        public readonly Span<T> AsSpan(Range range)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(range);
        }
        /// <summary>Gets a span from the specified index to the end of the array.</summary>
        /// <param name="start">The first element to expose.</param>
        /// <returns>A span valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The start is outside the array.</exception>
        public readonly Span<T> AsSpan(int start)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(start);
        }
        /// <summary>Gets a span covering a slice of the current array.</summary>
        /// <param name="start">The first element to expose.</param>
        /// <param name="length">The number of elements to expose.</param>
        /// <returns>A span valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The slice is outside the array.</exception>
        public readonly Span<T> AsSpan(int start, int length)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(start, length);
        }
        /// <summary>Ends the shared lease and returns its nonempty array at most once.</summary>
        /// <remarks>
        /// The lease stays disposed even if the pool throws when returning the array. Disposing an
        /// uninitialized container ends only that value's empty lifetime, without allocating shared state.
        /// </remarks>
        public void Dispose()
        {
            if (_array is null)
            {
                _array = Array.Empty<T>();
                return;
            }
            if (!_lifetime.TryDispose())
            {
                return;
            }

            var array = _array;
            _array = Array.Empty<T>();
            if (array.Length != 0)
            {
                CurrentPool.Return(array, _clearArrayWhenReturn);
            }
        }
        /// <summary>Creates an enumerator sharing the current lease's disposal state.</summary>
        /// <returns>An enumerator that is invalidated by disposal or resize of this lease.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public Enumerator GetEnumerator()
        {
            ThrowIfDisposed();
            return new Enumerator(this);
        }
        /// <inheritdoc/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Rejects an ended lease while allowing an uninitialized empty container.</summary>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private readonly void ThrowIfDisposed()
        {
            if (_array is not null)
            {
                _lifetime.ThrowIfDisposed(nameof(PooledArray<T>));
            }
        }

        /// <summary>Rounds a size up to the next power of two.</summary>
        /// <param name="n">The nonnegative size to round.</param>
        /// <returns>The rounded size, or a negative value if it exceeds the positive integer range.</returns>
        private static int RoundUpToPowerOf2(int n)
        {
            if (n == 0)
            {
                return 1;
            }

            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }

        /// <summary>Views a live lease as a span without transferring ownership.</summary>
        /// <param name="lease">The lease to view.</param>
        /// <returns>A span valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public static implicit operator Span<T>(PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }
        /// <summary>Views a live lease as memory without transferring ownership.</summary>
        /// <param name="lease">The lease to view.</param>
        /// <returns>Memory valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public static implicit operator Memory<T>(PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }
        /// <summary>Views a live lease as its underlying array without transferring ownership.</summary>
        /// <param name="lease">The lease to view.</param>
        /// <returns>The array, valid only until disposal or resize.</returns>
        /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
        public static implicit operator T[](PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }

        /// <summary>Enumerates array elements while observing the shared lease's disposal state.</summary>
        public struct Enumerator : IEnumerator<T>, IDisposable, IEnumerator
        {
            /// <summary>The next array index to enumerate.</summary>
            private int _index;
            /// <summary>The most recently read value exposed through the enumerator interfaces.</summary>
            private T? _current;

            /// <summary>A copy of the array lease sharing its disposal lifetime.</summary>
            private readonly PooledArray<T> _array;

            /// <summary>Gets a writable reference to the current element.</summary>
            /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
            /// <exception cref="IndexOutOfRangeException">The enumerator has not reached an element.</exception>
            public ref T Current
            {
                get
                {
                    return ref _array[_index - 1];
                }
            }
            /// <inheritdoc/>
            T IEnumerator<T>.Current
            {
                get
                {
                    _array.ThrowIfDisposed();
                    return _current!;
                }
            }

            /// <inheritdoc/>
            object IEnumerator.Current
            {
                get
                {
                    _array.ThrowIfDisposed();
                    return _current!;
                }
            }

            /// <summary>Creates an enumerator for the supplied lease.</summary>
            /// <param name="array">The lease to enumerate.</param>
            /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
            public Enumerator(PooledArray<T> array)
            {
                array.ThrowIfDisposed();
                _array = array;
                _index = 0;
                _current = default;
            }

            /// <summary>Ends enumeration without disposing the underlying array lease.</summary>
            public void Dispose()
            {

            }
            /// <summary>Advances to the next array element.</summary>
            /// <returns>True if an element was read; false if enumeration is complete.</returns>
            /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
            public bool MoveNext()
            {
                _array.ThrowIfDisposed();
                if (_index < _array.Length)
                {
                    _current = _array[_index];
                    _index++;
                    return true;
                }
                return false;
            }

            /// <summary>Restarts enumeration before the first element.</summary>
            /// <exception cref="ObjectDisposedException">The shared lease has ended.</exception>
            void IEnumerator.Reset()
            {
                _array.ThrowIfDisposed();
                _index = 0;
                _current = default!;
            }
        }
    }
}
