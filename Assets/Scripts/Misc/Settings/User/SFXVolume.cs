using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    public class SFXVolume
    {
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Global { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float BGM { get; set; } = 1f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Track { get; set; } = 1f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Answer { get; set; } = 0.8f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Tap { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Ex { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Break { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Slide { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Touch { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Hanabi { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Voice { get; set; } = 1f;
    }
}
