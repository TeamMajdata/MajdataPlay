#if UNITY_STANDALONE
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using UnityEngine;

#nullable enable
namespace MajdataPlay.IO
{
    internal sealed class PipeLedDevice : IODevice, ILedDevice
    {
        readonly LedOutputState _output = new();
        readonly Color[] _lastColors = new Color[8];
        readonly bool _throttler;
        readonly TimeSpan _pollingInterval;
        NamedPipeClientStream? _stream;
        bool _forceUpdate = true;

        protected override string DaemonThreadName => "IO/Led Thread";
        protected override TimeSpan PollingInterval => _pollingInterval;

        public PipeLedDevice()
        {
            var options = MajEnv.Settings.IO.OutputDevice.Led;
            _throttler = options.Throttler;
            _pollingInterval = TimeSpan.FromMilliseconds(Math.Min(500, Math.Max(0, options.RefreshRateMs)));
        }

        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            _output.WriteLeds(colors, cabinetBrightness);
        }

        protected override bool Connect(CancellationToken token)
        {
            var stream = new NamedPipeClientStream(".", $"MajdataPlay.IO.Led.{IODetector.PlayerIndex}P", PipeDirection.InOut, PipeOptions.Asynchronous);
            Interlocked.Exchange(ref _stream, stream)?.Dispose();
            using var cancellation = token.Register(() => stream.Dispose());
            token.ThrowIfCancellationRequested();
            stream.Connect(2000);
            token.ThrowIfCancellationRequested();
            _forceUpdate = true;
            return true;
        }

        protected override void Update(CancellationToken token)
        {
            var stream = _stream ?? throw new IOException("LED pipe is disconnected.");
            Span<Color> colors = stackalloc Color[8];
            Span<byte> payload = stackalloc byte[32];
            Span<byte> packetBuffer = stackalloc byte[PipePacket.PACKET_HEADER_LENGTH + 32];
            _output.ReadLeds(colors, out _);
            var length = 0;
            for (var i = 0; i < colors.Length; i++)
            {
                var color = colors[i];
                if (!_forceUpdate && _throttler && color == _lastColors[i])
                {
                    continue;
                }
                payload[length++] = (byte)i;
                payload[length++] = (byte)(color.r * byte.MaxValue);
                payload[length++] = (byte)(color.g * byte.MaxValue);
                payload[length++] = (byte)(color.b * byte.MaxValue);
            }
            var packet = new PipePacket
            {
                Type = length == 0 ? PipePacketType.HeartBeat : PipePacketType.Report,
                Length = (ushort)length,
                Payload = payload.Slice(0, length),
            };
            var packetLength = packet.Write(packetBuffer);
            stream.Write(packetBuffer.Slice(0, packetLength));
            colors.CopyTo(_lastColors);
            _forceUpdate = false;
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            // This pipe only sends LED reports and heartbeats.
        }

        protected override void Disconnect()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
        }
    }
}
#endif
