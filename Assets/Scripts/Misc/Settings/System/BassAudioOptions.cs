#nullable enable
namespace MajdataPlay.Settings
{
#if UNITY_STANDALONE
#endif

    /// <summary>
    /// Contains the buffer options of the BASS output.
    /// </summary>
    public class BassAudioOptions
    {
        /// <summary>
        /// The default playback buffer length in milliseconds.
        /// </summary>
        private const int DefaultBufferLengthMs = 1000;

        /// <summary>
        /// The default update period in milliseconds.
        /// </summary>
        private const int DefaultUpdatePeriodMs = 200;
#if UNITY_ANDROID || UNITY_IOS

        /// <summary>
        /// The default device buffer length in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceBufferLengthMs = 32;

        /// <summary>
        /// The default device update period in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceUpdatePeriodMs = 8;
#else

        /// <summary>
        /// The default device buffer length in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceBufferLengthMs = 64;

        /// <summary>
        /// The default device update period in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceUpdatePeriodMs = 16;
#endif
#if UNITY_ANDROID

        /// <summary>
        /// Gets or sets whether BASS outputs through AAudio.
        /// </summary>
        public bool EnableAAudio { get; set; } = true;
#endif

        /// <summary>
        /// Gets or sets the playback buffer length in milliseconds.
        /// </summary>
        public int BufferLengthMs { get; set; } = DefaultBufferLengthMs;

        /// <summary>
        /// Gets or sets the update period in milliseconds.
        /// </summary>
        public int UpdatePeriodMs { get; set; } = DefaultUpdatePeriodMs;

        /// <summary>
        /// Gets or sets the device buffer length in milliseconds.
        /// </summary>
        public int DeviceBufferLengthMs { get; set; } = DefaultDeviceBufferLengthMs;

        /// <summary>
        /// Gets or sets the device update period in milliseconds.
        /// </summary>
        public int DeviceUpdatePeriodMs { get; set; } = DefaultDeviceUpdatePeriodMs;
    }
}
