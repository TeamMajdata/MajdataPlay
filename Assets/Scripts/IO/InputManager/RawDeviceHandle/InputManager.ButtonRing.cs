using Cysharp.Threading.Tasks;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using MajdataPlay.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using System.IO.Pipes;
using System.Buffers.Binary;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
#if UNITY_STANDALONE_WIN
using MajdataPlay.Platform.Win32.IO;
#endif
#if UNITY_ANDROID || UNITY_EDITOR
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.IO;
#endif
#if UNITY_IOS || UNITY_EDITOR
using MajdataPlay.Platform.iOS;
#endif

#if UNITY_STANDALONE
using HidSharp;
#endif

//using Microsoft.Win32;
//using System.Windows.Forms;
//using Application = UnityEngine.Application;
//using System.Security.Policy;
#nullable enable
namespace MajdataPlay.IO
{
    internal static partial class InputManager
    {
        static class ButtonRing
        {
            const string DAEMON_THREAD_NAME = "IO/ButtonRing Thread";
            public static bool IsConnected { get; private set; } = false;

            static int _isInited = 0;
            static bool _isEnabled = false;
            static SpinLock _syncLock = new();
            static Task _buttonRingUpdateLoop = Task.CompletedTask;
            static MobileExternalButtonRingOption _mobileExternalbuttonRingOption;

            readonly static bool[] _buttonStates = new bool[12];
            readonly static bool[] _buttonRealTimeStates = new bool[12];
            readonly static bool[] _isBtnHadOn = new bool[12];
            readonly static bool[] _isBtnHadOff = new bool[12];

            readonly static bool[] _isBtnHadOnInternal = new bool[12];
            readonly static bool[] _isBtnHadOffInternal = new bool[12];

