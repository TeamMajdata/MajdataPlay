#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the options of a serial port connection.
    /// </summary>
    public class SerialPortOptions
    {
        /// <summary>
        /// Gets or sets the port, as a COM port number on Windows and a device path elsewhere, or null for the default port.
        /// </summary>
#if UNITY_STANDALONE_WIN
        public int? Port { get; set; } = null;
#else
        public string? Port { get; set; } = null;
#endif

        /// <summary>
        /// Gets or sets the baud rate, or null for the default rate of the device.
        /// </summary>
        public int? BaudRate { get; set; } = null;
    }
}
