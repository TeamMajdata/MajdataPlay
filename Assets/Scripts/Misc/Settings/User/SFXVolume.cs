using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the volumes of the music and sound effects, from 0 to 2.
    /// </summary>
    public class SFXVolume
    {
        /// <summary>
        /// Gets or sets the master volume.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Global { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the background music in song select, result and other scenes outside gameplay.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float BGM { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the music track during gameplay.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Track { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the chart guide sounds and the lead-in metronome.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Answer { get; set; } = 0.8f;

        /// <summary>
        /// Gets or sets the volume of the tap and hold judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Tap { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the sound played by a judged EX tap or hold.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Ex { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the break judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Break { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the slide sound effects.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Slide { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the touch and touch hold judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Touch { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the firework sound of touch hanabi effects.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Hanabi { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the mascot voice lines.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Voice { get; set; } = 1f;
    }
}
