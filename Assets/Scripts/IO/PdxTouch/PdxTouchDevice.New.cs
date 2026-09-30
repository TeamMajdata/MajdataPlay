#if UNITY_STANDALONE_WIN
using System;

namespace MajdataPlay.IO
{
    internal sealed partial class PdxTouchDevice
    {
        private const int NewSlotCount = 10;
        private const int NewSlotSize = 6;

        private void OnNewTouchData(byte[] data)
        {
            if (data[0] != ReportId) return;

            for (var i = 0; i < NewSlotCount; i++)
            {
                var index = i * NewSlotSize + 1;
                if (data[index] == 0) continue;

                var isPressed = (data[index] & 0x01) == 1;
                var fingerId = data[index + 1];
                var x = BitConverter.ToUInt16(data, index + 2);
                var y = BitConverter.ToUInt16(data, index + 4);
                HandleFinger(x, y, fingerId, isPressed);
            }
        }
    }
}
#endif
