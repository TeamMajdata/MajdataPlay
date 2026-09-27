#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.IO;

/// <summary>
/// NPro 自定义固件的 GAME 管道设备（WinUSB）。
///
/// 一个设备同时提供按键、触摸和灯板通道，因此由 <see cref="NproDeviceHost"/>
/// 统一持有连接，按键、触摸和 LED 三个 IO 线程只共享这份连接。
/// </summary>
internal sealed class NproDevice : IDisposable
{
    internal const ushort Vid = 0x2E3C;
    internal const ushort Player1Pid = 0x5751;
    internal const ushort Player2Pid = 0x5752;
    internal const byte GameInterface = 0;
    internal const string GameInterfaceName = "NoronDX GAME";
    internal static readonly Guid GameInterfaceGuid = new("7F2A9C41-5E3B-4D8A-9F16-3C7E5B2D9A48");

    private const byte Sync = 0xE0;
    private const byte Escape = 0xD0;
    private const byte DstNodeId = 0x11;
    private const byte SrcNodeId = 0x01;
    private const int InputReportLength = 8;
    private const ulong TouchMask = (1UL << 34) - 1;

    private const byte CmdSetLedGs8Bit = 0x31;
    private const byte CmdSetLedGs8BitMulti = 0x32;
    private const byte CmdSetLedGs8BitMultiFade = 0x33;
    private const byte CmdSetLedFet = 0x39;
    private const byte CmdSetLedGsUpdate = 0x3C;

    private const int KeepAliveIntervalMs = 1000;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly int _player;
    private readonly object _writeLock = new();
    private readonly Action<ulong, byte, byte> _onInput;

    private WinUsbIo.Device _device;
    private Thread _readThread;
    private volatile bool _stopping;
    private long _lastKeepAliveTicks;

    public bool IsConnected
    {
        get
        {
            var device = Volatile.Read(ref _device);
            return device != null && device.IsOpen;
        }
    }

    public NproDevice(int player, Action<ulong, byte, byte> onInput)
    {
        _player = player;
        _onInput = onInput;
    }

    public bool TryConnect()
    {
        if (IsConnected) return true;

        var targetPid = _player == 0 ? Player1Pid : Player2Pid;
        List<WinUsbIo.DevicePath> paths;
        try
        {
            paths = WinUsbIo.EnumerateWinUsbInterfaces(
                Vid, targetPid, GameInterface, GameInterfaceName, GameInterfaceGuid);
        }
        catch (Exception e)
        {
            MajDebug.LogWarning($"[NPro] 枚举 GAME 接口失败: {e.Message}");
            return false;
        }

        foreach (var path in paths)
        {
            if (path.Pid != targetPid) continue;

            try
            {
                var device = WinUsbIo.Device.Open(path.Path);
                device.SetAutoSuspend(false);
                Interlocked.Exchange(ref _device, device)?.Dispose();
                _stopping = false;
                _lastKeepAliveTicks = Clock.ElapsedMilliseconds;
                StartReadThread(device);
                MajDebug.LogInfo($"[NPro] {_player + 1}P 已连接 GAME 管道");
                return true;
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"[NPro] 打开 GAME 管道失败: {e.Message}");
                Interlocked.Exchange(ref _device, null)?.Dispose();
            }
        }

