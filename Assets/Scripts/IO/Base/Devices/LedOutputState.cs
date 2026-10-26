#if UNITY_STANDALONE
using System;
using UnityEngine;

namespace MajdataPlay.IO
{
    /// <summary>Transfers a complete output snapshot from the game thread to an I/O worker.</summary>
    internal sealed class LedOutputState
    {
        readonly object _sync = new();
        readonly Color[] _colors = new Color[8];
        byte _cabinetBrightness = byte.MaxValue;

        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            lock (_sync)
            {
                colors.CopyTo(_colors);
                _cabinetBrightness = cabinetBrightness;
            }
        }

        public void ReadLeds(Span<Color> colors, out byte cabinetBrightness)
        {
            lock (_sync)
            {
                _colors.AsSpan().CopyTo(colors);
                cabinetBrightness = _cabinetBrightness;
            }
        }
    }
}
#endif
