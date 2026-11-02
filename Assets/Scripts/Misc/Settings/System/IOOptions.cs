#nullable enable
namespace MajdataPlay.Settings
{
    public class IOOptions
    {
#if UNITY_STANDALONE
        public DeviceManufacturerOption? Manufacturer { get; set; } = SettingDefaults.IO.Manufacturer;

        public int Player { get; set; } = SettingDefaults.IO.Player;
#endif

        public InputDeviceOptions InputDevice { get; set; } = new();
#if UNITY_STANDALONE

        public OutputDeviceOptions OutputDevice { get; set; } = new();
#endif
    }
}
