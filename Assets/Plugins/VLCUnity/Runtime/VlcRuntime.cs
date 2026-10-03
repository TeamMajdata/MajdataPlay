using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace LibVLCSharp
{
    /// <summary>Initializes the platform's supplied VLC libraries without claiming missing binaries are available.</summary>
    public static class VlcRuntime
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string PortableBridge = "__Internal";
#else
        const string PortableBridge = "VLCUnityPlugin";
#endif

        /// <summary>
        /// Returns a usable library or a diagnostic. Non-Windows requires the
        /// portable bridge and matching LibVLC 4 native dependencies for that ABI.
        /// Must be called on Unity's main thread before constructing video output.
        /// </summary>
        public static bool TryCreateLibrary(out LibVLC library, out string diagnostic, bool enableDebugLogs = false)
        {
            library = null;
            diagnostic = null;
            try
            {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                Core.Initialize(Path.Combine(Application.dataPath, "Plugins"));
#else
#if UNITY_ANDROID && !UNITY_EDITOR
                // Load through Java so Android calls JNI_OnLoad with the real
                // VM/classloader before LibVLC creates a MediaCodec context.
                // A native dlopen alone does not perform JNI initialization.
                using (var javaSystem = new AndroidJavaClass("java.lang.System"))
                {
                    javaSystem.CallStatic("loadLibrary", "vlc");
                    javaSystem.CallStatic("loadLibrary", "VLCUnityPlugin");
                }
#endif
                if (NativeBridgeAbi() != 1)
                    throw new NotSupportedException("The portable VLCUnity bridge ABI is not version 1.");
                string plugins = FindPluginDirectory();
                if (plugins != null)
                    NativeSetPluginPath(plugins);
                if (NativeValidateLibVlc() != 1)
                    throw new NotSupportedException(ReadNativeError());
                // The platform bindings bypass the Windows desktop loader. Unity
                // resolves shared libraries, or __Internal symbols in an iOS build.
                Core.Initialize();
#endif
                library = new LibVLC(enableDebugLogs: enableDebugLogs, "--no-audio");
                return true;
            }
            catch (Exception error)
            {
                library?.Dispose();
                library = null;
                diagnostic = "VLC is unavailable on " + Application.platform + ": " + error.Message +
                    " Supply the matching platform/architecture native VLCUnity bridge, pinned LibVLC 4 libraries and decoder modules.";
                return false;
            }
        }

#if !(UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR))
        static string FindPluginDirectory()
        {
            var candidates = new List<string>();
#if UNITY_EDITOR_LINUX
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/VLCUnity/Runtime/Plugins/Linux/x86_64/plugins"));
#elif UNITY_EDITOR_OSX
            string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x86_64";
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/VLCUnity/Runtime/Plugins/MacOS/universal/plugins"));
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/VLCUnity/Runtime/Plugins/MacOS", architecture, "plugins"));
#elif UNITY_STANDALONE_LINUX
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/x86_64/plugins"));
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/plugins"));
#elif UNITY_STANDALONE_OSX
            candidates.Add(Path.Combine(Application.dataPath, "Plugins/plugins"));
            candidates.Add(Path.Combine(Application.dataPath, "PlugIns/plugins"));
#endif
            // Android/iOS packages use linked/static modules, not desktop VLC's
            // filesystem search paths. Do not point them into an APK or IPA URL.
            foreach (string candidate in candidates)
                if (Directory.Exists(candidate))
                    return candidate;
            return null; // Retain the native runtime's configured/default location.
        }

        static string ReadNativeError()
        {
            IntPtr pointer = NativeLastError();
            if (pointer == IntPtr.Zero)
                return "The portable bridge could not load the pinned LibVLC runtime.";
            int length = 0;
            while (length < 4096 && Marshal.ReadByte(pointer, length) != 0)
                ++length;
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport(PortableBridge, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_bridge_abi")]
        static extern int NativeBridgeAbi();
        [DllImport(PortableBridge, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_validate_libvlc")]
        static extern int NativeValidateLibVlc();
        [DllImport(PortableBridge, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_last_error")]
        static extern IntPtr NativeLastError();
        [DllImport(PortableBridge, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetPluginPath")]
        static extern void NativeSetPluginPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif
    }
}
