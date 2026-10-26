#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>
    /// NPro 自定义固件的 GAME 管道设备（WinUSB，接口 0，bulk 0x81 读 / 0x01 写）。
    /// 上行是 8 字节输入报告（触摸掩码 6 字节 + 外键 + 扩展键），下行是 SEGA 灯板协议，
    /// 因此一条连接同时提供外键、触摸与灯板输出，三个能力共用同一条 daemon 线程。
    /// </summary>
    internal sealed class NproDevice : UsbDevice, IButtonRingDevice, ITouchPanelDevice, ILedDevice
    {
        /// <summary>NPro 固件的厂商 ID。</summary>
        internal const ushort Vid = 0x2E3C;

        /// <summary>1P 机型的产品 ID。</summary>
        internal const ushort Player1Pid = 0x5751;

        /// <summary>2P 机型的产品 ID。</summary>
        internal const ushort Player2Pid = 0x5752;

        /// <summary>GAME 管道所在的接口号。</summary>
        internal const byte GameInterface = 0;

        /// <summary>GAME 管道的接口名。</summary>
        internal const string GameInterfaceName = "NoronDX GAME";

        /// <summary>固件为 GAME 管道声明的 WCID 接口 GUID。</summary>
        internal static readonly Guid GameInterfaceGuid = new Guid("7F2A9C41-5E3B-4D8A-9F16-3C7E5B2D9A48");

        /// <summary>固件在灯板通道静默 5 秒后回落到 idle 灯效，保活间隔必须明显小于它。</summary>
        const int KeepAliveIntervalMs = 1000;

        /// <summary>当前玩家槽位对应的产品 ID；GAME 管道没有 HID 接口，1P/2P 只能靠 PID 区分。</summary>
        static ushort LocalProductId => IODetector.PlayerIndex == 2 ? Player2Pid : Player1Pid;

        readonly bool _writeLeds;
        readonly InputStateBuffer _buttons = new InputStateBuffer(NproProtocol.ButtonCount);
        readonly InputStateBuffer _sensors = new InputStateBuffer(NproProtocol.SensorCount);
        readonly NproReportFramer _framer = new NproReportFramer();
        readonly LedOutputState _output = new LedOutputState();
        readonly Color[] _lastColors = new Color[8];
        readonly byte[] _frameBuffer = new byte[NproProtocol.MaxLedFrameLength];
        readonly float _brightness;
        readonly bool _throttler;
        readonly long _refreshTicks;
        readonly long _keepAliveTicks;
        bool _forceUpdate = true;
        long _nextLedUpdate;
        long _lastKeepAlive;

        /// <summary>创建 NPro 复合设备。</summary>
        /// <param name="writeLeds">是否接管灯板输出；为 false 时只读输入，固件保持自己的灯效。</param>
        public NproDevice(bool writeLeds)
            : base(Vid, LocalProductId, string.Empty, NproReportFramer.MaxChunkLength)
        {
            _writeLeds = writeLeds;
            var options = MajEnv.Settings.IO.OutputDevice.Led;
            _brightness = Mathf.Clamp01(options.Brightness);
            _throttler = options.Throttler;
            _refreshTicks = Stopwatch.Frequency * Math.Max(0, options.RefreshRateMs) / 1000;
            _keepAliveTicks = Stopwatch.Frequency * KeepAliveIntervalMs / 1000;
        }

        /// <summary>日志中的设备名。</summary>
        protected override string DiagnosticName => "NPro";

        /// <summary>固件以 1 kHz 持续上报输入，尽快排空 FIFO，避免丢掉两次读取之间的短脉冲。</summary>
        protected override TimeSpan PollingInterval => TimeSpan.Zero;

        /// <summary>只枚举 GAME 管道，同一台设备上的 COMMAND 管道留给固件工具使用。</summary>
        /// <returns>GAME 管道的设备路径。</returns>
        protected override IEnumerable<WinUsbIo.DevicePath> EnumerateDevices() =>
            WinUsbIo.EnumerateWinUsbInterfaces(Vid, LocalProductId, GameInterface, GameInterfaceName, GameInterfaceGuid);

        /// <summary>重连后灯板状态未知，必须全量重发一次颜色。</summary>
        protected override void OnDeviceConnected() => _forceUpdate = true;

        /// <summary>清空输入与灯板状态，避免断线后残留卡键或错位的半包报告。</summary>
        protected override void OnDisconnected()
        {
            _buttons.Clear();
            _sensors.Clear();
            _framer.Clear();
            _forceUpdate = true;
        }

        /// <summary>先下发灯光再读输入：读取会阻塞，灯光必须抢先一步写出。</summary>
        /// <param name="token">daemon 线程的取消令牌。</param>
        protected override void Update(CancellationToken token)
        {
            if (_writeLeds)
            {
                UpdateLeds(token);
            }
            base.Update(token);
        }

        /// <summary>按 8 字节重组输入报告并发布外键与触摸状态。</summary>
        /// <param name="data">本次从 GAME 管道读到的字节流。</param>
        protected override void Parse(ReadOnlySpan<byte> data)
        {
            _framer.Append(data);

            Span<byte> report = stackalloc byte[NproReportFramer.ReportLength];
            Span<bool> buttons = stackalloc bool[NproProtocol.ButtonCount];
            Span<bool> sensors = stackalloc bool[NproProtocol.SensorCount];

            while (_framer.TryReadReport(report))
            {
                NproProtocol.DecodeInputReport(report, buttons, sensors);
                // 与其它外键设备一致：允许键盘补足设备没有上报的功能键。
                KeyboardHelper.ReadFunctionButtons(buttons);
                _buttons.Publish(buttons);
                _sensors.Publish(sensors);
            }
        }

        /// <inheritdoc/>
        public override void OnPreUpdate()
        {
            _buttons.OnPreUpdate();
            _sensors.OnPreUpdate();
        }

        /// <inheritdoc/>
        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _buttons.CopyTo(states, hadOn, hadOff);

        /// <inheritdoc/>
        public bool IsButtonCurrentlyOn(int index) => _buttons.IsCurrentlyOn(index);

        /// <inheritdoc/>
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _sensors.CopyTo(states, hadOn, hadOff);

        /// <inheritdoc/>
        public bool IsSensorCurrentlyOn(int index) => _sensors.IsCurrentlyOn(index);

        /// <inheritdoc/>
        public void WriteLeds(ReadOnlySpan<Color> colors, byte cabinetBrightness) =>
            _output.WriteLeds(colors, cabinetBrightness);

        /// <summary>按刷新率下发变化的灯区，并在静止时保活灯板链路。</summary>
        /// <param name="token">daemon 线程的取消令牌。</param>
        void UpdateLeds(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var now = Stopwatch.GetTimestamp();
            if (_forceUpdate || now >= _nextLedUpdate)
            {
                _nextLedUpdate = now + _refreshTicks;
                Span<Color> colors = stackalloc Color[8];
                _output.ReadLeds(colors, out _);
                var needUpdate = false;
                for (var i = 0; i < colors.Length; i++)
                {
                    var color = colors[i];
                    if (!_forceUpdate && _throttler && _lastColors[i] == color)
                    {
                        continue;
                    }
                    var length = NproProtocol.BuildLedColorFrame(_frameBuffer, i, color, _brightness);
                    WriteFrame(length);
                    needUpdate = true;
                }
                if (needUpdate)
                {
                    colors.CopyTo(_lastColors);
                    WriteFrame(NproProtocol.BuildLedUpdateFrame(_frameBuffer));
                }
                _forceUpdate = false;
            }

            TickKeepAlive(now);
        }

        /// <summary>静止时周期性发送幂等的刷新命令，阻止固件回落到 idle 灯效。</summary>
        /// <param name="now">当前时间戳，取自 <see cref="Stopwatch.GetTimestamp"/>。</param>
        void TickKeepAlive(long now)
        {
            if (now - _lastKeepAlive < _keepAliveTicks)
            {
                return;
            }
            _lastKeepAlive = now;
            WriteFrame(NproProtocol.BuildLedUpdateFrame(_frameBuffer));
        }

        /// <summary>把缓冲里的灯板帧写到 GAME 管道；写失败按断线处理，由基类重连。</summary>
        /// <param name="length">缓冲中有效帧数据的长度。</param>
        /// <exception cref="IOException">管道已断开或写入字节数不符时抛出。</exception>
        void WriteFrame(int length)
        {
            var connection = Connection ?? throw new IOException("NPro GAME 管道已断开。");
            if (connection.Write(_frameBuffer, 0, length, 100) != length)
            {
                throw new IOException("NPro 灯板帧写入失败。");
            }
        }
    }
}
#endif
