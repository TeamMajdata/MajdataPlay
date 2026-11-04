#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Identifies the Unity build target shared by setting UI rules and JSON exclusions.
    /// </summary>
    internal static class SettingPlatformInfo
    {
        /// <summary>
        /// Gets the build target selected by the Unity platform defines, or <see cref="SettingPlatform.None"/>
        /// for an unsupported target. In the Editor this is the selected build target, rather than the host platform.
        /// </summary>
        internal static SettingPlatform Current
        {
            get
            {
#if UNITY_ANDROID
                return SettingPlatform.Android;
#elif UNITY_IOS
                return SettingPlatform.IOS;
#elif UNITY_STANDALONE_WIN
                return SettingPlatform.Windows;
#elif UNITY_STANDALONE_LINUX
                return SettingPlatform.Linux;
#elif UNITY_STANDALONE_OSX
                return SettingPlatform.MacOS;
#else
                return SettingPlatform.None;
#endif
            }
        }
    }
}
