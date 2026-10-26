using Newtonsoft.Json;
using MajdataPlay.Net;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains every user setting stored in settings.json.
    /// </summary>
    /// <remarks>
    /// Each property is one setting category, and the declaration order is the category order of the setting UI.
    /// The classes only describe the current layout; documents written by older versions are upgraded by
    /// <see cref="GameSettingMigration"/> before they are deserialized.
    /// </remarks>
    public class GameSetting
    {
        /// <summary>
        /// Gets the gameplay options.
        /// </summary>
        public GameOptions Game { get; init; } = new();

        /// <summary>
        /// Gets the judgment timing options.
        /// </summary>
        public JudgeOptions Judge { get; init; } = new();

        /// <summary>
        /// Gets the display, judgment presentation and screen layout options.
        /// </summary>
        public DisplayOptions Display { get; init; } = new();

        /// <summary>
        /// Gets the rendering quality, frame pacing and output options.
        /// </summary>
        public RenderingOptions Rendering { get; init; } = new();

        /// <summary>
        /// Gets the audio volume and output backend options.
        /// </summary>
        public SoundOptions Audio { get; init; } = new();

        /// <summary>
        /// Gets the gameplay modifiers, which only last for the current session and are never saved.
        /// </summary>
        [JsonIgnore]
        public ModOptions Mod { get; init; } = new();

        [ShowInSettingUIWhen($"{nameof(Game)}.{nameof(GameOptions.RecordMode)}", RecordModeOption.FFmpeg)]
        public FFmpegEncoderOptions Encoding { get; init; } = new();

        public FFmpegDecoderOptions Decoding { get; init; } = new();

        /// <summary>
        /// Gets the diagnostics and debugging options.
        /// </summary>
        public DebugOptions Debug { get; init; } = new();

        /// <summary>
        /// Gets the online service options, which are only edited in settings.json.
        /// </summary>
        [HideInSettingUI]
        public OnlineOptions Online { get; init; } = new();

        /// <summary>
        /// Gets the input and output device options, which are only edited in settings.json.
        /// </summary>
        [HideInSettingUI]
        public IOOptions IO { get; init; } = new();
    }

    /// <summary>
    /// Contains the gameplay options.
    /// </summary>
    public class GameOptions
    {
        /// <summary>
        /// The default action of a tap on the sub-screen during gameplay on the current platform.
        /// </summary>
#if UNITY_ANDROID || UNITY_IOS
        public const GameplaySubScreenClickBehaviorOption DefaultGameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay;
#else
        public const GameplaySubScreenClickBehaviorOption DefaultGameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip;
#endif

        /// <summary>
        /// Gets or sets how fast notes approach the judgment ring.
        /// </summary>
        [Step("0.25")]
        public float TapSpeed { get; set; } = 7.5f;

        /// <summary>
        /// Gets or sets how fast touch notes appear and close in.
        /// </summary>
        [Step("0.25")]
        public float TouchSpeed { get; set; } = 7.5f;

        /// <summary>
        /// Gets or sets the appearance timing offset of slide paths in the selected offset unit; positive values are later.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float SlideFadeInOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the background dimming during gameplay, from 0 to 1; higher values are darker.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        public float BackgroundDim { get; set; } = 0.8f;

        /// <summary>
        /// Gets or sets whether stars rotate while approaching the judgment ring.
        /// </summary>
        public bool StarRotation { get; set; } = true;

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

        /// <summary>
        /// Gets or sets whether the real-time timing offset gauge and the average judgment position are shown.
        /// </summary>
        public bool EnableJudgeTimingGauge { get; set; } = false;

        /// <summary>
        /// Gets or sets whether holding buttons 2, 3, 6 and 7 skips the track.
        /// </summary>
        public bool TrackSkip { get; set; } = true;

        /// <summary>
        /// Gets or sets when the track is skipped or retried automatically because the target achievement is no longer reachable.
        /// </summary>
        public EnforceGameFailureCondition EnforceGameFailure { get; set; } = EnforceGameFailureCondition.Disabled;

        /// <summary>
        /// Gets or sets whether holding buttons 3, 4, 5 and 6 retries the track.
        /// </summary>
        public bool FastRetry { get; set; } = true;

        /// <summary>
        /// Gets or sets whether pressing buttons 1, 2, 7 and 8 during gameplay opens the practice mode menu.
        /// </summary>
        public bool FastPractice { get; set; } = false;

        /// <summary>
        /// Gets or sets what a tap on the sub-screen does during gameplay.
        /// </summary>
        public GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior { get; set; } = DefaultGameplaySubScreenClickBehavior;

        /// <summary>
        /// Gets or sets the axis the chart is mirrored by.
        /// </summary>
        public MirrorOption Mirror { get; set; } = MirrorOption.Off;

        /// <summary>
        /// Gets or sets the chart rotation in buttons, 45 degrees each; positive values are clockwise.
        /// </summary>
        [Step("1")]
        [Range("-7", "7")]
        public int Rotation { get; set; } = 0;

        /// <summary>
        /// Gets or sets whether slide sections can be skipped.
        /// </summary>
        public bool SlideSkipping { get; set; } = true;

        /// <summary>
        /// Gets or sets how note positions are randomized.
        /// </summary>
        public RandomModeOption Random { get; set; } = RandomModeOption.Disabled;
#if UNITY_ANDROID || UNITY_IOS

        /// <summary>
        /// Gets or sets whether slides and touch notes can be played with the buttons.
        /// </summary>
        public bool ButtonRingForTouch { get; set; } = true;
#endif
        /// <summary>
        /// Gets or sets how gameplay is recorded.
        /// </summary>
        public RecordModeOption RecordMode { get; set; } = RecordModeOption.Disable;

        /// <summary>
        /// Gets or sets the wait in seconds between entering the gameplay screen and the start of the chart.
        /// </summary>
        [Step("1")]
        [Range("1", "5")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int LeadInTime { get; set; } = 1;

        /// <summary>
        /// Gets or sets whether gameplay waits for a button press after loading, as used in tournaments.
        /// </summary>
        public bool ManualStartGame { get; set; } = false;
    }

    /// <summary>
    /// Contains the judgment timing options.
    /// </summary>
    public class JudgeOptions
    {
        /// <summary>
        /// Gets or sets the offset that aligns the chart to the music in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the judgment window in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the guide sound in the selected offset unit, which does not affect judgment.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset compensating for the input latency of the touch panel in the selected offset unit.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets how hold and slide judgments behave.
        /// </summary>
        public JudgeModeOption Mode { get; set; } = JudgeModeOption.Modern;
    }

    /// <summary>
    /// Contains the display, judgment presentation and screen layout options.
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

        /// <summary>
        /// Gets or sets whether critical perfect judgments are displayed during gameplay.
        /// </summary>
        public bool DisplayCriticalPerfect { get; set; } = false;

        /// <summary>
        /// Gets or sets whether break notes display their score instead of the judgment.
        /// </summary>
        public bool DisplayBreakScore { get; set; } = true;

        /// <summary>
        /// Gets or sets the judgment levels that show fast or late; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption FastLateType { get; set; } = JudgeDisplayOption.Disable;

        /// <summary>
        /// Gets or sets the judgment levels shown for tap and hold notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption NoteJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for touch notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption TouchJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for slides; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption SlideJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels shown for break notes; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakJudgeType { get; set; } = JudgeDisplayOption.All;

        /// <summary>
        /// Gets or sets the judgment levels of break notes that show fast or late; higher judgments are hidden.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public JudgeDisplayOption BreakFastLateType { get; set; } = JudgeDisplayOption.Disable;

        /// <summary>
        /// Gets or sets whether newer slides are drawn below (modern) or above (classic) older slides.
        /// </summary>
        public JudgeModeOption SlideSortOrder { get; set; } = JudgeModeOption.Modern;

        /// <summary>
        /// Gets or sets where judgments of ring notes such as taps, stars, holds and breaks appear, from 0 at the center to 1 at the ring.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float OuterJudgeDistance { get; set; } = 1f;

        /// <summary>
        /// Gets or sets where judgments of touch and touch hold notes appear, from 0 at the center to 1 at the ring.
        /// </summary>
        [Step("0.05")]
        [Range("0", "1")]
        [TranslateValues("0")]
        public float InnerJudgeDistance { get; set; } = 1f;

        /// <summary>
        /// Gets or sets whether the judgment of a hold head is shown.
        /// </summary>
        public bool DisplayHoldHeadJudgeResult { get; set; } = false;

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
        /// Gets or sets where the reaction effect of a touch on the screen is displayed.
        /// </summary>
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        public TouchFeedbackLevel TouchFeedback { get; set; } = TouchFeedbackLevel.Outer_Only;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets the window resolution as <c>width</c>x<c>height</c>.
        /// </summary>
        [HideInSettingUI]
        public string Resolution { get; set; } = "1080x1920";
#endif

        /// <summary>
        /// Gets or sets whether the ratio and position of the main screen are adjusted manually instead of using the default layout.
        /// </summary>
#if UNITY_ANDROID || UNITY_IOS
        public bool MainScreenTransform { get; set; } = true;
#else
        public bool MainScreenTransform { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets the size multiplier of the main gameplay screen while <see cref="MainScreenTransform"/> is enabled.
        /// </summary>
        [Step("0.01")]
        [Range("0.05", "1.5")]
        public float MainScreenScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the position of the main gameplay screen on the display while <see cref="MainScreenTransform"/> is enabled.
        /// </summary>
        [Step("0.01")]
        [Range("-1", "1")]
        public float MainScreenOffset { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the vertical screen center of the last applied main screen layout, cached by the layout provider.
        /// </summary>
        [HideInSettingUI]
        public float MainScreenCachedScreenCenterY { get; set; } = 960f;

        /// <summary>
        /// Gets or sets the position of the secondary display area.
        /// </summary>
        [Step("0.01")]
        [Range("-5", "5")]
        public float SubDisplayOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the size multiplier of the secondary display area.
        /// </summary>
        [Step("0.01")]
        public float SubDisplayScale { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the screen orientation during gameplay.
        /// </summary>
        public GameplayScreenRotationAngleOption GameplayScreenRotationAngle { get; set; } = GameplayScreenRotationAngleOption.Zero;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets whether the game window stays on top of other windows.
        /// </summary>
        [HideInSettingUI]
        public bool Topmost { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets whether the video of an online chart is skipped while it is downloaded.
        /// </summary>
        public bool SkipVideoDownload { get; set; } = false;
    }

    /// <summary>
    /// Contains the audio volume and output backend options.
    /// </summary>
    public class SoundOptions
    {
        /// <summary>
        /// The default audio backend of the current platform.
        /// </summary>
#if UNITY_IOS || UNITY_ANDROID
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.BassSimple;
#else
        private static readonly SoundBackendOption s_defaultSoundBackend = SoundBackendOption.Wasapi;
#endif

        /// <summary>
        /// Gets or sets whether every output channel plays a mono downmix.
        /// </summary>
        public bool ForceMono { get; set; } = false;

        /// <summary>
        /// Gets or sets the volumes of the music and sound effects.
        /// </summary>
        public SFXVolume Volume { get; set; } = new();
#if UNITY_STANDALONE
#if UNITY_STANDALONE_WIN

        /// <summary>
        /// Gets or sets the options of the WASAPI backend.
        /// </summary>
        public WasapiOptions Wasapi { get; set; } = new();

        /// <summary>
        /// Gets or sets the options of the ASIO backend.
        /// </summary>
        public AsioOptions Asio { get; set; } = new();
#endif

        /// <summary>
        /// Gets or sets the volumes of the speaker channel pairs.
        /// </summary>
        public ChannelOptions Channel { get; set; } = new();
#endif

        /// <summary>
        /// Gets or sets the buffer options of the BASS output.
        /// </summary>
        public BassAudioOptions Bass { get; set; } = new();

        /// <summary>
        /// Gets or sets the audio output backend.
        /// </summary>
        public SoundBackendOption Backend { get; set; } = s_defaultSoundBackend;
    }

    /// <summary>
    /// Contains the volumes of the music and sound effects, from 0 to 2.
    /// </summary>
    public class SFXVolume
    {
        /// <summary>
        /// Gets or sets the master volume.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Global { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the background music in song select, result and other scenes outside gameplay.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float BGM { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the music track during gameplay.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Track { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the chart guide sounds and the lead-in metronome.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Answer { get; set; } = 0.8f;

        /// <summary>
        /// Gets or sets the volume of the tap and hold judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Tap { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the sound played by a judged EX tap or hold.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Ex { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the break judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Break { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the slide sound effects.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Slide { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the touch and touch hold judgment sounds.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Touch { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the firework sound of touch hanabi effects.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Hanabi { get; set; } = 0.3f;

        /// <summary>
        /// Gets or sets the volume of the mascot voice lines.
        /// </summary>
        [Step("0.05")]
        [Range("0", "2")]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Voice { get; set; } = 1f;
    }

    /// <summary>
    /// Contains the gameplay modifiers of the current session.
    /// </summary>
    public class ModOptions
    {
        /// <summary>
        /// Gets or sets the playback speed multiplier of the track.
        /// </summary>
        [Step("0.05")]
        [Range("0", null)]
        public float PlaybackSpeed { get; set; } = 1f;

        /// <summary>
        /// Gets or sets how the chart is played automatically.
        /// </summary>
        public AutoplayModeOption AutoPlay { get; set; } = AutoplayModeOption.Disable;

        /// <summary>
        /// Gets or sets how strict the judgment windows are.
        /// </summary>
        public JudgeStyleOption JudgeStyle { get; set; } = JudgeStyleOption.DEFAULT;

        /// <summary>
        /// Gets or sets whether slides can receive judgments below critical perfect.
        /// </summary>
        public bool SubdivideSlideJudgeGrade { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes a break note.
        /// </summary>
        public bool AllBreak { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes an EX note.
        /// </summary>
        public bool AllEx { get; set; } = false;

        /// <summary>
        /// Gets or sets whether every note becomes a touch note.
        /// </summary>
        public bool AllTouch { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the head tap of every slide is removed.
        /// </summary>
        public bool SlideNoHead { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the body of every slide is removed.
        /// </summary>
        public bool SlideNoTrack { get; set; } = false;
#if !(UNITY_ANDROID || UNITY_IOS)

        /// <summary>
        /// Gets or sets whether slides and touch notes can be played with the buttons.
        /// </summary>
        public bool ButtonRingForTouch { get; set; } = false;
#endif

        /// <summary>
        /// Gets or sets the name of the mask covering part of the gameplay screen, or <c>Disable</c> for none.
        /// </summary>
        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask { get; set; } = "Disable";

        /// <summary>
        /// Determines whether a modifier that changes the playback, the notes or the judgment is active.
        /// </summary>
        /// <returns>True when the playback speed is not 1 or any of the all-break, all-EX, all-touch, autoplay or judgment style modifiers is set.</returns>
        public bool IsAnyModActive()
        {
            return !(PlaybackSpeed == 1f &&
                !AllBreak && !AllEx && !AllTouch && AutoPlay == AutoplayModeOption.Disable && JudgeStyle == JudgeStyleOption.DEFAULT);
        }
    }

    /// <summary>
    /// Contains the online service options.
    /// </summary>
    public class OnlineOptions
    {
        /// <summary>
        /// Gets or sets whether the online services are enabled; on iOS the native settings decide and the value is not saved.
        /// </summary>
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool Enable { get; set; } = false;
#if UNITY_STANDALONE

        /// <summary>
        /// Gets whether HTTP requests use a proxy: <see cref="Proxy"/> when it is set, otherwise the system proxy.
        /// </summary>
        public bool UseProxy { get; init; } = true;

        /// <summary>
        /// Gets the absolute HTTP or HTTPS proxy URL, optionally with <c>user:password</c> credentials; empty uses the system proxy.
        /// </summary>
        public string Proxy { get; init; } = string.Empty;
#endif

        /// <summary>
        /// Gets or sets the online service endpoints.
        /// </summary>
        public ApiEndpoint[] ApiEndpoints { get; set; } = new ApiEndpoint[]
        {
#if !UNITY_IOS
            new ApiEndpoint()
            {
                Name = "MajdataNET",
                Url = new("https://majdata.net/api3/api/"),
                Username = "YourUsername",
                Password = "YourPassword"
            }
#endif
        };
    }

    /// <summary>
    /// Contains the input and output device options.
    /// </summary>
    public class IOOptions
    {
#if UNITY_STANDALONE
        /// <summary>
        /// Gets or sets the manufacturer of the cabinet devices, or null to detect it.
        /// </summary>
        public DeviceManufacturerOption? Manufacturer { get; set; } = null;
#endif

        /// <summary>
        /// Gets or sets the input device options.
        /// </summary>
        public InputDeviceOptions InputDevice { get; set; } = new();
#if UNITY_STANDALONE

        /// <summary>
        /// Gets or sets the output device options.
        /// </summary>
        public OutputDeviceOptions OutputDevice { get; set; } = new();
#endif
    }

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
#if UNITY_STANDALONE

    /// <summary>
    /// Contains the output device options.
    /// </summary>
    public class OutputDeviceOptions
    {
        /// <summary>
        /// Gets or sets the cabinet LED options.
        /// </summary>
        public LedOptions Led { get; set; } = new();
    }

    /// <summary>
    /// Contains the cabinet LED options.
    /// </summary>
    public class LedOptions
    {
        /// <summary>
        /// Gets whether the cabinet LEDs are driven.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets the LED brightness multiplier, from 0 to 1.
        /// </summary>
        public float Brightness { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the minimum interval in milliseconds between LED updates.
        /// </summary>
        public int RefreshRateMs { get; set; } = 16;

        /// <summary>
        /// Gets or sets whether LED updates are limited to <see cref="RefreshRateMs"/>.
        /// </summary>
        public bool Throttler { get; set; } = true;

        /// <summary>
        /// Gets or sets the serial port of serial LED devices.
        /// </summary>
        public SerialPortOptions SerialPortOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the HID connection of HID LED devices.
        /// </summary>
        public HidOptions HidOptions { get; set; } = new();
    }

    /// <summary>
    /// Contains the button ring options.
    /// </summary>
    public class ButtonRingOptions
    {
        /// <summary>
        /// Gets whether the button ring is opened.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets the button ring device type, or null to detect it.
        /// </summary>
        public ButtonRingDeviceOption? Type { get; set; } = null;

        /// <summary>
        /// Gets or sets whether button input is debounced by <see cref="DebounceThresholdMs"/>.
        /// </summary>
        public bool Debounce { get; set; } = false;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds; 0 polls without waiting.
        /// </summary>
        public int PollingRateMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the debounce threshold in milliseconds.
        /// </summary>
        public int DebounceThresholdMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the HID connection of HID button rings.
        /// </summary>
        public HidOptions HidOptions { get; set; } = new();
    }

    /// <summary>
    /// Contains the touch panel options.
    /// </summary>
    public class TouchPanelOptions
    {
        /// <summary>
        /// Gets whether the touch panel is opened.
        /// </summary>
        public bool Enable { get; init; } = true;

        /// <summary>
        /// Gets or sets whether sensor input is debounced by <see cref="DebounceThresholdMs"/>.
        /// </summary>
        public bool Debounce { get; set; } = false;

        /// <summary>
        /// Gets or sets the sensitivity of each sensor group, sent to touch panels that support it.
        /// </summary>
        public TouchPanelSensitivityConfig Sensitivities { get; set; } = default;

        /// <summary>
        /// Gets or sets the polling interval in milliseconds; 0 polls without waiting.
        /// </summary>
        public int PollingRateMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the debounce threshold in milliseconds.
        /// </summary>
        public int DebounceThresholdMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets the serial port of serial touch panels.
        /// </summary>
        public SerialPortOptions SerialPortOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the USB connection of USB touch panels.
        /// </summary>
        public UsbOptions UsbOptions { get; set; } = new();

        /// <summary>
        /// Gets or sets the options of capacitive touch panels.
        /// </summary>
        public CapacitiveTouchPanelOptions CapacitivePanelOptions { get; set; } = new();
    }

    /// <summary>
    /// Contains the identification and open options of a HID device.
    /// </summary>
    public class HidOptions
    {
        /// <summary>
        /// Gets or sets the device name to match, or null for the default device.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// Gets or sets the product ID to match, or null for the default device.
        /// </summary>
        public int? ProductId { get; set; } = null;

        /// <summary>
        /// Gets or sets the vendor ID to match, or null for the default device.
        /// </summary>
        public int? VendorId { get; set; } = null;

        /// <summary>
        /// Gets or sets whether the device is opened exclusively.
        /// </summary>
        public bool Exclusice { get; set; } = false;

        /// <summary>
        /// Gets or sets the priority the device is opened with.
        /// </summary>
        public HidOpenPriority OpenPriority { get; set; } = HidOpenPriority.VeryHigh;
    }

    /// <summary>
    /// Contains the identification and open options of a USB device.
    /// </summary>
    public class UsbOptions
    {
        /// <summary>
        /// Gets or sets the device name to match, or null for the default device.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// Gets or sets the product ID to match, or null for the default device.
        /// </summary>
        public int? ProductId { get; set; } = null;

        /// <summary>
        /// Gets or sets the vendor ID to match, or null for the default device.
        /// </summary>
        public int? VendorId { get; set; } = null;

        /// <summary>
        /// Gets or sets whether the device is opened exclusively.
        /// </summary>
        public bool Exclusice { get; set; } = false;
    }

    /// <summary>
    /// Contains the options of capacitive touch panels.
    /// </summary>
    public class CapacitiveTouchPanelOptions
    {
        /// <summary>
        /// Gets or sets the radius of a touch point in panel units.
        /// </summary>
        public int TouchRadius { get; set; } = 30;

        /// <summary>
        /// Gets or sets the extra radius added to the touch point for each sensor group.
        /// </summary>
        public CapacitiveTouchPanelRadiusOffsetConfig RadiusOffset { get; set; } = new()
        {
            A = 0,
            B = 20,
            C = 0,
            D = 0,
            E = 25
        };
    }

    /// <summary>
    /// Contains the options of a serial port connection.
    /// </summary>
    public class SerialPortOptions
    {
        /// <summary>
        /// Gets or sets the port, as a COM port number on Windows and a device path elsewhere, or null for the default port.
        /// </summary>
#if UNITY_STANDALONE_WIN
        public int? Port { get; set; } = null;
#else
        public string? Port { get; set; } = null;
#endif

        /// <summary>
        /// Gets or sets the baud rate, or null for the default rate of the device.
        /// </summary>
        public int? BaudRate { get; set; } = null;
    }

    /// <summary>
    /// Contains the touch panel sensitivity of each sensor group.
    /// </summary>
    public struct TouchPanelSensitivityConfig
    {
        /// <summary>
        /// Gets or sets the sensitivity of the A sensors.
        /// </summary>
        public short A { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the B sensors.
        /// </summary>
        public short B { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the C sensors.
        /// </summary>
        public short C { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the D sensors.
        /// </summary>
        public short D { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the E sensors.
        /// </summary>
        public short E { get; set; }
    }

    /// <summary>
    /// Contains the extra touch point radius of each sensor group on capacitive touch panels.
    /// </summary>
    public struct CapacitiveTouchPanelRadiusOffsetConfig
    {
        /// <summary>
        /// Gets or sets the extra radius for the A sensors.
        /// </summary>
        public int A { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the B sensors.
        /// </summary>
        public int B { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the C sensors.
        /// </summary>
        public int C { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the D sensors.
        /// </summary>
        public int D { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the E sensors.
        /// </summary>
        public int E { get; set; }
    }

    /// <summary>
    /// Contains the volumes of the speaker channel pairs of multichannel outputs.
    /// </summary>
    public class ChannelOptions
    {
        /// <summary>
        /// Gets or sets the volume of the front left and right channels.
        /// </summary>
        public float FrontVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the center and LFE channels.
        /// </summary>
        public float CenterAndLFEVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the side left and right channels.
        /// </summary>
        public float SideVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the rear left and right channels.
        /// </summary>
        public float RearVolume { get; set; } = 1f;
    }

    /// <summary>
    /// Contains the options of the ASIO backend.
    /// </summary>
    public class AsioOptions
    {
        /// <summary>
        /// Gets or sets the index of the ASIO device.
        /// </summary>
        public int DeviceIndex { get; set; } = 0;

        /// <summary>
        /// Gets or sets the sample rate in hertz.
        /// </summary>
        public int SampleRate { get; set; } = 44100;
    }

    /// <summary>
    /// Contains the options of the WASAPI backend.
    /// </summary>
    public class WasapiOptions
    {
        /// <summary>
        /// Gets or sets whether the device is opened in exclusive mode.
        /// </summary>
        public bool Exclusive { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the device is opened in raw mode, bypassing system audio effects.
        /// </summary>
        public bool RawMode { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the device is fed asynchronously.
        /// </summary>
        public bool AsyncMode { get; set; } = true;

        /// <summary>
        /// Gets or sets the buffer length in seconds in exclusive mode.
        /// </summary>
        public float BufferSize { get; set; } = 0.02f;

        /// <summary>
        /// Gets or sets the update period in seconds in exclusive mode.
        /// </summary>
        public float Period { get; set; } = 0.005f;
    }
#endif

    /// <summary>
    /// Contains the buffer options of the BASS output.
    /// </summary>
    public class BassAudioOptions
    {
        /// <summary>
        /// The default playback buffer length in milliseconds.
        /// </summary>
        private const int DefaultBufferLengthMs = 1000;

        /// <summary>
        /// The default update period in milliseconds.
        /// </summary>
        private const int DefaultUpdatePeriodMs = 200;
#if UNITY_ANDROID || UNITY_IOS

        /// <summary>
        /// The default device buffer length in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceBufferLengthMs = 32;

        /// <summary>
        /// The default device update period in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceUpdatePeriodMs = 8;
#else

        /// <summary>
        /// The default device buffer length in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceBufferLengthMs = 64;

        /// <summary>
        /// The default device update period in milliseconds on the current platform.
        /// </summary>
        private const int DefaultDeviceUpdatePeriodMs = 16;
#endif
#if UNITY_ANDROID

        /// <summary>
        /// Gets or sets whether BASS outputs through AAudio.
        /// </summary>
        public bool EnableAAudio { get; set; } = true;
#endif

        /// <summary>
        /// Gets or sets the playback buffer length in milliseconds.
        /// </summary>
        public int BufferLengthMs { get; set; } = DefaultBufferLengthMs;

        /// <summary>
        /// Gets or sets the update period in milliseconds.
        /// </summary>
        public int UpdatePeriodMs { get; set; } = DefaultUpdatePeriodMs;

        /// <summary>
        /// Gets or sets the device buffer length in milliseconds.
        /// </summary>
        public int DeviceBufferLengthMs { get; set; } = DefaultDeviceBufferLengthMs;

        /// <summary>
        /// Gets or sets the device update period in milliseconds.
        /// </summary>
        public int DeviceUpdatePeriodMs { get; set; } = DefaultDeviceUpdatePeriodMs;
    }
}