            #region Public Methods
            public static void Init()
            {
                if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0)
                {
                    return;
                }
                MajDebug.LogInfo(nameof(ButtonRing), "Start initialization");
#if UNITY_STANDALONE
                _isEnabled = MajEnv.Settings.IO.InputDevice.ButtonRing.Enable;
                if (!_isEnabled)
                {
                    MajDebug.LogInfo(nameof(ButtonRing), "Disabled");
                    return;
                }
                else if (!_buttonRingUpdateLoop.IsCompleted)
                {
                    return;
                }
                var manufacturer = IODetector.DeviceManufacturer;
                var buttonRingDevice = IODetector.ButtonRingDevice;
                if (manufacturer == DeviceManufacturerOption.General)
                {
                    switch (buttonRingDevice)
                    {
                        case ButtonRingDeviceOption.Keyboard:
                            _buttonRingUpdateLoop = Task.Factory.StartNew(KeyboardUpdateLoop, TaskCreationOptions.LongRunning);
                            break;
                        case ButtonRingDeviceOption.HID:
                            _buttonRingUpdateLoop = Task.Factory.StartNew(HIDUpdateLoop, TaskCreationOptions.LongRunning);
                            break;
                        default:
                            MajDebug.LogWarning(nameof(ButtonRing), $"Not supported button ring device: {buttonRingDevice}");
                            break;
                    }
                }
                else if (manufacturer is DeviceManufacturerOption.Yuan or DeviceManufacturerOption.Dao)
                {
                    _buttonRingUpdateLoop = Task.Factory.StartNew(HIDUpdateLoop, TaskCreationOptions.LongRunning);
                }
                else if (manufacturer is DeviceManufacturerOption.Nov)
                {
                    _buttonRingUpdateLoop = Task.Factory.StartNew(KeyboardUpdateLoop, TaskCreationOptions.LongRunning);
                }
                else if (manufacturer is DeviceManufacturerOption.Pipe)
                {
                    _buttonRingUpdateLoop = Task.Factory.StartNew(PipeUpdateLoop, TaskCreationOptions.LongRunning);
                }
                else
                {
                    MajDebug.LogWarning(nameof(ButtonRing), $"Not supported button ring manufacturer: {manufacturer}");
                }
#elif UNITY_ANDROID || UNITY_IOS
                _mobileExternalbuttonRingOption = MajEnv.Settings.IO.InputDevice.ExternalButtonRing;
#endif
                MajDebug.LogInfo(nameof(ButtonRing), "Initialization completed");
            }
            /// <summary>
            /// Update the button ring state of the this frame
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void OnPreUpdate()
            {
                using (UnityProfiler.Create("ButtonRing.OnPreUpdate"))
                {
                    using (new LockDisposable())
                    {
#if UNITY_ANDROID || UNITY_IOS
                        switch (_mobileExternalbuttonRingOption)
                        {
                            case MobileExternalButtonRingOption.Keyboard:
                                KeyboardPreUpdate();
                                break;
                            case MobileExternalButtonRingOption.Gamepad:
                                GamepadPreUpdate();
                                break;
                        }
#endif
                        var isBtnHadOn = _isBtnHadOn.AsSpan();
                        var isBtnHadOff = _isBtnHadOff.AsSpan();
                        var buttonStates = _buttonStates.AsSpan();
                        var isBtnHadOnInternal = _isBtnHadOnInternal.AsSpan();
                        var isBtnHadOffInternal = _isBtnHadOffInternal.AsSpan();
                        var buttonRealTimeStates = _buttonRealTimeStates.AsSpan();

                        isBtnHadOnInternal.CopyTo(isBtnHadOn);
                        isBtnHadOffInternal.CopyTo(isBtnHadOff);
                        buttonRealTimeStates.CopyTo(buttonStates);

                        isBtnHadOnInternal.Clear();
                        isBtnHadOffInternal.Clear();
                    }
                }
            }
            /// <summary>
            /// Determines whether the button at the given index was ever ON
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button was ON at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _isBtnHadOn[index];
            }
            /// <summary>
            /// Determines whether the button at the given index is ON in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button state is ON in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _buttonStates[index];
            }
            /// <summary>
            /// Determines whether the button at the given index was ever OFF
            /// during the interval between the two most recent OnPreUpdate calls.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button was OFF at any point during that interval; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _isBtnHadOff[index];
            }
            /// <summary>
            /// Determines whether the button at the given index is OFF in the this frame.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button state is OFF in this frame; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(int index)
            {
                return !IsOn(index);
            }
            /// <summary>
            /// Retrieves the real‑time state of the button at the given index
            /// as read from the IO thread, indicating whether it is currently ON.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button is ON according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(int index)
            {
                if (!index.InRange(0, 11))
                    return false;

                return _buttonRealTimeStates[index];
            }
            /// <summary>
            /// Retrieves the real‑time state of the button at the given index
            /// as read from the IO thread, indicating whether it is currently OFF.
            /// </summary>
            /// <param name="index">
            /// Zero‑based button index (valid range 0–11).
            /// </param>
            /// <returns>
            /// True if the button is OFF according to the latest IO thread reading; otherwise, false.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(int index)
            {
                return !IsCurrentlyOn(index);
            }


            /// <summary>
            /// See also <seealso cref="IsHadOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOn(ButtonZone area)
            {
                return IsHadOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOn(ButtonZone area)
            {
                return IsOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsHadOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsHadOff(ButtonZone area)
            {
                return IsHadOff(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsOff(ButtonZone area)
            {
                return IsOff(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOn(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOn(ButtonZone area)
            {
                return IsCurrentlyOn(GetIndexFromArea(area));
            }
            /// <summary>
            /// See also <seealso cref="IsCurrentlyOff(int)"/>
            /// </summary>
            /// <param name="area"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCurrentlyOff(ButtonZone area)
            {
                return IsCurrentlyOff(GetIndexFromArea(area));
            }
            #endregion

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int GetIndexFromArea(ButtonZone area)
            {
                if (area < ButtonZone.A1 || area > ButtonZone.P2)
                {
                    ThrowHelper.OutOfRange(nameof(area));
                }
                return (int)area;
            }
#if UNITY_ANDROID || UNITY_IOS
            static void KeyboardPreUpdate()
            {
                var gameButtons = _buttons.Slice(0, 8);
                try
                {
#if UNITY_ANDROID
                    IsConnected = true;
                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = button.BindingKey;
                        var androidKeyCode = (keyCode switch
                        {
                            KeyCode.B1 => MajdataPlay.Platform.Android.IO.KeyCode.W,
                            KeyCode.B2 => MajdataPlay.Platform.Android.IO.KeyCode.E,
                            KeyCode.B3 => MajdataPlay.Platform.Android.IO.KeyCode.D,
                            KeyCode.B4 => MajdataPlay.Platform.Android.IO.KeyCode.C,
                            KeyCode.B5 => MajdataPlay.Platform.Android.IO.KeyCode.X,
                            KeyCode.B6 => MajdataPlay.Platform.Android.IO.KeyCode.Z,
                            KeyCode.B7 => MajdataPlay.Platform.Android.IO.KeyCode.A,
                            KeyCode.B8 => MajdataPlay.Platform.Android.IO.KeyCode.Q,

                            KeyCode.Test => MajdataPlay.Platform.Android.IO.KeyCode.Numpad9,
                            KeyCode.SelectP1 => MajdataPlay.Platform.Android.IO.KeyCode.NumpadMultiply,
                            KeyCode.Service => MajdataPlay.Platform.Android.IO.KeyCode.Numpad7,
                            KeyCode.SelectP2 => MajdataPlay.Platform.Android.IO.KeyCode.Numpad3,

                            _ => MajdataPlay.Platform.Android.IO.KeyCode.Unknown
                        });
                        _buttonRealTimeStates[i] = AndroidKeyboard.IsPreesedUnsafe(androidKeyCode);
                    }
#elif UNITY_IOS
                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = KeyboardHelper.ToiOSGCKeyCode(button.BindingKey);
                        var @return = NativeKeyboard.IsPressed(keyCode, ref _buttonRealTimeStates[i]);
                        switch(@return)
                        {
                            case ErrorCode.NoError:
                                IsConnected = true;
                                break;
                            case ErrorCode.NoDevice:
                            case ErrorCode.NotSupported:
                                IsConnected = false;
                                break;
                            case ErrorCode.InvalidOperation: // Not init
                                @return = NativeKeyboard.Init();
                                if(@return == ErrorCode.NoError)
                                {
                                    i--;
                                    IsConnected = true;
                                    continue;
                                }
                                IsConnected = false;
                                MajDebug.LogError(nameof(ButtonRing), $"Failed to initialize NativeKeyboard: {@return}");
                                return;
                            default:
                                IsConnected = false;
                                MajDebug.LogError(nameof(ButtonRing), $"Error occurred while reading key states from NativeKeyboard: {@return}");
                                break;
                        }
                    }
#endif
                    var states = _buttonRealTimeStates;
                    var hadOn = _isBtnHadOnInternal;
                    var hadOff = _isBtnHadOffInternal;

                    for (int i = 0; i < 12; i++)
                    {
                        var state = states[i];
                        hadOn[i] |= state;
                        hadOff[i] |= !state;
                    }
                }
                catch (Exception e)
                {
                    IsConnected = false;
                    MajDebug.LogError($"From Keyboard listener: \n{e}");
                }
            }
            static void GamepadPreUpdate()
            {
                var gameButtons = _buttons.Slice(0, 8);
                try
                {
                    var gamepad = Gamepad.current;
                    var now = MajTimeline.UnscaledTime;

                    for (var i = 0; i < gameButtons.Length; i++)
                    {
                        var button = gameButtons.Span[i];
                        var keyCode = button.BindingKey;
                        var state = (keyCode switch
                        {
                            KeyCode.B1 => gamepad?.buttonNorth,
                            KeyCode.B2 => gamepad?.buttonEast,
                            KeyCode.B3 => gamepad?.buttonSouth,
                            KeyCode.B4 => gamepad?.buttonWest,

                            KeyCode.B5 => gamepad?.dpad.up,
                            KeyCode.B6 => gamepad?.dpad.right,
                            KeyCode.B7 => gamepad?.dpad.down,
                            KeyCode.B8 => gamepad?.dpad.left,

                            KeyCode.Test => gamepad?.leftShoulder,
                            KeyCode.SelectP1 => gamepad?.rightShoulder,
                            KeyCode.Service => gamepad?.leftStickButton,
                            KeyCode.SelectP2 => gamepad?.rightStickButton,

                            _ => null
                        })?.isPressed ?? false;
                        _buttonRealTimeStates[i] = state;
                    }
                    IsConnected = true;

                    var states = _buttonRealTimeStates;
                    var hadOn = _isBtnHadOnInternal;
                    var hadOff = _isBtnHadOffInternal;

                    for (int i = 0; i < 12; i++)
                    {
                        var state = states[i];
                        hadOn[i] |= state;
                        hadOff[i] |= !state;
                    }
                }
                catch (Exception e)
                {
                    IsConnected = false;
                    MajDebug.LogError($"From Gamepad listener: \n{e}");
                }
            }
#elif UNITY_STANDALONE
            static void KeyboardUpdateLoop()
            {
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var pollingRate = _btnPollingRateMs;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var gameButtons = _buttons.Span.Slice(0, 8);
                var fnButtons = _buttons.Span.Slice(8);
                var fnBuffer = _buttonRealTimeStates.AsSpan(8);
                ref var @lock = ref _syncLock;

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(ButtonRing), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(ButtonRing), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                stopwatch.Start();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var now = MajTimeline.UnscaledTime;

                            for (var i = 0; i < gameButtons.Length; i++)
                            {
                                var button = gameButtons[i];
                                var keyCode = button.BindingKey;
                                var state = KeyboardHelper.IsKeyDown(keyCode);
                                _buttonRealTimeStates[i] = state;
                            }
                            fnBuffer.Clear();
                            UpdateKeyboardFn(fnButtons, fnBuffer);
                            IsConnected = true;

                            var isLocked = false;
                            try
                            {
                                @lock.Enter(ref isLocked);
                                var states = _buttonRealTimeStates.AsSpan();
                                var hadOn = _isBtnHadOnInternal.AsSpan();
                                var hadOff = _isBtnHadOffInternal.AsSpan();

                                for (int i = 0; i < 12; i++)
                                {
                                    var state = states[i];
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
                        }
                        catch (Exception e)
                        {
                            IsConnected = false;
                            MajDebug.LogError(nameof(ButtonRing), $"From Keyboard listener: \n{e}");
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
                }
                finally
                {
                    IsConnected = false;
                    MajDebug.LogWarning(nameof(ButtonRing), "Thread has exited");
                }
            }
            static void HIDUpdateLoop()
            {
                ref var @lock = ref _syncLock;
                var hidOptions = IODetector.ButtonRingHidConnInfo;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var pollingRate = _btnPollingRateMs;
                var fnButtons = _buttons.Span.Slice(8);
                var fnBuffer = _buttonRealTimeStates.AsSpan(8);
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var pid = hidOptions.ProductId;
                var vid = hidOptions.VendorId;
                var manufacturer = IODetector.DeviceManufacturer;
                var deviceType = IODetector.ButtonRingDevice;
                var deviceName = string.IsNullOrEmpty(hidOptions.DeviceName) ? GetHIDDeviceName(deviceType, manufacturer) : hidOptions.DeviceName;
                var hidConfig = new OpenConfiguration();
                var filter = new DeviceFilter()
                {
                    DeviceName = deviceName,
                    ProductId = pid,
                    VendorId = vid,
                };

                hidConfig.SetOption(OpenOption.Exclusive, hidOptions.Exclusice);
                hidConfig.SetOption(OpenOption.Priority, (OpenPriority)hidOptions.OpenPriority);
                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(ButtonRing), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(ButtonRing), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                var hidDevice = default(HidDevice?);
                var hidStream = default(HidStream?);
                var isReconnecting = false;

                try
                {
                    Memory<byte> memory = Array.Empty<byte>();
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC);
                        hidDevice = default;
                        hidStream = default;
                        if (!HidHelper.TryGetAndOpenDevice(nameof(ButtonRing),
                            filter, hidConfig, out hidDevice, out hidStream, isReconnecting))
                        {
                            continue;
                        }
                        MajDebug.LogInfo(nameof(ButtonRing), $"Opened\nDevice: {hidDevice}");
                        // HID device has been opened
                        var deviceReportBufferLength = hidDevice.GetMaxInputReportLength();
                        if (deviceReportBufferLength > memory.Length)
                        {
                            memory = new byte[deviceReportBufferLength];
                        }
                        else
                        {
                            if (memory.IsEmpty)
                            {
                                memory = new byte[Math.Min(4096, deviceReportBufferLength)];
                            }
                        }
                        _ioThreadSync.ReadBufferMemory = memory;
                        _ioThreadSync.SignalReadReady();
                        Span<byte> buffer = memory.Span;
                        IsConnected = true;
                        MajDebug.LogInfo(nameof(ButtonRing), $"Connected");
                        stopwatch.Start();
                        t1 = stopwatch.Elapsed;
                        IsConnected = true;
                        isReconnecting = true;
                        #region Polling
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                var now = MajTimeline.UnscaledTime;
                                hidStream.Read(buffer);
                                switch (manufacturer)
                                {
                                    case DeviceManufacturerOption.General:
                                        GeneralHIDDevice.Parse(buffer, _buttonRealTimeStates);
                                        break;
                                    case DeviceManufacturerOption.Yuan:
                                        AdxHIDDevice.Parse(buffer, _buttonRealTimeStates);
                                        break;
                                    case DeviceManufacturerOption.Dao:
                                        _ioThreadSync.SignalReadReady();
                                        DaoHIDDevice.Parse(buffer, _buttonRealTimeStates);
                                        _ioThreadSync.WaitReadConsumed();
                                        break;
                                }
                                UpdateKeyboardFn(fnButtons, fnBuffer);
                                IsConnected = true;
                                var isLocked = false;
                                try
                                {
                                    @lock.Enter(ref isLocked);
                                    var states = _buttonRealTimeStates.AsSpan();
                                    var hadOn = _isBtnHadOnInternal.AsSpan();
                                    var hadOff = _isBtnHadOffInternal.AsSpan();

                                    for (int i = 0; i < 12; i++)
                                    {
                                        var state = states[i];
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
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (IOException ioE)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(ButtonRing), $"{ioE}");
                                hidStream.Close();
                                hidStream.Dispose();
                                MajDebug.LogInfo(nameof(ButtonRing), $"Disconnected");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(ButtonRing), $"{e}");
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
                        #endregion
                    }
                }
                finally
                {
                    IsConnected = false;
                    hidStream?.Close();
                    hidStream?.Dispose();
                    MajDebug.LogWarning(nameof(ButtonRing), "Thread has exited");
                }
            }
            static void PipeUpdateLoop()
            {
                /// Payload structure
                ///                    64bit                    
                /// |<-            Device states             ->|                         
                /// 00000000 00000000 00000000 00000000 00000000
                /// 

                var pipeName = $"MajdataPlay.IO.ButtonRing.{IODetector.PlayerIndex}P";
                var token = MajEnv.GlobalCT;
                var pollingRate = _btnPollingRateMs;
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
                        var states = _buttonRealTimeStates.AsSpan();
                        var hadOn = _isBtnHadOnInternal.AsSpan();
                        var hadOff = _isBtnHadOffInternal.AsSpan();

                        for (int i = 0; i < 12; i++)
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

                MajDebug.LogInfo(nameof(ButtonRing), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(ButtonRing), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

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
                            MajDebug.LogInfo(nameof(ButtonRing), $"Attempting connect to pipe \"{pipeName}\"...");
                            pipeClientStream.Connect(2000);
                            MajDebug.LogInfo(nameof(ButtonRing), "Connected");
                        }
                        catch (Exception) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError(nameof(ButtonRing), $"Failed to connect to pipe\n{e}");
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
                                MajDebug.LogError(nameof(ButtonRing), $"{ioE}");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(ButtonRing), $"{e}");
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
                        using (new LockDisposable())
                        {
                            _buttonRealTimeStates.AsSpan().Clear();
                            _isBtnHadOffInternal.AsSpan().Fill(true);
                        }
                        pipeClientStream.Dispose();
                    }
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void UpdateKeyboardFn(in ReadOnlySpan<Button> fnButtons, in Span<bool> buffer)
            {
                for (var i = 0; i < fnButtons.Length; i++)
                {
                    var button = fnButtons[i];
                    var keyCode = button.BindingKey;
                    var state = KeyboardHelper.IsKeyDown(keyCode);
                    buffer[i] |= state;
                }
            }
            static string GetHIDDeviceName(ButtonRingDeviceOption deviceType, DeviceManufacturerOption manufacturer)
            {
                switch (deviceType)
                {
                    case ButtonRingDeviceOption.HID:
                        if (manufacturer == DeviceManufacturerOption.General)
                        {
                            return string.Empty;
                        }
                        else if (manufacturer == DeviceManufacturerOption.Yuan)
                        {
                            //return "MusicGame Composite USB";
                            return string.Empty;
                        }
                        else if (manufacturer == DeviceManufacturerOption.Dao)
                        {
                            //return "SkyStar Maimoller";
                            return string.Empty;
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    default:
                        throw new NotSupportedException();
                }
            }
            static class AdxHIDDevice
            {
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
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
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
                }
            }
            static class GeneralHIDDevice
            {
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
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
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
                }
            }
            static class DaoHIDDevice
            {
                public static void Parse(ReadOnlySpan<byte> reportData, Span<bool> buffer)
                {
                    const int BUTTON_INDEX = 5;
                    const int SIDE_BUTTON_INDEX = 6;

                    reportData = reportData.Slice(1);// skip report id

                    var btnData = reportData[BUTTON_INDEX];
                    var sideBtnData = reportData[SIDE_BUTTON_INDEX];

                    for (var i = 0; i < 8; i++)
                    {
                        var bit = 1 << i;
                        buffer[i] = (btnData & bit) != 0;
                    }
                    buffer[9] = (sideBtnData & (1 << 3)) != 0;  // SELECT P1
                    buffer[8] = (sideBtnData & (1 << 2)) != 0;  // TEST
                    buffer[11] = (sideBtnData & (1 << 1)) != 0; // SELECT P2
                    buffer[10] = (sideBtnData & (1 << 0)) != 0; // SERVICE
                }
            }
#endif
            protected ref struct LockDisposable
            {
                bool _isLocked;
                public LockDisposable()
                {
#if !(UNITY_ANDROID || UNITY_IOS)
                    _syncLock.Enter(ref _isLocked);
#endif
                }
                public void Dispose()
                {
#if !(UNITY_ANDROID || UNITY_IOS)
                    if (_isLocked)
                    {
                        _syncLock.Exit();
                        _isLocked = false;
                    }
#endif
                }
            }
        }
    }
}
