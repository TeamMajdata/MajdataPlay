#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using MajdataPlay.Settings;
using TouchUpdate = MajdataPlay.IO.TouchPanelState.TouchUpdate;

namespace MajdataPlay.IO
{
    /// <summary>
    /// FLTouch 独占触摸。需要先发 Feature Report 切到多点输入模式才能拿到完整触点。
    /// </summary>
    internal sealed class FlTouchDevice : UsbDevice, ITouchPanelDevice
    {
        private const ushort Vid = 0x227D;
        private const ushort Pid = 0x0103;
        private const byte ReportId = 2;
        private const int SlotStart = 2;
        private const int SlotSize = 10;
        private const int SlotsPerReport = 6;
        private static readonly byte[] MultipleInputModeReport = { 0x04, 0x02, 0x00 };

        // 一帧超过 6 个点时会拆成多个报告连续发来，只有首个报告带总数
        private int _remaining;
        private readonly List<TouchUpdate> _pendingUpdates = new(SlotsPerReport * 2);
        private readonly List<TouchUpdate> _releaseUpdates = new(SlotsPerReport);
        private readonly object _reportLock = new();
        private readonly TouchPanelState _touch;

        public FlTouchDevice(string identifier, int radius, CapacitiveTouchPanelRadiusOffsetConfig radiusOffset)
            : base(Vid, Pid, identifier, packetSize: 64)
        {
            _touch = new TouchPanelState(
                minX: 18432, minY: 0, maxX: 0, maxY: 32767, flip: true, radius,
                radiusOffset.A, radiusOffset.B, radiusOffset.C, radiusOffset.D, radiusOffset.E,
                timeoutMilliseconds: 100);
        }

        protected override string DiagnosticName => "FL";

        protected override UsbEndpoint ResolveEndpoint(WinUsbIo.DevicePath devicePath)
            => devicePath.InterfaceNumber == 0
                ? new UsbEndpoint(0, 0x81)
                : throw new InvalidOperationException($"FLTouch 设备接口号不支持: {devicePath.InterfaceNumber}");

        protected override void InitializeDevice(WinUsbIo.Device device)
        {
            // 按 Windows HID 顺序先查询 Feature Report 3，再切到多点输入模式
            var reportInfo = new byte[2];
            if (!device.ControlTransfer(0xA1, 0x01, 0x0303, 0, reportInfo, 0, reportInfo.Length,
                    out var reportInfoLength) ||
                reportInfoLength != reportInfo.Length || reportInfo[0] != 0x03)
            {
                throw new InvalidOperationException("FLTouch 能力报告查询失败");
            }

            if (!device.ControlTransfer(0x21, 0x09, 0x0304, 0, MultipleInputModeReport, 0,
                    MultipleInputModeReport.Length, out var lengthTransferred) ||
                lengthTransferred != MultipleInputModeReport.Length)
            {
                throw new InvalidOperationException("FLTouch 多点输入模式设置失败");
            }
        }

        protected override void OnDeviceConnected()
        {
            lock (_reportLock)
            {
                ResetFrameState();
            }
        }

        protected override void OnDisconnected()
        {
            lock (_reportLock)
            {
                ResetFrameState();
                _touch.Clear();
            }
        }

        public override void OnPreUpdate() => _touch.OnPreUpdate();
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _touch.ReadTouchPanel(states, hadOn, hadOff);
        public bool IsSensorCurrentlyOn(int index) => _touch.IsSensorCurrentlyOn(index);

        private void ResetFrameState()
        {
            _remaining = 0;
            _pendingUpdates.Clear();
            _releaseUpdates.Clear();
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < 62) return;
            lock (_reportLock)
            {
                OnTouchDataCore(data);
            }
        }

        private void OnTouchDataCore(ReadOnlySpan<byte> data)
        {
            if (data[0] != ReportId) return;

            int count = data[1];
            if (count > 0)
            {
                _pendingUpdates.Clear();
                // 新帧的帧头。上一帧没收满就丢了，这里直接重置
                _remaining = count;
            }
            else if (_remaining <= 0)
            {
                // 从帧中间开始读，没有帧头，只能丢
                return;
            }

            // 剩余数量之外的槽里是上一个报告的残留数据，不清零，读了会变成幻影触摸
            int take = Math.Min(_remaining, SlotsPerReport);
            _releaseUpdates.Clear();
            for (int i = 0; i < take; i++)
            {
                var index = SlotStart + i * SlotSize;
                var fingerId = data[index + 1];
                ushort x = BitConverter.ToUInt16(data.Slice(index + 2, 2));
                ushort y = BitConverter.ToUInt16(data.Slice(index + 4, 2));
                ushort w = BitConverter.ToUInt16(data.Slice(index + 6, 2));
                ushort h = BitConverter.ToUInt16(data.Slice(index + 8, 2));

                // 一次触摸的状态序列是 04(有面积) -> 07 -> 04(面积归零) -> 00，
                // Tip Switch 位只在 07 出现。用面积判定比等 Tip Switch 早一帧
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
                _touch.HandleReleases(_releaseUpdates);
            }

            if (_remaining == 0)
            {
                _touch.HandleFrame(_pendingUpdates);
                _pendingUpdates.Clear();
            }
        }
    }
}
#endif
