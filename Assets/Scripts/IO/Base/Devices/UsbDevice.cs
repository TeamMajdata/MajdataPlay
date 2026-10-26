#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MajdataPlay.Diagnostics;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>WinUSB transport with overridable discovery, endpoints and initialization.</summary>
    internal abstract class UsbDevice : IODevice
    {
        protected readonly record struct UsbEndpoint(byte InterfaceNumber, byte EndpointId);

        readonly ushort _vendorId;
        readonly ushort _productId;
        readonly string _identifier;
        readonly byte[] _readBuffer;
        readonly object _connectionLock = new();
        WinUsbIo.Device? _connection;

        protected UsbDevice(ushort vendorId, ushort productId, string identifier, int packetSize)
        {
            _vendorId = vendorId;
            _productId = productId;
            _identifier = identifier;
            _readBuffer = new byte[packetSize];
        }

        protected virtual string DiagnosticName => GetType().Name;
        protected override string DaemonThreadName => $"IO/{DiagnosticName} Thread";
        protected WinUsbIo.Device? Connection
        {
            get
            {
                lock (_connectionLock)
                    return _connection;
            }
        }
        protected virtual IEnumerable<WinUsbIo.DevicePath> EnumerateDevices() =>
            WinUsbIo.EnumerateWinUsbInterfaces(_vendorId, _productId);
        protected virtual UsbEndpoint ResolveEndpoint(WinUsbIo.DevicePath path) =>
            new(path.InterfaceNumber, 0x81);
        protected virtual void InitializeDevice(WinUsbIo.Device device) { }
        protected virtual void OnDeviceConnected() { }

        // A selection controller can drive candidates on its own worker without starting nested daemons.
        internal bool ConnectTransport(CancellationToken token) => IsConnected = Connect(token);
        internal void UpdateTransport(CancellationToken token) => Update(token);
        internal void CloseTransport()
        {
            IsConnected = false;
            Disconnect();
        }
        internal void ResetTransport() => OnDisconnected();
        internal void ParseReport(ReadOnlySpan<byte> data) => Parse(data);

        protected override bool Connect(CancellationToken token)
        {
            foreach (var path in EnumerateDevices())
            {
                token.ThrowIfCancellationRequested();
                WinUsbIo.Device? candidate = null;
                try
                {
                    var endpoint = ResolveEndpoint(path);
                    candidate = WinUsbIo.Device.Open(path.Path, endpoint.EndpointId);
                    candidate.SetAutoSuspend(false);
                    if (!path.MatchesIdentifier(_identifier) &&
                        !string.Equals(_identifier?.Trim(), candidate.SerialNumber?.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.Dispose();
                        continue;
                    }

                    // Publish before initialization so cancellation can abort a blocked control transfer.
                    lock (_connectionLock)
                    {
                        token.ThrowIfCancellationRequested();
                        _connection = candidate;
                    }
                    InitializeDevice(candidate);
                    token.ThrowIfCancellationRequested();
                    OnDeviceConnected();
                    MajDebug.LogInfo($"[{DiagnosticName}] Connected {path.Path} " +
                        $"(interface {endpoint.InterfaceNumber}, endpoint 0x{endpoint.EndpointId:X2})");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    candidate?.Dispose();
                    Disconnect();
                    throw;
                }
                catch (Exception e)
                {
                    candidate?.Dispose();
                    Disconnect();
                    MajDebug.LogDebug($"[{DiagnosticName}] Failed to open {path.Path}: {e.Message}");
                }
            }
            return false;
        }

        protected override void Update(CancellationToken token)
        {
            WinUsbIo.Device connection;
            lock (_connectionLock)
                connection = _connection ?? throw new IOException("USB device is disconnected");
            var count = connection.Read(_readBuffer, 0, _readBuffer.Length, 100);
            if (count < 0)
                throw new IOException("USB read failed");
            if (count == 0)
                return;
            token.ThrowIfCancellationRequested();
            Parse(_readBuffer.AsSpan(0, count));
        }

        protected override void Disconnect()
        {
            WinUsbIo.Device? connection;
            lock (_connectionLock)
            {
                connection = _connection;
                _connection = null;
            }
            connection?.Dispose();
        }
    }
}
#endif
