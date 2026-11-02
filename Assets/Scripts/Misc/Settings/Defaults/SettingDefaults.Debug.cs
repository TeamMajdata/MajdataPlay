using MajdataPlay.Diagnostics;
#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Debug
        {
            public const int MenuOptionIterationSpeed = 45;

            public const float DisplayOffset = 0f;

#if UNITY_STANDALONE
            public const bool HideCursorInGame = true;
#endif

            public const bool NoteFolding = true;

            public const DJAutoPolicyOption DJAutoPolicy = DJAutoPolicyOption.Strict;

            public const int MaxQueuedFrames = 2;

            public const bool FrameTimingDiagnostics = false;

            public const bool LowLatencyVSync = true;

            public const LowLatencyVSyncBackendOption LowLatencyVSyncBackend = LowLatencyVSyncBackendOption.Auto;

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
