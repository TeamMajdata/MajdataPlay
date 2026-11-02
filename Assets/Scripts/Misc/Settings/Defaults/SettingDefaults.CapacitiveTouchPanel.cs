#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class CapacitiveTouchPanel
        {
            public const int TouchRadius = 30;

            public static CapacitiveTouchPanelRadiusOffsetConfig RadiusOffset
            {
                get
                {
                    return new()
                    {
                        A = 0,
                        B = 20,
                        C = 0,
                        D = 0,
                        E = 25
                    };
                }
            }
        }
    }
}
