using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    public class DisplayOptions
    {
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = SettingDefaults.Display.Language;

        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = SettingDefaults.Display.Skin;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption BGInfo { get; set; } = SettingDefaults.Display.BGInfo;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SecondaryBGInfo { get; set; } = SettingDefaults.Display.SecondaryBGInfo;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public BGInfoOption SubScreenBGInfo { get; set; } = SettingDefaults.Display.SubScreenBGInfo;

        public TopInfoDisplayOption TopInfo { get; set; } = SettingDefaults.Display.TopInfo;

        [Step("0.01")]
        [Range("0", "2")]
        public float TapScale { get; set; } = SettingDefaults.Display.TapScale;

        [Step("0.01")]
        [Range("0", "2")]
        public float HoldScale { get; set; } = SettingDefaults.Display.HoldScale;

        [Step("0.01")]
        [Range("0", "2")]
        public float TouchScale { get; set; } = SettingDefaults.Display.TouchScale;

        [Step("0.01")]
        [Range("0", "2")]
        public float SlideScale { get; set; } = SettingDefaults.Display.SlideScale;

        public SlidePathStyle SlidePathStyle { get; set; } = SettingDefaults.Display.SlidePathStyle;

        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public TouchFeedbackLevel TouchFeedback { get; set; } = SettingDefaults.Display.TouchFeedback;

        public bool DisplaySensor { get; set; } = SettingDefaults.Display.DisplaySensor;

        public SensorDebugPosition SensorPosition { get; set; } = SettingDefaults.Display.SensorPosition;

        public SensorDebugStyle SensorStyle { get; set; } = SettingDefaults.Display.SensorStyle;

        [Step("10")]
        [Range("10", "90")]
        public int SensorOpacity { get; set; } = SettingDefaults.Display.SensorOpacity;
    }
}
