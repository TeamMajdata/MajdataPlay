using MajdataPlay.Diagnostics;
#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Debug
        {
            public const float DisplayOffset = 0f;

            public const int MaxQueuedFrames = 2;

            public const bool FrameTimingDiagnostics = false;

#if UNITY_ANDROID || UNITY_IOS
            public const int TapPoolCapacity = 48;

            public const int HoldPoolCapacity = 48;
#else
            public const int TapPoolCapacity = 96;

            public const int HoldPoolCapacity = 96;
#endif

            public const int TouchPoolCapacity = 64;

            public const int TouchHoldPoolCapacity = 64;

#if UNITY_ANDROID || UNITY_IOS
            public const int EachLinePoolCapacity = 24;
#else
            public const int EachLinePoolCapacity = 48;
#endif

            public const LogLevel DebugLevel = LogLevel.Info;
        }
    }
}
