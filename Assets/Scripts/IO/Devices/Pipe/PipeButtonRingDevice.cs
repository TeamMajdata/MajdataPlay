#if UNITY_STANDALONE
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using MajdataPlay.Buffers;

#nullable enable
namespace MajdataPlay.IO
{
    internal sealed class PipeButtonRingDevice : IODevice, IButtonRingDevice
    {
        readonly InputStateBuffer _states = new InputStateBuffer(12);
        readonly byte[] _readBuffer = new byte[1024];
        readonly byte[] _pending = new byte[8192];
        readonly bool[] _buttons = new bool[12];
        readonly PipePacket.PacketReceivedCallback _onPacket;
        int _pendingCount;
        NamedPipeClientStream? _stream;
        public PipeButtonRingDevice() => _onPacket = OnPacketReceived;
        protected override string DaemonThreadName => "IO/ButtonRing Thread";
        protected override TimeSpan PollingInterval => TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);

        protected override bool Connect(CancellationToken token)
        {
            var stream = new NamedPipeClientStream(".", $"MajdataPlay.IO.ButtonRing.{IODetector.PlayerIndex}P", PipeDirection.InOut, PipeOptions.Asynchronous);
            Interlocked.Exchange(ref _stream, stream)?.Dispose();
            token.ThrowIfCancellationRequested();
            stream.Connect(2000);
            token.ThrowIfCancellationRequested();
            _pendingCount = 0;
            return true;
        }

        protected override void Disconnect() => Interlocked.Exchange(ref _stream, null)?.Dispose();
        protected override void OnDisconnected() => _states.Clear();
        protected override void Update(CancellationToken token)
        {
            var read = _stream!.Read(_readBuffer, 0, _readBuffer.Length);
            if (read == 0) throw new EndOfStreamException();
            Parse(_readBuffer.AsSpan(0, read));
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            var packets = new SpanBuffer(_pending);
            packets.Write(_pending.AsSpan(0, _pendingCount));
            if (packets.Write(data) != data.Length)
                throw new InvalidDataException("Button ring pipe packet buffer overflow");
            PipePacket.Parse(ref packets, 1024, _onPacket);
            _pendingCount = packets.Data.Length;
        }

        void OnPacketReceived(PipePacket packet)
        {
            if (packet.Type != PipePacketType.Report || packet.Payload.Length != 8) return;
            var bits = BinaryPrimitives.ReadUInt64LittleEndian(packet.Payload);
            for (var i = 0; i < _buttons.Length; i++) _buttons[i] = (bits & (1UL << i)) != 0;
            _states.Publish(_buttons);
        }

        public override void OnPreUpdate() => _states.OnPreUpdate();
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) => _states.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _states.IsCurrentlyOn(index);
    }
}
#endif
