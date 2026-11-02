using MajdataPlay.Diagnostics;
using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class DebugOptions
    {
        [Range("0", null)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = SettingDefaults.Debug.DisplayOffset;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.Mobile)]
        public int MaxQueuedFrames { get; set; } = SettingDefaults.Debug.MaxQueuedFrames;

        [HideInSettingUI]
        public bool FrameTimingDiagnostics { get; set; } = SettingDefaults.Debug.FrameTimingDiagnostics;

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
