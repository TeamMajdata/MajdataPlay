#if UNITY_STANDALONE_WIN
using System;
using System.Threading;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>Selects the first available exclusive touch protocol on one device worker.</summary>
    internal sealed class ExclusiveTouchHost : IODevice, ITouchPanelDevice
    {
        readonly UsbDevice[] _candidates;
        readonly bool[] _states = new bool[35];
        readonly bool[] _hadOn = new bool[35];
        readonly bool[] _hadOff = new bool[35];
        UsbDevice? _active;

        public ExclusiveTouchHost()
        {
            var connection = IODetector.TouchPanelUsbConnInfo;
            var options = MajEnv.Settings.IO.InputDevice.TouchPanel.CapacitivePanelOptions;
            _candidates = new UsbDevice[]
            {
                new PdxTouchDevice((ushort)connection.VendorId, (ushort)connection.ProductId,
                    connection.DeviceName, options.TouchRadius, options.RadiusOffset),
                new FlTouchDevice(connection.DeviceName, options.TouchRadius, options.RadiusOffset)
            };
        }

        protected override string DaemonThreadName => "IO/ExclusiveTouch Thread";
        // Drain multi-report USB frames immediately; game-frame publication happens in OnPreUpdate.
        protected override TimeSpan PollingInterval => TimeSpan.Zero;

        protected override bool Connect(CancellationToken token)
        {
            foreach (var candidate in _candidates)
            {
                Volatile.Write(ref _active, candidate);
                token.ThrowIfCancellationRequested();
                if (candidate.ConnectTransport(token))
                    return true;
                candidate.CloseTransport();
                candidate.ResetTransport();
            }
            Volatile.Write(ref _active, null);
            return false;
        }

        protected override void Update(CancellationToken token) =>
            Volatile.Read(ref _active)!.UpdateTransport(token);
        protected override void Parse(ReadOnlySpan<byte> data) =>
            Volatile.Read(ref _active)?.ParseReport(data);

        protected override void Disconnect() => Volatile.Read(ref _active)?.CloseTransport();
        protected override void OnDisconnected() =>
            Interlocked.Exchange(ref _active, null)?.ResetTransport();

        public override void OnPreUpdate()
        {
            var active = Volatile.Read(ref _active);
            if (active != null)
            {
                active.OnPreUpdate();
                ((ITouchPanelDevice)active).ReadTouchPanel(_states, _hadOn, _hadOff);
            }
            else
            {
                _states.AsSpan().Clear();
                _hadOn.AsSpan().Clear();
                _hadOff.AsSpan().Fill(true);
            }
        }

        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff)
        {
            _states.AsSpan().CopyTo(states);
            _hadOn.AsSpan().CopyTo(hadOn);
            _hadOff.AsSpan().CopyTo(hadOff);
        }

        public bool IsSensorCurrentlyOn(int index) =>
            (Volatile.Read(ref _active) as ITouchPanelDevice)?.IsSensorCurrentlyOn(index) ?? false;
    }
}
#endif
