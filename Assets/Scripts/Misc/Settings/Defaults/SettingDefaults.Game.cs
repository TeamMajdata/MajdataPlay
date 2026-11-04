#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Game
        {
            public const float TapSpeed = 7.5f;

            public const float TouchSpeed = 7.5f;

            public const float SlideFadeInOffset = 0f;

            public const float BackgroundDim = 0.8f;

            public const bool SkipVideoDownload = false;

            public const bool StarRotation = true;

            public const bool TrackSkip = true;

            public const EnforceGameFailureCondition EnforceGameFailure = EnforceGameFailureCondition.Disabled;

            public const bool FastRetry = true;

            public const bool FastPractice = false;

#if UNITY_ANDROID || UNITY_IOS
            public const GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay;
#else
            public const GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip;
#endif

            public const MirrorOption Mirror = MirrorOption.Off;

            public const int Rotation = 0;

            public const bool SlideSkipping = true;

            public const RandomModeOption Random = RandomModeOption.Disabled;

#if UNITY_ANDROID || UNITY_IOS
            public const bool ButtonRingForTouch = true;
#else
            public const bool ButtonRingForTouch = false;
#endif

            public const int LeadInTime = 1;

            public const bool ManualStartGame = false;

            public const float NoteAppearRate = 0.265f;
        }
    }
}
