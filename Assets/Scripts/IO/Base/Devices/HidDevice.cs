#if UNITY_STANDALONE
using HidSharp;
using System;
using System.IO;
using System.Threading;

#nullable enable
namespace MajdataPlay.IO
{
    internal abstract class HidDevice : IODevice
    {
        protected HidSharp.HidDevice? Device { get; set; }
        protected HidStream? Stream
        {
            get => Volatile.Read(ref _stream);
            set => Volatile.Write(ref _stream, value);
        }

        readonly IODetector.HidConnInfo _connectionInfo;
        HidStream? _stream;
        byte[] _readBuffer = Array.Empty<byte>();
        bool _hasConnected;

        protected HidDevice(IODetector.HidConnInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        protected override bool Connect(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var filter = new DeviceFilter
            {
                DeviceName = _connectionInfo.DeviceName,
                ProductId = _connectionInfo.ProductId,
                VendorId = _connectionInfo.VendorId,
            };
            var configuration = new OpenConfiguration();
            configuration.SetOption(OpenOption.Exclusive, _connectionInfo.Exclusice);
            configuration.SetOption(OpenOption.Priority, (OpenPriority)_connectionInfo.OpenPriority);
            if (!HidHelper.TryGetAndOpenDevice(GetType().Name, filter, configuration, out var device, out var stream, _hasConnected))
            {
                return false;
            }
            Device = device;
            Stream = stream;
            stream.ReadTimeout = 250;
            stream.WriteTimeout = 2000;
            _readBuffer = new byte[Math.Max(1, device.GetMaxInputReportLength())];
            token.ThrowIfCancellationRequested();
            OnConnected(token);
            _hasConnected = true;
            return true;
        }

        protected virtual void OnConnected(CancellationToken token)
        {
        }

        protected override void Update(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var stream = Stream ?? throw new IOException("The HID device is disconnected.");
            int count;
            try
            {
                count = stream.Read(_readBuffer, 0, _readBuffer.Length);
            }
            catch (TimeoutException)
            {
                // An idle input endpoint is still connected; writes and parsers can fail independently.
                token.ThrowIfCancellationRequested();
                return;
            }
            token.ThrowIfCancellationRequested();
            if (count == 0)
            {
                throw new EndOfStreamException("The HID device stopped reporting input.");
            }
            Parse(_readBuffer.AsSpan(0, count));
        }

        protected override void Disconnect()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            Device = null;
            stream?.Dispose();
        }
    }
}
#endif
