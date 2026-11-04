#nullable enable
namespace MajdataPlay.Settings
{
    public class SoundOptions
    {
        public bool ForceMono { get; set; } = SettingDefaults.Sound.ForceMono;
#if UNITY_STANDALONE
#if UNITY_STANDALONE_WIN

        public WasapiOptions Wasapi { get; set; } = new();

        public AsioOptions Asio { get; set; } = new();
#endif

        public ChannelOptions Channel { get; set; } = new();
#endif

        public BassAudioOptions Bass { get; set; } = new();

        public SoundBackendOption Backend { get; set; } = SettingDefaults.Sound.Backend;
    }
}
