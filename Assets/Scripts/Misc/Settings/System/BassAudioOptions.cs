#nullable enable
namespace MajdataPlay.Settings
{
#if UNITY_STANDALONE
#endif

    public class BassAudioOptions
    {
#if UNITY_ANDROID
        public bool EnableAAudio { get; set; } = SettingDefaults.BassAudio.EnableAAudio;
#endif

        public int BufferLengthMs { get; set; } = SettingDefaults.BassAudio.BufferLengthMs;

        public int UpdatePeriodMs { get; set; } = SettingDefaults.BassAudio.UpdatePeriodMs;

        public int DeviceBufferLengthMs { get; set; } = SettingDefaults.BassAudio.DeviceBufferLengthMs;

        public int DeviceUpdatePeriodMs { get; set; } = SettingDefaults.BassAudio.DeviceUpdatePeriodMs;
    }
}
