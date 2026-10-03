using System;
using FFmpeg.AutoGen;
using MajdataPlay.Video.Internal;
using MajdataPlay.Video.Interop;
using UnityEngine;

namespace MajdataPlay.Video
{
    public sealed partial class FFmpegVideoPlayer
    {
        HardwareVideoPresenter _hardwarePresenter;
        partial void CheckHardwareErrors() => _hardwarePresenter?.CheckErrors();
        partial void ConfigureHardware(DecoderOptions options)
        {
            if (!_preferHardwareDecoding && !options.RequireHardwareDecoding) return;
            if (HardwareVideoPresenter.SupportsD3D11)
            {
                options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
                options.AcquireD3D11Device = HardwareVideoPresenter.AcquireD3D11Device;
            }
            else if (HardwareVideoPresenter.SupportsMetal)
                options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
            else if (HardwareVideoPresenter.SupportsLinuxVulkan)
            {
                options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI;
                options.AcquireHardwareDevice = VulkanVideoInterop.AcquireLinuxDevice;
                options.MapHardwareFrame = VulkanVideoInterop.MapLinuxFrame;
            }
            else if (HardwareVideoPresenter.SupportsAndroidVulkan)
            {
                VulkanVideoInterop.InitializeAndroid();
                options.HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC;
                options.CreateHardwareSession = VulkanVideoInterop.CreateAndroidSession;
            }
            else
            {
                HardwareFallbackReason = HardwareVideoPresenter.AvailabilityReason;
                return;
            }
            options.KeepNativeFrames = true;
        }

        partial void PresentHardware(DecodedVideoFrame frame, ref Texture output)
        {
            if (!frame.IsHardwareFrame) return;
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
