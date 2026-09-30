#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.IO
{
    /// <summary>
    /// 独占触摸设备基类。
    ///
    /// 设备通过 WinUSB 独占打开，由独立读线程持续解析报告；触摸状态以 34 位掩码
    /// （A1..E8）对外提供，读线程断开时自动重连。
    /// </summary>
    internal abstract class ExclusiveTouchBase
    {
        protected readonly record struct TouchEndpoint(byte InterfaceNumber, byte EndpointId);

        private WinUsbIo.Device? _connection;
        private readonly object _deviceLock = new();
        private volatile bool _stopping;
        private TouchSensorMapper _touchSensorMapper;
        private readonly CancellationToken _cancellationToken;

        protected readonly struct TouchUpdate
        {
            public readonly ushort X;
            public readonly ushort Y;
            public readonly int FingerId;
            public readonly bool IsPressed;

            public TouchUpdate(ushort x, ushort y, int fingerId, bool isPressed)
            {
                X = x;
                Y = y;
                FingerId = fingerId;
                IsPressed = isPressed;
            }
        }

        private sealed class TouchPoint
        {
            public ulong Mask;
            public long LastUpdateTick;
            public bool IsActive;
        }

        // 防吃键：两次轮询之间完成的一次按下+抬起也要被读到
        private sealed class InputLatch
        {
            private ulong _current;
            private ulong _accumulated;

            public void Update(ulong state)
            {
                _accumulated |= state & ~_current;
                _current = state;
            }

            public ulong Read()
            {
                var result = _current | _accumulated;
                _accumulated = 0;
                return result;
            }
        }

        private readonly ushort _vid;
        private readonly ushort _pid;
        private readonly string _identifier;
        private readonly int _packetSize;

        private readonly TouchPoint[] _allFingerPoints = new TouchPoint[256];
        private readonly InputLatch _touchLatch = new();
        private readonly object _touchLock = new();
        private readonly AutoResetEvent _dataSignal = new(false);

        private readonly long _touchTimeoutTicks;

        protected ExclusiveTouchBase(ushort vid, ushort pid, string identifier, int packetSize,
            int minX, int minY, int maxX, int maxY, bool flip, int radius,
            float aExtraRadius = 0, float bExtraRadius = 0, float cExtraRadius = 0,
            float dExtraRadius = 0, float eExtraRadius = 0, int timeoutMilliseconds = 20)
        {
            _vid = vid;
            _pid = pid;
            _identifier = identifier;
            _packetSize = packetSize;
            _cancellationToken = MajEnv.GlobalCT;
            _touchTimeoutTicks = Stopwatch.Frequency * timeoutMilliseconds / 1000;
            _touchSensorMapper = new TouchSensorMapper(minX, minY, maxX, maxY, radius, flip,
                aExtraRadius, bExtraRadius, cExtraRadius, dExtraRadius, eExtraRadius);
        }

        protected virtual string DiagnosticName => "ExclusiveTouch";

        protected virtual void OnDeviceConnected() { }

        protected virtual void OnDeviceDisconnected() { }

        protected virtual void InitializeDevice(WinUsbIo.Device device) { }

        /// <summary>按 WinUSB 接口解析实际使用的接口和读端点。</summary>
        protected abstract TouchEndpoint ResolveEndpoint(WinUsbIo.DevicePath devicePath);

        protected abstract void OnTouchData(byte[] data);

        public bool IsConnected
        {
            get
            {
                lock (_deviceLock)
                {
                    return _connection != null;
                }
            }
        }

        public bool Start(bool logConnectionFailure = true)
        {
            _stopping = false;

            for (var i = 0; i < _allFingerPoints.Length; i++)
            {
                _allFingerPoints[i] = new TouchPoint();
            }

            if (!TryConnectDevice())
            {
                if (logConnectionFailure)
                {
                    MajDebug.LogWarning($"[{DiagnosticName}] 未找到设备");
                }
                return false;
            }

            _cancellationToken.Register(Stop);

            var readThread = new Thread(ReadThread)
            {
                IsBackground = true,
                Name = $"IO/{DiagnosticName} Thread",
                Priority = MajEnv.THREAD_PRIORITY_IO
            };
            readThread.Start();
            return true;
        }

        public void Stop()
        {
            _stopping = true;
            CloseCurrentDevice();
            _dataSignal.Set();
        }

        private bool TryConnectDevice()
        {
            List<WinUsbIo.DevicePath> paths;
            try
            {
                paths = WinUsbIo.EnumerateWinUsbInterfaces(_vid, _pid);
            }
            catch (Exception e)
            {
                MajDebug.LogError($"[{DiagnosticName}] 枚举设备失败: {e.Message}");
                return false;
            }

            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index];
                WinUsbIo.Device? newDevice = null;
                try
                {
                    var metadataMatch = path.MatchesIdentifier(_identifier);
                    var selectedEndpoint = ResolveEndpoint(path);
                    newDevice = WinUsbIo.Device.Open(path.Path, selectedEndpoint.EndpointId);
                    newDevice.SetAutoSuspend(false);

                    if (!metadataMatch && !MatchesSerial(newDevice))
                    {
                        newDevice.Dispose();
                        continue;
                    }

                    InitializeDevice(newDevice);
                    OnDeviceConnected();
                    lock (_deviceLock)
                    {
                        if (_stopping)
                        {
                            CloseDevice(newDevice);
                            return false;
                        }

                        _connection = newDevice;
                    }
                    MajDebug.LogInfo($"[{DiagnosticName}] 已连接 {path.Path} " +
                                     $"(interface {selectedEndpoint.InterfaceNumber}, endpoint 0x{selectedEndpoint.EndpointId:X2})");
                    return true;
                }
                catch (Exception e)
                {
                    newDevice?.Dispose();
                    MajDebug.LogDebug($"[{DiagnosticName}] 打开候选设备失败 {path.Path}: {e.Message}");
                }
            }

            return false;
        }

        private bool MatchesSerial(WinUsbIo.Device device)
        {
            var serial = device.SerialNumber?.Trim();
            return !string.IsNullOrWhiteSpace(serial) &&
                   string.Equals(_identifier?.Trim(), serial, StringComparison.OrdinalIgnoreCase);
        }

        private void CloseCurrentDevice()
        {
            WinUsbIo.Device? oldConnection;
            lock (_deviceLock)
            {
                oldConnection = _connection;
                _connection = null;
            }

            if (oldConnection != null)
            {
                CloseDevice(oldConnection);
                OnDeviceDisconnected();
            }
        }

        private bool IsCurrentDevice(WinUsbIo.Device target)
        {
            lock (_deviceLock)
            {
                return ReferenceEquals(target, _connection);
            }
        }

        private void CloseDevice(WinUsbIo.Device target)
        {
            try
            {
                target.Dispose();
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"[{DiagnosticName}] 关闭设备失败: {e.Message}");
            }
        }

        private void ReadThread()
        {
            var buffer = new byte[_packetSize];

            try
            {
                while (!_stopping)
                {
                    WinUsbIo.Device? currentConnection;
                    lock (_deviceLock)
                    {
                        currentConnection = _connection;
                    }

                    if (currentConnection == null)
                    {
                        _dataSignal.WaitOne(1000);
                        if (_stopping) break;
                        TryConnectDevice();
                        continue;
                    }

                    try
                    {
                        while (!_stopping)
                        {
                            var bytesRead = currentConnection.Read(buffer, 0, buffer.Length, 100);
                            if (bytesRead == 0) continue; // 超时，继续等
                            if (bytesRead < 0)
                            {
                                if (_stopping) break;
                                MajDebug.LogInfo($"[{DiagnosticName}] 读取错误，尝试重连");
                                CloseCurrentDevice();
                                break;
                            }

                            if (IsCurrentDevice(currentConnection))
                            {
                                OnTouchData(buffer);
                            }
                            // 复用缓冲：清掉尾部残留，避免短读时把上一次报告当成本次数据
                            Array.Clear(buffer, 0, buffer.Length);
                        }
                    }
                    catch (Exception e)
                    {
                        if (_stopping) break;
                        MajDebug.LogWarning($"[{DiagnosticName}] 读取异常: {e.Message}，尝试重连");
                        CloseCurrentDevice();
                    }
                }
            }
            finally
            {
                CloseCurrentDevice();
            }
        }

        private void ApplyFinger(TouchUpdate update, long timestamp)
        {
            if (update.FingerId < 0 || update.FingerId >= 256) return;

            var point = _allFingerPoints[update.FingerId];
            if (update.IsPressed)
            {
                point.Mask = _touchSensorMapper.ParseTouchPoint(update.X, update.Y);
                point.IsActive = true;
                point.LastUpdateTick = timestamp;
            }
            else
            {
                point.IsActive = false;
            }
        }

        protected void HandleFinger(ushort x, ushort y, int fingerId, bool isPressed)
        {
            if (fingerId < 0 || fingerId >= 256) return;

            lock (_touchLock)
            {
                ApplyFinger(new TouchUpdate(x, y, fingerId, isPressed), Stopwatch.GetTimestamp());
                _touchLatch.Update(ComputeActiveMask());
            }
            _dataSignal.Set();
        }

        private void HandleUpdates(List<TouchUpdate> updates, bool replaceState)
        {
            lock (_touchLock)
            {
                var now = Stopwatch.GetTimestamp();
                if (replaceState)
                {
                    for (var i = 0; i < _allFingerPoints.Length; i++)
                    {
                        _allFingerPoints[i].IsActive = false;
                    }
                }

                for (var i = 0; i < updates.Count; i++)
                {
                    ApplyFinger(updates[i], now);
                }

                _touchLatch.Update(ComputeActiveMask());
            }
            _dataSignal.Set();
        }

        /// <summary>提交一帧完整快照：帧内未出现的触点全部释放。</summary>
        protected void HandleFrame(List<TouchUpdate> updates)
            => HandleUpdates(updates, replaceState: true);

        /// <summary>仅提交帧内的释放，保留仍在按下的触点。</summary>
        protected void HandleReleases(List<TouchUpdate> updates)
            => HandleUpdates(updates, replaceState: false);

        private ulong ComputeActiveMask()
        {
            var mask = 0UL;
            for (var i = 0; i < _allFingerPoints.Length; i++)
            {
                if (_allFingerPoints[i].IsActive)
                {
                    mask |= _allFingerPoints[i].Mask;
                }
            }
            return mask;
        }

        /// <summary>读取当前触摸状态（34 位掩码），并清理超时未更新的触点。</summary>
        public ulong GetTouchState()
        {
            lock (_touchLock)
            {
                var now = Stopwatch.GetTimestamp();
                for (var i = 0; i < _allFingerPoints.Length; i++)
                {
                    var point = _allFingerPoints[i];
                    if (point.IsActive && now - point.LastUpdateTick > _touchTimeoutTicks)
                    {
                        point.IsActive = false;
                    }
                }

                var state = ComputeActiveMask();
                _touchLatch.Update(state);
                return _touchLatch.Read();
            }
        }

        /// <summary>等待新数据或超时，用于轮询循环降低空转。</summary>
        public void WaitForData(int millisecondsTimeout)
        {
            _dataSignal.WaitOne(millisecondsTimeout);
        }
    }
}
#endif
