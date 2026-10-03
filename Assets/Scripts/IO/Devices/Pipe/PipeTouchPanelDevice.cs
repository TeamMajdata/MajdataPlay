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
    internal sealed class PipeTouchPanelDevice : IODevice, ITouchPanelDevice
    {
        readonly InputStateBuffer _states = new(35);
        readonly byte[] _readBuffer = new byte[1024];
        readonly byte[] _pending = new byte[8192];
        readonly PipePacket.PacketReceivedCallback _onPacket;
        NamedPipeClientStream? _stream;
        int _pendingLength;

        public PipeTouchPanelDevice() => _onPacket = OnPacket;

        protected override string DaemonThreadName => "IO/TouchPanel Thread";
        protected override TimeSpan PollingInterval =>
            TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.TouchPanel.PollingRateMs);

        public override void OnPreUpdate() => _states.OnPreUpdate();
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _states.CopyTo(states, hadOn, hadOff);
        public bool IsSensorCurrentlyOn(int index) => _states.IsCurrentlyOn(index);

        protected override bool Connect(CancellationToken token)
        {
            _pendingLength = 0;
            var stream = new NamedPipeClientStream(".",
                $"MajdataPlay.IO.TouchPanel.{IODetector.PlayerIndex}P",
                PipeDirection.InOut, PipeOptions.Asynchronous);
            Interlocked.Exchange(ref _stream, stream)?.Dispose();
            token.ThrowIfCancellationRequested();
            stream.Connect(2000);
            return true;
        }

        protected override void Update(CancellationToken token)
        {
            var stream = _stream ?? throw new IOException("Touch panel pipe is disconnected");
            var count = stream.Read(_readBuffer);
            if (count == 0)
                throw new EndOfStreamException("Touch panel pipe is disconnected");
            Parse(_readBuffer.AsSpan(0, count));
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            var buffer = new SpanBuffer(_pending);
            // Reconstruct the cursor while retaining incomplete packets across reads.
            buffer.Write(_pending.AsSpan(0, _pendingLength));
            if (buffer.Write(data) != data.Length)
                throw new InvalidDataException("Touch panel pipe packet buffer overflow");
            PipePacket.Parse(ref buffer, 1024, _onPacket);
            _pendingLength = buffer.Data.Length;
        }

        void OnPacket(PipePacket packet)
        {
            if (packet.Type == PipePacketType.HeartBeat || packet.Payload.Length != sizeof(ulong))
                return;
            var mask = BinaryPrimitives.ReadUInt64LittleEndian(packet.Payload);
            Span<bool> states = stackalloc bool[35];
            for (var i = 0; i < states.Length; i++)
                states[i] = (mask & (1UL << i)) != 0;
            _states.Publish(states);
        }

        protected override void Disconnect() => Interlocked.Exchange(ref _stream, null)?.Dispose();
        protected override void OnDisconnected()
        {
            _pendingLength = 0;
            _states.Clear();
        }
    }
}
#endif
