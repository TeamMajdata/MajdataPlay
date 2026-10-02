#if UNITY_STANDALONE
using System;
using System.Threading;
using UnityEngine;

namespace MajdataPlay.IO
{
    /// <summary>A single Maimoller connection supplies buttons, touch and optionally LEDs.</summary>
    internal sealed class DaoCompositeDevice : HidDevice, IButtonRingDevice, ITouchPanelDevice, ILedDevice
    {
        readonly InputStateBuffer _buttons = new InputStateBuffer(12);
        readonly InputStateBuffer _touch = new InputStateBuffer(35);
        readonly bool[] _reportTouch = new bool[35];
        readonly DaoLedOutput _leds = new DaoLedOutput();
        readonly object _reportLock = new object();
        readonly bool _readInputs;
        readonly bool _writeLeds;

        public DaoCompositeDevice(bool writeLeds) : this(
            MajEnv.Settings.IO.InputDevice.ButtonRing.Enable || MajEnv.Settings.IO.InputDevice.TouchPanel.Enable,
            writeLeds)
        {
        }

        DaoCompositeDevice(bool readInputs, bool writeLeds)
            // Input and output share one physical HID connection. Preserve the LED connection
            // settings when this device is used exclusively for lighting.
            : base(readInputs ? IODetector.ButtonRingHidConnInfo : IODetector.LedDeviceHidConnInfo)
        {
            _readInputs = readInputs;
            _writeLeds = writeLeds;
        }

        protected override string DaemonThreadName => "IO/Dao Thread";
        protected override TimeSpan PollingInterval
        {
            get
            {
                var input = MajEnv.Settings.IO.InputDevice;
                var interval = double.MaxValue;
                if (input.ButtonRing.Enable) interval = Math.Min(interval, input.ButtonRing.PollingRateMs);
                if (input.TouchPanel.Enable) interval = Math.Min(interval, input.TouchPanel.PollingRateMs);
                if (_writeLeds) interval = Math.Min(interval, MajEnv.Settings.IO.OutputDevice.Led.RefreshRateMs);
                return TimeSpan.FromMilliseconds(interval == double.MaxValue ? 0 : Math.Max(0, interval));
            }
        }

        protected override void OnConnected(CancellationToken token)
        {
            base.OnConnected(token);
            // Poll output even when the input report stream is idle.
            if (_readInputs && _writeLeds)
                Stream!.ReadTimeout = (int)Math.Max(1, Math.Min(10, MajEnv.Settings.IO.OutputDevice.Led.RefreshRateMs));
            _leds.Reset();
        }

        protected override void Update(CancellationToken token)
        {
            if (_writeLeds) _leds.WriteTo(Stream!, Device!);
            if (_readInputs) base.Update(token);
        }

        protected override void Parse(ReadOnlySpan<byte> report)
        {
            if (report.Length < 8) return;
            lock (_reportLock)
            {
                var data = report.Slice(1);
                Span<bool> states = stackalloc bool[12];
                for (var i = 0; i < 8; i++) states[i] = (data[5] & (1 << i)) != 0;
                states[8] = (data[6] & 4) != 0;
                states[9] = (data[6] & 8) != 0;
                states[10] = (data[6] & 1) != 0;
                states[11] = (data[6] & 2) != 0;
                KeyboardHelper.ReadFunctionButtons(states);

                for (var i = 0; i < 8; i++)
                {
                    _reportTouch[i] = (data[0] & (1 << i)) != 0;
                    _reportTouch[i + 8] = (data[1] & (1 << i)) != 0;
                    _reportTouch[i + 18] = (data[3] & (1 << i)) != 0;
                    _reportTouch[i + 26] = (data[4] & (1 << i)) != 0;
                }
                _reportTouch[16] = (data[2] & 1) != 0;
                _reportTouch[17] = (data[2] & 2) != 0;
                _buttons.Publish(states);
                _touch.Publish(_reportTouch);
            }
        }

        protected override void OnDisconnected()
        {
            lock (_reportLock)
            {
                _buttons.Clear();
                _touch.Clear();
            }
        }

        public override void OnPreUpdate()
        {
            lock (_reportLock)
            {
                _buttons.OnPreUpdate();
                _touch.OnPreUpdate();
            }
        }

        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) => _buttons.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _buttons.IsCurrentlyOn(index);
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) => _touch.CopyTo(states, hadOn, hadOff);
        public bool IsSensorCurrentlyOn(int index) => _touch.IsCurrentlyOn(index);
        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness) => _leds.WriteLeds(colors, cabinetBrightness);
    }
}
#endif
