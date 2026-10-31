#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the cabinet LED options.
    /// </summary>
    public class LedOptions
    {
        /// <summary>
        /// Gets whether the cabinet LEDs are driven.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets the LED brightness multiplier, from 0 to 1.
        /// </summary>
        public float Brightness { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the minimum interval in milliseconds between LED updates.
        /// </summary>
        public int RefreshRateMs { get; set; } = 16;

        /// <summary>
        /// Gets or sets whether LED updates are limited to <see cref="RefreshRateMs"/>.
        /// </summary>
        public bool Throttler { get; set; } = true;

        /// <summary>
        /// Gets or sets the serial port of serial LED devices.
        /// </summary>
        public SerialPortOptions SerialPortOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the HID connection of HID LED devices.
        /// </summary>
        public HidOptions HidOptions { get; set; } = new();
    }
}
