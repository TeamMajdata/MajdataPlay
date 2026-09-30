using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#nullable enable
namespace MajdataPlay.Buffers
{
    public class PooledList<T> : IList<T>, ICollection<T>, IReadOnlyList<T>, IDisposable
    {
        public struct Enumerator : IEnumerator<T>, IDisposable, IEnumerator
        {
            int _index;
            uint _version;
            T _current;

            PooledList<T> _list;

            public T Current
            {
                get
                {
                    ThrowIfDisposed();
                    return _current;
                }
            }

            object? IEnumerator.Current
            {
                get
                {
                    ThrowIfDisposed();
                    if (_index == 0 || _index == _list._size + 1)
                    {
                        throw new InvalidOperationException();
                    }

                    return Current;
                }
            }

            internal Enumerator(PooledList<T> list)
            {
                this._list = list;
                _index = 0;
                _version = list._version;
                _current = default!;
            }

            public void Dispose()
            {

            }
            public bool MoveNext()
            {
                ThrowIfDisposed();
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("Enumeration failed version check.");
                }
                if (_index < _list._size)
                {
                    _current = _list._array[_index];
                    _index++;
                    return true;
                }

                return false;
            }

            void IEnumerator.Reset()
            {
                ThrowIfDisposed();
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("Enumeration failed version check.");
                }

                _index = 0;
                _current = default!;
            }
            void ThrowIfDisposed()
            {
                if (_list._isDisposed)
                {
                    throw new ObjectDisposedException(nameof(PooledList<T>), "This rented array has been disposed.");
                }
            }
        }
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _size;
            }
        }
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return _array.Length;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                ThrowIfDisposed();
                if (value < _size)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Capacity cannot be less than the current size.");
                }
                if(value == _array.Length)
                {
                    return;
                }
                var newArray = new PooledArray<T>(_pool.Rent(value), _pool, true);
                if (_size > 0)
                {
                    Array.Copy(_array, newArray, _size);
                }
                _array.Dispose();
                _array = newArray;
            }
        }
        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
                }
                return _array[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                ThrowIfDisposed();
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
                }
                _array[index] = value;
                _version++;
            }
        }
        bool ICollection<T>.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        private int _size = 0;
        private uint _version = 0;
        private PooledArray<T> _array;
        private bool _isDisposed = false;
        private readonly ArrayPool<T> _pool;

        ~PooledList()
        {
            Dispose();
        }
        public PooledList() : this(8, Pool<T>.ArrayPool)
        {

        }
        public PooledList(IEnumerable<T> items) : this(8, Pool<T>.ArrayPool)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items), "Items cannot be null.");
            }
            AddRange(items);
        }
        public PooledList(int capacity): this(capacity, Pool<T>.ArrayPool)
        {
            
        }
        public PooledList(int capacity, ArrayPool<T> pool)
        {
            _pool = pool;
            _array = new PooledArray<T>(_pool.Rent(capacity), _pool, true);
        }
        public void Add(T item)
        {
            ThrowIfDisposed();
            EnsureCapacity(_size + 1);
            _array[_size++] = item;
            _version++;
        }
        public void AddRange(IEnumerable<T> items)
        {
            ThrowIfDisposed();
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items), "Items cannot be null.");
            }
            foreach (var item in items)
            {
                Add(item);
            }
        }
        public void AddRange(ReadOnlySpan<T> items)
        {
            ThrowIfDisposed();
            for (var i = 0; i < items.Length; i++)
            {
                Add(items[i]);
            }
        }
        public void Insert(int index, T item)
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
            }
            EnsureCapacity(_size + 1);
            if (index < _size - 1)
            {
                Array.Copy(_array, index, _array, index + 1, _size - index - 1);
            }
            _array[index] = item;
            _size++;
            _version++;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadUnsafe(int index)
        {
            return Unsafe.Add(ref MemoryMarshal.GetReference(_array.AsSpan()), index);
        }
        public void Clear()
        {
            ThrowIfDisposed();
            if (_size == 0)
            {
                return; // Nothing to clear
            }
            _size = 0;
            _version++;
            Array.Clear(_array, 0, _size);
        }
        public int IndexOf(T item)
        {
            ThrowIfDisposed();

            return Array.IndexOf(_array, item);
        }
        public bool Remove(T item)
        {
            ThrowIfDisposed();
            var index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            RemoveAt(index);
            _version++;
            return true;
        }
        public void RemoveAt(int index)
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
            }
            if (index != _size - 1)
            {
                Array.Copy(_array, index + 1, _array, index, _size - index - 1);
            }
            _size--;
            _array[_size] = default!;
            _version++;
        }
        public bool Contains(T item)
        {
            ThrowIfDisposed();
            for (var i = 0; i < _size; i++)
            {
                var current = _array[i];
                if (EqualityComparer<T>.Default.Equals(current, item))
                {
                    return true;
                }
            }
            return false;
        }
        public void CopyTo(T[] array, int arrayIndex)
        {
            ThrowIfDisposed();
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array), "Array cannot be null.");
            }
            if (arrayIndex < 0 || arrayIndex + _size > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "Array index is out of range.");
            }
            else if (_size == 0)
            {
                return;
            }
            Array.Copy(_array, 0, array, arrayIndex, _size);
        }
        public void CopyTo(Span<T> span)
        {
            ThrowIfDisposed();
            if (span.Length < _size)
            {
                throw new ArgumentException("Span is too small to copy the elements.");
            }
            else if (_size == 0)
            {
                return;
            }
            var array = _array.AsSpan(0, _size);
            array.CopyTo(span);
        }
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _array.Dispose();
        }
        public T[] ToArray()
        {
            ThrowIfDisposed();
            if (_size == 0)
            {
                return Array.Empty<T>();
            }
            var array = new T[_size];
            Array.Copy(_array, array, _size);

            return array;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan()
        {
            return _array.AsSpan(0, _size);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> AsMemory()
        {
            return _array.AsMemory(0, _size);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArraySegment<T> AsArraySegment()
        {
            return new ArraySegment<T>(_array, 0, _size);
        }
        void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(PooledList<T>), "This rented array has been disposed.");
            }
        }
        void EnsureCapacity(int minCapacity)
        {
            if (_array.Length < minCapacity)
            {
                var newCapacity = ((_array.Length == 0) ? 16 : (_array.Length * 2));

                if (newCapacity < minCapacity)
                {
                    newCapacity = minCapacity;
                }
                Capacity = newCapacity;
            }
        }
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}