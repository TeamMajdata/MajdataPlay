#nullable enable
namespace MajdataPlay.Settings
{
    public class InputDeviceOptions
    {
#if UNITY_STANDALONE
        public int Player { get; set; } = 1;

        public ButtonRingOptions ButtonRing { get; set; } = new();

        public TouchPanelOptions TouchPanel { get; set; } = new();
#else
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public MobileExternalButtonRingOption ExternalButtonRing { get; init; } = MobileExternalButtonRingOption.None;
#endif
    }
}
