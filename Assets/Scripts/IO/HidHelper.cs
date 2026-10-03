#if UNITY_STANDALONE
using HidSharp;
using MajdataPlay.Collections;
using MajdataPlay.Extensions;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MajdataPlay.Settings;
using MajdataPlay.Diagnostics;
using MajdataPlay.Buffers;

#nullable enable
namespace MajdataPlay.IO
{

    internal static class HidHelper
    {
        public static IEnumerable<HidSharp.HidDevice> Devices
        {
            get
            {
                return DeviceList.Local.GetHidDevices();
            }
        }
        public static bool TryGetDevices(DeviceFilter filter, [NotNullWhen(true)] out IEnumerable<HidSharp.HidDevice> devices)
        {
            try
            {
                var pid = filter.ProductId;
                var vid = filter.VendorId;
                var deviceName = filter.DeviceName;
                var result = new PooledList<HidSharp.HidDevice>();

                foreach (var d in Devices)
                {
                    if (pid == d.ProductID && vid == d.VendorID)
                    {
                        var isMatch = false;
                        if (!string.IsNullOrEmpty(deviceName))
                        {
                            if ($"{d.GetManufacturer()} {d.GetProductName()}" == deviceName)
                            {
                                isMatch = true;
                            }
                        }
                        else
                        {
                            isMatch = true;
                        }
                        if (isMatch)
                        {
                            result.Add(d);
                        }
                    }
                }
                if (result.Count != 0)
                {
                    devices = result.ToArray()
                                    .OrderBy(x => x.GetInterfaceIndex());
                    return true;
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }

            devices = Array.Empty<HidSharp.HidDevice>();
            return false;
        }
        public static bool TryGetAndOpenDevice(
            string tag,
            DeviceFilter filter,
            OpenConfiguration hidConfig,
            [NotNullWhen(true)] out HidSharp.HidDevice? hidDevice,
            [NotNullWhen(true)] out HidStream? hidStream,
            bool isReconnecting)
        {
            hidDevice = default;
            hidStream = default;
            if (!HidHelper.TryGetDevices(filter, out var devices))
            {
                if (isReconnecting)
                {
                    MajDebug.LogError(tag, $"HID device was lost, waiting for device to reconnect");
                    return false;
                }
                else
                {
                    MajDebug.LogWarning(tag, "HID device not found");
                    return false;
                }
            }
            foreach (var d in devices)
            {
                MajDebug.LogInfo(tag, $"Trying to open HID device...\nDevice: {d}");
                if (d.TryOpen(hidConfig, out hidStream))
                {
                    hidDevice = d;
                    break;
                }
                else
                {
                    MajDebug.LogError(tag, $"cannot open HID devices:\nDevice: {d}");
                }
            }
            if (hidStream is null || hidDevice is null)
            {
                MajDebug.LogError(tag, $"No HID devices available");
                hidDevice = default;
                hidStream = default;
                if (isReconnecting)
                {
                    return false;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
#endif
