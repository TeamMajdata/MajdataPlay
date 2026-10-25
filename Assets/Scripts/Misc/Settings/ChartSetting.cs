using MajdataPlay.Settings.OptionEnumerators;
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
        public OffsetUnitOption Unit { get; set; } = OffsetUnitOption.Second;
        [Preserve]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;
        [Preserve]
        [Step("0.05")]
        [Range("-2", "2")]
        public float TrackVolumeOffset { get; set; } = 0f;
        public bool DisableVideoBG { get; set; } = false;
        [Optional]
        public bool? SlideSkipping { get; set; } = null;
    }
}
