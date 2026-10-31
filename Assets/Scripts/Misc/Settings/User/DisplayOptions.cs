using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the display options: language, skin, note sizes, sensor visualization and gameplay information.
    /// </summary>
    public class DisplayOptions
    {
        /// <summary>
        /// Gets or sets the game language; empty selects the language of the system.
        /// </summary>
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = "";

        /// <summary>
        /// Gets or sets the name of the note skin used during gameplay.
        /// </summary>
        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = "default";

        /// <summary>Gets or sets whether touch sensor visualization is rendered.</summary>
        public bool DisplaySensor { get; set; } = false;

        /// <summary>Stores the selected sensor visualization display region.</summary>
        private SensorDebugPosition _sensorPosition = SensorDebugPosition.MainScreen;

        /// <summary>Gets or sets the main or secondary display region used for sensor visualization.</summary>
        public SensorDebugPosition SensorPosition
        {
            get => _sensorPosition;
            set => _sensorPosition = value == SensorDebugPosition.SubScreen ? value : SensorDebugPosition.MainScreen;
        }

        /// <summary>Stores the selected visualization without changing older configuration defaults.</summary>
        private SensorDebugStyle _sensorStyle = SensorDebugStyle.RedGreen;

        /// <summary>Gets or sets the brightness, red/green border, or classic full-area visualization.</summary>
        public SensorDebugStyle SensorStyle
        {
            get => _sensorStyle;
            set => _sensorStyle = value switch
            {
                SensorDebugStyle.Brightness => value,
                SensorDebugStyle.Classic => value,
                _ => SensorDebugStyle.RedGreen
            };
        }

        /// <summary>Stores the sensor visualization opacity percentage.</summary>
        private int _sensorOpacity = 20;
        /// <summary>Gets or sets visualization strength from ten to ninety percent. Red/green and classic use this alpha; brightness uses half at rest and twice on contact, capped at full opacity.</summary>
        [Step("10")]
        [Range("10", "90")]
        public int SensorOpacity
        {
            get => _sensorOpacity;
            // 菜单、外部 JSON 和直接赋值统一为 10%～90%，步长 10%，旧配置缺项默认 20%。
            set => _sensorOpacity = value <= 10 ? 10 : value >= 90 ? 90 : ((value + 5) / 10) * 10;
        }

        /// <summary>
        /// Gets or sets the size multiplier of tap notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float TapScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of hold notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float HoldScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of touch notes during gameplay.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float TouchScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the size multiplier of slides during gameplay, which does not affect Wi-Fi slides.
        /// </summary>
        [Step("0.01")]
        [Range("0", "2")]
        public float SlideScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets how the arrows of connected slides such as <c>1-4q7</c> are placed along their segments.
        /// </summary>
        public SlidePathStyle SlidePathStyle { get; set; } = SlidePathStyle.Classic;

        /// <summary>
        /// Gets or sets where the reaction effect of a touch on the screen is displayed.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public TouchFeedbackLevel TouchFeedback { get; set; } = TouchFeedbackLevel.Outer_Only;

        /// <summary>
        /// Gets or sets the information displayed at the center of the screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption BGInfo { get; set; } = BGInfoOption.Combo;

        /// <summary>
        /// Gets or sets the information displayed below the center of the screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SecondaryBGInfo { get; set; } = BGInfoOption.None;

        /// <summary>
        /// Gets or sets the information displayed on the sub-screen during gameplay.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SubScreenBGInfo { get; set; } = BGInfoOption.Achievement;

        /// <summary>
        /// Gets or sets the information displayed on the bar between buttons 1 and 8.
        /// </summary>
        public TopInfoDisplayOption TopInfo { get; set; } = TopInfoDisplayOption.None;
    }
}
