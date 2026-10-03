using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LibVLCSharp
{
    class OnLoad
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string UnityPlugin = "__Internal";
#else
        const string UnityPlugin = "VLCUnityPlugin";
#endif
        [DllImport(UnityPlugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_set_color_space")]
        static extern void SetColorSpace(UnityColorSpace colorSpace);

        enum UnityColorSpace
        {
            Gamma = 0,
            Linear = 1,
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoadRuntimeMethod()
        {
            try
            {
                SetColorSpace(PlayerColorSpace);
            }
            catch (DllNotFoundException) { } // Bootstrap reports availability once.
            catch (EntryPointNotFoundException) { }
            catch (BadImageFormatException) { }
        }
        static UnityColorSpace PlayerColorSpace => QualitySettings.activeColorSpace == 0 ? UnityColorSpace.Gamma : UnityColorSpace.Linear;
    }
}
