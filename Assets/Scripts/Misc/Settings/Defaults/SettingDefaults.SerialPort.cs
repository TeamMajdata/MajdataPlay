#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class SerialPort
        {
#if UNITY_STANDALONE_WIN
            public static int? Port
            {
                get => null;
            }
#else
            public const string? Port = null;
#endif

            public static int? BaudRate
            {
                get => null;
            }
        }
    }
}
