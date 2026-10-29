using MajdataPlay.Diagnostics;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>Contains persistent diagnostics, sensor, and gameplay debugging options.</summary>
    public class DebugOptions
    {

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

#if UNITY_ANDROID

        /// <summary>Gets or sets the simulated touch radius in sensor-space units.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchSimulationRadius { get; set; } = 0.5f;

        /// <summary>Gets or sets the additional hit radius for group A.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchAAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group B.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchBAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group C.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;

        /// <summary>Gets or sets the additional hit radius for group D.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;

        /// <summary>Gets or sets the additional hit radius for group E.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;

        /// <summary>Gets or sets the global simulated touch-radius adjustment.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchRadiusAdjust { get; set; } = 0f;
#else

        /// <summary>Gets or sets the simulated touch radius in sensor-space units.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchSimulationRadius { get; set; } = 0.5f;

        /// <summary>Gets or sets the additional hit radius for group A.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchAAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group B.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchBAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group C.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;

        /// <summary>Gets or sets the additional hit radius for group D.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;

        /// <summary>Gets or sets the additional hit radius for group E.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;

        /// <summary>Gets or sets the global simulated touch-radius adjustment.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchRadiusAdjust { get; set; } = 0f;
#endif

        /// <summary>Gets or sets whether runtime statistics are displayed.</summary>
        public bool DisplayRuntimeInfo { get; set; } = true;
#if UNITY_STANDALONE
        /// <summary>Gets or sets the legacy full-screen debug option.</summary>
        [HideInSettingUI]
        public bool FullScreen { get; set; } = true;
#endif
        /// <summary>Gets or sets the repeated menu-option input rate.</summary>
        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = 45;

        /// <summary>Gets or sets the visual timing offset in the selected offset unit.</summary>
        [Range("0", null)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = 0f;

        /// <summary>Gets or sets the note appearance threshold.</summary>
        [Step("0.001")]
        public float NoteAppearRate { get; set; } = 0.265f;

        /// <summary>Gets or sets the unit used for gameplay timing offsets.</summary>
        public OffsetUnitOption OffsetUnit { get; set; } = OffsetUnitOption.Frame;
#if UNITY_STANDALONE
        /// <summary>Gets or sets whether the pointer is hidden during gameplay.</summary>
        [HideInSettingUI]
        public bool HideCursorInGame { get; set; } = true;
#endif
        /// <summary>Gets or sets whether notes use the folding behavior.</summary>
        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public bool NoteFolding { get; set; } = true;

        /// <summary>Gets or sets the DJ Auto judgment policy.</summary>
        public DJAutoPolicyOption DJAutoPolicy { get; set; } = DJAutoPolicyOption.Strict;
        /// <summary>Gets or sets the maximum queued graphics frames.</summary>
        [HideInSettingUI]
        public int MaxQueuedFrames { get; set; } = 2;
        /// <summary>Gets or sets whether frame timing and render queue latency are measured, logged and shown in the runtime info overlay.</summary>
        [HideInSettingUI]
        public bool FrameTimingDiagnostics { get; set; } = false;
        /// <summary>Gets or sets whether frames are paced slightly below the refresh rate when VSync is on with Vulkan on desktop, which keeps the swapchain queue empty and lowers display latency at the cost of an occasional repeated frame.</summary>
        [HideInSettingUI]
        public bool LowLatencyVSync { get; set; } = true;
#if UNITY_IOS || UNITY_ANDROID
        /// <summary>Gets or sets the initial pooled tap-note capacity.</summary>
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 48;
        /// <summary>Gets or sets the initial pooled hold-note capacity.</summary>
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 48;
        /// <summary>Gets or sets the initial pooled touch-note capacity.</summary>
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        /// <summary>Gets or sets the initial pooled touch-hold-note capacity.</summary>
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        /// <summary>Gets or sets the initial pooled simultaneous-note line capacity.</summary>
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 24;
#else
        /// <summary>Gets or sets the initial pooled tap-note capacity.</summary>
        [HideInSettingUI]
        public int TapPoolCapacity { get; set; } = 96;
        /// <summary>Gets or sets the initial pooled hold-note capacity.</summary>
        [HideInSettingUI]
        public int HoldPoolCapacity { get; set; } = 96;
        /// <summary>Gets or sets the initial pooled touch-note capacity.</summary>
        [HideInSettingUI]
        public int TouchPoolCapacity { get; set; } = 64;
        /// <summary>Gets or sets the initial pooled touch-hold-note capacity.</summary>
        [HideInSettingUI]
        public int TouchHoldPoolCapacity { get; set; } = 64;
        /// <summary>Gets or sets the initial pooled simultaneous-note line capacity.</summary>
        [HideInSettingUI]
        public int EachLinePoolCapacity { get; set; } = 48;
#endif

        /// <summary>Gets or sets the minimum diagnostic logging severity.</summary>
        [HideInSettingUI]
#if UNITY_IOS
        [JsonIgnore]
#endif
        public LogLevel DebugLevel { get; set; } = LogLevel.Info;
    }
}
