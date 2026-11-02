#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class BassAudio
        {
#if UNITY_ANDROID
            public const bool EnableAAudio = true;
#endif

            public const int BufferLengthMs = 1000;

            public const int UpdatePeriodMs = 200;

#if UNITY_ANDROID || UNITY_IOS
            public const int DeviceBufferLengthMs = 32;

            public const int DeviceUpdatePeriodMs = 8;
#else
            public const int DeviceBufferLengthMs = 64;

            public const int DeviceUpdatePeriodMs = 16;
#endif
        }
    }
}
