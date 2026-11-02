#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class InputDevice
        {
#if !UNITY_STANDALONE
            public const MobileExternalButtonRingOption ExternalButtonRing = MobileExternalButtonRingOption.None;
#endif
        }
    }
}
