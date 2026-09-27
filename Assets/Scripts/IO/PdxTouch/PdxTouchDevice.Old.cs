#if UNITY_STANDALONE_WIN
using System;

namespace MajdataPlay.IO
{
    internal sealed partial class PdxTouchDevice
    {
        private const int OldSlotStart = 1;
        private const int OldSlotSize = 10;
        private const int OldCountIndex = 61;

        private void OnOldTouchData(byte[] data)
        {
            if (data[0] != ReportId) return;

            // 一帧超过 6 个点时会拆成多个报告连续发来，只有首个报告带总数
            int count = data[OldCountIndex];
            if (count > 0)
            {
                _pendingUpdates.Clear();
                _remaining = count;
            }
            else if (_remaining <= 0)
            {
                // 从帧中间开始读，没有帧头，只能丢
                return;
            }

            int take = Math.Min(_remaining, SlotsPerReport);
            _releaseUpdates.Clear();
            for (int i = 0; i < take; i++)
            {
                var index = OldSlotStart + i * OldSlotSize;
                var fingerId = data[index + 1];
                ushort x = BitConverter.ToUInt16(data, index + 2);
                ushort y = BitConverter.ToUInt16(data, index + 4);
                ushort w = BitConverter.ToUInt16(data, index + 6);
                ushort h = BitConverter.ToUInt16(data, index + 8);

                // 旧 PDX 首帧 status=04 也带面积，用面积判定比 Tip Switch 早一帧
                bool isPressed = w > 0 || h > 0;
                var update = new TouchUpdate(x, y, fingerId, isPressed);
                _pendingUpdates.Add(update);
                if (!isPressed)
                {
                    _releaseUpdates.Add(update);
                }
            }

            _remaining -= take;
            if (_releaseUpdates.Count > 0)
            {
                HandleReleases(_releaseUpdates);
            }

            if (_remaining == 0)
            {
                HandleFrame(_pendingUpdates);
                _pendingUpdates.Clear();
            }
        }
    }
}
#endif
