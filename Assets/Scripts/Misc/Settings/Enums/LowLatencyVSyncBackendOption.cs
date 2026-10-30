using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>Selects the frame pacing backends that low-latency VSync may use on Windows players.</summary>
    [Preserve]
    public enum LowLatencyVSyncBackendOption
    {
        /// <summary>Uses present waiting (VK_KHR_present_wait) and then the driver's low-latency mode (VK_NV_low_latency2 or VK_AMD_anti_lag), each when available, and the timer when neither is.</summary>
        Auto,

        /// <summary>Uses present waiting when available and the timer otherwise; skips the driver's low-latency mode.</summary>
        PresentWait,

        /// <summary>Always paces with the timer.</summary>
        Timer,
    }
}
