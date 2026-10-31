#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the input device options.
    /// </summary>
    public class InputDeviceOptions
    {
#if UNITY_STANDALONE
        /// <summary>
        /// Gets or sets the cabinet side, 1 or 2, whose devices are opened.
        /// </summary>
        public int Player { get; set; } = 1;

        /// <summary>
        /// Gets or sets the button ring options.
        /// </summary>
        public ButtonRingOptions ButtonRing { get; set; } = new();

        /// <summary>
        /// Gets or sets the touch panel options.
        /// </summary>
        public TouchPanelOptions TouchPanel { get; set; } = new();
#else
        /// <summary>
        /// Gets the external device used as the button ring.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public MobileExternalButtonRingOption ExternalButtonRing { get; init; } = MobileExternalButtonRingOption.None;
#endif
    }
}
