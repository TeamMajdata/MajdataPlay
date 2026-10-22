using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace MajdataPlay.Collections
{
    internal static class NativeArrayExtensions
    {
        unsafe class NativeArrayHandle<T> : MemoryManager<T>, IMemoryOwner<T> where T: struct
        {
            bool _isDisposed = false;

            // Not readonly: disposing a readonly struct field would dispose a defensive copy and leave this one created.
            NativeArray<T> _nativeArray;
            readonly void* _pointer;
            readonly int _length;

            ~NativeArrayHandle()
            {
                Dispose(false);
            }

            public NativeArrayHandle(NativeArray<T> nativeArray)
            {
                if(!nativeArray.IsCreated)
                {
                    throw new ArgumentException("The NativeArray is not created or disposed.", nameof(nativeArray));
                }
                _nativeArray = nativeArray;
                _length = _nativeArray.Length;
                _pointer = (void*)_nativeArray.GetUnsafePtr();
            }

            public override Span<T> GetSpan()
            {
                ThrowIfDisposed();
                return new Span<T>(_pointer, _length);
            }
            public override MemoryHandle Pin(int elementIndex = 0)
            {
                ThrowIfDisposed();
                if (elementIndex < 0 || elementIndex >= _length)
                {
                    throw new ArgumentOutOfRangeException(nameof(elementIndex));
                }
                return new MemoryHandle((byte*)_pointer + Unsafe.SizeOf<T>() * elementIndex, default, this);
            }
            public override void Unpin()
            {
                ThrowIfDisposed();
            }
            protected override void Dispose(bool disposing)
            {
                if(_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _nativeArray.Dispose();
            }

            void ThrowIfDisposed()
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(NativeArrayHandle<T>));
                }
                else if(!_nativeArray.IsCreated)
                {
                    throw new ObjectDisposedException(nameof(NativeArray<T>));
                }
            }
        }
        public static IMemoryOwner<T> AsMemoryOwner<T>(this NativeArray<T> nativeArray) where T : struct
        {
            return new NativeArrayHandle<T>(nativeArray);
        }
    }
}
