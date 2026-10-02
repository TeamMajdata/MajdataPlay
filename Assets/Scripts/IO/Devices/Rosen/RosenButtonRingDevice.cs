#if UNITY_STANDALONE
using System;
namespace MajdataPlay.IO
{
    internal sealed class RosenButtonRingDevice : HidDevice, IButtonRingDevice
    {
        readonly InputStateBuffer _buttons = new InputStateBuffer(12);

        public RosenButtonRingDevice() : base(IODetector.ButtonRingHidConnInfo) { }
        protected override string DaemonThreadName => "IO/ButtonRing Thread";
        protected override TimeSpan PollingInterval =>
            TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);

        protected override void OnDisconnected() => _buttons.Clear();
        public override void OnPreUpdate() => _buttons.OnPreUpdate();
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _buttons.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _buttons.IsCurrentlyOn(index);

        const int IO4_BA1_OFFSET = 0b00000100;
        const int IO4_BA2_OFFSET = 0b00001000;
        const int IO4_BA3_OFFSET = 0b00000001;
        const int IO4_BA4_OFFSET = 0b10000000;
        const int IO4_BA5_OFFSET = 0b01000000;
        const int IO4_BA6_OFFSET = 0b00100000;
        const int IO4_BA7_OFFSET = 0b00010000;
        const int IO4_BA8_OFFSET = 0b00001000;
        const int IO4_TEST_OFFSET = 0b00000010;
        const int IO4_SELECT_P1_OFFSET = 0b00000010;
        const int IO4_SERVICE_OFFSET = 0b00000001;
        const int IO4_SELECT_P2_OFFSET = 0b01000000;

        const int IO4_BA1_1P_INDEX = 28;
        const int IO4_BA2_1P_INDEX = 28;
        const int IO4_BA3_1P_INDEX = 28;
        const int IO4_BA4_1P_INDEX = 29;
        const int IO4_BA5_1P_INDEX = 29;
        const int IO4_BA6_1P_INDEX = 29;
        const int IO4_BA7_1P_INDEX = 29;
        const int IO4_BA8_1P_INDEX = 29;

        const int IO4_BA1_2P_INDEX = 30;
        const int IO4_BA2_2P_INDEX = 30;
        const int IO4_BA3_2P_INDEX = 30;
        const int IO4_BA4_2P_INDEX = 31;
        const int IO4_BA5_2P_INDEX = 31;
        const int IO4_BA6_2P_INDEX = 31;
        const int IO4_BA7_2P_INDEX = 31;
        const int IO4_BA8_2P_INDEX = 31;

        const int IO4_TEST_INDEX = 29;
        const int IO4_SELECT_P1_INDEX = 28;
        const int IO4_SERVICE_INDEX = 25;
        const int IO4_SELECT_P2_INDEX = 28;
        protected override void Parse(ReadOnlySpan<byte> reportData)
        {
            if (reportData.Length < (IODetector.PlayerIndex == 2 ? 33 : 31)) return;
            Span<bool> buffer = stackalloc bool[12];
            buffer.Clear();
            reportData = reportData.Slice(1); // skip report id
            switch (IODetector.PlayerIndex)
            {
                case 1:
                    buffer[0] = (~reportData[IO4_BA1_1P_INDEX] & IO4_BA1_OFFSET) != 0;
                    buffer[1] = (~reportData[IO4_BA2_1P_INDEX] & IO4_BA2_OFFSET) != 0;
                    buffer[2] = (~reportData[IO4_BA3_1P_INDEX] & IO4_BA3_OFFSET) != 0;
                    buffer[3] = (~reportData[IO4_BA4_1P_INDEX] & IO4_BA4_OFFSET) != 0;
                    buffer[4] = (~reportData[IO4_BA5_1P_INDEX] & IO4_BA5_OFFSET) != 0;
                    buffer[5] = (~reportData[IO4_BA6_1P_INDEX] & IO4_BA6_OFFSET) != 0;
                    buffer[6] = (~reportData[IO4_BA7_1P_INDEX] & IO4_BA7_OFFSET) != 0;
                    buffer[7] = (~reportData[IO4_BA8_1P_INDEX] & IO4_BA8_OFFSET) != 0;
                    break;
                case 2:
                    buffer[0] = (~reportData[IO4_BA1_2P_INDEX] & IO4_BA1_OFFSET) != 0;
                    buffer[1] = (~reportData[IO4_BA2_2P_INDEX] & IO4_BA2_OFFSET) != 0;
                    buffer[2] = (~reportData[IO4_BA3_2P_INDEX] & IO4_BA3_OFFSET) != 0;
                    buffer[3] = (~reportData[IO4_BA4_2P_INDEX] & IO4_BA4_OFFSET) != 0;
                    buffer[4] = (~reportData[IO4_BA5_2P_INDEX] & IO4_BA5_OFFSET) != 0;
                    buffer[5] = (~reportData[IO4_BA6_2P_INDEX] & IO4_BA6_OFFSET) != 0;
                    buffer[6] = (~reportData[IO4_BA7_2P_INDEX] & IO4_BA7_OFFSET) != 0;
                    buffer[7] = (~reportData[IO4_BA8_2P_INDEX] & IO4_BA8_OFFSET) != 0;
                    break;
            }
            buffer[8] = (reportData[IO4_TEST_INDEX] & IO4_TEST_OFFSET) != 0;
            buffer[9] = (reportData[IO4_SELECT_P1_INDEX] & IO4_SELECT_P1_OFFSET) != 0;
            buffer[10] = (reportData[IO4_SERVICE_INDEX] & IO4_SERVICE_OFFSET) != 0;
            buffer[11] = (reportData[IO4_SELECT_P2_INDEX] & IO4_SELECT_P2_OFFSET) != 0;
            KeyboardHelper.ReadFunctionButtons(buffer);
            _buttons.Publish(buffer);
        }
    }
}
#endif
