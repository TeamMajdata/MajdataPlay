using MajdataPlay.Diagnostics;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
#nullable enable
namespace MajdataPlay.Settings
{
    public class DebugOptions
    {
        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = 45;

        [Range("0", null)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = 0f;

#if UNITY_STANDALONE
        [HideInSettingUI]
        public bool HideCursorInGame { get; set; } = true;
#endif
        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool NoteFolding { get; set; } = true;

        public DJAutoPolicyOption DJAutoPolicy { get; set; } = DJAutoPolicyOption.Strict;
        [HideInSettingUI]
        public int MaxQueuedFrames { get; set; } = 2;
        [HideInSettingUI]
        public bool FrameTimingDiagnostics { get; set; } = false;
        [HideInSettingUI]
        public bool LowLatencyVSync { get; set; } = true;
        [HideInSettingUI]
        public LowLatencyVSyncBackendOption LowLatencyVSyncBackend { get; set; } = LowLatencyVSyncBackendOption.Auto;
#if UNITY_IOS || UNITY_ANDROID
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 48;
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 48;
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 24;
#else
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 96;
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 96;
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 48;
#endif

        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public LogLevel DebugLevel { get; set; } = LogLevel.Info;
    }
}
