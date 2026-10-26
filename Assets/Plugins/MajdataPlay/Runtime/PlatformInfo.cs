using MajdataPlay.Syscall.Darwin;
using MajdataPlay.Syscall.Linux;
using MajdataPlay.Syscall.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MajdataPlay.Runtime
{
    public static class PlatformInfo
    {
        public static bool IsWindows
        {
            get
            {
#if UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }
        public static bool IsLinux
        {
            get
            {
#if UNITY_STANDALONE_LINUX
                return true;
#else
                return false;
#endif
            }
        }
        public static bool IsMacOS
        {
            get
            {
#if UNITY_STANDALONE_OSX
                return true;
#else
                return false;
#endif
            }
        }
        public static bool IsAndroid
        {
            get
            {
#if UNITY_ANDROID
                return true;
#else
                return false;
#endif
            }
        }
        public static bool IsIOS
        {
            get
            {
#if UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsX86Processor
        {
            get
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.X86;
            }
        }
        public static bool IsX64Processor
        {
            get
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.X64;
            }
        }
        public static bool IsArmV7Processor
        {
            get
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm;
            }
        }
        public static bool IsArm64Processor
        {
            get
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            }
        }
        public static bool Is32bitProcessor
        {
            get
            {
                return IntPtr.Size == 4;
            }
        }
        public static bool Is64bitProcessor
        {
            get
            {
                return IntPtr.Size == 8;
            }
        }

        public static ulong GetCurrentOSThreadId()
        {
#if UNITY_STANDALONE_WIN
            return Win32API.GetCurrentThreadId();
#elif UNITY_STANDALONE_LINUX
            return (ulong)Glibc.gettid();
#elif UNITY_ANDROID
            return (ulong)Bionic.gettid();
#elif UNITY_IOS || UNITY_STANDALONE_OSX
            DarwinApi.pthread_threadid_np(IntPtr.Zero, out var tid);
            return tid;
#endif
        }
    }
}
