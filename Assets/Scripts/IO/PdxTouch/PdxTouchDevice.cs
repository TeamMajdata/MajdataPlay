#if UNITY_STANDALONE_WIN
using System;
using MajdataPlay.Settings;

namespace MajdataPlay.IO
{
    /// <summary>
    /// PDX 独占触摸。新老两款主控板共用 VID/PID，按 USB 接口号区分：
    /// 接口 1 / Ep02 为新款，接口 0 / Ep01 为 AT32 老款。
    /// </summary>
    internal sealed partial class PdxTouchDevice : ExclusiveTouchBase
    {
        private const byte ReportId = 2;
        private const int NewSlotCount = 10;
        private const int NewSlotSize = 6;

        public PdxTouchDevice(ushort vid, ushort pid, string identifier, int radius,
            CapacitiveTouchPanelRadiusOffsetConfig radiusOffset)
            : base(vid, pid, identifier, packetSize: 64,
                minX: 18432, minY: 0, maxX: 0, maxY: 32767, flip: true, radius,
                radiusOffset.A, radiusOffset.B, radiusOffset.C, radiusOffset.D, radiusOffset.E)
        { }

        protected override string DiagnosticName => "PDX";

        protected override TouchEndpoint ResolveEndpoint(WinUsbIo.DevicePath devicePath)
        {
            // Ep02 是新款 PDX，Ep01 是 AT32 老款 PDX
            if (devicePath.InterfaceNumber == 1)
            {
                return new TouchEndpoint(1, 0x82);
            }

            throw new InvalidOperationException($"PDX 设备接口号不支持: {devicePath.InterfaceNumber}");
        }

        protected override void OnTouchData(byte[] data)
        {
            OnNewTouchData(data);
        }
    }
}
#endif
