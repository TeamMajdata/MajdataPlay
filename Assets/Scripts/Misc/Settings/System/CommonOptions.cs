#nullable enable
namespace MajdataPlay.Settings
{
    public class CommonOptions
    {
        public RecordModeOption RecordMode { get; set; } = SettingDefaults.Common.RecordMode;

        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = SettingDefaults.Common.MenuOptionIterationSpeed;

        [HideInSettingUI]
        [DisableValues(Platforms = SettingPlatform.Mobile)]
        public bool HideCursorInGame { get; set; } = SettingDefaults.Common.HideCursorInGame;

        [HideInSettingUI]
        [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
        public bool NoteFolding { get; set; } = SettingDefaults.Common.NoteFolding;

        public DJAutoPolicyOption DJAutoPolicy { get; set; } = SettingDefaults.Common.DJAutoPolicy;
    }
}
