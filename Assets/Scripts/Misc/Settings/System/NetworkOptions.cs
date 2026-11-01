#nullable enable
namespace MajdataPlay.Settings
{
    public class NetworkOptions
    {
        public OnlineOptions Online { get; init; } = new();

        public ProxyOptions Proxy { get; init; } = new();
    }
}
