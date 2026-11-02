#nullable enable
namespace MajdataPlay.Settings
{
    public class WasapiOptions
    {
        public bool Exclusive { get; set; } = SettingDefaults.Wasapi.Exclusive;

        public bool RawMode { get; set; } = SettingDefaults.Wasapi.RawMode;

        public bool AsyncMode { get; set; } = SettingDefaults.Wasapi.AsyncMode;

        public float BufferSize { get; set; } = SettingDefaults.Wasapi.BufferSize;

        public float Period { get; set; } = SettingDefaults.Wasapi.Period;
    }
}
