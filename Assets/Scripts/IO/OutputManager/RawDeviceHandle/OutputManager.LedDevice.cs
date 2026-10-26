#if UNITY_STANDALONE
using System;
using UnityEngine;

#nullable enable
namespace MajdataPlay.IO
{
    public static partial class OutputManager
    {
        static class LedDevice
        {
            static ILedDevice? _device;

            public static bool IsConnected => _device?.IsConnected ?? false;

            public static void Init()
            {
                GameDeviceManager.Init();
                _device = GameDeviceManager.LedDevice;
                _device?.WriteLeds(_ledRingColors, _cabinetLightBrightness);
            }

            public static void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
            {
                _device?.WriteLeds(colors, cabinetBrightness);
            }
        }
    }
}
#endif
