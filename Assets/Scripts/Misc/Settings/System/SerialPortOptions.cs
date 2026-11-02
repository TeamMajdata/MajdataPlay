#nullable enable
namespace MajdataPlay.Settings
{
    public class SerialPortOptions
    {
#if UNITY_STANDALONE_WIN
        public int? Port { get; set; } = SettingDefaults.SerialPort.Port;
#else
        public string? Port { get; set; } = SettingDefaults.SerialPort.Port;
#endif

        public int? BaudRate { get; set; } = SettingDefaults.SerialPort.BaudRate;
    }
}
