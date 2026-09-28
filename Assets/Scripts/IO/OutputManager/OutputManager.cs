using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay.IO
{
    public static partial class OutputManager
    {
        static byte _cabinetLightBrightness = 255;
        readonly static Color[] _ledRingColors = new Color[8];

        
        public static void Init()
        {
#if UNITY_STANDALONE
            LedDevice.Init();
#endif
        }
        public static void SetLedRingColorData(ReadOnlySpan<Color> colors)
        {
            colors.CopyTo(_ledRingColors);
        }
        public static void SetCabinetLightBrightness(float brightness)
        {
            _cabinetLightBrightness = (byte)Mathf.RoundToInt(Mathf.Clamp01(brightness) * 255f);
#if UNITY_ANDROID && !UNITY_EDITOR
            OniimaiController.SubmitLights(_ledRingColors, _cabinetLightBrightness);
#endif
        }
    }
}
