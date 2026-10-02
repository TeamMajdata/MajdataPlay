#if UNITY_STANDALONE
using System;
namespace MajdataPlay.IO
{
    internal sealed class YuanButtonRingDevice : HidDevice, IButtonRingDevice
    {
        readonly InputStateBuffer _buttons = new InputStateBuffer(12);

        public YuanButtonRingDevice() : base(IODetector.ButtonRingHidConnInfo) { }
        protected override string DaemonThreadName => "IO/ButtonRing Thread";
        protected override TimeSpan PollingInterval =>
            TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);

        protected override void OnDisconnected() => _buttons.Clear();
        public override void OnPreUpdate() => _buttons.OnPreUpdate();
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _buttons.CopyTo(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _buttons.IsCurrentlyOn(index);

        const int HID_BA1_INDEX = 4;
        const int HID_BA2_INDEX = 3;
        const int HID_BA3_INDEX = 2;
        const int HID_BA4_INDEX = 1;
        const int HID_BA5_INDEX = 8;
        const int HID_BA6_INDEX = 7;
        const int HID_BA7_INDEX = 6;
        const int HID_BA8_INDEX = 5;
        const int HID_TEST_INDEX = 10;
        const int HID_SELECT_P1_INDEX = 9;
        const int HID_SERVICE_INDEX = 12;
        const int HID_SELECT_P2_INDEX = 11;
        protected override void Parse(ReadOnlySpan<byte> reportData)
        {
            if (reportData.Length < 14) return;
            Span<bool> buffer = stackalloc bool[12];
            buffer.Clear();
            reportData = reportData.Slice(1); // skip report id
            for (var i = 1; i < 13; i++)
            {
                switch (i)
                {
                    case HID_BA1_INDEX:
                        buffer[0] = reportData[i] == 1;
                        break;
                    case HID_BA2_INDEX:
                        buffer[1] = reportData[i] == 1;
                        break;
                    case HID_BA3_INDEX:
                        buffer[2] = reportData[i] == 1;
                        break;
                    case HID_BA4_INDEX:
                        buffer[3] = reportData[i] == 1;
                        break;
                    case HID_BA5_INDEX:
                        buffer[4] = reportData[i] == 1;
                        break;
                    case HID_BA6_INDEX:
                        buffer[5] = reportData[i] == 1;
                        break;
                    case HID_BA7_INDEX:
                        buffer[6] = reportData[i] == 1;
                        break;
                    case HID_BA8_INDEX:
                        buffer[7] = reportData[i] == 1;
                        break;
                    case HID_TEST_INDEX:
                        buffer[8] = reportData[i] == 1;
                        break;
                    case HID_SELECT_P1_INDEX:
                        buffer[9] = reportData[i] == 1;
                        break;
                    case HID_SERVICE_INDEX:
                        buffer[10] = reportData[i] == 1;
                        break;
                    case HID_SELECT_P2_INDEX:
                        buffer[11] = reportData[i] == 1;
                        break;
                }
            }
            KeyboardHelper.ReadFunctionButtons(buffer);
            _buttons.Publish(buffer);
        }
    }
}
#endif
