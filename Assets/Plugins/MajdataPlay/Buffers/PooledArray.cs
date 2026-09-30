using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
#nullable enable
namespace MajdataPlay.Buffers
{
    public struct PooledArray<T> : IEnumerable<T>, IEnumerable, IDisposable
    {
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return ref CurrentArray[index];
            }
        }
        public int Length { get => CurrentArray.Length; }
        public bool IsEmpty { get => Length == 0; }

        private readonly T[] CurrentArray
        {
            get
            {
                return _array ?? Array.Empty<T>();
            }
        }
        private readonly ArrayPool<T> CurrentPool
        {
            get
            {
                return _pool ?? ArrayPool<T>.Shared;
            }
        }

        private int _isDisposed;

        private T[] _array;
        private readonly bool _clearArrayWhenReturn;
        private readonly ArrayPool<T> _pool;


        public PooledArray(int minimumLength, bool clearArrayWhenReturn) : this(minimumLength, ArrayPool<T>.Shared, clearArrayWhenReturn)
        {

        }
        public PooledArray(int minimumLength, ArrayPool<T> pool, bool clearArrayWhenReturn)
        {
            _array = pool.Rent(minimumLength);
            _pool = pool;
            _clearArrayWhenReturn = clearArrayWhenReturn;
        }
        internal PooledArray(T[] array, ArrayPool<T> pool, bool clearArrayWhenReturn)
        {
            _array = array;
            _pool = pool;
            _clearArrayWhenReturn = clearArrayWhenReturn;
        }

        
        public void Resize(int newSize)
        {
            ThrowIfDisposed();
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newSize));
            }
            else if(newSize == 0)
            {
                CurrentPool.Return(CurrentArray);
                _array = Array.Empty<T>();
                return;
            }
            newSize = RoundUpToPowerOf2(newSize);
            var newArray = CurrentPool.Rent(newSize);
            var copyLength = Math.Min(Length, newArray.Length);

            CurrentArray.AsSpan(0, copyLength).CopyTo(newArray);

            CurrentPool.Return(CurrentArray);
            _array = newArray;
        }
        public void EnsureLength(int length)
        {
            ThrowIfDisposed();
            if (length > Length)
            {
                Resize(length);
            }
        }
        public readonly T[] AsArray()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        public readonly Memory<T> AsMemory()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        public readonly Memory<T> AsMemory(Range range)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(range);
        }
        public readonly Memory<T> AsMemory(int start)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(start);
        }
        public readonly Memory<T> AsMemory(int start, int length)
        {
            ThrowIfDisposed();
            return CurrentArray.AsMemory(start, length);
        }
        public readonly Span<T> AsSpan()
        {
            ThrowIfDisposed();
            return CurrentArray;
        }
        public readonly Span<T> AsSpan(Range range)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(range);
        }
        public readonly Span<T> AsSpan(int start)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(start);
        }
        public readonly Span<T> AsSpan(int start, int length)
        {
            ThrowIfDisposed();
            return CurrentArray.AsSpan(start, length);
        }
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 1)
            {
                return;
            }

            if (CurrentArray.Length != 0)
            {
                CurrentPool.Return(_array, _clearArrayWhenReturn);
                _array = Array.Empty<T>();
            }
        }
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private readonly void ThrowIfDisposed()
        {
            if(_isDisposed == 0)
            {
                return;
            }
            throw new ObjectDisposedException(nameof(PooledArray<T>));
        }

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

        public static implicit operator Span<T>(PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }
        public static implicit operator Memory<T>(PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }
        public static implicit operator T[](PooledArray<T> lease)
        {
            lease.ThrowIfDisposed();
            return lease.CurrentArray;
        }

        public struct Enumerator : IEnumerator<T>, IDisposable, IEnumerator
        {
            int _index;
            T? _current;

            PooledArray<T> _array;

            public ref T Current
            {
                get
                {
                    return ref _array[_index - 1];
                }
            }
            T IEnumerator<T>.Current
            {
                get
                {
                    return _current!;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return _current!;
                }
            }

            public Enumerator(PooledArray<T> array)
            {
                this._array = array;
                _index = 0;
                _current = default;
            }

            public void Dispose()
            {

            }
            public bool MoveNext()
            {
                if (_index < _array.Length)
                {
                    _current = _array[_index];
                    _index++;
                    return true;
                }
                return false;
            }

            void IEnumerator.Reset()
            {
                _index = 0;
                _current = default!;
            }
        }
    }
}
