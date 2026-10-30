using System;
using System.Runtime.InteropServices;

#nullable enable
namespace MajdataPlay.Syscall
{
    /// <summary>
    /// Binds the heap functions of the platform's C runtime.
    /// </summary>
    /// <remarks>
    /// Windows uses the Universal C Runtime (<c>ucrtbase</c>), macOS <c>libSystem</c>, Android Bionic and Linux glibc.
    /// iOS links the functions statically through the <c>malloc_</c> and <c>free_</c> wrappers in
    /// <c>Assets/Plugins/iOS/SyscallBridge.mm</c>. Memory from <see cref="Malloc"/> must be released with
    /// <see cref="Free"/>, and <see cref="Free"/> must only receive memory from this C runtime: a native library that
    /// links its own C runtime statically may use a different heap.
    /// </remarks>
    public static class CRuntime
    {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        /// <summary>The library that exports the C runtime heap functions.</summary>
        private const string LibraryName = "ucrtbase";
#elif UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        /// <summary>The library that exports the C runtime heap functions.</summary>
        private const string LibraryName = "libSystem.B.dylib";
#elif UNITY_IOS && !UNITY_EDITOR
        /// <summary>The library that exports the C runtime heap functions.</summary>
        private const string LibraryName = "__Internal";
#elif UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>The library that exports the C runtime heap functions.</summary>
        private const string LibraryName = "libc";
#else
        /// <summary>The library that exports the C runtime heap functions.</summary>
        private const string LibraryName = "libc.so.6";
#endif

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>The exported name of the allocation function.</summary>
        private const string MallocEntryPoint = "malloc_";
        /// <summary>The exported name of the release function.</summary>
        private const string FreeEntryPoint = "free_";
#else
        /// <summary>The exported name of the allocation function.</summary>
        private const string MallocEntryPoint = "malloc";
        /// <summary>The exported name of the release function.</summary>
        private const string FreeEntryPoint = "free";
#endif

        /// <summary>
        /// Allocates an uninitialized block from the C runtime heap with C <c>malloc</c>.
        /// </summary>
        /// <param name="size">The number of bytes to allocate.</param>
        /// <returns>
        /// The address of the block, aligned for any fundamental C type, or <see cref="IntPtr.Zero"/> when the
        /// allocation fails. A zero <paramref name="size"/> returns either <see cref="IntPtr.Zero"/> or a block that
        /// must not be accessed but must still be freed.
        /// </returns>
        /// <exception cref="DllNotFoundException">The C runtime library cannot be loaded.</exception>
        /// <exception cref="EntryPointNotFoundException">The C runtime library does not export the function.</exception>
        [DllImport(LibraryName, EntryPoint = MallocEntryPoint, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Malloc(UIntPtr size);

        /// <summary>
        /// Returns a block from <see cref="Malloc"/> to the C runtime heap with C <c>free</c>.
        /// </summary>
        /// <param name="pointer">The block to release, or <see cref="IntPtr.Zero"/> to do nothing.</param>
        /// <exception cref="DllNotFoundException">The C runtime library cannot be loaded.</exception>
        /// <exception cref="EntryPointNotFoundException">The C runtime library does not export the function.</exception>
        [DllImport(LibraryName, EntryPoint = FreeEntryPoint, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr pointer);
    }
}
