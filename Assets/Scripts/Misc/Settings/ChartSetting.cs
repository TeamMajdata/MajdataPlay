using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    [Preserve]
    public class ChartSetting
    {
        [HideInSettingUI, Preserve]
        public string Hash { get; init; }
        [HideInSettingUI, Preserve]
        public OffsetUnitOption Unit { get; set; } = SettingDefaults.Chart.Unit;
        [Preserve]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = SettingDefaults.Chart.AudioOffset;
        [Preserve]
        [Step("0.05")]
        [Range("-2", "2")]
        public float TrackVolumeOffset { get; set; } = SettingDefaults.Chart.TrackVolumeOffset;
        public bool DisableVideoBG { get; set; } = SettingDefaults.Chart.DisableVideoBG;
        [Optional]
        [UseTranslation(TranslationTarget.OptionValue)]
        public bool? SlideSkipping { get; set; } = SettingDefaults.Chart.SlideSkipping;
    }
}
