#if UNITY_STANDALONE
using System;
using System.IO;
using System.Threading;
using MajdataPlay.Settings;
using UnityEngine;

namespace MajdataPlay.IO
{
    internal sealed class RosenLedDevice : SerialDevice, ILedDevice
    {
        static readonly byte[] UpdatePacket = { 0xE0, 0x11, 0x01, 0x01, 0x3C, 0x4F };
        readonly LedOutputState _output = new();
        readonly Color[] _lastColors = new Color[8];
        readonly bool _throttler;
        readonly float _brightness;
        readonly TimeSpan _pollingInterval;
        bool _forceUpdate = true;

        protected override string DaemonThreadName => "IO/Led Thread";
        protected override TimeSpan PollingInterval => _pollingInterval;

        public RosenLedDevice() : base(IODetector.LedDeviceSerialConnInfo)
        {
            var options = MajEnv.Settings.IO.OutputDevice.Led;
            _brightness = Mathf.Clamp01(options.Brightness);
            _throttler = options.Throttler;
            var refreshRate = Math.Max(0, options.RefreshRateMs);
            if (IODetector.DeviceManufacturer == DeviceManufacturerOption.Yuan)
            {
                refreshRate = Math.Max(100, refreshRate);
            }
            _pollingInterval = TimeSpan.FromMilliseconds(refreshRate);
        }

        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            _output.WriteLeds(colors, cabinetBrightness);
        }

        protected override void OnConnected(CancellationToken token)
        {
            base.OnConnected(token);
            _forceUpdate = true;
        }

        protected override void Update(CancellationToken token)
        {
            var stream = Stream ?? throw new IOException("LED serial device is disconnected.");
            Span<Color> colors = stackalloc Color[8];
            Span<byte> packet = stackalloc byte[10];
            _output.ReadLeds(colors, out _);
            var needUpdate = false;
            for (var i = 0; i < colors.Length; i++)
            {
                if (!_forceUpdate && _throttler && _lastColors[i] == colors[i])
                {
                    continue;
                }
                stream.Write(BuildSetColorPacket(packet, i, colors[i], _brightness));
                needUpdate = true;
            }
            if (needUpdate)
            {
                stream.Write(UpdatePacket);
                colors.CopyTo(_lastColors);
            }
            _forceUpdate = false;
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            // This protocol only writes lighting commands; it has no input reports.
        }

        internal static ReadOnlySpan<byte> BuildSetColorPacket(Span<byte> packet, int index, Color color, float brightness)
        {
            packet[0] = 0xE0;
            packet[1] = 0x11;
            packet[2] = 0x01;
            packet[3] = 0x05;
            packet[4] = 0x31;
            packet[5] = (byte)index;
            packet[6] = (byte)(color.r * 255 * brightness);
            packet[7] = (byte)(color.g * 255 * brightness);
            packet[8] = (byte)(color.b * 255 * brightness);
            byte checksum = 0;
            for (var i = 1; i < 9; i++)
            {
                checksum += packet[i];
            }
            packet[9] = checksum;
            return packet.Slice(0, 10);
        }
    }
}
#endif
