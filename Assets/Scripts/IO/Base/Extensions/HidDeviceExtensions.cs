#if UNITY_STANDALONE
using HidSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MajdataPlay.IO
{
    internal static class HidDeviceExtensions
    {
        readonly static Regex _deviceInterfaceRegex = new Regex("&mi_([0-9A-Fa-f]{2})");
        public static int GetInterfaceIndex(this HidSharp.HidDevice device)
        {
            var matchResult = _deviceInterfaceRegex.Match(device.DevicePath);
            try
            {
                if (matchResult.Success && int.TryParse(matchResult.Groups[1].Value, NumberStyles.HexNumber, null, out var interfaceIndex))
                {
                    return interfaceIndex;
                }
                else
                {
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

    }
}
#endif
