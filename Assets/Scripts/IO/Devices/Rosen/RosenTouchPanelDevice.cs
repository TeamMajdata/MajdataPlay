#if UNITY_STANDALONE
using System;
using System.IO;
using System.Text;
using System.Threading;
using HidSharp;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.IO
{
    /// <summary>The general and Yuan serial touch-panel protocol.</summary>
    internal sealed class RosenTouchPanelDevice : SerialDevice, ITouchPanelDevice
    {
        readonly InputStateBuffer _states = new(35);
        readonly byte[] _packet = new byte[9];
        int _packetLength;

        public RosenTouchPanelDevice() : base(IODetector.TouchPanelSerialConnInfo) { }

        protected override string DaemonThreadName => "IO/TouchPanel Thread";
        protected override TimeSpan PollingInterval =>
            TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.TouchPanel.PollingRateMs);

        public override void OnPreUpdate() => _states.OnPreUpdate();
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _states.CopyTo(states, hadOn, hadOff);
        public bool IsSensorCurrentlyOn(int index) => _states.IsCurrentlyOn(index);

        protected override void OnConnected(CancellationToken token)
        {
            _packetLength = 0;
            var stream = Stream!;
            var readTimeout = stream.ReadTimeout;
            stream.ReadTimeout = 2000;
            try
            {
                if (!InitTouchPanel(stream, token))
                    throw new IOException("Failed to initialize the touch panel");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    stream.ReadTimeout = readTimeout;
            }
        }

        protected override void OnDisconnected()
        {
            _packetLength = 0;
            _states.Clear();
        }

        protected override void Parse(ReadOnlySpan<byte> data)
        {
            Span<bool> states = stackalloc bool[35];
            foreach (var value in data)
            {
                if (_packetLength == 0 && value != (byte)'(')
                    continue;
                _packet[_packetLength++] = value;
                if (_packetLength != _packet.Length)
                    continue;
                if (_packet[8] == (byte)')')
                {
                    for (var i = 0; i < 35; i++)
                        states[i] = (_packet[1 + i / 5] & (1 << (i % 5))) != 0;
                    // Publish every complete report, including pulses within a single read.
                    _states.Publish(states);
                    _packetLength = 0;
                }
                else
                {
                    var nextHeader = _packet.AsSpan(1).IndexOf((byte)'(');
                    if (nextHeader < 0)
                        _packetLength = 0;
                    else
                    {
                        nextHeader++;
                        _packetLength -= nextHeader;
                        _packet.AsSpan(nextHeader, _packetLength).CopyTo(_packet);
                    }
                }
            }
        }

        static bool InitTouchPanel(SerialStream serialStream, CancellationToken token)
        {
            try
            {
                MajDebug.LogInfo(nameof(RosenTouchPanelDevice), $"Starting to initialize the touch panel...");
                var sensConfig = MajEnv.Settings.IO.InputDevice.TouchPanel.Sensitivities;
                var index = IODetector.PlayerIndex == 1 ? 'L' : 'R';
                var sens = (sensConfig.A, sensConfig.B, sensConfig.C, sensConfig.D, sensConfig.E);
                MajDebug.LogInfo(nameof(RosenTouchPanelDevice), $"Sensitivities:\nA:{sens.A}\nB:{sens.B}\nC:{sens.C}\nD:{sens.D}\nE:{sens.E}");
                //see also https://github.com/Sucareto/Mai2Touch/tree/main/Mai2Touch
                serialStream.Write("{RSET}");
                MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Sent: {{RSET}}");
                MajDebug.LogInfo(nameof(RosenTouchPanelDevice), "Waiting for TouchPanel reset");
#if UNITY_STANDALONE_LINUX
                if (token.WaitHandle.WaitOne(4000)) token.ThrowIfCancellationRequested();
#elif UNITY_STANDALONE_WIN
                // Calling Thread.Sleep on Windows causes the driver to hang
                // So we read the first 10 bytes returned by the device to determine whether the reset is complete
                try
                {
                    Span<byte> buffer = stackalloc byte[10];
                    var offset = 0;
                    while (offset < 10)
                    {
                        token.ThrowIfCancellationRequested();
                        var read = serialStream.Read(buffer.Slice(offset, 10 - offset));
                        if (read == 0) throw new IOException("Touch panel disconnected during reset");
                        offset += read;
                    }
                    MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Recv: {Encoding.UTF8.GetString(buffer)}");
                }
                catch (TimeoutException)
                {
                    MajDebug.LogWarning(nameof(RosenTouchPanelDevice), "RSET read response timeout");
                }
#endif
                MajDebug.LogInfo(nameof(RosenTouchPanelDevice), "TouchPanel has been reset");

                serialStream.Write("{HALT}");
                MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Sent: {{HALT}}");
                //send ratio
                for (byte a = 0x41; a <= 0x62; a++)
                {
                    var cmd = $"{{{index}{(char)a}r2}}";
                    serialStream.Write(cmd);
                    MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Sent: {cmd}");
                }
                try
                {
                    for (byte a = 0x41; a <= 0x62; a++)
                    {
                        var value = GetSensitivityValue(a, sens);
                        var cmd = $"{{{index}{(char)a}k{(char)value}}}";
                        serialStream.Write(cmd);
                        MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Sent: {cmd}");
                    }
                }
                catch (TimeoutException)
                {
                    MajDebug.LogWarning(nameof(RosenTouchPanelDevice), $"TouchPanel does not support sensitivity override: Write timeout");
                }
                catch (Exception e)
                {
                    MajDebug.LogError(nameof(RosenTouchPanelDevice), $"Failed to override sensitivity: \n{e}");
                    return false;
                }
                serialStream.Write("{STAT}");
                MajDebug.LogDebug(nameof(RosenTouchPanelDevice), $"Sent: {{STAT}}");
                MajDebug.LogInfo(nameof(RosenTouchPanelDevice), "Initialization complete.");
                return true;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                return false;
            }
        }
        static byte GetSensitivityValue(byte sensor, (short A, short B, short C, short D, short E) sens)
        {
            const int A1 = 0x41;
            const int A2 = 0x42;
            const int A3 = 0x43;
            const int A4 = 0x44;
            const int A5 = 0x45;
            const int A6 = 0x46;
            const int A7 = 0x47;
            const int A8 = 0x48;

            const int B1 = 0x49;
            const int B2 = 0x4A;
            const int B3 = 0x4B;
            const int B4 = 0x4C;
            const int B5 = 0x4D;
            const int B6 = 0x4E;
            const int B7 = 0x4F;
            const int B8 = 0x50;

            const int C1 = 0x51;
            const int C2 = 0x52;

            const int D1 = 0x53;
            const int D2 = 0x54;
            const int D3 = 0x55;
            const int D4 = 0x56;
            const int D5 = 0x57;
            const int D6 = 0x58;
            const int D7 = 0x59;
            const int D8 = 0x5A;

            const int E1 = 0x5B;
            const int E2 = 0x5C;
            const int E3 = 0x5D;
            const int E4 = 0x5E;
            const int E5 = 0x5F;
            const int E6 = 0x60;
            const int E7 = 0x61;
            const int E8 = 0x62;

            var (A, B, C, D, E) = sens;
            var s = 0;

            switch (sensor)
            {
                case A1:
                case A2:
                case A3:
                case A4:
                case A5:
                case A6:
                case A7:
                case A8:
                    s = A;
                    goto SENS_A_RETURN;
                case B1:
                case B2:
                case B3:
                case B4:
                case B5:
                case B6:
                case B7:
                case B8:
                    s = B;
                    goto SENS_B_C_D_E_RETURN;
                case C1:
                case C2:
                    s = C;
                    goto SENS_B_C_D_E_RETURN;
                case D1:
                case D2:
                case D3:
                case D4:
                case D5:
                case D6:
                case D7:
                case D8:
                    s = D;
                    goto SENS_B_C_D_E_RETURN;
                case E1:
                case E2:
                case E3:
                case E4:
                case E5:
                case E6:
                case E7:
                case E8:
                    s = E;
                    goto SENS_B_C_D_E_RETURN;
                SENS_A_RETURN:
                    return s switch
                    {
                        -5 => 0x5A, // -5
                        -4 => 0x50, // -4
                        -3 => 0x46, // -3
                        -2 => 0x3C, // -2
                        -1 => 0x32, // -1
                        1 => 0x1E,  // +1
                        2 => 0x1A,  // +2
                        3 => 0x17,  // +3
                        4 => 0x14,  // +4
                        5 => 0x0A,  // +5
                        _ => 0x28   // 0
                    };
                SENS_B_C_D_E_RETURN:
                    return s switch
                    {
                        -5 => 0x46, // -5
                        -4 => 0x3C, // -4
                        -3 => 0x32, // -3
                        -2 => 0x28, // -2
                        -1 => 0x1E, // -1
                        1 => 0x0F,  // +1
                        2 => 0x0A,  // +2
                        3 => 0x05,  // +3
                        4 => 0x01,  // +4
                        5 => 0x01,  // +5
                        _ => 0x14   // 0
                    };
                default:
                    return 0x28;
            }
        }
    }
}
#endif
