using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MajdataPlay.Syscall.Darwin
{
    public static class DarwinApi
    {
#if UNITY_IOS
        const string DLL_NAME = "__Internal";
        const string ENTRY_POINT = "pthread_threadid_np_";
#else
        const string DLL_NAME = "libSystem.B.dylib";
        const string ENTRY_POINT = "pthread_threadid_np";
#endif
        [DllImport(DLL_NAME, EntryPoint = ENTRY_POINT)]
        public static extern int pthread_threadid_np(IntPtr thread, out ulong threadId);
    }
}
