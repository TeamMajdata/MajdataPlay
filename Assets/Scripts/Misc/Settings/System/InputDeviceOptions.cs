#nullable enable
namespace MajdataPlay.Settings
{
    public class InputDeviceOptions
    {
#if UNITY_STANDALONE
        public int Player { get; set; } = SettingDefaults.InputDevice.Player;

        public ButtonRingOptions ButtonRing { get; set; } = new();

        public TouchPanelOptions TouchPanel { get; set; } = new();
#else
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public MobileExternalButtonRingOption ExternalButtonRing { get; init; } = SettingDefaults.InputDevice.ExternalButtonRing;
#endif
    }
}
