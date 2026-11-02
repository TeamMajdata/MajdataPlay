using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    public class GameOptions
    {
        [Step("0.25")]
        public float TapSpeed { get; set; } = SettingDefaults.Game.TapSpeed;

        [Step("0.25")]
        public float TouchSpeed { get; set; } = SettingDefaults.Game.TouchSpeed;

        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float SlideFadeInOffset { get; set; } = SettingDefaults.Game.SlideFadeInOffset;

        [Step("0.05")]
        [Range("0", "1")]
        public float BackgroundDim { get; set; } = SettingDefaults.Game.BackgroundDim;

        public bool SkipVideoDownload { get; set; } = SettingDefaults.Game.SkipVideoDownload;

        public bool StarRotation { get; set; } = SettingDefaults.Game.StarRotation;

        public bool TrackSkip { get; set; } = SettingDefaults.Game.TrackSkip;

        public EnforceGameFailureCondition EnforceGameFailure { get; set; } = SettingDefaults.Game.EnforceGameFailure;

        public bool FastRetry { get; set; } = SettingDefaults.Game.FastRetry;

        public bool FastPractice { get; set; } = SettingDefaults.Game.FastPractice;

        public GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior { get; set; } = SettingDefaults.Game.GameplaySubScreenClickBehavior;

        public MirrorOption Mirror { get; set; } = SettingDefaults.Game.Mirror;

        [Step("1")]
        [Range("-7", "7")]
        public int Rotation { get; set; } = SettingDefaults.Game.Rotation;

        public bool SlideSkipping { get; set; } = SettingDefaults.Game.SlideSkipping;

        public RandomModeOption Random { get; set; } = SettingDefaults.Game.Random;

        public bool ButtonRingForTouch { get; set; } = SettingDefaults.Game.ButtonRingForTouch;

        [Step("1")]
        [Range("1", "5")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int LeadInTime { get; set; } = SettingDefaults.Game.LeadInTime;

        public bool ManualStartGame { get; set; } = SettingDefaults.Game.ManualStartGame;

        [Step("0.001")]
        public float NoteAppearRate { get; set; } = SettingDefaults.Game.NoteAppearRate;
    }
}
