#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Display
        {
            public const string Language = "";

            public const string Skin = "default";

            public const BGInfoOption BGInfo = BGInfoOption.Combo;

            public const BGInfoOption SecondaryBGInfo = BGInfoOption.None;

            public const BGInfoOption SubScreenBGInfo = BGInfoOption.Achievement;

            public const TopInfoDisplayOption TopInfo = TopInfoDisplayOption.None;

            public const float TapScale = 1f;

            public const float HoldScale = 1f;

            public const float TouchScale = 1f;

            public const float SlideScale = 1f;

            public const SlidePathStyle SlidePathStyle = global::MajdataPlay.Settings.SlidePathStyle.Classic;

            public const TouchFeedbackLevel TouchFeedback = TouchFeedbackLevel.Outer_Only;

            public const bool DisplaySensor = false;

            public const SensorDebugPosition SensorPosition = SensorDebugPosition.MainScreen;

            public const SensorDebugStyle SensorStyle = SensorDebugStyle.RedGreen;

            public const int SensorOpacity = 20;
        }
    }
}
