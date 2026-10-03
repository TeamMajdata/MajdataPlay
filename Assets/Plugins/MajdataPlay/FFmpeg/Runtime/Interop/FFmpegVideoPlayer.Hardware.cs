using System;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.FFmpeg
{
    public sealed partial class FFmpegVideoPlayer
    {
        HardwareVideoPresenter _hardwarePresenter;
        partial void CheckHardwareErrors() => _hardwarePresenter?.CheckErrors();
        partial void ConfigureHardware(DecoderOptions options, bool preferNative)
        {
            var graphics = SystemInfo.graphicsDeviceType;
            bool d3d12 = !_platformBackendOnly && graphics == GraphicsDeviceType.Direct3D12 && HardwareVideoPresenter.SupportsD3D12Video;
            bool vulkan = !_platformBackendOnly && graphics == GraphicsDeviceType.Vulkan && HardwareVideoPresenter.SupportsVulkanVideoDecoding;
            ConfigurePlatformHardware(options, preferNative, !d3d12 && !vulkan);
            if (!d3d12 && !vulkan)
            {
                if (!_platformBackendOnly && (graphics == GraphicsDeviceType.Direct3D12 || graphics == GraphicsDeviceType.Vulkan))
                    MajDebug.LogInfo("FFmpeg", "[Interop] Native video backend unavailable: " + HardwareVideoPresenter.DescribeNativeBackendStatus(graphics) +
                        "; using " + options.HardwareDeviceType + ".");
                return;
            }

            options.FallbackHardwareOptions = options.Copy();
            options.HardwareDeviceType = d3d12 ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA : AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN;
            options.AcquireHardwareDevice = d3d12 ? HardwareVideoPresenter.AcquireD3D12Device : VulkanVideoInterop.AcquireVideoDevice;
            options.AcquireD3D11Device = null;
            options.MapHardwareFrame = null;
            options.CreateHardwareSession = null;
            options.KeepNativeFrames = preferNative;
            options.HardwareDeviceDescription = (d3d12 ? "D3D12VA" : "Vulkan Video") + " device shared with Unity renderer: " +
                SystemInfo.graphicsDeviceName + "; driver=" + SystemInfo.graphicsDeviceVersion;
            MajDebug.LogInfo("FFmpeg", "[Interop] Preferred native decoder=" + options.HardwareDeviceType +
                "; fallback=" + options.FallbackHardwareOptions.HardwareDeviceType + "; native textures=" + preferNative + ".");
        }

        void ConfigurePlatformHardware(DecoderOptions options, bool preferNative, bool report)
        {
            var platform = Application.platform;
            bool windows = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
            bool linux = platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;
            bool apple = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.IPhonePlayer;
            options.HardwareDeviceType = windows ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA :
                linux ? AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI :
                apple ? AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX :
                platform == RuntimePlatform.Android ? AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC : AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            string graphics = SystemInfo.graphicsDeviceType + "; GPU=" + SystemInfo.graphicsDeviceName +
                "; vendor=" + SystemInfo.graphicsDeviceVendor + "; device ID=0x" + SystemInfo.graphicsDeviceID.ToString("X") +
                "; driver=" + SystemInfo.graphicsDeviceVersion;
            // A standalone decode device may differ from Unity's renderer on multi-GPU systems.
            options.HardwareDeviceDescription = "Default " + options.HardwareDeviceType + " decode device; Unity renderer: " + graphics;
            if (platform == RuntimePlatform.Android)
            {
                try { VulkanVideoInterop.InitializeAndroid(); }
                catch (Exception error) when (!options.RequireHardwareDecoding || !report)
                {
                    if (report) HardwareFallbackReason = error.Message;
                    options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                    if (report) MajDebug.LogWarning("FFmpeg", "[Interop] MediaCodec initialization failed; using software decoding. " + error.Message);
                    return;
                }
            }
            if (preferNative)
            {
                if (windows && HardwareVideoPresenter.SupportsD3D11)
                {
                    options.AcquireD3D11Device = HardwareVideoPresenter.AcquireD3D11Device;
                    options.KeepNativeFrames = true;
                    options.HardwareDeviceDescription = "D3D11VA device matched to Unity renderer: " + graphics;
                }
                else if (apple && HardwareVideoPresenter.SupportsMetal)
                    options.KeepNativeFrames = true;
                else if (linux && HardwareVideoPresenter.SupportsLinuxVulkan)
                {
                    options.AcquireHardwareDevice = VulkanVideoInterop.AcquireLinuxDevice;
                    options.MapHardwareFrame = VulkanVideoInterop.MapLinuxFrame;
                    options.KeepNativeFrames = true;
                    options.HardwareDeviceDescription = "VAAPI DRM render node matched to Unity Vulkan renderer: " + graphics;
                }
                else if (platform == RuntimePlatform.Android && HardwareVideoPresenter.SupportsAndroidVulkan)
                {
                    options.CreateHardwareSession = VulkanVideoInterop.CreateAndroidSession;
                    options.KeepNativeFrames = true;
                }
                else
                {
                    if (report) HardwareFallbackReason = HardwareVideoPresenter.AvailabilityReason ?? "Native texture transport is unavailable.";
                    if (report && !options.RequireHardwareDecoding)
                        MajDebug.LogWarning("FFmpeg", "[Interop] GPU texture sharing unavailable; attempting hardware decoding with CPU upload. " + HardwareFallbackReason);
                }
            }
            if (report) MajDebug.LogInfo("FFmpeg", "[Interop] Requested hardware API=" + options.HardwareDeviceType +
                "; native textures=" + options.KeepNativeFrames + "; " + options.HardwareDeviceDescription + ".");
        }

        partial void PresentHardware(DecodedVideoFrame frame, ref Texture output)
        {
            if (!frame.IsHardwareFrame) return;
            using var profile = UnityProfiler.Create("FFmpeg.Interop.PresentHardware");
            _hardwarePresenter ??= new HardwareVideoPresenter();
            if (!_hardwarePresenter.TryPresent(frame, out output))
                throw new NotSupportedException("The decoded surface cannot be shared safely with the active Unity graphics device.");
            TransferMode = _hardwarePresenter.TransferMode;
        }

        partial void ReleasePresentation()
        {
            _hardwarePresenter?.Dispose();
            _hardwarePresenter = null;
        }
    }
}
