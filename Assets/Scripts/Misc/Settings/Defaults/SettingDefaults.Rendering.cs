#nullable enable
namespace MajdataPlay.Settings
{
    public static partial class SettingDefaults
    {
        public static class Rendering
        {
            public const RenderingQualityOption RenderQuality = RenderingQualityOption.Performance;

            public const int FPSLimit = 120;

            public const bool VSync = true;

            public const bool VulkanLowLatencyVSync = true;

            public const LowLatencyVSyncBackendOption VulkanLowLatencyVSyncBackend = LowLatencyVSyncBackendOption.Auto;

            public const RenderingAntialiasingOption AntiAliasing = RenderingAntialiasingOption.Disabled;

            public const RenderingTextureQualityOption TextureQuality = RenderingTextureQualityOption.High;

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
