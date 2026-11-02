#nullable enable
namespace MajdataPlay.Settings
{
    public class CapacitiveTouchPanelOptions
    {
        public int TouchRadius { get; set; } = SettingDefaults.CapacitiveTouchPanel.TouchRadius;

        public CapacitiveTouchPanelRadiusOffsetConfig RadiusOffset { get; set; } = SettingDefaults.CapacitiveTouchPanel.RadiusOffset;
    }
}
