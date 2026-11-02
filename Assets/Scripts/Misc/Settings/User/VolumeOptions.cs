using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class VolumeOptions
    {
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Global { get; set; } = SettingDefaults.Volume.Global;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float BGM { get; set; } = SettingDefaults.Volume.BGM;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Track { get; set; } = SettingDefaults.Volume.Track;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Answer { get; set; } = SettingDefaults.Volume.Answer;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Tap { get; set; } = SettingDefaults.Volume.Tap;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Ex { get; set; } = SettingDefaults.Volume.Ex;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Break { get; set; } = SettingDefaults.Volume.Break;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Slide { get; set; } = SettingDefaults.Volume.Slide;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Touch { get; set; } = SettingDefaults.Volume.Touch;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Hanabi { get; set; } = SettingDefaults.Volume.Hanabi;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Voice { get; set; } = SettingDefaults.Volume.Voice;
    }
}
