#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
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
        private const int MaxContacts = 40;
        private const int SlotsPerReport = 6;

        private enum PdxKind
        {
            New,
            OldAt32,
        }

        private PdxKind _kind;
        private int _remaining;
        private readonly List<TouchUpdate> _pendingUpdates = new(MaxContacts);
        private readonly List<TouchUpdate> _releaseUpdates = new(SlotsPerReport);
        private readonly object _reportLock = new();

        public PdxTouchDevice(ushort vid, ushort pid, string identifier, int radius,
            CapacitiveTouchPanelRadiusOffsetConfig radiusOffset)
            : base(vid, pid, identifier, packetSize: 64,
                minX: 18432, minY: 0, maxX: 0, maxY: 32767, flip: true, radius,
                radiusOffset.A, radiusOffset.B, radiusOffset.C, radiusOffset.D, radiusOffset.E)
        { }

        protected override string DiagnosticName => _kind == PdxKind.OldAt32 ? "PDX-AT32" : "PDX";

        protected override TouchEndpoint ResolveEndpoint(WinUsbIo.DevicePath devicePath)
        {
            // Ep02 是新款 PDX，Ep01 是 AT32 老款 PDX
            if (devicePath.InterfaceNumber == 1)
            {
                _kind = PdxKind.New;
                return new TouchEndpoint(1, 0x82);
            }

            if (devicePath.InterfaceNumber == 0)
            {
                _kind = PdxKind.OldAt32;
                return new TouchEndpoint(0, 0x81);
            }

            throw new InvalidOperationException($"PDX 设备接口号不支持: {devicePath.InterfaceNumber}");
        }

        protected override void OnDeviceConnected()
        {
            lock (_reportLock)
            {
                ResetFrameState();
            }
        }

        protected override void OnDeviceDisconnected()
        {
            lock (_reportLock)
            {
                ResetFrameState();
            }
        }

        private void ResetFrameState()
        {
            _remaining = 0;
            _pendingUpdates.Clear();
            _releaseUpdates.Clear();
        }

        protected override void OnTouchData(byte[] data)
        {
            lock (_reportLock)
            {
                switch (_kind)
                {
                    case PdxKind.New:
                        OnNewTouchData(data);
                        break;
                    case PdxKind.OldAt32:
                        OnOldTouchData(data);
                        break;
                    default:
                        throw new InvalidOperationException($"不支持的 PDX 类型: {_kind}");
                }
            }
        }
    }
}
#endif
