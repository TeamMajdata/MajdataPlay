using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.UnsafeKit
{
    public static unsafe class UnsafeHelper
    {
        public static T* Alloc<T>(long size) where T : unmanaged
        {
            return (T*)Marshal.AllocHGlobal((IntPtr)(size * (long)sizeof(T)));
        }
        public static IntPtr Alloc(IntPtr size)
        {
            return Marshal.AllocHGlobal(size);
        }
        public static void Free<T>(T* ptr) where T : unmanaged
        {
            Marshal.FreeHGlobal((IntPtr)ptr);
        }
        public static void Free(IntPtr ptr)
        {
            Marshal.FreeHGlobal(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* AddByteOffset<T>(T* ptr, long offset) where T : unmanaged
        {
            return (T*)((byte*)ptr + offset);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* AddOffset<T>(T* ptr, long elementOffset) where T : unmanaged
        {
            return ptr + elementOffset;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetElement<T>(T* ptr, long elementOffset) where T : unmanaged
        {
            ptr = AddOffset(ptr, elementOffset);

            return ref *ptr;
        }

        public static bool TryGetInstanceFromGCHandle<T>(IntPtr handle, [NotNullWhen(true)]out T? instance)
        {
            instance = default;
            if (handle == IntPtr.Zero)
            {
                return false;
            }
            var gcHandle = GCHandle.FromIntPtr(handle);
            if (!gcHandle.IsAllocated)
            {
                return false;
            }
            instance = (T?)gcHandle.Target;

            return instance is not null;
        }
    }
}
