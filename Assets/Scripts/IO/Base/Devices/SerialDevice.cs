#if UNITY_STANDALONE
using HidSharp;
using System;
using System.IO;
using System.Threading;

#nullable enable
namespace MajdataPlay.IO
{
    internal abstract class SerialDevice : IODevice
    {
        protected SerialStream? Stream
        {
            get => Volatile.Read(ref _stream);
            set => Volatile.Write(ref _stream, value);
        }

        readonly IODetector.SerialPortConnInfo _connectionInfo;
        readonly byte[] _readBuffer = new byte[8192];
        SerialStream? _stream;

        protected SerialDevice(IODetector.SerialPortConnInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        protected override bool Connect(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var device = DeviceList.Local.GetSerialDeviceOrNull(_connectionInfo.PortName);
            if (device is null || !device.TryOpen(out var stream))
            {
                return false;
            }
            Stream = stream;
            stream.BaudRate = _connectionInfo.BaudRate;
            stream.DataBits = 8;
            stream.Parity = SerialParity.None;
            stream.StopBits = 1;
            stream.DtrEnable = true;
            stream.RtsEnable = true;
            stream.ReadTimeout = 250;
            stream.WriteTimeout = 2000;
            token.ThrowIfCancellationRequested();
            OnConnected(token);
            return true;
        }

        protected virtual void OnConnected(CancellationToken token)
        {
        }

        protected override void Update(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var stream = Stream ?? throw new IOException("The serial device is disconnected.");
            int count;
            try
            {
                count = stream.Read(_readBuffer, 0, _readBuffer.Length);
            }
            catch (TimeoutException)
            {
                token.ThrowIfCancellationRequested();
                return;
            }
            token.ThrowIfCancellationRequested();
            if (count == 0)
            {
                throw new EndOfStreamException("The serial device stopped reporting input.");
            }
            Parse(_readBuffer.AsSpan(0, count));
        }

        protected override void Disconnect()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
        }
    }
}
#endif
