using MajdataPlay.Diagnostics;
using MajdataPlay.Settings.OptionEnumerators;
using Newtonsoft.Json;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>Contains persistent diagnostics and gameplay debugging options.</summary>
    public class DebugOptions
    {
        /// <summary>Gets or sets the repeated menu-option input rate.</summary>
        [HideInSettingUI]
        public int MenuOptionIterationSpeed { get; set; } = 45;

        /// <summary>Gets or sets the visual timing offset in the selected offset unit.</summary>
        [Range("0", null)]
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float DisplayOffset { get; set; } = 0f;

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
        /// <summary>Gets or sets the pacing backends low-latency VSync may use on Windows players: present waiting (VK_KHR_present_wait) followed by the driver's low-latency mode (VK_NV_low_latency2 or VK_AMD_anti_lag), each when available, or the timer when neither is.</summary>
        [HideInSettingUI]
        public LowLatencyVSyncBackendOption LowLatencyVSyncBackend { get; set; } = LowLatencyVSyncBackendOption.Auto;
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
