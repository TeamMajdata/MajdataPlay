#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the options of the WASAPI backend.
    /// </summary>
    public class WasapiOptions
    {
        /// <summary>
        /// Gets or sets whether the device is opened in exclusive mode.
        /// </summary>
        public bool Exclusive { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the device is opened in raw mode, bypassing system audio effects.
        /// </summary>
        public bool RawMode { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the device is fed asynchronously.
        /// </summary>
        public bool AsyncMode { get; set; } = true;

        /// <summary>
        /// Gets or sets the buffer length in seconds in exclusive mode.
        /// </summary>
        public float BufferSize { get; set; } = 0.02f;

        /// <summary>
        /// Gets or sets the update period in seconds in exclusive mode.
        /// </summary>
        public float Period { get; set; } = 0.005f;
    }
}
