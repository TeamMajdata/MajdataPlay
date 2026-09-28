using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine.Scripting;
using Newtonsoft.Json;
using MajdataPlay.Net;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Diagnostics;
#nullable enable
namespace MajdataPlay.Settings
{
    public class GameSetting
    {
        
        public GameOptions Game { get; init; } = new();
        
        public JudgeOptions Judge { get; init; } = new();
        
        public DisplayOptions Display { get; init; } = new();
        
        public SoundOptions Audio { get; init; } = new();
        [JsonIgnore]
        public ModOptions Mod { get; init; } = new();
        
        public DebugOptions Debug { get; init; } = new();
        [HideInSettingUI]
        public OnlineOptions Online { get; init; } = new();
        [HideInSettingUI]
        public IOOptions IO { get; init; } = new();
    }
    
    public class GameOptions
    {
#if UNITY_ANDROID || UNITY_IOS
        public const GameplaySubScreenClickBehaviorOption DEFAULT_GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay;
#else
        public const GameplaySubScreenClickBehaviorOption DEFAULT_GameplaySubScreenClickBehavior = GameplaySubScreenClickBehaviorOption.TrackSkip;
#endif

        [Step("0.25")]
        [Range(HasMax = false, HasMin = false)]
        public float TapSpeed { get; set; } = 7.5f;
        
        [Step("0.25")]
        [Range(HasMax = false, HasMin = false)]
        public float TouchSpeed { get; set; } = 7.5f;
        
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float SlideFadeInOffset { get; set; } = 0f;
        
        [Step("0.05")]
        [Range("0", "1" ,HasMax = true, HasMin = true)]
        public float BackgroundDim { get; set; } = 0.8f;
        
        public bool StarRotation { get; set; } = true;
        
        public BGInfoOption BGInfo { get; set; } = BGInfoOption.Combo;
        public BGInfoOption SecondaryBGInfo { get; set; } = BGInfoOption.None;
        public BGInfoOption SubScreenBGInfo { get; set; } = BGInfoOption.Achievement;
        
        public TopInfoDisplayOption TopInfo { get; set; } = TopInfoDisplayOption.None;
        public bool EnableJudgeTimingGauge { get; set; } = false;

        public bool TrackSkip { get; set; } = true;

        public EnforceGameFailureCondition EnforceGameFailure { get; set; } = EnforceGameFailureCondition.Disabled;
        
        public bool FastRetry { get; set; } = true;

        public bool FastPractice { get; set; } = false;

        public GameplaySubScreenClickBehaviorOption GameplaySubScreenClickBehavior { get; set; } = DEFAULT_GameplaySubScreenClickBehavior;
        
        public MirrorOption Mirror { get; set; } = MirrorOption.Off;
        
        [Step("1")]
        [Range("-7", "7", HasMax = true, HasMin = true)]
        public int Rotation { get; set; } = 0;
        public bool SlideSkipping { get; set; } = true;

        public RandomModeOption Random { get; set; } = RandomModeOption.Disabled;
#if UNITY_ANDROID || UNITY_IOS
        
        public bool ButtonRingForTouch { get; set; } = true;
#endif

#if UNITY_STANDALONE
        
        public RecordModeOption RecordMode { get; set; } = RecordModeOption.Disable;
#endif
        [Step("1")]
        [Range("1", "5", HasMax = true, HasMin = true)]
        public int LeadInTime { get; set; } = 1;

        public bool ManualStartGame { get; set; } = false;
    }
    
    public class JudgeOptions
    {
        
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;
        
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = 0f;
        
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = 0f;
        
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = 0f;
        
        public JudgeModeOption Mode { get; set; } = JudgeModeOption.Modern;
    }
    
    public class DisplayOptions
    {
#if UNITY_ANDROID || UNITY_IOS
        private const RenderQualityOption DefaultQuality = RenderQualityOption.Low;
        private const int DefaultRednerScale = 75;
        private const bool DefaultVSync = true;
#else
        private const RenderQualityOption DefaultQuality = RenderQualityOption.Medium;
        private const int DefaultRednerScale = 100;
        private const bool DefaultVSync = true;
#endif


        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = "";
        
        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = "default";
        
        public bool DisplayCriticalPerfect { get; set; } = false;
        
        public bool DisplayBreakScore { get; set; } = true;

        
        public JudgeDisplayOption FastLateType { get; set; } = JudgeDisplayOption.Disable;
        
        public JudgeDisplayOption NoteJudgeType { get; set; } = JudgeDisplayOption.All;
        
        public JudgeDisplayOption TouchJudgeType { get; set; } = JudgeDisplayOption.All;
        
        public JudgeDisplayOption SlideJudgeType { get; set; } = JudgeDisplayOption.All;
        
