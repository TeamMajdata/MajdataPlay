using MajdataPlay.Rendering;
#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Rendering
        {
            /// <summary>Uses the Beautiful preset when no saved rendering quality is available.</summary>
            public const RenderingQualityOption RenderQuality = RenderingQualityOption.Beautiful;

            public const int FPSLimit = 120;

            public const bool VSync = true;

            public const bool VulkanLowLatencyVSync = true;

            public const VulkanLowLatencyVSyncBackendOption VulkanLowLatencyVSyncBackend = VulkanLowLatencyVSyncBackendOption.Timer;

            /// <summary>Leaves MSAA disabled for new settings; Custom can opt into a sample count explicitly.</summary>
            public const RenderingAntialiasingOption AntiAliasing = RenderingAntialiasingOption.Disabled;

            public const RenderingTextureQualityOption TextureQuality = RenderingTextureQualityOption.High;

            /// <summary>Uses complete outlined alpha-blended UI for the default Beautiful preset.</summary>
            public const UiOutlineMode UIOutlineMode = UiOutlineMode.All;

            /// <summary>Uses Gaussian blur when the Refined panel effect has no saved style.</summary>
            public const UiBackdropStyle UIBackdropStyle = UiBackdropStyle.GaussianBlur;

            public const RenderingOutputMode OutputMode = RenderingOutputMode.Direct;

            public const bool CompatibilityMode = false;

            public const int SystemResolutionScaling = 100;

            public const int RenderScale = 100;

            public const bool DisplayRuntimeInfo = true;

#if UNITY_STANDALONE
            public const string Resolution = "1080x1920";

            public const bool FullScreen = true;

            public const bool Topmost = false;
#endif

#if UNITY_ANDROID || UNITY_IOS
            public const bool MainScreenTransform = true;
#else
            public const bool MainScreenTransform = false;
#endif

            public const float MainScreenScale = 1f;

            public const float MainScreenOffset = 1f;

            public const float MainScreenCachedScreenCenterY = 960f;

            public const float SubDisplayOffset = 0f;

            public const float SubDisplayScale = 1f;

            public const GameplayScreenRotationAngleOption GameplayScreenRotationAngle = GameplayScreenRotationAngleOption.Zero;
        }
    }
}
