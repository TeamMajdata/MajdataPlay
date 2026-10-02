#if UNITY_STANDALONE
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HidSharp;
using UnityEngine;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>LED protocol shared by combined Dao input/output devices and output-only devices.</summary>
    internal sealed class DaoLedOutput
    {
        readonly LedOutputState _output = new();
        readonly Color[] _lastColors = new Color[8];
        readonly bool _throttler;
        readonly float _brightness;
        readonly long _refreshTicks;
        HidSharp.HidDevice? _reportDevice;
        byte[] _packet = Array.Empty<byte>();
        byte _reportId;
        byte _lastCabinetBrightness;
        bool _forceUpdate = true;
        long _nextUpdate;

        public DaoLedOutput()
        {
            var options = MajEnv.Settings.IO.OutputDevice.Led;
            _throttler = options.Throttler;
            _brightness = Mathf.Clamp01(options.Brightness);
            _refreshTicks = (long)(Math.Max(0, options.RefreshRateMs) * (double)Stopwatch.Frequency / 1000);
        }

        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            _output.WriteLeds(colors, cabinetBrightness);
        }

        public void Reset()
        {
            _forceUpdate = true;
            _reportDevice = null;
            _nextUpdate = 0;
        }

        public void WriteTo(HidStream stream, HidSharp.HidDevice device)
        {
            var now = Stopwatch.GetTimestamp();
            if (!_forceUpdate && now < _nextUpdate)
            {
                return;
            }
            if (!ReferenceEquals(_reportDevice, device))
            {
                var reportLength = device.GetMaxOutputReportLength();
                if (reportLength < 26)
                {
                    throw new InvalidDataException($"Dao LED output report is too short: {reportLength}.");
                }
                _packet = new byte[reportLength];
                _reportId = device.GetReportDescriptor().OutputReports.FirstOrDefault()?.ReportID ?? 0;
                _reportDevice = device;
                _forceUpdate = true;
            }

            Span<Color> colors = stackalloc Color[8];
            _output.ReadLeds(colors, out var cabinetBrightness);
            var needUpdate = _forceUpdate || !_throttler || cabinetBrightness != _lastCabinetBrightness;
            for (var i = 0; i < colors.Length && !needUpdate; i++)
            {
                needUpdate = colors[i] != _lastColors[i];
            }
            if (needUpdate)
            {
                stream.Write(BuildUpdatePacket(_packet, _reportId, colors, cabinetBrightness, _brightness));
                colors.CopyTo(_lastColors);
                _lastCabinetBrightness = cabinetBrightness;
                _forceUpdate = false;
            }
            _nextUpdate = now + _refreshTicks;
        }

        internal static ReadOnlySpan<byte> BuildUpdatePacket(Span<byte> packet, byte reportId, ReadOnlySpan<Color> colors, byte cabinetBrightness, float brightness)
        {
            packet.Clear();
            packet[0] = reportId;
            for (var i = 0; i < 8; i++)
            {
                var color = colors[i];
                var offset = 1 + i * 3;
                packet[offset] = (byte)(color.r * 255 * brightness);
                packet[offset + 1] = (byte)(color.g * 255 * brightness);
                packet[offset + 2] = (byte)(color.b * 255 * brightness);
            }
            packet[25] = (byte)(cabinetBrightness * brightness);
            return packet;
        }
    }
}
#endif
