using System;
using UnityEngine;


namespace MajdataPlay.IO
{
    public static partial class OutputManager
    {
        static byte _cabinetLightBrightness = 255;
        readonly static Color[] _ledRingColors = new Color[8];
        readonly static object _ledStateLock = new();

        
        public static void Init()
        {
#if UNITY_STANDALONE
            lock (_ledStateLock)
            {
                LedDevice.Init();
            }
#endif
        }
        public static void SetLedRingColorData(ReadOnlySpan<Color> colors)
        {
            lock (_ledStateLock)
            {
                colors.CopyTo(_ledRingColors);
#if UNITY_STANDALONE
                LedDevice.WriteLeds(_ledRingColors, _cabinetLightBrightness);
#endif
            }
        }
        public static void SetCabinetLightBrightness(float brightness)
        {
            lock (_ledStateLock)
            {
                _cabinetLightBrightness = (byte)Mathf.RoundToInt(Mathf.Clamp01(brightness) * 255f);
#if UNITY_STANDALONE
                LedDevice.WriteLeds(_ledRingColors, _cabinetLightBrightness);
#endif
            }
        }
    }
}
