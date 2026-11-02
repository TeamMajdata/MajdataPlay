#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Sound
        {
            public const bool ForceMono = false;

#if UNITY_ANDROID || UNITY_IOS
            public const SoundBackendOption Backend = SoundBackendOption.BassSimple;
#else
            public const SoundBackendOption Backend = SoundBackendOption.Wasapi;
#endif
        }
    }
}
