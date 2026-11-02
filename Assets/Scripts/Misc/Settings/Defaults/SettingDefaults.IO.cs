#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class IO
        {
#if UNITY_STANDALONE
            public static DeviceManufacturerOption? Manufacturer
            {
                get => null;
            }
#endif
        }
    }
}
