#nullable enable
namespace MajdataPlay.Settings
{
    public class SoundOptions
    {
#if UNITY_IOS || UNITY_ANDROID
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.BassSimple;
#else
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.Wasapi;
#endif

        public bool ForceMono { get; set; } = false;
#if UNITY_STANDALONE
#if UNITY_STANDALONE_WIN

        public WasapiOptions Wasapi { get; set; } = new();

        public AsioOptions Asio { get; set; } = new();
#endif

        public ChannelOptions Channel { get; set; } = new();
#endif

        public BassAudioOptions Bass { get; set; } = new();

        public SoundBackendOption Backend { get; set; } = s_defaultSoundBackend;
    }
}