        public JudgeDisplayOption BreakJudgeType { get; set; } = JudgeDisplayOption.All;
        
        public JudgeDisplayOption BreakFastLateType { get; set; } = JudgeDisplayOption.Disable;
        
        public JudgeModeOption SlideSortOrder { get; set; } = JudgeModeOption.Modern;
        /// <summary>
        /// Such like Tap、Star、Hold and Break
        /// </summary>
        
        [Step("0.05")]
        [Range("0", "1", HasMax = true, HasMin = true)]
        public float OuterJudgeDistance { get; set; } = 1f;
        /// <summary>
        /// Such like Touch and TouchHold
        /// </summary>
        
        [Step("0.05")]
        [Range("0", "1", HasMax = true, HasMin = true)]
        public float InnerJudgeDistance { get; set; } = 1f;
        
        public bool DisplayHoldHeadJudgeResult { get; set; } = false;
        
        [Step("0.01")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TapScale { get; set; } = 1f;
        
        [Step("0.01")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float HoldScale { get; set; } = 1f;
        
        [Step("0.01")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchScale { get; set; } = 1f;
        
        [Step("0.01")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float SlideScale { get; set; } = 1f;
        
        public TouchFeedbackLevel TouchFeedback { get; set; } = TouchFeedbackLevel.Outer_Only;
#if UNITY_STANDALONE
        [HideInSettingUI]
        public string Resolution { get; set; } = "1080x1920";
#endif
        
#if UNITY_ANDROID || UNITY_IOS
        public bool MainScreenTransform { get; set; } = true;
#else
        public bool MainScreenTransform { get; set; } = false;
#endif
        
        [Step("0.01")]
        [Range("0.05", "1.5", HasMax = true, HasMin = true)]
        public float MainScreenScale { get; set; } = 1f;
        
        [Step("0.01")]
        [Range("-1", "1", HasMax = true, HasMin = true)]
        public float MainScreenOffset { get; set; } = 1f;
        [HideInSettingUI]
        public float MainScreenCachedScreenCenterY { get; set; } = 960f;
        
        [Step("0.01")]
        [Range("-5", "5", HasMax = true, HasMin = true)]
        public float SubDisplayOffset { get; set; } = 0f;
        
        [Step("0.01")]
        public float SubDisplayScale { get; set; } = 1f;
        public GameplayScreenRotationAngleOption GameplayScreenRotationAngle { get; set; } = GameplayScreenRotationAngleOption.Zero;

        [OptionEnumerator(typeof(EngineEnumSettingEnumerator))]
        public RenderQualityOption RenderQuality { get; set; } = DefaultQuality;

        [Step("5")]
        [Range("50", "100", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(EngineNumberSettingEnumerator))]
        public int RenderScale { get; set; } = DefaultRednerScale;
#if UNITY_STANDALONE
        [HideInSettingUI]
        public bool Topmost { get; set; } = false;
#endif
        
#if UNITY_ANDROID
        [HideInSettingUI]
        // The native Oniimai display panel owns this setting, across user profiles.
        // Accept old JSON files without letting them override the saved 60/120 choice.
        public int FPSLimit
        {
#if UNITY_EDITOR
            get => 60;
#else
            get => IO.OniimaiController.GameFrameRate;
#endif
            set { }
        }
#else
        [Step("1")]
        [Range("-1", null, HasMax = false, HasMin = true)]
        [OptionEnumerator(typeof(EngineNumberSettingEnumerator))]
        public int FPSLimit { get; set; } = 120;
#endif
#if !(UNITY_ANDROID || UNITY_IOS)
        
        [OptionEnumerator(typeof(EngineBooleanSettingEnumerator))]
        public bool VSync { get; set; } = DefaultVSync;
#endif
        
        public bool SkipVideoDownload { get; set; } = false;
    }
    
    public class SoundOptions
    {
#if UNITY_IOS || UNITY_ANDROID
        readonly static SoundBackendOption DEFAULT_SOUND_BACKEND = SoundBackendOption.BassSimple;
#else
        readonly static SoundBackendOption DEFAULT_SOUND_BACKEND = SoundBackendOption.Wasapi;
#endif
        
        public bool ForceMono { get; set; } = false;
        
        public SFXVolume Volume { get; set; } = new();
#if UNITY_STANDALONE
#if UNITY_STANDALONE_WIN
        public WasapiOptions Wasapi { get; set; } = new();
        
        public AsioOptions Asio { get; set; } = new();
#endif
        public ChannelOptions Channel { get; set; } = new();
#endif
        public BassAudioOptions Bass { get; set; } = new();

        
        public SoundBackendOption Backend { get; set; } = DEFAULT_SOUND_BACKEND;
    }
    
    public class SFXVolume
    {
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Global { get; set; } = 0.3f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float BGM { get; set; } = 1f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Track { get; set; } = 1f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Answer { get; set; } = 0.8f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Tap { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Ex { get; set; } = 0.3f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Break { get; set; } = 0.3f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Slide { get; set; } = 0.3f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Touch { get; set; } = 0.3f;

        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Hanabi { get; set; } = 0.3f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        [OptionEnumerator(typeof(AudioVolumeEnumerator))]
        public float Voice { get; set; } = 1f;
    }
    
    public class ModOptions
    {
        
        [Step("0.05")]
        [Range("0", null, HasMax = false, HasMin = true)]
        public float PlaybackSpeed { get; set; } = 1f;
        
        public AutoplayModeOption AutoPlay { get; set; } = AutoplayModeOption.Disable;
        
        public JudgeStyleOption JudgeStyle { get; set; } = JudgeStyleOption.DEFAULT;
        
        public bool SubdivideSlideJudgeGrade { get; set; } = false;
        
        public bool AllBreak { get; set; } = false;
        
        public bool AllEx { get; set; } = false;
        
        public bool AllTouch { get; set; } = false;
        
        public bool SlideNoHead { get; set; } = false;
        
        public bool SlideNoTrack { get; set; } = false;
#if !(UNITY_ANDROID || UNITY_IOS)
        
        public bool ButtonRingForTouch { get; set; } = false;
#endif
        
        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask { get; set; } = "Disable";

        public bool IsAnyModActive()
        {
            return !(PlaybackSpeed == 1f &&
                !AllBreak && !AllEx && !AllTouch && AutoPlay == AutoplayModeOption.Disable && JudgeStyle == JudgeStyleOption.DEFAULT);
        }

    }
    
    public class OnlineOptions
    {
        
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool Enable { get; set; } = false;
#if UNITY_STANDALONE
        public bool UseProxy { get; init; } = true;
        public string Proxy { get; init; } = string.Empty;
#endif
        
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
    
    public class DebugOptions
    {
        
        public bool DisplaySensor { get; set; } = false;
#if UNITY_ANDROID
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchSimulationRadius { get; set; } = 0.5f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchAAreaExtraRadius { get; set; } = 0f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchBAreaExtraRadius { get; set; } = 0f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchRadiusAdjust { get; set; } = 0f;
#else
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchSimulationRadius { get; set; } = 0.5f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchAAreaExtraRadius { get; set; } = 0f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchBAreaExtraRadius { get; set; } = 0f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;
        
        [Step("0.05")]
        [Range("0", "2", HasMax = true, HasMin = true)]
        public float TouchRadiusAdjust { get; set; } = 0f;
#endif
        
        public bool DisplayRuntimeInfo { get; set; } = true;
#if UNITY_STANDALONE
        [HideInSettingUI]
        public bool FullScreen { get; set; } = true;
#endif
        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = 45;
        
        [Range("0", null, HasMax = false, HasMin = true)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = 0f;
        
        [Step("0.001")]
        public float NoteAppearRate { get; set; } = 0.265f;
        
        public OffsetUnitOption OffsetUnit { get; set; } = OffsetUnitOption.Frame;
#if UNITY_STANDALONE
        [HideInSettingUI]
        public bool HideCursorInGame { get; set; } = true;
#endif
        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool NoteFolding { get; set; } = true;
        
        public DJAutoPolicyOption DJAutoPolicy { get; set; } = DJAutoPolicyOption.Strict;
        [HideInSettingUI]
        public int MaxQueuedFrames { get; set; } = 2;
#if UNITY_IOS || UNITY_ANDROID
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 48;
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 48;
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 24;
#else
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 96;
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 96;
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 48;
#endif
        
        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public LogLevel DebugLevel { get; set; } = LogLevel.Info;
    }
    
    public class IOOptions
    {
#if UNITY_STANDALONE
        
        public DeviceManufacturerOption? Manufacturer { get; set; } = null;
#endif
        
        public InputDeviceOptions InputDevice { get; set; } = new();
#if UNITY_STANDALONE
        
        public OutputDeviceOptions OutputDevice { get; set; } = new();
#endif
    }
    
    public class InputDeviceOptions
    {
#if UNITY_STANDALONE
        
        public int Player { get; set; } = 1;
        
        public ButtonRingOptions ButtonRing { get; set; } = new();
        
        public TouchPanelOptions TouchPanel { get; set; } = new();
#else
        public MobileExternalButtonRingOption ExternalButtonRing { get; init; } = MobileExternalButtonRingOption.None;
#endif
    }
#if UNITY_STANDALONE
    
    public class OutputDeviceOptions
    {
        
        public LedOptions Led { get; set; } = new();
    }
    
    public class LedOptions
    {
        
        public bool Enable { get; init; } = true;
        
        public float Brightness { get; set; } = 1.0f;
        
        public int RefreshRateMs { get; set; } = 16;
        
        public bool Throttler { get; set; } = true;
        
        public SerialPortOptions SerialPortOptions { get; set; } = new();
        
        public HidOptions HidOptions { get; set; } = new();
    }
    
    public class ButtonRingOptions
    {
        public bool Enable { get; init; } = true;

        public ButtonRingDeviceOption? Type { get; set; } = null;
        
        public bool Debounce { get; set; } = false;
        
        public int PollingRateMs { get; set; } = 0;
        
        public int DebounceThresholdMs { get; set; } = 0;
        
        public HidOptions HidOptions { get; set; } = new();
    }
    
    public class TouchPanelOptions
    {
        public bool Enable { get; init; } = true;

        public bool Debounce { get; set; } = false;
        
        public TouchPanelSensitivityConfig Sensitivities { get; set; } = default;
        
        public int PollingRateMs { get; set; } = 0;
        
        public int DebounceThresholdMs { get; set; } = 0;
        
        public SerialPortOptions SerialPortOptions { get; set; } = new();
        
        public UsbOptions UsbOptions { get; set; } = new();
        
        public CapacitiveTouchPanelOptions CapacitivePanelOptions { get; set; } = new();
    }

    
    public class HidOptions
    {
        
        public string? DeviceName { get; set; } = null;
        
        public int? ProductId { get; set; } = null;
        
        public int? VendorId { get; set; } = null;
        
        public bool Exclusice { get; set; } = false;
        
        public HidOpenPriority OpenPriority { get; set; } = HidOpenPriority.VeryHigh;
    }
    
    public class UsbOptions
    {
        
        public string? DeviceName { get; set; } = null;
        
        public int? ProductId { get; set; } = null;
        
        public int? VendorId { get; set; } = null;
        
        public bool Exclusice { get; set; } = false;
    }
    
    public class CapacitiveTouchPanelOptions
    {
        
        public int TouchRadius { get; set; } = 30;
        public CapacitiveTouchPanelRadiusOffsetConfig RadiusOffset { get; set; } = new()
        {
            A = 0,
            B = 20,
            C = 0,
            D = 0,
            E = 25
        };
    }
    
    public class SerialPortOptions
    {
        
#if UNITY_STANDALONE_WIN
        public int? Port { get; set; } = null;
#else
        public string? Port { get; set; } = null;
#endif
        
        public int? BaudRate { get; set; } = null;
    }
    
    public struct TouchPanelSensitivityConfig
    {
        
        public short A { get; set; }
        
        public short B { get; set; }
        
        public short C { get; set; }
        
        public short D { get; set; }
        
        public short E { get; set; }
    }
    public struct CapacitiveTouchPanelRadiusOffsetConfig
    {

        public int A { get; set; }

        public int B { get; set; }

        public int C { get; set; }

        public int D { get; set; }

        public int E { get; set; }
    }

    public class ChannelOptions
    {
        // Front (LF / RF)
        // Rear (LR / RR)
        // Side (LS / RS) (rear center)
        // CenterAndLFE (LFE / Center)
        public float FrontVolume { get; set; } = 1f;
        public float CenterAndLFEVolume { get; set; } = 1f;
        public float SideVolume { get; set; } = 1f;
        public float RearVolume { get; set; } = 1f;

    }
    public class AsioOptions
    {
        public int DeviceIndex { get; set; } = 0;
        public int SampleRate { get; set; } = 44100;
    }
    public class WasapiOptions
    {
        public bool Exclusive { get; set; } = true;
        public bool RawMode { get; set; } = true;
        public bool AsyncMode { get; set; } = true;
        public float BufferSize { get; set; } = 0.02f;
        public float Period { get; set; } = 0.005f;
    }
#endif
    public class BassAudioOptions
    {
        const int DEFAULT_BufferLengthMs = 1000;
        const int DEFAULT_UpdatePeriodMs = 200;
#if UNITY_ANDROID || UNITY_IOS
        const int DEFAULT_DeviceBufferLengthMs = 32;
        const int DEFAULT_DeviceUpdatePeriodMs = 8;
#else
        const int DEFAULT_DeviceBufferLengthMs = 64;
        const int DEFAULT_DeviceUpdatePeriodMs = 16;
#endif
#if UNITY_ANDROID // Android Only (AAudio)
        public bool EnableAAudio { get; set; } = true;
#endif
        public int BufferLengthMs { get; set; } = DEFAULT_BufferLengthMs;
        public int UpdatePeriodMs { get; set; } = DEFAULT_UpdatePeriodMs;
        public int DeviceBufferLengthMs { get; set; } = DEFAULT_DeviceBufferLengthMs;
        public int DeviceUpdatePeriodMs { get; set; } = DEFAULT_DeviceUpdatePeriodMs;
    }
}
