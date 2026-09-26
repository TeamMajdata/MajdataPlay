#if UNITY_STANDALONE
using HidSharp;
using HidSharp.Reports;
#endif
using LibUsbDotNet;
using LibUsbDotNet.Main;
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
                    case DeviceManufacturerOption.Nov:
                        _touchPanelUpdateLoop = Task.Factory.StartNew(UsbUpdateLoop, TaskCreationOptions.LongRunning);
                        break;
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
            static void UsbUpdateLoop()
            {
                const int READ_TIMEOUT_MS = 100;

                ref var @lock = ref _syncLock;
                var touchPanelOptions = MajEnv.Settings.IO.InputDevice.TouchPanel;
                var usbOptions = IODetector.TouchPanelUsbConnInfo;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var pollingRate = _sensorPollingRateMs;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var pid = usbOptions.ProductId;
                var vid = usbOptions.VendorId;
                var manufacturer = IODetector.DeviceManufacturer;
                var deviceName = string.IsNullOrEmpty(usbOptions.DeviceName) ? GetUsbDeviceName(manufacturer) : usbOptions.DeviceName;

                var deviceFinder = new UsbDeviceFinder(vid, pid);
                var usbDevice = UsbDevice.OpenUsbDevice(deviceFinder);

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                if (usbDevice is null)
                {
                    MajDebug.LogError("[TouchPanel]usb device not found or cannot open usb device");
                    return;
                }
                else if (usbDevice is IUsbDevice wholeDevice)
                {
                    wholeDevice.SetConfiguration(usbOptions.Configuration);
                    wholeDevice.ClaimInterface(usbOptions.Interface);
                }

                try
                {
                    var bufferArray = new byte[usbOptions.PacketSize];
                    Span<byte> buffer = bufferArray.AsSpan();
                    MajDebug.LogInfo($"TouchPanel connected\nDevice: {usbDevice.Info} (VID {vid}, PID {pid})");
                    using var usbReader = usbDevice.OpenEndpointReader(ReadEndpointID.Ep02);
                    stopwatch.Start();
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var now = MajTimeline.UnscaledTime;
                            var usbReadResult = usbReader.Read(bufferArray, READ_TIMEOUT_MS, out int bytesRead);
                            if (usbReadResult != ErrorCode.None)
                            {
                                if (usbReadResult != ErrorCode.IoTimedOut)
                                {
                                    MajDebug.LogError($"[TouchPanel]Usb read error: {usbReadResult}");
                                    break;
                                }
                            }
                            else if (bytesRead != usbOptions.PacketSize)
                            {
                                continue;
                            }
                            NovHIDTouchPanel.Parse(buffer.Slice(0, bytesRead), _sensorRealTimeStates);
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
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (IOException ioE)
                        {
                            IsConnected = false;
                            MajDebug.LogError($"[TouchPanel]from USB reader: \n{ioE}");
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError($"[TouchPanel]from USB reader: \n{e}");
                        }
                        finally
                        {
                            buffer.Clear();
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
                    usbDevice.Close();
                    IsConnected = false;
                }
            }
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
            static string GetUsbDeviceName(DeviceManufacturerOption manufacturer)
            {
                switch (manufacturer)
                {
                    case DeviceManufacturerOption.General:
                    case DeviceManufacturerOption.Yuan:
                    case DeviceManufacturerOption.Dao:
                        throw new NotSupportedException();
                    case DeviceManufacturerOption.Nov:
                        return string.Empty;
                    default:
                        throw new NotSupportedException();
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
            static class NovHIDTouchPanel
            {
                const byte REPORT_ID = 2;
                const int MAX_TOUCH_POINTS = 10;
                const int TOUCH_POINT_SIZE = 6;

                // PDX 坐标范围
                const float MIN_X = 18432f;
                const float MIN_Y = 0f;
                const float MAX_X = 0f;
                const float MAX_Y = 32767f;
                const bool FLIP = true;

                const int TOUCH_TIMEOUT_MS = 20;

                readonly static TouchSensorMapper _mapper;
                readonly static FingerPoint[] _fingerPoints = new FingerPoint[256];

                static NovHIDTouchPanel()
                {
                    var rad = MajEnv.Settings.IO.InputDevice.TouchPanel.CapacitivePanelOptions.TouchRadius;
                    var radiusOffset = MajEnv.Settings.IO.InputDevice.TouchPanel.CapacitivePanelOptions.RadiusOffset;
                    _mapper = new TouchSensorMapper(MIN_X, MIN_Y, MAX_X, MAX_Y, rad, radiusOffset, FLIP);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
                    const int FINGER_ID_INDEX_OFFSET = 1;
                    const int X_POINT_INDEX_OFFSET = 2;
                    const int Y_POINT_INDEX_OFFSET = 4;

                    var now = MajTimeline.UnscaledTime;
                    if (reportData.IsEmpty)
                    {
                        ReadFingerPoints(buffer, now);
                        return;
                    }
                    else if (reportData[0] != REPORT_ID)
                    {
                        return;
                    }
                    reportData = reportData.Slice(1); //skip report id
                    // 解析触摸点
                    for (var i = 0; i < MAX_TOUCH_POINTS; i++)
                    {
                        var index = i * TOUCH_POINT_SIZE;
                        if (reportData[index] == 0)
                        {
                            continue;
                        }

                        var isPressed = (reportData[index] & 0x01) == 1;
                        var fingerId = reportData[index + FINGER_ID_INDEX_OFFSET];
                        var x = BitConverter.ToUInt16(reportData.Slice(index + X_POINT_INDEX_OFFSET));
                        var y = BitConverter.ToUInt16(reportData.Slice(index + Y_POINT_INDEX_OFFSET));

                        HandleFinger(x, y, fingerId, isPressed, now);
                    }
                    ReadFingerPoints(buffer, now);
                }
                static void HandleFinger(ushort x, ushort y, byte fingerId, bool isPressed, TimeSpan now)
                {
                    ref var point = ref _fingerPoints[fingerId];

                    if (isPressed)
                    {
                        ulong fingerTouchMask = _mapper.ParseTouchPoint(x, y);
                        point.IsActive = true;
                        point.Mask = fingerTouchMask;
                        point.LastActive = now;
                    }
                    else
                    {
                        point.IsActive = false;
                        point.Mask = 0;
                    }
                }
                static void ReadFingerPoints(Span<bool> buffer, TimeSpan now)
                {
                    var touchMask = 0UL;
                    for (int i = 0; i < _fingerPoints.Length; i++)
                    {
                        ref var point = ref _fingerPoints[i];
                        if (point.IsActive)
                        {
                            var interval = (int)((now - point.LastActive).TotalMilliseconds);
                            if (interval > TOUCH_TIMEOUT_MS)
                            {
                                point.IsActive = false;
                                point.Mask = 0;
                            }
                            else
                            {
                                touchMask |= point.Mask;
                            }
                        }
                    }
                    for (int i = 0; i < 34; i++)
                    {
                        buffer[i] = (touchMask & (1UL << i)) != 0;
                    }
                }
                struct FingerPoint
                {
                    public bool IsActive;
                    public ulong Mask;
                    public TimeSpan LastActive;
                }
                class TouchSensorMapper
                {
                    private readonly float _minX;
                    private readonly float _minY;
                    private readonly float _maxX;
                    private readonly float _maxY;
                    private readonly float _radius;
                    private readonly bool _flip;
                    private readonly RadiusOffset _radiusOffset;

                    public TouchSensorMapper(float minX,
                        float minY,
                        float maxX,
                        float maxY,
                        float radius,
                        CapacitiveTouchPanelRadiusOffsetConfig radiusOffset,
                        bool flip)
                    {
                        _minX = minX;
                        _minY = minY;
                        _maxX = maxX;
                        _maxY = maxY;
                        _radius = radius;
                        _radiusOffset = new();
                        _flip = flip;
                    }

                    private static readonly Vector2[][] _sensors = new Vector2[][] {
                        // A1 (0)
                        MakePolygon(786, 11, new Vector2[] {
                                new Vector2(150, 28), new Vector2(245, 65), new Vector2(360, 133), new Vector2(208, 338),
                                new Vector2(145, 338), new Vector2(49, 297), new Vector2(0, 249), new Vector2(35, 0)
                            }),

                        // A2 (1)
                        MakePolygon(1091, 292, new Vector2[] {
                                new Vector2(261, 101), new Vector2(303, 195), new Vector2(339, 327), new Vector2(91, 362),
                                new Vector2(42, 314), new Vector2(0, 219), new Vector2(0, 150), new Vector2(202, 0)
                            }),

                        // A3 (2)
                        MakePolygon(1092, 786, new Vector2[] {
                                new Vector2(305, 150), new Vector2(269, 246), new Vector2(201, 364), new Vector2(0, 213),
                                new Vector2(0, 144), new Vector2(41, 48), new Vector2(89, 0), new Vector2(337, 34)
                            }),

                        // A4 (3)
                        MakePolygon(786, 1092, new Vector2[] {
                                new Vector2(260, 259), new Vector2(167, 301), new Vector2(37, 335), new Vector2(0, 83),
                                new Vector2(48, 35), new Vector2(144, 0), new Vector2(212, 0), new Vector2(364, 200)
                            }),

                        // A5 (4)
                        MakePolygon(291, 1092, new Vector2[] {
                                new Vector2(104, 259), new Vector2(197, 301), new Vector2(327, 335), new Vector2(363, 83),
                                new Vector2(316, 35), new Vector2(220, 0), new Vector2(152, 0), new Vector2(0, 201)
                            }),

                        // A6 (5)
                        MakePolygon(16, 785, new Vector2[] {
                                new Vector2(32, 150), new Vector2(68, 246), new Vector2(133, 365), new Vector2(333, 214),
                                new Vector2(333, 144), new Vector2(296, 48), new Vector2(248, 0), new Vector2(0, 35)
                            }),

                        // A7 (6)
                        MakePolygon(16, 291, new Vector2[] {
                                new Vector2(78, 101), new Vector2(36, 195), new Vector2(0, 327), new Vector2(248, 362),
                                new Vector2(297, 314), new Vector2(333, 219), new Vector2(333, 151), new Vector2(132, 0)
                            }),

                        // A8 (7)
                        MakePolygon(295, 11, new Vector2[] {
                                new Vector2(210, 28), new Vector2(115, 65), new Vector2(0, 138), new Vector2(153, 338),
                                new Vector2(215, 338), new Vector2(311, 297), new Vector2(359, 249), new Vector2(324, 0)
                                }),

                        // B1 (8)
                        MakePolygon(720, 346, new Vector2[] {
                                new Vector2(0, 78), new Vector2(78, 0), new Vector2(209, 55),
                                new Vector2(209, 165), new Vector2(180, 195), new Vector2(70, 195), new Vector2(0, 130)
                            }),

                        // B2 (9)
                        MakePolygon(900, 511, new Vector2[] {
                                new Vector2(117, 209), new Vector2(195, 132), new Vector2(140, 0),
                                new Vector2(30, 0), new Vector2(0, 30), new Vector2(0, 139), new Vector2(65, 209)
                            }),

                        // B3 (10)
                        MakePolygon(900, 721, new Vector2[] {
                                new Vector2(120, 0), new Vector2(198, 78), new Vector2(140, 208),
                                new Vector2(30, 208), new Vector2(0, 180), new Vector2(0, 71), new Vector2(65, 0)
                            }),

                        // B4 (11)
                        MakePolygon(721, 901, new Vector2[] {
                                new Vector2(0, 112), new Vector2(87, 198), new Vector2(208, 140),
                                new Vector2(208, 29), new Vector2(177, 0), new Vector2(71, 0), new Vector2(0, 65)
                            }),

                        // B5 (12)
                        MakePolygon(512, 901, new Vector2[] {
                                new Vector2(208, 112), new Vector2(121, 198), new Vector2(0, 140),
                                new Vector2(0, 29), new Vector2(31, 0), new Vector2(137, 0), new Vector2(208, 65)
                            }),

                        // B6 (13)
                        MakePolygon(349, 721, new Vector2[] {
                                new Vector2(78, 0), new Vector2(0, 78), new Vector2(58, 208),
                                new Vector2(163, 208), new Vector2(193, 180), new Vector2(193, 71), new Vector2(133, 0)
                                }),

                        // B7 (14)
                        MakePolygon(345, 511, new Vector2[] {
                                new Vector2(82, 209), new Vector2(0, 127), new Vector2(55, 0),
                                new Vector2(165, 0), new Vector2(195, 30), new Vector2(195, 139), new Vector2(137, 209)
                            }),

                        // B8 (15)
                        MakePolygon(511, 346, new Vector2[] {
                                new Vector2(209, 78), new Vector2(131, 0), new Vector2(0, 55),
                                new Vector2(0, 165), new Vector2(29, 195), new Vector2(139, 195), new Vector2(209, 130)
                            }),

                        // C1 (16)
                        MakePolygon(720, 583, new Vector2[] {
                                new Vector2(0, 0), new Vector2(60, 0), new Vector2(140, 80),
                                new Vector2(140, 200), new Vector2(60, 280), new Vector2(0, 280), new Vector2(0, 0)
                            }),

                        // C2 (17)
                        MakePolygon(579, 583, new Vector2[] {
                                new Vector2(141, 280), new Vector2(81, 280), new Vector2(0, 199),
                                new Vector2(1, 81), new Vector2(81, 0), new Vector2(141, 0), new Vector2(141, 280)
                            }),

                        // D1 (18)
                        MakePolygon(620, 6, new Vector2[] {
                                new Vector2(0, 5), new Vector2(50, 2), new Vector2(100, 0), new Vector2(150, 2),
                                new Vector2(200, 5), new Vector2(165, 253), new Vector2(100, 188), new Vector2(35, 253)
                            }),

                        // D2 (19)
                        MakePolygon(995, 144, new Vector2[] {
                                new Vector2(153, 0), new Vector2(187, 32), new Vector2(225, 67), new Vector2(259, 104),
                                new Vector2(295, 147), new Vector2(96, 297), new Vector2(96, 205), new Vector2(0, 205)
                                }),

                        // D3 (20)
                        MakePolygon(1182, 620, new Vector2[] {
                                new Vector2(248, 0), new Vector2(251, 48), new Vector2(253, 100), new Vector2(251, 150),
                                new Vector2(247, 199), new Vector2(0, 165), new Vector2(65, 100), new Vector2(0, 35)
                            }),

                        // D4 (21)
                        MakePolygon(1000, 1000, new Vector2[] {
                                new Vector2(292, 151), new Vector2(260, 187), new Vector2(225, 225), new Vector2(188, 259),
                                new Vector2(151, 291), new Vector2(0, 92), new Vector2(92, 92), new Vector2(92, 0)
                            }),

                        // D5 (22)
                        MakePolygon(621, 1175, new Vector2[] {
                                new Vector2(199, 252), new Vector2(151, 255), new Vector2(99, 257), new Vector2(49, 255),
                                new Vector2(0, 252), new Vector2(34, 0), new Vector2(99, 65), new Vector2(164, 0)
                            }),

                        // D6 (23)
                        MakePolygon(150, 1000, new Vector2[] {
                                new Vector2(140, 292), new Vector2(104, 260), new Vector2(66, 225), new Vector2(32, 188),
                                new Vector2(0, 151), new Vector2(199, 0), new Vector2(199, 92), new Vector2(291, 92)
                            }),

                        // D7 (24)
                        MakePolygon(10, 620, new Vector2[] {
                                new Vector2(5, 199), new Vector2(2, 151), new Vector2(0, 99), new Vector2(2, 49),
                                new Vector2(6, 0), new Vector2(253, 34), new Vector2(188, 99), new Vector2(253, 164)
                            }),

                        // D8 (25)
                        MakePolygon(149, 150, new Vector2[] {
                                new Vector2(0, 140), new Vector2(32, 104), new Vector2(67, 66), new Vector2(104, 32),
                                new Vector2(145, 0), new Vector2(298, 199), new Vector2(200, 199), new Vector2(200, 291)
                                }),

                        // E1 (26)
                        MakePolygon(607, 195, new Vector2[] {
                                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
                            }),

                        // E2 (27)
                        MakePolygon(930, 350, new Vector2[] {
                                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                                new Vector2(160, 0), new Vector2(0, 0)
                            }),

                        // E3 (28)
                        MakePolygon(1020, 607, new Vector2[] {
                                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
                            }),

                        // E4 (29)
                        MakePolygon(930, 930, new Vector2[] {
                                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                                new Vector2(160, 0), new Vector2(0, 0)
                            }),

                        // E5 (30)
                        MakePolygon(607, 1013, new Vector2[] {
                                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
                            }),

                        // E6 (31)
                        MakePolygon(350, 930, new Vector2[] {
                                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                                new Vector2(160, 0), new Vector2(0, 0)
                                }),

                        // E7 (32)
                        MakePolygon(200, 607, new Vector2[] {
                                new Vector2(0, 113), new Vector2(113, 0), new Vector2(226, 113), new Vector2(113, 226)
                            }),

                        // E8 (33)
                        MakePolygon(350, 350, new Vector2[] {
                                new Vector2(0, 0), new Vector2(0, 160), new Vector2(160, 160),
                                new Vector2(160, 0), new Vector2(0, 0)
                            }),
                    };

                    private static Vector2[] MakePolygon(int offsetX, int offsetY, Vector2[] points)
                    {
                        return points.Select(p => p + new Vector2(offsetX, offsetY)).ToArray();
                    }

                    public ulong ParseTouchPoint(float x, float y)
                    {
                        var canvasPoint = new Vector2(MapCoordinate(x, _minX, _maxX, 0, 1440), MapCoordinate(y, _minY, _maxY, 0, 1440));
                        if (canvasPoint.x < 0 || canvasPoint.x > 1440 || canvasPoint.y < 0 || canvasPoint.y > 1440)
                        {
                            return 0;
                        }

                        if (_flip)
                        {
                            canvasPoint = new Vector2(canvasPoint.y, canvasPoint.x);
                        }

                        ulong res = 0;

                        // 检查所有传感器
                        for (int i = 0; i < 34; i++)
                        {
                            bool isInsidePolygon;
                            var sensor = _sensors[i];
                            var radius = i switch
                            {
                                0 => _radius + _radiusOffset.A,
                                1 => _radius + _radiusOffset.A,
                                2 => _radius + _radiusOffset.A,
                                3 => _radius + _radiusOffset.A,
                                4 => _radius + _radiusOffset.A,
                                5 => _radius + _radiusOffset.A,
                                6 => _radius + _radiusOffset.A,
                                7 => _radius + _radiusOffset.A,
                                8 => _radius + _radiusOffset.B,
                                9 => _radius + _radiusOffset.B,
                                10 => _radius + _radiusOffset.B,
                                11 => _radius + _radiusOffset.B,
                                12 => _radius + _radiusOffset.B,
                                13 => _radius + _radiusOffset.B,
                                14 => _radius + _radiusOffset.B,
                                15 => _radius + _radiusOffset.B,
                                16 => _radius + _radiusOffset.C,
                                17 => _radius + _radiusOffset.C,
                                18 => _radius + _radiusOffset.D,
                                19 => _radius + _radiusOffset.D,
                                20 => _radius + _radiusOffset.D,
                                21 => _radius + _radiusOffset.D,
                                22 => _radius + _radiusOffset.D,
                                23 => _radius + _radiusOffset.D,
                                24 => _radius + _radiusOffset.D,
                                25 => _radius + _radiusOffset.D,
                                26 => _radius + _radiusOffset.E,
                                27 => _radius + _radiusOffset.E,
                                28 => _radius + _radiusOffset.E,
                                29 => _radius + _radiusOffset.E,
                                30 => _radius + _radiusOffset.E,
                                31 => _radius + _radiusOffset.E,
                                32 => _radius + _radiusOffset.E,
                                33 => _radius + _radiusOffset.E,
                                _ => _radius
                            };

                            if (radius > 0)
                            {
                                // 当有半径时，需要检查圆与多边形的关系
                                isInsidePolygon = PolygonRaycasting.IsVertDistance(sensor, canvasPoint, radius);
                                if (!isInsidePolygon)
                                {
                                    isInsidePolygon = PolygonRaycasting.IsCircleIntersectingPolygonEdges(sensor, canvasPoint, radius);
                                }
                                if (!isInsidePolygon)
                                {
                                    isInsidePolygon = PolygonRaycasting.InPointInInternal(sensor, canvasPoint);
                                }
                            }
                            else
                            {
                                // 当半径为0时，只需要检查点是否在多边形内部
                                isInsidePolygon = PolygonRaycasting.InPointInInternal(sensor, canvasPoint);
                            }

                            if (isInsidePolygon)
                            {
                                res |= 1ul << i;
                            }
                        }

                        return res;
                    }

                    /// <summary>
                    /// 线性映射坐标
                    /// </summary>
                    private float MapCoordinate(float value, float fromMin, float fromMax, float toMin, float toMax)
                    {
                        return toMin + (value - fromMin) * (toMax - toMin) / (fromMax - fromMin);
                    }
                    struct RadiusOffset
                    {
                        public readonly int A;
                        public readonly int B;
                        public readonly int C;
                        public readonly int D;
                        public readonly int E;

                        public RadiusOffset(CapacitiveTouchPanelRadiusOffsetConfig config)
                        {
                            A = config.A;
                            B = config.B;
                            C = config.C;
                            D = config.D;
                            E = config.E;
                        }
                    }
                }
            }


        }
#endif
    }
}
