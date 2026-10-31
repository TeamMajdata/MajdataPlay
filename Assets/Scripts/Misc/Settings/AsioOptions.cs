#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the options of the ASIO backend.
    /// </summary>
    public class AsioOptions
    {
        /// <summary>
        /// Gets or sets the index of the ASIO device.
        /// </summary>
        public int DeviceIndex { get; set; } = 0;

        /// <summary>
        /// Gets or sets the sample rate in hertz.
        /// </summary>
        public int SampleRate { get; set; } = 44100;
    }
}
