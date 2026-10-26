using System;
using System.Dynamic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
using NativeLong = System.Int32;
#else
// C long is pointer-sized on Android/Unix, including 32-bit ARM.
using NativeLong = System.IntPtr;
#endif

#nullable enable
namespace MajdataPlay.Net.Curl.Core.PInvoke
{
    internal class LibCurl
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string DLL_NAME = "__Internal";
#else
        const string DLL_NAME = "libcurl";
#endif
        public const uint CURL_READFUNC_ABORT = 0x10000000;
        public const uint CURL_READFUNC_PAUSE = 0x10000001;

        public const uint CURL_WRITEFUNC_PAUSE = 0x10000001;
        public const uint CURL_WRITEFUNC_ERROR = 0xFFFFFFFF;


        public static class Easy
        {
            [DllImport(DLL_NAME, EntryPoint = "curl_easy_init", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr Init();

            [DllImport(DLL_NAME,EntryPoint = "curl_easy_perform", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode Perform(IntPtr handle);
            public static string? GetErrorMessage(CurlCode errornum)
            {
                var ptr = curl_easy_strerror(errornum);

                if (ptr == IntPtr.Zero)
                {
                    return null;
                }
                else
                {
                    return Marshal.PtrToStringUTF8(ptr);
                }
            }

            [DllImport(DLL_NAME, EntryPoint = "curl_easy_pause", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode Pause(IntPtr handle, CurlPauseAction action);

            [DllImport(DLL_NAME, EntryPoint = "curl_unity_setopt_string", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode SetOption(IntPtr handle, CurlOption option, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
            [DllImport(DLL_NAME, EntryPoint = "curl_unity_setopt_long", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode SetOption(IntPtr handle, CurlOption option, long value);
            [DllImport(DLL_NAME, EntryPoint = "curl_unity_setopt_ptr", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode SetOption(IntPtr handle, CurlOption option, IntPtr value);

            public static CurlCode SetOption<T>(IntPtr handle, CurlOption option, T value) where T : struct, Enum
            {
                var num = Unsafe.As<T, long>(ref value);

                return SetOption(handle, option, num);
            }



            [DllImport(DLL_NAME, EntryPoint = "curl_unity_getinfo_double", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode GetInfo(IntPtr curl, CurlInfo info, out double value);

            [DllImport(DLL_NAME, EntryPoint = "curl_unity_getinfo_long", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode GetInfo(IntPtr curl, CurlInfo info, out long value);
            [DllImport(DLL_NAME, EntryPoint = "curl_unity_getinfo_ptr", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlCode GetInfo(IntPtr curl, CurlInfo info, out IntPtr value);
            public static CurlCode GetInfo(IntPtr curl, CurlInfo info, out string? value)
            {
                var strPtr = IntPtr.Zero;
                var result = GetInfo(curl, info, out strPtr);
                if (strPtr != IntPtr.Zero)
                {
                    value = Marshal.PtrToStringUTF8(strPtr);
                }
                else
                {
                    value = default;
                }
                return result;
            }


            [DllImport(DLL_NAME, EntryPoint = "curl_easy_cleanup", CallingConvention = CallingConvention.Cdecl)]
            public static extern void CleanUp(IntPtr handle);



            [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
            static extern IntPtr curl_easy_strerror(CurlCode errornum);
            //[DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
            //static extern CurlCode curl_unity_setopt_read_function(IntPtr handle, CurlReadOrWriteCallback value);
            //[DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
            //static extern CurlCode curl_unity_setopt_write_function(IntPtr handle, CurlReadOrWriteCallback value);
            //[DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
            //static extern CurlCode curl_unity_setopt_header_function(IntPtr handle, CurlReadOrWriteCallback value);
        }

        public static class Multi
        {
            [DllImport(DLL_NAME, EntryPoint = "curl_multi_init", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr Init();


            [DllImport(DLL_NAME, EntryPoint = "curl_multi_add_handle", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode AddEasyHandle(IntPtr multiHandle, IntPtr easyHandle);

            [DllImport(DLL_NAME, EntryPoint = "curl_multi_remove_handle", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode RemoveEasyHandle(IntPtr multiHandle, IntPtr easyHandle);

            [DllImport(DLL_NAME, EntryPoint = "curl_multi_perform", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode Perform(IntPtr multiHandle, out int runningHandleCount);

            public static CurlMsg? GetMessage(IntPtr multiHandle, out int remainingMsgCount)
            {
                var msgHandle = curl_multi_info_read(multiHandle, out remainingMsgCount);
                if(msgHandle != IntPtr.Zero)
                {
                    return Marshal.PtrToStructure<CurlMsg>(msgHandle);
                }
                else
                {
                    return null;
                }
            }

            [DllImport(DLL_NAME, EntryPoint = "curl_multi_poll", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode Poll(IntPtr multiHandle, IntPtr extraFds, uint extraNfds, int timeoutMs, out int numFds);


            [DllImport(DLL_NAME, EntryPoint = "curl_multi_wakeup", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode Wakeup(IntPtr multiHandle);


            [DllImport(DLL_NAME, EntryPoint = "curl_unity_multi_setopt_long", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode SetOption(IntPtr multiHandle, CurlMOption option, long value);


            [DllImport(DLL_NAME, EntryPoint = "curl_unity_multi_setopt_ptr", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode SetOption(IntPtr multiHandle, CurlMOption option, IntPtr pointer);


            [DllImport(DLL_NAME, EntryPoint = "curl_multi_cleanup", CallingConvention = CallingConvention.Cdecl)]
            public static extern CurlMCode CleanUp(IntPtr multi_handle);


            [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
            static extern IntPtr curl_multi_info_read(IntPtr multiHandle, out int remainingMsgCount);
        }



        public static CurlCode Init(CurlInitOption flags) => curl_global_init((NativeLong)(long)flags);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        static extern CurlCode curl_global_init(NativeLong flags);

        [DllImport(DLL_NAME, EntryPoint = "curl_global_cleanup", CallingConvention = CallingConvention.Cdecl)]
        public static extern void CleanUp();



        [DllImport(DLL_NAME, EntryPoint = "curl_slist_append", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SListAppend(IntPtr list, [MarshalAs(UnmanagedType.LPUTF8Str)] string str);

        [DllImport(DLL_NAME, EntryPoint = "curl_slist_free_all", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SListFreeAll(IntPtr list);

        public static unsafe CurlVersionInfo GetVersionInfo(int age)
        {
            var ptr = curl_version_info(age);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("curl_version_info returned a null pointer.");
            }
            var info = Marshal.PtrToStructure<CurlVersionInfoRawData>(ptr);
            var protocols = Array.Empty<string>();
            var protocolsPtr = info.Protocols;
            var protocolCount = 0;

            while (protocolsPtr != null && *protocolsPtr != null)
            {
                protocolsPtr++;
                protocolCount++;
            }
            protocolsPtr = info.Protocols;
            if(protocolCount != 0)
            {
                protocols = new string[protocolCount];
            }
            for (var i = 0; i < protocolCount; i++)
            {
                var p = info.Protocols + i;
                var protocol = Marshal.PtrToStringUTF8((IntPtr)(*p));
                protocols[i] = protocol;
            }
            return new()
            {
                Age = info.Age,
                Version = Marshal.PtrToStringUTF8((IntPtr)info.Version),
                VersionNum = info.VersionNum,
                Host = Marshal.PtrToStringUTF8((IntPtr)info.Host),
                Features = info.Features,
                SslVersion = Marshal.PtrToStringUTF8((IntPtr)info.SslVersion),
                SslVersionNum = (long)info.SslVersionNum,
                LibzVersion = Marshal.PtrToStringUTF8((IntPtr)info.LibzVersion),
                Protocols = protocols
            };
        }

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr curl_version_info(int age);


        [StructLayout(LayoutKind.Sequential)]
        unsafe struct CurlVersionInfoRawData
        {
            public int Age;

            public byte* Version;

            public uint VersionNum;

            public byte* Host;

            public int Features;

            public byte* SslVersion;

            public NativeLong SslVersionNum;

            public byte* LibzVersion;

            public byte** Protocols;
        }
    }
}
