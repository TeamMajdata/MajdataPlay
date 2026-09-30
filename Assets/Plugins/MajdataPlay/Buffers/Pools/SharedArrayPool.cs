using MajdataPlay.Runtime;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Buffers.Pools
{
    internal class SharedArrayPool<T> : ArrayPool<T>
    {
        readonly Bucket[] _buckets;

        readonly int _minArraySize;
        readonly int _maxArraySize;
        readonly GCCallback _onGCCallback;

        public SharedArrayPool(int minArrayLength, int maxArrayLength, int maxArraysPerBucket)
        {
            if (minArrayLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minArrayLength));
            }
            if (maxArrayLength < minArrayLength)
            {
                throw new ArgumentOutOfRangeException(nameof(maxArrayLength));
            }
            if (maxArraysPerBucket <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxArraysPerBucket));
            }

            _minArraySize = GetNextPowerOf2(minArrayLength);
            _maxArraySize = GetNextPowerOf2(maxArrayLength);
            _onGCCallback = OnGCCallback;

            var numBuckets = 0;
            
            for(var sizeCounter = (long)_minArraySize; sizeCounter <= _maxArraySize; sizeCounter <<= 1)
            {
                numBuckets++;
            }

            _buckets = new Bucket[numBuckets];
            var currentSize = _minArraySize;

            for (var i = 0; i < numBuckets; i++)
            {
                _buckets[i] = new Bucket(currentSize, maxArraysPerBucket);
                currentSize <<= 1;
            }

            GCCallbackRegistration.RegisterGCCallback(_onGCCallback);
        }

        public override T[] Rent(int minimumLength)
        {
            if (minimumLength <= 0)
            {
                return Array.Empty<T>();
            }

            if (minimumLength > _maxArraySize)
            {
                return new T[minimumLength];
            }

            var index = GetBucketIndex(minimumLength);
            return _buckets[index].Rent();
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            if (array == null || array.Length == 0)
            {
                return;
            }

            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }

            if (array.Length > _maxArraySize)
            {
                return;
            }

            var index = GetBucketIndex(array.Length);

            if (_buckets[index].ArraySize == array.Length)
            {
                _buckets[index].Return(array);
            }
        }

        public void Trim()
        {
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i].Trim();
            }
        }
        int GetBucketIndex(int length)
        {
            if (length <= _minArraySize)
            {
                return 0;
            }

            length--;
            var index = 0;
            while (length >= _minArraySize)
            {
                length >>= 1;
                index++;
            }
            return index;
        }

        bool OnGCCallback()
        {
            Trim();
            return true;
        }

        static int GetNextPowerOf2(int value)
        {
            if (value <= 1)
            {
                return 1;
            }
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        class Bucket
        {
            public int ArraySize { get; }
            

            int _count = 0;

            readonly int _maxArrayCount;
            readonly ConcurrentStack<T[]> _buffers = new();

            public Bucket(int arraySize, int maxArrayCount)
            {
                ArraySize = arraySize;
                _maxArrayCount = maxArrayCount;
            }

            public T[] Rent()
            {
                if (_buffers.TryPop(out var buffer))
                {
                    Interlocked.Decrement(ref _count);
                    return buffer;
                }
                return new T[ArraySize];
            }

            public void Return(T[] array)
            {
                if (_count < _maxArrayCount)
                {
                    _buffers.Push(array);
                    Interlocked.Increment(ref _count);
                }
            }

            public void Trim()
            {
                _buffers.Clear();
                _count = 0;
            }
        }
    }
}
