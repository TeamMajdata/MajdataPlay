#nullable enable
namespace MajdataPlay.Settings
{
#if UNITY_STANDALONE
#endif

    public class BassAudioOptions
    {
        private const int DefaultBufferLengthMs = 1000;

        private const int DefaultUpdatePeriodMs = 200;
#if UNITY_ANDROID || UNITY_IOS

        private const int DefaultDeviceBufferLengthMs = 32;

        private const int DefaultDeviceUpdatePeriodMs = 8;
#else

        private const int DefaultDeviceBufferLengthMs = 64;

        private const int DefaultDeviceUpdatePeriodMs = 16;
#endif
#if UNITY_ANDROID

        public bool EnableAAudio { get; set; } = true;
#endif

        public int BufferLengthMs { get; set; } = DefaultBufferLengthMs;

        public int UpdatePeriodMs { get; set; } = DefaultUpdatePeriodMs;

        public int DeviceBufferLengthMs { get; set; } = DefaultDeviceBufferLengthMs;

        public int DeviceUpdatePeriodMs { get; set; } = DefaultDeviceUpdatePeriodMs;
    }
}
