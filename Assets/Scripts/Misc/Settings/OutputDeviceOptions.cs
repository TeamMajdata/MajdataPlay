#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the output device options.
    /// </summary>
    public class OutputDeviceOptions
    {
        /// <summary>
        /// Gets or sets the cabinet LED options.
        /// </summary>
        public LedOptions Led { get; set; } = new();
    }
}
