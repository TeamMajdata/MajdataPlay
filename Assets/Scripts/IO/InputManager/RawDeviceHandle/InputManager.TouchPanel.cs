#if UNITY_STANDALONE
using HidSharp;
using HidSharp.Reports;
#endif
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Numerics;
using MajdataPlay.Runtime;
using MajdataPlay.Settings;
using MajdataPlay.Threading;
using MajdataPlay.Utils;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

//using Microsoft.Win32;
//using System.Windows.Forms;
//using Application = UnityEngine.Application;
//using System.Security.Policy;
#nullable enable
namespace MajdataPlay.IO
{
    internal static unsafe partial class InputManager
    {
#if UNITY_STANDALONE
        static class TouchPanel
        {
            const string DAEMON_THREAD_NAME = "IO/TouchPanel Thread";
            public static bool IsConnected { get; private set; } = false;

            static int _isInited = 0;
            static bool _isEnabled = false;
            static SpinLock _syncLock = new();
            static Task _touchPanelUpdateLoop = Task.CompletedTask;

            readonly static bool[] _sensorStates = new bool[35];
            readonly static bool[] _sensorRealTimeStates = new bool[35];
            readonly static bool[] _isSensorHadOn = new bool[35];
            readonly static bool[] _isSensorHadOff = new bool[35];

            readonly static bool[] _isSensorHadOnInternal = new bool[35];
            readonly static bool[] _isSensorHadOffInternal = new bool[35];

