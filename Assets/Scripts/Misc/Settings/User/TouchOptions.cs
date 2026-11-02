#nullable enable
namespace MajdataPlay.Settings
{
    public class TouchOptions
    {
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchSimulationRadius { get; set; } = SettingDefaults.Touch.TouchSimulationRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchAAreaExtraRadius { get; set; } = SettingDefaults.Touch.TouchAAreaExtraRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchBAreaExtraRadius { get; set; } = SettingDefaults.Touch.TouchBAreaExtraRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchCAreaExtraRadius { get; set; } = SettingDefaults.Touch.TouchCAreaExtraRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchDAreaExtraRadius { get; set; } = SettingDefaults.Touch.TouchDAreaExtraRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchEAreaExtraRadius { get; set; } = SettingDefaults.Touch.TouchEAreaExtraRadius;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchRadiusAdjust { get; set; } = SettingDefaults.Touch.TouchRadiusAdjust;
    }
}
