using MajdataPlay.Diagnostics;
using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class DebugOptions
    {
        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = SettingDefaults.Debug.MenuOptionIterationSpeed;

        [Range("0", null)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = SettingDefaults.Debug.DisplayOffset;

        [HideInSettingUI]
        [DisableValues(Platforms = SettingPlatform.Mobile)]
        public bool HideCursorInGame { get; set; } = SettingDefaults.Debug.HideCursorInGame;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
        public bool NoteFolding { get; set; } = SettingDefaults.Debug.NoteFolding;

        public DJAutoPolicyOption DJAutoPolicy { get; set; } = SettingDefaults.Debug.DJAutoPolicy;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.Mobile)]
        public int MaxQueuedFrames { get; set; } = SettingDefaults.Debug.MaxQueuedFrames;

        [HideInSettingUI]
        public bool FrameTimingDiagnostics { get; set; } = SettingDefaults.Debug.FrameTimingDiagnostics;

        [HideInSettingUI]
        public bool LowLatencyVSync { get; set; } = SettingDefaults.Debug.LowLatencyVSync;

        [HideInSettingUI]
        public LowLatencyVSyncBackendOption LowLatencyVSyncBackend { get; set; } = SettingDefaults.Debug.LowLatencyVSyncBackend;

        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = SettingDefaults.Debug.TapPoolCapacity;

        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = SettingDefaults.Debug.HoldPoolCapacity;

        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = SettingDefaults.Debug.TouchPoolCapacity;

        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = SettingDefaults.Debug.TouchHoldPoolCapacity;

        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = SettingDefaults.Debug.EachLinePoolCapacity;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
        public LogLevel DebugLevel { get; set; } = SettingDefaults.Debug.DebugLevel;
    }
}
