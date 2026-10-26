using System;
using UnityEngine;

namespace MajdataPlay.IO
{
    internal interface ILedDevice : IGameDevice
    {
        /// <summary>Buffers eight ring colors and cabinet brightness for the device's I/O worker.</summary>
        void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness);
    }
}
