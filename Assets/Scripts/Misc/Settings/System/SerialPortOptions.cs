#nullable enable
namespace MajdataPlay.Settings
{
    public class SerialPortOptions
    {
#if UNITY_STANDALONE_WIN
        public int? Port { get; set; } = null;
#else
        public string? Port { get; set; } = null;
#endif

        public int? BaudRate { get; set; } = null;
    }
}
