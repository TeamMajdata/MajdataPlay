using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    public class DisplayOptions
    {
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = "";

        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = "default";

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption BGInfo { get; set; } = BGInfoOption.Combo;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SecondaryBGInfo { get; set; } = BGInfoOption.None;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SubScreenBGInfo { get; set; } = BGInfoOption.Achievement;

        public TopInfoDisplayOption TopInfo { get; set; } = TopInfoDisplayOption.None;

        [Step("0.01")]
        [Range("0", "2")]
        public float TapScale { get; set; } = 1f;

        [Step("0.01")]
        [Range("0", "2")]
        public float HoldScale { get; set; } = 1f;

        [Step("0.01")]
        [Range("0", "2")]
        public float TouchScale { get; set; } = 1f;

        [Step("0.01")]
        [Range("0", "2")]
        public float SlideScale { get; set; } = 1f;

        public SlidePathStyle SlidePathStyle { get; set; } = SlidePathStyle.Classic;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public TouchFeedbackLevel TouchFeedback { get; set; } = TouchFeedbackLevel.Outer_Only;

        public bool DisplaySensor { get; set; } = false;

        public SensorDebugPosition SensorPosition { get; set; } = SensorDebugPosition.MainScreen;

        public SensorDebugStyle SensorStyle { get; set; } = SensorDebugStyle.RedGreen;

        [Step("10")]
        [Range("10", "90")]
        public int SensorOpacity { get; set; } = 20;
    }
}
