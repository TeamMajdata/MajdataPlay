#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Hid
        {
            public const string? DeviceName = null;

            public static int? ProductId
            {
                get => null;
            }

            public static int? VendorId
            {
                get => null;
            }

            public const bool Exclusice = false;

            public const HidOpenPriority OpenPriority = HidOpenPriority.VeryHigh;
        }
    }
}
