#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the button ring options.
    /// </summary>
    public class ButtonRingOptions
    {
        /// <summary>
        /// Gets whether the button ring is opened.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets the button ring device type, or null to detect it.
        /// </summary>
        public ButtonRingDeviceOption? Type { get; set; } = null;

        /// <summary>
        /// Gets or sets whether button input is debounced by <see cref="DebounceThresholdMs"/>.
        /// </summary>
        public bool Debounce { get; set; } = false;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds; 0 polls without waiting.
        /// </summary>
        public int PollingRateMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the debounce threshold in milliseconds.
        /// </summary>
        public int DebounceThresholdMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the HID connection of HID button rings.
        /// </summary>
        public HidOptions HidOptions { get; set; } = new();
    }
}
