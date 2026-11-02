#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Chart
        {
            public const OffsetUnitOption Unit = OffsetUnitOption.Second;

            public const float AudioOffset = 0f;

            public const float TrackVolumeOffset = 0f;

            public const bool DisableVideoBG = false;

            public static bool? SlideSkipping
            {
                get => null;
            }
        }
    }
}