            #region Public Methods
            public static void Init()
            {
                if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0)
                {
                    return;
                }
                MajDebug.LogInfo("[TouchPanel]Start initialization");
                _isEnabled = MajEnv.Settings.IO.InputDevice.TouchPanel.Enable;
                if (!_isEnabled)
                {
                    MajDebug.LogInfo("[TouchPanel]Disabled");
                    return;
                }
                if (!_touchPanelUpdateLoop.IsCompleted)
                {
                    return;
                }
                var deviceManufacturer = IODetector.DeviceManufacturer;
                switch (deviceManufacturer)
                {
                    case DeviceManufacturerOption.Yuan:
                    case DeviceManufacturerOption.General:
                        _touchPanelUpdateLoop = Task.Factory.StartNew(SerialPortUpdateLoop, TaskCreationOptions.LongRunning);
                        break;
                    case DeviceManufacturerOption.Dao:
                        _touchPanelUpdateLoop = Task.Factory.StartNew(SlaveThreadUpdateLoop, TaskCreationOptions.LongRunning);
                        break;
#if UNITY_STANDALONE_WIN
                    case DeviceManufacturerOption.Nov:
                        _touchPanelUpdateLoop = Task.Factory.StartNew(PdxUpdateLoop, TaskCreationOptions.LongRunning);
                        break;
#endif
                    case DeviceManufacturerOption.Pipe:
                        _touchPanelUpdateLoop = Task.Factory.StartNew(PipeUpdateLoop, TaskCreationOptions.LongRunning);
                        break;
                    default:
                        MajDebug.LogWarning($"Not supported touch panel manufacturer: {MajEnv.Settings.IO.Manufacturer}");
                        break;
                }
                _touchPanelUpdateLoop.RegisterAsWorker("TouchPanel I/O Worker");
                MajDebug.LogInfo("[TouchPanel]Initialization completed");
            }
            /// <summary>
            /// Update the touchpanel state of the this frame
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void OnPreUpdate()
            {
                Profiler.BeginSample("TouchPanel.OnPreUpdate");
                ref var @lock = ref _syncLock;
                var isLocked = false;
                try
                {
                    @lock.Enter(ref isLocked);
                    var hadOn = _isSensorHadOn.AsSpan();
                    var hadOff = _isSensorHadOff.AsSpan();
                    var states = _sensorStates.AsSpan();
                    var hadOnInternal = _isSensorHadOnInternal.AsSpan();
                    var hadOffInternal = _isSensorHadOffInternal.AsSpan();
                    var realTimeStates = _sensorRealTimeStates.AsSpan();

                    hadOnInternal.CopyTo(hadOn);
                    hadOffInternal.CopyTo(hadOff);
                    realTimeStates.CopyTo(states);

                    hadOnInternal.Clear();
                    hadOffInternal.Clear();
                }
                finally
                {
                    if (isLocked)
                    {
                        @lock.Exit();
                    }
                }
                Profiler.EndSample();
            }
            /// <summary>
            /// See also <seealso cref="IsHadOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(SensorArea area)
            {
                if (area < SensorArea.C)
                {
                    return _isSensorHadOn[(int)area];
                }
                else if (area == SensorArea.C)
                {
                    return _isSensorHadOn[16] || _isSensorHadOn[17];
                }
                else if (area <= SensorArea.E8)
                {
                    return _isSensorHadOn[(int)area + 1];
                }
                return false;
            }
            /// <summary>
            /// See also <seealso cref="IsOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(SensorArea area)
            {
                if (area < SensorArea.C)
                {
                    return _sensorStates[(int)area];
                }
                else if (area == SensorArea.C)
                {
                    return _sensorStates[16] || _sensorStates[17];
                }
                else if (area <= SensorArea.E8)
                {
                    return _sensorStates[(int)area + 1];
                }
                return false;
            }
            /// <summary>
            /// See also <seealso cref="IsHadOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(SensorArea area)
            {
                if (area < SensorArea.C)
                {
                    return _isSensorHadOff[(int)area];
                }
                else if (area == SensorArea.C)
                {
                    return _isSensorHadOff[16] && _isSensorHadOff[17];
                }
                else if (area <= SensorArea.E8)
                {
                    return _isSensorHadOff[(int)area + 1];
                }
                return false;
            }
            /// <summary>
            /// See also <seealso cref="IsOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(SensorArea area)
            {
                return !IsOn(area);
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(SensorArea area)
            {
                if (area < SensorArea.C)
                {
                    return _sensorRealTimeStates[(int)area];
                }
                else if (area == SensorArea.C)
                {
                    return _sensorRealTimeStates[16] || _sensorRealTimeStates[17];
                }
                else if (area <= SensorArea.E8)
                {
                    return _sensorRealTimeStates[(int)area + 1];
                }
                return false;
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(SensorArea area)
            {
                return !IsCurrentlyOn(area);
            }



            /// <summary>
            /// Determines whether the sensor at the given index was ever ON
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor was ON at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(int index)
            {
                if (!index.InRange(0, 33))
                    return false;

                return _isSensorHadOn[index];
            }
            /// <summary>
            /// Determines whether the sensor at the given index is ON in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor state is ON in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(int index)
            {
                if (!index.InRange(0, 33))
                    return false;

                return _sensorStates[index];
            }
            /// <summary>
            /// Determines whether the sensor at the given index was ever OFF
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor was OFF at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(int index)
            {
                if (!index.InRange(0, 33))
                    return false;

                return _isSensorHadOff[index];
            }
            /// <summary>
            /// Determines whether the sensor at the given index is OFF in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor state is OFF in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(int index)
            {
                return !IsOn(index);
            }
            /// <summary>
            /// Retrieves the real‑time state of the sensor at the given index
            /// as read from the IO thread, indicating whether it is currently ON.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor is ON according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(int index)
            {
                if (!index.InRange(0, 33))
                    return false;

                return _sensorRealTimeStates[index];
            }
            /// <summary>
            /// Retrieves the real‑time state of the sensor at the given index
            /// as read from the IO thread, indicating whether it is currently OFF.
            /// </summary>
            /// <param name="index">
            /// Zero‑based sensor index (valid range 0-33).
            /// </param>
            /// <returns>
            /// True if the sensor is OFF according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(int index)
            {
                return !IsCurrentlyOn(index);
            }
            #endregion

            static void SerialPortUpdateLoop()
            {
                ref var @lock = ref _syncLock;
                var serialPortOptions = IODetector.TouchPanelSerialConnInfo;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var pollingRate = _sensorPollingRateMs;
                var comPort = serialPortOptions.PortName;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;

                var buffer = (stackalloc byte[8192]);
                var readBuffer = new SpanBuffer(buffer);

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(TouchPanel), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(TouchPanel), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                var serialDevice = default(SerialDevice?);
                var serialStream = default(SerialStream?);
                var isReconnecting = false;
                stopwatch.Start();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC);
                        readBuffer.Clear();
                        serialDevice = DeviceList.Local.GetSerialDeviceOrNull(comPort);
                        serialStream = default(SerialStream?);
                        if (serialDevice is null)
                        {
                            if (isReconnecting)
                            {
                                MajDebug.LogError(nameof(TouchPanel), $"{comPort} was lost, waiting for serial device to reconnect");
                                continue;
                            }
                            else
                            {
                                MajDebug.LogWarning(nameof(TouchPanel), $"{comPort} not found, using Mouse as fallback");
                                return;
                            }
                        }
                        else
                        {
                            MajDebug.LogInfo(nameof(TouchPanel), $"Trying to open serial port \"{comPort}\" with {serialPortOptions.BaudRate} baud rate...");
                            if (serialDevice.TryOpen(out serialStream))
                            {
                                MajDebug.LogInfo(nameof(TouchPanel), $"\"{comPort}\" is opened");
                                serialStream.BaudRate = serialPortOptions.BaudRate;
                                serialStream.DataBits = 8;
                                serialStream.Parity = SerialParity.None;
                                serialStream.StopBits = 1;
                                serialStream.DtrEnable = true;
                                serialStream.RtsEnable = true;
                                serialStream.ReadTimeout = 2000;
                                serialStream.WriteTimeout = 2000;
                                if (InitTouchPanel(serialStream))
                                {
                                    MajDebug.LogInfo(nameof(TouchPanel), "Connected");
                                }
                                else
                                {
                                    MajDebug.LogError(nameof(TouchPanel), "Failed to initialize the touch panel.");
                                    if (isReconnecting)
                                    {
                                        serialStream?.Close();
                                        serialStream?.Dispose();
                                        serialStream = default;
                                        serialDevice = default;
                                        MajDebug.LogInfo(nameof(TouchPanel), $"Disconnected");
                                        continue;
                                    }
                                    else
                                    {
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                if (isReconnecting)
                                {
                                    MajDebug.LogError(nameof(TouchPanel), $"Cannot open {comPort}");
                                    continue;
                                }
                                else
                                {
                                    MajDebug.LogError(nameof(TouchPanel), $"Cannot open {comPort}, using Mouse as fallback.");
                                    return;
                                }
                            }
                        }
                        IsConnected = true;
                        isReconnecting = true;
                        t1 = stopwatch.Elapsed;
                        #region Polling
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                ReadFromSerialStream(serialStream, _sensorRealTimeStates, ref readBuffer);
                                IsConnected = true;
                                var isLocked = false;
                                try
                                {
                                    @lock.Enter(ref isLocked);
                                    var sensorRealTimeStates = _sensorRealTimeStates.AsSpan();
                                    var isSensorHadOnInternal = _isSensorHadOnInternal.AsSpan();
                                    var isSensorHadOffInternal = _isSensorHadOffInternal.AsSpan();

                                    for (var i = 0; i < 35; i++)
                                    {
                                        var state = sensorRealTimeStates[i];
                                        isSensorHadOnInternal[i] |= state;
                                        isSensorHadOffInternal[i] |= !state;
                                    }
                                }
                                finally
                                {
                                    if (isLocked)
                                    {
                                        @lock.Exit();
                                    }
                                }
                            }
                            catch (IOException e)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(TouchPanel), e);
                                serialStream?.Close();
                                serialStream?.Dispose();
                                serialStream = default;
                                serialDevice = default;
                                MajDebug.LogInfo(nameof(TouchPanel), $"Disconnected");
                                break;
                            }
                            catch (TimeoutException)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(TouchPanel), "Read timeout");
                            }
                            catch (Exception e)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(TouchPanel), e);
                            }
                            finally
                            {
                                if (pollingRate.TotalMilliseconds > 0)
                                {
                                    var t2 = stopwatch.Elapsed;
                                    var elapsed = t2 - t1;
                                    t1 = t2;
                                    if (elapsed < pollingRate)
                                    {
                                        Thread.Sleep(pollingRate - elapsed);
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                }
                finally
                {
                    _useDummy = true;
                    IsConnected = false;
                    serialStream?.Close();
                    serialStream?.Dispose();
                    MajDebug.LogWarning(nameof(TouchPanel), "Thread has exited");
                }
            }
#if UNITY_STANDALONE_WIN
            static void PdxUpdateLoop()
            {
                ref var @lock = ref _syncLock;
                var touchPanelOptions = MajEnv.Settings.IO.InputDevice.TouchPanel;
                var capacitiveOptions = touchPanelOptions.CapacitivePanelOptions;
                var usbOptions = IODetector.TouchPanelUsbConnInfo;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var pollingRate = _sensorPollingRateMs;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                ExclusiveTouchHost.Start(
                    () => new PdxTouchDevice((ushort)usbOptions.VendorId, (ushort)usbOptions.ProductId,
                        usbOptions.DeviceName, capacitiveOptions.TouchRadius, capacitiveOptions.RadiusOffset),
                    () => new FlTouchDevice(usbOptions.DeviceName, capacitiveOptions.TouchRadius,
                        capacitiveOptions.RadiusOffset));

                try
                {
                    stopwatch.Start();
                    while (!token.IsCancellationRequested)
                    {
                        var touchMask = ExclusiveTouchHost.GetTouchState();
                        IsConnected = ExclusiveTouchHost.IsConnected;
                        var isLocked = false;
                        try
                        {
                            @lock.Enter(ref isLocked);
                            var sensorRealTimeStates = _sensorRealTimeStates.AsSpan();
                            var isSensorHadOnInternal = _isSensorHadOnInternal.AsSpan();
                            var isSensorHadOffInternal = _isSensorHadOffInternal.AsSpan();

                            for (var i = 0; i < 35; i++)
                            {
                                var state = (touchMask & (1UL << i)) != 0;
                                sensorRealTimeStates[i] = state;
                                isSensorHadOnInternal[i] |= state;
                                isSensorHadOffInternal[i] |= !state;
                            }
                        }
                        finally
                        {
                            if (isLocked)
                            {
                                @lock.Exit();
                            }
                        }

                        if (pollingRate.TotalMilliseconds > 0)
                        {
                            var t2 = stopwatch.Elapsed;
                            var elapsed = t2 - t1;
                            t1 = t2;
                            if (elapsed < pollingRate)
                            {
                                Thread.Sleep(pollingRate - elapsed);
                                continue;
                            }
                        }
                        ExclusiveTouchHost.WaitForData(2);
                    }
                }
                finally
                {
                    ExclusiveTouchHost.Stop();
                    IsConnected = false;
                }
            }
#endif
            static void SlaveThreadUpdateLoop()
            {
                ref var @lock = ref _syncLock;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var manufacturer = IODetector.DeviceManufacturer;

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                try
                {
                    _ioThreadSync.WaitReadReady();
                    ReadOnlySpan<byte> buffer = _ioThreadSync.ReadBuffer;
                    IsConnected = true;
                    MajDebug.LogInfo($"[TouchPanel]Slave thread has started");
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            _ioThreadSync.WaitReadReady();
                            switch (manufacturer)
                            {
                                case DeviceManufacturerOption.Dao:
                                    DaoHIDTouchPanel.Parse(buffer, _sensorRealTimeStates);
                                    break;
                                case DeviceManufacturerOption.Pipe:
                                    PipeTouchPanel.Parse(buffer, _sensorRealTimeStates);
                                    break;
                            }

                            _ioThreadSync.SignalReadConsumed();
                            var isLocked = false;
                            try
                            {
                                @lock.Enter(ref isLocked);
                                var sensorRealTimeStates = _sensorRealTimeStates.AsSpan();
                                var isSensorHadOnInternal = _isSensorHadOnInternal.AsSpan();
                                var isSensorHadOffInternal = _isSensorHadOffInternal.AsSpan();

                                for (var i = 0; i < 35; i++)
                                {
                                    var state = sensorRealTimeStates[i];
                                    isSensorHadOnInternal[i] |= state;
                                    isSensorHadOffInternal[i] |= !state;
                                }
                            }
                            finally
                            {
                                if (isLocked)
                                {
                                    @lock.Exit();
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (IOException ioE)
                        {
                            IsConnected = false;
                            MajDebug.LogError($"[TouchPanel]{ioE}");
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError($"[TouchPanel]{e}");
                        }
                    }
                }
                finally
                {
                    IsConnected = false;
                }
            }
            static void PipeUpdateLoop()
            {
                /// Payload structure
                ///                    64bit                    
                /// |<-            Device states             ->|                         
                /// 00000000 00000000 00000000 00000000 00000000
                /// 

                ref var @lock = ref _syncLock;
                var pipeName = $"MajdataPlay.IO.TouchPanel.{IODetector.PlayerIndex}P";
                var token = MajEnv.GlobalCT;
                var pollingRate = _sensorPollingRateMs;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var currentThread = Thread.CurrentThread;
                var callback = (PipePacket.PacketReceivedCallback)((PipePacket packet) =>
                {
                    if (packet.Type == PipePacketType.HeartBeat)
                    {
                        return;
                    }
                    else if (packet.Payload.Length != 64 / 8)
                    {
                        return;
                    }
                    var data = BinaryPrimitives.ReadUInt64LittleEndian(packet.Payload);
                    ref var @lock = ref _syncLock;
                    var isLocked = false;
                    try
                    {
                        @lock.Enter(ref isLocked);
                        var states = _sensorRealTimeStates.AsSpan();
                        var hadOn = _isSensorHadOnInternal.AsSpan();
                        var hadOff = _isSensorHadOffInternal.AsSpan();

                        for (int i = 0; i < 35; i++)
                        {
                            ref var state = ref states[i];
                            state = (data & (1UL << i)) != 0;
                            hadOn[i] |= state;
                            hadOff[i] |= !state;
                        }
                    }
                    finally
                    {
                        if (isLocked)
                        {
                            @lock.Exit();
                        }
                    }
                });

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(TouchPanel), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(TouchPanel), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                Span<byte> rawBuffer = stackalloc byte[1024];
                Span<byte> accumulationBuffer = stackalloc byte[8192];
                while (!token.IsCancellationRequested)
                {
                    if (token.WaitHandle.WaitOne(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC))
                    {
                        break;
                    }
                    using var pipeClientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    using var cancellation = token.Register(() => pipeClientStream.Dispose());
                    try
                    {
                        try
                        {
                            MajDebug.LogInfo(nameof(TouchPanel), $"Attempting connect to pipe \"{pipeName}\"...");
                            pipeClientStream.Connect(2000);
                            MajDebug.LogInfo(nameof(TouchPanel), "Connected");
                        }
                        catch (Exception) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError(nameof(TouchPanel), $"Failed to connect to pipe\n{e}");
                            continue;
                        }
                        IsConnected = true;
                        var buffer = new SpanBuffer(accumulationBuffer);
                        stopwatch.Restart();
                        while (!token.IsCancellationRequested)
                        {
                            t1 = stopwatch.Elapsed;
                            try
                            {
                                var read = pipeClientStream.Read(rawBuffer);
                                if (read == 0)
                                {
                                    break;
                                }
                                buffer.Write(rawBuffer.Slice(0, read));
                                PipePacket.Parse(ref buffer, 1024, callback);
                            }
                            catch (Exception) when (token.IsCancellationRequested)
                            {
                                break;
                            }
                            catch (IOException ioE)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(TouchPanel), $"{ioE}");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(TouchPanel), $"{e}");
                                break;
                            }
                            finally
                            {
                                // Preserve incomplete packets until the next read.
                                if (pollingRate.TotalMilliseconds > 0)
                                {
                                    var t2 = stopwatch.Elapsed;
                                    var elapsed = t2 - t1;
                                    t1 = t2;
                                    if (elapsed < pollingRate)
                                    {
                                        Thread.Sleep(pollingRate - elapsed);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        IsConnected = false;
                        var isLocked = false;
                        try
                        {
                            @lock.Enter(ref isLocked);
                            _sensorRealTimeStates.AsSpan().Clear();
                            _isSensorHadOffInternal.AsSpan().Fill(true);
                        }
                        finally
                        {
                            if (isLocked)
                                @lock.Exit();
                            pipeClientStream.Dispose();
                        }
                    }
                }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ReadFromSerialStream(SerialStream serial, Span<bool> buffer, ref SpanBuffer readBuffer)
            {
                var tmpReadBuffer = (stackalloc byte[1024]);
                //the SerialPort.BaseStream will be eaten by serialport's own buffer so we dont do that
                var read = serial.Read(tmpReadBuffer);
                readBuffer.Write(tmpReadBuffer.Slice(0, read));
                GeneralSerialTouchPanel.Parse(ref readBuffer, buffer);
            }
            static bool InitTouchPanel(SerialStream serialStream)
            {
                try
                {
                    MajDebug.LogInfo(nameof(TouchPanel), $"Starting to initialize the touch panel...");
                    var sensConfig = MajEnv.Settings.IO.InputDevice.TouchPanel.Sensitivities;
                    var index = IODetector.PlayerIndex == 1 ? 'L' : 'R';
                    var sens = (sensConfig.A, sensConfig.B, sensConfig.C, sensConfig.D, sensConfig.E);
                    MajDebug.LogInfo(nameof(TouchPanel), $"Sensitivities:\nA:{sens.A}\nB:{sens.B}\nC:{sens.C}\nD:{sens.D}\nE:{sens.E}");
                    //see also https://github.com/Sucareto/Mai2Touch/tree/main/Mai2Touch
                    serialStream.Write("{RSET}");
                    MajDebug.LogDebug(nameof(TouchPanel), $"Sent: {{REST}}");
                    MajDebug.LogInfo(nameof(TouchPanel), "Waiting for TouchPanel reset");
#if UNITY_STANDALONE_LINUX
                    Thread.Sleep(4000);
#elif UNITY_STANDALONE_WIN
                    // Calling Thread.Sleep on Windows causes the driver to hang
                    // So we read the first 10 bytes returned by the device to determine whether the reset is complete
                    try
                    {
                        var buffer = (stackalloc byte[10]);
                        var offset = 0;
                        while (offset < 10)
                        {
                            var read = serialStream.Read(buffer.Slice(offset, 10 - offset));
                            offset += read;
                        }
                        MajDebug.LogDebug(nameof(TouchPanel), $"Recv: {Encoding.UTF8.GetString(buffer)}");
                    }
                    catch (TimeoutException)
                    {
                        MajDebug.LogWarning(nameof(TouchPanel), "RSET read response timeout");
                    }
#endif
                    MajDebug.LogInfo(nameof(TouchPanel), "TouchPanel has been reset");

                    serialStream.Write("{HALT}");
                    MajDebug.LogDebug(nameof(TouchPanel), $"Sent: {{HALT}}");
                    //send ratio
                    for (byte a = 0x41; a <= 0x62; a++)
                    {
                        var cmd = $"{{{index}{(char)a}r2}}";
                        serialStream.Write(cmd);
                        MajDebug.LogDebug(nameof(TouchPanel), $"Sent: {cmd}");
                    }
                    try
                    {
                        for (byte a = 0x41; a <= 0x62; a++)
                        {
                            var value = GetSensitivityValue(a, sens);
                            var cmd = $"{{{index}{(char)a}k{(char)value}}}";
                            serialStream.Write(cmd);
                            MajDebug.LogDebug(nameof(TouchPanel), $"Sent: {cmd}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        MajDebug.LogWarning(nameof(TouchPanel), $"TouchPanel does not support sensitivity override: Write timeout");
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError(nameof(TouchPanel), $"Failed to override sensitivity: \n{e}");
                        return false;
                    }
                    serialStream.Write("{STAT}");
                    MajDebug.LogDebug(nameof(TouchPanel), $"Sent: {{STAT}}");
                    MajDebug.LogInfo(nameof(TouchPanel), "Initialization complete.");
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
            static class GeneralSerialTouchPanel
            {
                const int PACKET_LENGTH = 9; // '(' + body(7) + ')' = 9

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Parse(ref SpanBuffer packet, Span<bool> buffer)
                {
                    if (packet.IsEmpty || buffer.Length < 35)
                    {
                        return;
                    }

                    while (true)
                    {
                        var data = packet.Data;
                        var headIndex = data.IndexOf((byte)'(');

                        if (headIndex == -1)
                        {
                            packet.Clear();
                            return;
                        }

                        if (headIndex > 0)
                        {
                            packet.Skip(headIndex);
                            data = packet.Data;
                        }

                        if (data.Length < PACKET_LENGTH)
                        {
                            return;
                        }

                        if (data[PACKET_LENGTH - 1] == ')')
                        {
                            var body = data.Slice(1, 7);
                            var k = 0;

                            for (var i = 0; i < 7; i++)
                            {
                                var b = body[i];
                                for (var j = 0; j < 5; j++)
                                {
                                    buffer[k++] = (b & (1 << j)) != 0;
                                }
                            }

                            packet.Skip(PACKET_LENGTH);
                        }
                        else
                        {
                            packet.Skip(1);
                        }
                    }
                }
            }
            static class PipeTouchPanel
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
                    var data = BitConverter.ToUInt64(reportData);
                    for (var i = 0; i < 35; i++)
                    {
                        buffer[i] = (data & (1UL << (12 + i))) != 0;
                    }
                }
            }
            static class DaoHIDTouchPanel
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
                    reportData = reportData.Slice(1); //skip report id
                    var A = reportData[0];
                    var B = reportData[1];
                    var C = reportData[2];
                    var D = reportData[3];
                    var E = reportData[4];

                    for (var i = 0; i < 8; i++)
                    {
                        var bit = 1 << i;
                        buffer[i] = (A & bit) != 0;
                        buffer[i + 8] = (B & bit) != 0;
                        buffer[i + 18] = (D & bit) != 0;
                        buffer[i + 26] = (E & bit) != 0;
                    }
                    buffer[16] = (C & (1 << 0)) != 0; //C1
                    buffer[17] = (C & (1 << 1)) != 0; //C2
                }
            }

        }
#endif
    }
}
