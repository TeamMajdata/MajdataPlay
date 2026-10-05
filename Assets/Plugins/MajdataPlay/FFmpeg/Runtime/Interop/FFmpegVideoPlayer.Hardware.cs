#nullable enable
using System;
using FFmpeg.AutoGen;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Configures platform hardware decoding and owns native GPU presentation for the player.</summary>
    public sealed partial class FFmpegVideoPlayer
    {
        /// <summary>Owns the optional native GPU frame presenter.</summary>
        private HardwareVideoPresenter? _hardwarePresenter;
        /// <summary>Checks the active presenter for asynchronous graphics failures.</summary>
        private partial void CheckHardwareErrors() => _hardwarePresenter?.CheckErrors();
        /// <summary>Configures the preferred native decoding backend and its platform fallback.</summary>
        /// <param name="options">The resource limits and hardware configuration to use.</param>
        /// <param name="preferNative">Whether to request native GPU frame sharing instead of CPU pixel upload.</param>
        private partial void ConfigureHardware(DecoderOptions options, bool preferNative)
        {
            var graphics = SystemInfo.graphicsDeviceType;
            bool d3d12 = !_platformBackendOnly && graphics == GraphicsDeviceType.Direct3D12 && HardwareVideoPresenter.SupportsD3D12Video;
            bool vulkan = !_platformBackendOnly && graphics == GraphicsDeviceType.Vulkan && HardwareVideoPresenter.SupportsVulkanVideoDecoding;
            ConfigurePlatformHardware(options, preferNative, !d3d12 && !vulkan);
            if (!d3d12 && !vulkan)
            {
                if (!_platformBackendOnly && (graphics == GraphicsDeviceType.Direct3D12 || graphics == GraphicsDeviceType.Vulkan))
                {
                    MajDebug.LogInfo("FFmpeg", "[Interop] Native video backend unavailable: "
                        + HardwareVideoPresenter.DescribeNativeBackendStatus(graphics) + "; using " + options.HardwareDeviceType
                        + ".");
                }

                return;
            }

            options.FallbackHardwareOptions = options.Copy();
            options.HardwareDeviceType = d3d12 ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA : AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN;
            options.AcquireHardwareDevice = d3d12 ? HardwareVideoPresenter.AcquireD3D12Device : VulkanVideoInterop.AcquireVideoDevice;
            options.AcquireD3D11Device = null;
            options.MapHardwareFrame = null;
            options.CreateHardwareSession = null;
            options.KeepNativeFrames = preferNative;
            options.HardwareDeviceDescription = (d3d12 ? "D3D12VA" : "Vulkan Video") + " device shared with Unity renderer: "
                + SystemInfo.graphicsDeviceName + "; driver=" + SystemInfo.graphicsDeviceVersion;
            MajDebug.LogInfo("FFmpeg", "[Interop] Preferred native decoder=" + options.HardwareDeviceType
                + "; fallback=" + options.FallbackHardwareOptions.HardwareDeviceType + "; native textures="
                + preferNative + ".");
        }

        /// <summary>Configures platform decoding and optional sharing with Unity's graphics device.</summary>
        /// <param name="options">The resource limits and hardware configuration to use.</param>
        /// <param name="preferNative">Whether to request native GPU frame sharing instead of CPU pixel upload.</param>
        /// <param name="report">Whether to publish fallback diagnostics for this backend selection.</param>
        private void ConfigurePlatformHardware(DecoderOptions options, bool preferNative, bool report)
        {
            var platform = Application.platform;
            bool windows = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
            bool linux = platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;
            bool apple = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.IPhonePlayer;
            options.HardwareDeviceType = windows ? AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA
                : linux ? AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI : apple ? AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX
                : platform == RuntimePlatform.Android ? AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC : AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            string graphics = SystemInfo.graphicsDeviceType + "; GPU=" + SystemInfo.graphicsDeviceName
                + "; vendor=" + SystemInfo.graphicsDeviceVendor + "; device ID=0x" + SystemInfo.graphicsDeviceID.ToString("X")
                + "; driver=" + SystemInfo.graphicsDeviceVersion;
            // A standalone decode device may differ from Unity's renderer on multi-GPU systems.
            options.HardwareDeviceDescription = "Default " + options.HardwareDeviceType + " decode device; Unity renderer: " + graphics;
            if (platform == RuntimePlatform.Android)
            {
                try
                {
                    VulkanVideoInterop.InitializeAndroid();
                }
                catch (Exception error) when (!options.RequireHardwareDecoding || !report)
                {
                    if (report)
                    {
                        HardwareFallbackReason = error.Message;
                    }

                    options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                    if (report)
                    {
                        MajDebug.LogWarning("FFmpeg", "[Interop] MediaCodec initialization failed; using software decoding. " + error.Message);
                    }

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
                {
                    options.KeepNativeFrames = true;
                }
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
                    if (report)
                    {
                        HardwareFallbackReason = HardwareVideoPresenter.AvailabilityReason ?? "Native texture transport is unavailable.";
                    }

                    if (report && !options.RequireHardwareDecoding)
                    {
                        MajDebug.LogWarning("FFmpeg", "[Interop] GPU texture sharing unavailable; attempting hardware decoding with CPU upload. " + HardwareFallbackReason);
                    }
                }
            }

            if (report)
            {
                MajDebug.LogInfo("FFmpeg", "[Interop] Requested hardware API=" + options.HardwareDeviceType
                    + "; native textures=" + options.KeepNativeFrames + "; " + options.HardwareDeviceDescription
                    + ".");
            }
        }

        /// <summary>Presents a native frame and updates the reported GPU transfer mode.</summary>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <param name="output">Receives the presented texture when the frame uses native GPU resources.</param>
        /// <exception cref="NotSupportedException">The requested dimensions, codec, platform, or native transport cannot be supported.</exception>
        private partial void PresentHardware(DecodedVideoFrame frame, ref Texture? output)
        {
            if (!frame.IsHardwareFrame)
            {
                return;
            }

            using var profile = UnityProfiler.Create("FFmpeg.Interop.PresentHardware");
            _hardwarePresenter ??= new HardwareVideoPresenter();
            if (!_hardwarePresenter.TryPresent(frame, out output))
            {
                throw new NotSupportedException("The decoded surface cannot be shared safely with the active Unity graphics device.");
            }

            // Successful presentation always publishes its transfer description.
            TransferMode = _hardwarePresenter.TransferMode!;
        }

        /// <summary>Disposes the hardware presenter and clears its reference.</summary>
        private partial void ReleasePresentation()
        {
            _hardwarePresenter?.Dispose();
            _hardwarePresenter = null;
        }
    }
}