        return false;
    }

    private void StartReadThread(WinUsbIo.Device device)
    {
        _readThread = new Thread(() => ReadLoop(device))
        {
            IsBackground = true,
            Name = $"NPro-Read-{_player + 1}P",
            Priority = MajEnv.THREAD_PRIORITY_IO,
        };
        _readThread.Start();
    }

    private void ReadLoop(WinUsbIo.Device device)
    {
        var buffer = new byte[256];
        var pending = new List<byte>(512);

        while (!_stopping && ReferenceEquals(Volatile.Read(ref _device), device))
        {
            if (!device.IsOpen) break;

            int read;
            try
            {
                read = device.Read(buffer, 0, buffer.Length, 200);
            }
            catch
            {
                break;
            }

            if (read < 0) break;
            if (read == 0) continue;

            for (var i = 0; i < read; i++) pending.Add(buffer[i]);

            // bulk 是字节流，一次读可能包含多笔 8 字节报告。
            while (pending.Count >= InputReportLength)
            {
                ulong touch = 0;
                for (var i = 0; i < 6; i++) touch |= (ulong)pending[i] << (i * 8);
                var buttons = pending[6];
                var extButtons = pending[7];
                pending.RemoveRange(0, InputReportLength);
                _onInput?.Invoke(touch & TouchMask, buttons, extButtons);
            }
        }

        if (!_stopping) MarkDisconnected(device);
    }

    public void TickKeepAlive()
    {
        if (!IsConnected) return;

        var now = Clock.ElapsedMilliseconds;
        if (now - _lastKeepAliveTicks < KeepAliveIntervalMs) return;
        _lastKeepAliveTicks = now;

        // SetLedGsUpdate 是幂等的，适合在 LED 静止时维持固件链路。
        SendCommand(CmdSetLedGsUpdate, Array.Empty<byte>());
    }

    /// <summary>发送已经按 SEGA 灯板协议组好的明文帧。</summary>
    public void SendLedPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 6 || packet[0] != Sync) return;
        if (packet[3] + 5 != packet.Length) return;

        var command = packet[4];
        if (command != CmdSetLedGs8Bit &&
            command != CmdSetLedGs8BitMulti &&
            command != CmdSetLedGs8BitMultiFade &&
            command != CmdSetLedFet &&
            command != CmdSetLedGsUpdate)
        {
            return;
        }

        SendPlainPacket(packet);
    }

    private void SendCommand(byte command, byte[] payload)
    {
        var plain = new byte[5 + payload.Length + 1];
        plain[0] = Sync;
        plain[1] = DstNodeId;
        plain[2] = SrcNodeId;
        plain[3] = (byte)(1 + payload.Length);
        plain[4] = command;
        Buffer.BlockCopy(payload, 0, plain, 5, payload.Length);

        var sum = 0;
        for (var i = 1; i < plain.Length - 1; i++) sum += plain[i];
        plain[plain.Length - 1] = (byte)sum;
        SendPlainPacket(plain);
    }

    private void SendPlainPacket(ReadOnlySpan<byte> plain)
    {
        var device = Volatile.Read(ref _device);
        if (device == null || !device.IsOpen) return;

        // 0xE0/0xD0 在协议中需要转义，其余字节原样发送。
        var encoded = new byte[plain.Length * 2 + 1];
        var length = 1;
        encoded[0] = Sync;
        for (var i = 1; i < plain.Length; i++)
        {
            if (plain[i] == Sync || plain[i] == Escape)
            {
                encoded[length++] = Escape;
                encoded[length++] = (byte)(plain[i] - 1);
            }
            else
            {
                encoded[length++] = plain[i];
            }
        }

        lock (_writeLock)
        {
            try
            {
                if (device.Write(encoded, 0, length, 100) != length)
                {
                    MarkDisconnected(device);
                }
            }
            catch
            {
                MarkDisconnected(device);
            }
        }
    }

    public void Disconnect()
    {
        _stopping = true;
        var device = Interlocked.Exchange(ref _device, null);
        device?.Abort();
        device?.Dispose();
        _readThread = null;
    }

    private void MarkDisconnected(WinUsbIo.Device device)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _device, null, device), device)) return;
        device.Dispose();
    }

    public void Dispose() => Disconnect();
}

/// <summary>
/// NPro GAME 管道的进程级宿主，供按键、触摸和 LED 三个 IO 线程共享。
/// </summary>
internal static class NproDeviceHost
{
    private const int ReconnectIntervalMs = 1000;
    private static readonly object Sync = new();
    private static readonly object InputSync = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static NproDevice _device;
    private static bool _shutdownRegistered;
    private static long _nextConnectAttemptTicks;
    private static ulong _touchMask;
    private static int _buttons;
    private static int _extButtons;

    public static bool IsConnected
    {
        get
        {
            lock (Sync)
            {
                return _device != null && _device.IsConnected;
            }
        }
    }

    public static void GetInput(out ulong touchMask, out byte buttons, out byte extButtons)
    {
        lock (InputSync)
        {
            touchMask = _touchMask;
            buttons = (byte)_buttons;
            extButtons = (byte)_extButtons;
        }
    }

    public static bool IsDevicePresent(int playerIndex)
    {
        var pid = playerIndex == 1 ? NproDevice.Player1Pid : NproDevice.Player2Pid;
        try
        {
            return WinUsbIo.EnumerateWinUsbInterfaces(
                NproDevice.Vid, pid, NproDevice.GameInterface, NproDevice.GameInterfaceName,
                NproDevice.GameInterfaceGuid).Count > 0;
        }
        catch (Exception e)
        {
            MajDebug.LogWarning($"[NPro] 枚举设备失败: {e.Message}");
            return false;
        }
    }

    public static bool EnsureConnected()
    {
        RegisterShutdown();

        lock (Sync)
        {
            if (_device != null && _device.IsConnected) return true;

            ResetInput();
            _device?.Dispose();
            _device = null;

            var now = Clock.ElapsedMilliseconds;
            if (now < _nextConnectAttemptTicks) return false;
            _nextConnectAttemptTicks = now + ReconnectIntervalMs;

            var device = new NproDevice(IODetector.PlayerIndex - 1, OnInput);
            if (!device.TryConnect())
            {
                device.Dispose();
                return false;
            }

            _device = device;
            _nextConnectAttemptTicks = 0;
            return true;
        }
    }

    public static void TickKeepAlive()
    {
        lock (Sync)
        {
            _device?.TickKeepAlive();
        }
    }

    public static void SendLedPacket(ReadOnlySpan<byte> packet)
    {
        lock (Sync)
        {
            _device?.SendLedPacket(packet);
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _device?.Dispose();
            _device = null;
            ResetInput();
        }
    }

    private static void OnInput(ulong touch, byte buttons, byte extButtons)
    {
        lock (InputSync)
        {
            _touchMask = touch & ((1UL << 34) - 1);
            _buttons = buttons;
            _extButtons = extButtons;
        }
    }

    private static void ResetInput()
    {
        lock (InputSync)
        {
            _touchMask = 0;
            _buttons = 0;
            _extButtons = 0;
        }
    }

    private static void RegisterShutdown()
    {
        if (_shutdownRegistered) return;
        _shutdownRegistered = true;
        MajEnv.GlobalCT.Register(Stop);
    }
}
#endif
