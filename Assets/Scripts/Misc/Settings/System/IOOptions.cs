#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the input and output device options.
    /// </summary>
    public class IOOptions
    {
#if UNITY_STANDALONE
        /// <summary>
        /// Gets or sets the manufacturer of the cabinet devices, or null to detect it.
        /// </summary>
        public DeviceManufacturerOption? Manufacturer { get; set; } = null;
#endif

        /// <summary>
        /// Gets or sets the input device options.
        /// </summary>
        public InputDeviceOptions InputDevice { get; set; } = new();
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets the output device options.
        /// </summary>
        public OutputDeviceOptions OutputDevice { get; set; } = new();
#endif
    }
}
