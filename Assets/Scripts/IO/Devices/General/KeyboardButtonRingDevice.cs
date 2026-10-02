#if UNITY_STANDALONE
using System;
using System.Threading;

namespace MajdataPlay.IO
{
    internal sealed class KeyboardButtonRingDevice : IODevice, IButtonRingDevice
    {
        readonly InputStateBuffer _states = new InputStateBuffer(12);
        readonly bool[] _buttonRealTimeStates = new bool[12];
        protected override string DaemonThreadName => "IO/ButtonRing Thread";
        protected override TimeSpan PollingInterval =>
            TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);
        protected override bool Connect(CancellationToken token) => true;
        protected override void Parse(ReadOnlySpan<byte> data) { }
        protected override void Update(CancellationToken token)
        {
            var bindings = KeyboardHelper.ButtonBindings;
            for (var i = 0; i < bindings.Length; i++)
                _buttonRealTimeStates[i] = KeyboardHelper.IsKeyDown(bindings[i]);
            _states.Publish(_buttonRealTimeStates);
        }
        protected override void OnDisconnected() => _states.Clear();
        public override void OnPreUpdate() => _states.OnPreUpdate();
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) => _states.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _states.IsCurrentlyOn(index);
    }
}
#endif
