#nullable enable
namespace MajdataPlay.Settings
{
    public class ProxyOptions
    {
        public bool Enable { get; init; } = SettingDefaults.Proxy.Enable;

        public string Url { get; init; } = SettingDefaults.Proxy.Url;
    }
}
