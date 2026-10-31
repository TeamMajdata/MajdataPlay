#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the touch panel options.
    /// </summary>
    public class TouchPanelOptions
    {
        /// <summary>
        /// Gets whether the touch panel is opened.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets whether sensor input is debounced by <see cref="DebounceThresholdMs"/>.
        /// </summary>
        public bool Debounce { get; set; } = false;

        /// <summary>
        /// Gets or sets the sensitivity of each sensor group, sent to touch panels that support it.
        /// </summary>
        public TouchPanelSensitivityConfig Sensitivities { get; set; } = default;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds; 0 polls without waiting.
        /// </summary>
        public int PollingRateMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the debounce threshold in milliseconds.
        /// </summary>
        public int DebounceThresholdMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the serial port of serial touch panels.
        /// </summary>
        public SerialPortOptions SerialPortOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the USB connection of USB touch panels.
        /// </summary>
        public UsbOptions UsbOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the options of capacitive touch panels.
        /// </summary>
        public CapacitiveTouchPanelOptions CapacitivePanelOptions { get; set; } = new();
    }
}
