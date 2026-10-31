#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the audio volume and output backend options.
    /// </summary>
    public class SoundOptions
    {
        /// <summary>
        /// The default audio backend of the current platform.
        /// </summary>
#if UNITY_IOS || UNITY_ANDROID
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.BassSimple;
#else
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.Wasapi;
#endif

        /// <summary>
        /// Gets or sets whether every output channel plays a mono downmix.
        /// </summary>
        public bool ForceMono { get; set; } = false;

        /// <summary>
        /// Gets or sets the volumes of the music and sound effects.
        /// </summary>
        public SFXVolume Volume { get; set; } = new();
#if UNITY_STANDALONE
#if UNITY_STANDALONE_WIN

        /// <summary>
        /// Gets or sets the options of the WASAPI backend.
        /// </summary>
        public WasapiOptions Wasapi { get; set; } = new();

        /// <summary>
        /// Gets or sets the options of the ASIO backend.
        /// </summary>
        public AsioOptions Asio { get; set; } = new();
#endif

        /// <summary>
        /// Gets or sets the volumes of the speaker channel pairs.
        /// </summary>
        public ChannelOptions Channel { get; set; } = new();
#endif

        /// <summary>
        /// Gets or sets the buffer options of the BASS output.
        /// </summary>
        public BassAudioOptions Bass { get; set; } = new();

        /// <summary>
        /// Gets or sets the audio output backend.
        /// </summary>
        public SoundBackendOption Backend { get; set; } = s_defaultSoundBackend;
    }
}
