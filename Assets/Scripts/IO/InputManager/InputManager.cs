#if UNITY_STANDALONE
using HidSharp;
using HidSharp.Platform.Windows;
using LibUsbDotNet;
using LibUsbDotNet.Main;
#endif
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.UnsafeKit;
using MajdataPlay.Utils;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Pipes;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
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
        internal const float UI_CLICK_ANIMATION_DURATION_SEC = 0.1f;

        public static bool IsTouchPanelConnected
        {
            get
            {
#if UNITY_STANDALONE
                return TouchPanel.IsConnected;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return OniimaiController.TouchConnected;
#else
                return false;
#endif
            }
        }
        public static bool IsButtonRingConnected
        {
            get
            {
#if UNITY_STANDALONE
                return ButtonRing.IsConnected;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return ButtonRing.IsConnected || OniimaiController.ButtonConnected;
#else
                return false;
#endif
            }
        }
        public static float TouchButtonRingEdge
        {
            get => _lastTouchButtonRingEdge;
            set => _lastTouchButtonRingEdge = value;
        }
        public static float FingerRadius
        {
            get
            {
                return MajEnv.Settings.Debug.TouchSimulationRadius;
            }
        }
        public static ReadOnlySpan<SwitchStatus> ButtonStatusInThisFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _btnStatusInThisFrame;
        }
        public static ReadOnlySpan<SwitchStatus> ButtonStatusInPreviousFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _btnStatusInPreviousFrame;
        }
        public static ReadOnlySpan<SwitchStatus> SensorStatusInThisFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sensorStatusInThisFrame;
        }
        public static ReadOnlySpan<SwitchStatus> SensorStatusInPreviousFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sensorStatusInPreviousFrame;
        }
        public static ref readonly InputControlState GetButtonState(ButtonZone zone)
        {
            ThrowIfButtonIndexOutOfRange(zone);
            return ref _buttonControlStates[(int)zone];
        }
        public static ref readonly InputControlState GetSensorState(SensorArea area)
        {
            ThrowIfSensorIndexOutOfRange(area);
            return ref _sensorControlStates[(int)area];
        }
        public static bool IsButtonPressedInThisFrame(ButtonZone zone)
        {
            return GetButtonState(zone).PressedThisFrame;
        }
        public static bool IsSensorPressedInThisFrame(SensorArea area)
        {
            return GetSensorState(area).PressedThisFrame;
        }
        public static bool IsButtonClickCompletedInThisFrame(ButtonZone zone)
        {
            return GetButtonState(zone).ClickCompletedThisFrame;
        }
        public static bool IsSensorClickCompletedInThisFrame(SensorArea area)
        {
            return GetSensorState(area).ClickCompletedThisFrame;
        }
#if UNITY_ANDROID || UNITY_IOS
        public static ReadOnlySpan<int> ButtonClickedCountInThisFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _btnClickedCountInThisFrame;
        }
        public static ReadOnlySpan<int> SensorClickedCountInThisFrame
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sensorClickedCountInThisFrame;
        }
#endif
        public static ReadOnlySpan<bool> TouchPanelRawData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _sensorStates.Span;
            }
        }
        public static ReadOnlySpan<Vector4> UnitCircle
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _unitCircle.Span;
            }
        }
        public static ReadOnlySpan<ulong> TouchPanelPositionSamples
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return new ReadOnlySpan<ulong>(_posData, 1280 * 1280);
            }
        }
        /// <summary>
        /// Left, Top, Right, Bottom edge
        /// </summary>
        public static Vector4 SubScreenEdge { get; set; } = new Vector4();

        public static event EventHandler<InputEventArgs>? OnAnyAreaTrigger;

        static TimeSpan _btnDebounceThresholdMs = TimeSpan.Zero;
        static TimeSpan _sensorDebounceThresholdMs = TimeSpan.Zero;
        static TimeSpan _btnPollingRateMs = TimeSpan.Zero;
        static TimeSpan _sensorPollingRateMs = TimeSpan.Zero;

        readonly static ConcurrentQueue<InputDeviceReport> _touchPanelInputBuffer = new();
        readonly static ConcurrentQueue<InputDeviceReport> _buttonRingInputBuffer = new();

        readonly static ReadOnlyMemory<KeyCode> _bindingKeys = new KeyCode[12]
        {
            KeyCode.B1,
            KeyCode.B2,
            KeyCode.B3,
            KeyCode.B4,
            KeyCode.B5,
            KeyCode.B6,
            KeyCode.B7,
            KeyCode.B8,
            KeyCode.Test,
            KeyCode.SelectP1,
            KeyCode.Service,
            KeyCode.SelectP2
        };
        readonly static ReadOnlyMemory<Button> _buttons = new Button[12]
        {
            new Button(KeyCode.B1,ButtonZone.A1),
            new Button(KeyCode.B2,ButtonZone.A2),
            new Button(KeyCode.B3,ButtonZone.A3),
            new Button(KeyCode.B4,ButtonZone.A4),
            new Button(KeyCode.B5,ButtonZone.A5),
            new Button(KeyCode.B6,ButtonZone.A6),
            new Button(KeyCode.B7,ButtonZone.A7),
            new Button(KeyCode.B8,ButtonZone.A8),
            new Button(KeyCode.Test,ButtonZone.Test),
            new Button(KeyCode.SelectP1,ButtonZone.P1),
            new Button(KeyCode.Service,ButtonZone.Service),
            new Button(KeyCode.SelectP2,ButtonZone.P2),
        };
        readonly static TimeSpan[] _btnLastTriggerTimes = new TimeSpan[12];
        readonly static SwitchStatus[] _btnStatusInPreviousFrame = new SwitchStatus[12];
        readonly static SwitchStatus[] _btnStatusInThisFrame = new SwitchStatus[12];
        readonly static InputControlState[] _buttonControlStates = new InputControlState[12];

        readonly static ReadOnlyMemory<Sensor> _sensors = new Sensor[33]
        {
            new Sensor()
            {
                Area = SensorArea.A1,
            },
            new Sensor()
            {
                Area = SensorArea.A2,
            },
            new Sensor()
            {
                Area = SensorArea.A3,
            },
            new Sensor()
            {
                Area = SensorArea.A4,
            },
            new Sensor()
            {
                Area = SensorArea.A5,
            },
            new Sensor()
            {
                Area = SensorArea.A6,
            },
            new Sensor()
            {
                Area = SensorArea.A7,
            },
            new Sensor()
            {
                Area = SensorArea.A8,
            },
            new Sensor()
            {
                Area = SensorArea.B1,
            },
            new Sensor()
            {
                Area = SensorArea.B2,
            },
            new Sensor()
            {
                Area = SensorArea.B3,
            },
            new Sensor()
            {
                Area = SensorArea.B4,
            },
            new Sensor()
            {
                Area = SensorArea.B5,
            },
            new Sensor()
            {
                Area = SensorArea.B6,
            },
            new Sensor()
            {
                Area = SensorArea.B7,
            },
            new Sensor()
            {
                Area = SensorArea.B8,
            },
            new Sensor()
            {
                Area = SensorArea.C,
            },
            new Sensor()
            {
                Area = SensorArea.D1,
            },
            new Sensor()
            {
                Area = SensorArea.D2,
            },
            new Sensor()
            {
                Area = SensorArea.D3,
            },
            new Sensor()
            {
                Area = SensorArea.D4,
            },
            new Sensor()
            {
                Area = SensorArea.D5,
            },
            new Sensor()
            {
                Area = SensorArea.D6,
            },
            new Sensor()
            {
                Area = SensorArea.D7,
            },
            new Sensor()
            {
                Area = SensorArea.D8,
            },
            new Sensor()
            {
                Area = SensorArea.E1,
            },
            new Sensor()
            {
                Area = SensorArea.E2,
            },
            new Sensor()
            {
                Area = SensorArea.E3,
            },
            new Sensor()
            {
                Area = SensorArea.E4,
            },
            new Sensor()
            {
                Area = SensorArea.E5,
            },
            new Sensor()
            {
                Area = SensorArea.E6,
            },
            new Sensor()
            {
                Area = SensorArea.E7,
            },
            new Sensor()
            {
                Area = SensorArea.E8,
            },
        };
        readonly static TimeSpan[] _sensorLastTriggerTimes = new TimeSpan[33];
        //The serial port will report the status of 35 zones, but there are actually only 34 zones.
        readonly static Memory<bool> _sensorStates = new bool[35];
        readonly static SwitchStatus[] _sensorStatusInPreviousFrame = new SwitchStatus[33];
        readonly static SwitchStatus[] _sensorStatusInThisFrame = new SwitchStatus[33];
        readonly static InputControlState[] _sensorControlStates = new InputControlState[33];
#if UNITY_ANDROID || UNITY_IOS
        readonly static int[] _btnClickedCountInThisFrame = new int[8];
        readonly static int[] _sensorClickedCountInThisFrame = new int[33];
#endif
        static bool _isInited = false;
        static bool _useDummy = false;
        static bool _isBtnDebounceEnabled = false;
        static bool _isSensorDebounceEnabled = false;
        static bool _isSensorRendererEnabled = false;

        static IReadOnlyDictionary<int, int> _instanceID2SensorIndexMappingTable = new Dictionary<int, int>();

#if UNITY_STANDALONE
        readonly static IOThreadSynchronization _ioThreadSync = new IOThreadSynchronization();     
#endif
        internal static void Init(IReadOnlyDictionary<int, int> instanceID2SensorIndexMappingTable)
        {
            if(_isInited)
            {
                return;
            }
            MajDebug.LogInfo("[InputManager]Start initialization");
            _isInited = true;
            Input.multiTouchEnabled = true;
            EnhancedTouchSupport.Enable();
            MajDebug.LogDebug("[InputManager]Reading config from game settings");
            _isSensorRendererEnabled = MajEnv.Settings.Debug.DisplaySensor;
#if UNITY_STANDALONE
            _btnDebounceThresholdMs = TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.DebounceThresholdMs);
            _btnPollingRateMs = TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs);
            _sensorDebounceThresholdMs = TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.TouchPanel.DebounceThresholdMs);
            _sensorPollingRateMs = TimeSpan.FromMilliseconds(MajEnv.Settings.IO.InputDevice.TouchPanel.PollingRateMs);
            _isBtnDebounceEnabled = MajEnv.Settings.IO.InputDevice.ButtonRing.Debounce;
            _isSensorDebounceEnabled = MajEnv.Settings.IO.InputDevice.TouchPanel.Debounce;
#else
            _btnDebounceThresholdMs = TimeSpan.Zero;
            _btnPollingRateMs = TimeSpan.Zero;
            _sensorDebounceThresholdMs = TimeSpan.Zero;
            _sensorPollingRateMs = TimeSpan.Zero;
            _isBtnDebounceEnabled = false;
            _isSensorDebounceEnabled = false;
#endif
            for (var i = 0; i < 33; i++)
            {
                if (i.InRange(0, 7))
                {
                    _btnLastTriggerTimes[i] = TimeSpan.Zero;
                }
                _sensorLastTriggerTimes[i] = TimeSpan.Zero;
            }
            _posData = UnsafeHelper.Alloc<ulong>(1280 * 1280);
            var samples = new Vector4[TOUCH_ANGLE_SMAPLE_COUNT];
            var step = 2f * Mathf.PI / TOUCH_ANGLE_SMAPLE_COUNT;

            for (int i = 0; i < TOUCH_ANGLE_SMAPLE_COUNT; i++)
            {
                var angle = step * i;
                samples[i] = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
            }

            _unitCircle = samples;
            GameManager.OnAppQuit += OnApplicationQuit;
            _instanceID2SensorIndexMappingTable = instanceID2SensorIndexMappingTable;
            _lastScreenHeight = Screen.height;
            _lastScreenWidth = Screen.width;
            MajDebug.LogDebug("[InputManager]Screen dimensions initialized");
            MajDebug.LogDebug("[InputManager]Start generating sensor map");
            for (var x = -540; x <= 540; x++)
            {
                if ((x + 540) % 100 == 0)
                {
                    MajDebug.LogDebug($"[InputManager]Progress: {x + 540}/1080");
                }
                for (var y = -540; y <= 540; y++)
                {
                    var point = new Vector3(x / 100f, y / 100f, -10);
                    var ray = new Ray(point, Vector3.forward);
                    var ishit = Physics.Raycast(ray, out var hitInfom);
                    ref var posData = ref _posData[((x + 540) * 1280) + y + 540];
                    if (ishit)
                    {
                        var id = hitInfom.colliderInstanceID;
                        if (_instanceID2SensorIndexMappingTable.TryGetValue(id, out var index))
                        {
                            posData |= 1UL << (index + 12);
                        }
                        else
                        {
                            MajDebug.LogWarning($"[InputManager]Unknown collider instance id: {id}");
                        }
                    }
                }
            }
            MajDebug.LogDebug($"[InputManager]Sensor map generate finished");
#if UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            ButtonRing.Init();
#endif
#if UNITY_STANDALONE
            TouchPanel.Init();
#endif
            MajDebug.LogInfo("[InputManager]Initialization completed");
        }
        internal static void OnFixedUpdate()
        {
            //_updateIOListener();
        }
        internal static void OnPreUpdate()
        {
            Profiler.BeginSample("InputManager.OnPreUpdate");
            var buttons = _buttons.Span;
            var sensors = _sensors.Span;
            
            try
            {
#if UNITY_ANDROID || UNITY_IOS
                Array.Fill(_btnClickedCountInThisFrame, 0);
                Array.Fill(_sensorClickedCountInThisFrame, 0);
#endif
                InputSystem.Update();
#if UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
                ButtonRing.OnPreUpdate();
#endif
#if UNITY_STANDALONE
                TouchPanel.OnPreUpdate();
                
#endif
                var debugOptions = MajEnv.Settings.Debug;
                var displayOptions = MajEnv.Settings.Display;
                var height = Screen.height;
                var width = Screen.width;
                var fingerRad = Override_TouchSimulationRadius ?? FingerRadius;
                var touchRadiusAdjust = debugOptions.TouchRadiusAdjust;
                var aExtraRad = Override_TouchAAreaExtraRadius ?? debugOptions.TouchAAreaExtraRadius;
                var bExtraRad = Override_TouchBAreaExtraRadius ?? debugOptions.TouchBAreaExtraRadius;
                var cExtraRad = Override_TouchCAreaExtraRadius ?? debugOptions.TouchCAreaExtraRadius;
                var dExtraRad = Override_TouchDAreaExtraRadius ?? debugOptions.TouchDAreaExtraRadius;
                var eExtraRad = Override_TouchEAreaExtraRadius ?? debugOptions.TouchEAreaExtraRadius;
                var mainScreenTransform = OniimaiLayout.PhoneTransform(displayOptions.MainScreenTransform);
                var mainScreenOffset = displayOptions.MainScreenOffset;

                var isModified = height != _lastScreenHeight ||
                                 width != _lastScreenWidth ||
                                 fingerRad != _lastFingerRadius ||
                                 aExtraRad != _lastAAreaExtraRadius ||
                                 bExtraRad != _lastBAreaExtraRadius ||
                                 cExtraRad != _lastCAreaExtraRadius ||
                                 dExtraRad != _lastDAreaExtraRadius ||
                                 eExtraRad != _lastEAreaExtraRadius ||
                                 touchRadiusAdjust != _lastTouchRadiusAdjust ||
                                 mainScreenOffset != _lastMainScreenOffset;
                if (isModified)
                {
                    _lastScreenWidth = width;
                    _lastScreenHeight = height;
                    _lastFingerRadius = fingerRad;
                    _lastAAreaExtraRadius = aExtraRad;
                    _lastBAreaExtraRadius = bExtraRad;
                    _lastCAreaExtraRadius = cExtraRad;
                    _lastDAreaExtraRadius = dExtraRad;
                    _lastEAreaExtraRadius = eExtraRad;
                    _lastTouchRadiusAdjust = touchRadiusAdjust;
                    _lastMainScreenTransform = mainScreenTransform;
                    if(_lastMainScreenTransform)
                    {
                        _lastMainScreenOffset = mainScreenOffset;
                    }
                    else
                    {
                        _lastMainScreenOffset = 1f;
                    }
                    _version++;
                }                
                UpdateMousePosition();
                UpdateButtonState();
                UpdateSensorState();
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
            
            for (var i = 0; i < 12; i++)
            {
                var btn = buttons[i];
                _btnStatusInPreviousFrame[i] = _btnStatusInThisFrame[i];
                _btnStatusInThisFrame[i] = btn.State;
#if UNITY_ANDROID || UNITY_IOS
                if(i < 8)
                {
                    var isClicked = _btnStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                    (ButtonRing.IsOn(i) || ButtonRing.IsHadOn(i));
                    if(isClicked)
                    {
                        _btnClickedCountInThisFrame[i]++;
                    }
                }
#endif
            }
            for (var i = 0; i < 33; i++)
            {
                var sen = sensors[i];
                _sensorStatusInPreviousFrame[i] = _sensorStatusInThisFrame[i];
                _sensorStatusInThisFrame[i] = sen.State;
            }
            UpdateControlStates(MajTimeline.UnscaledDeltaTime);
            Profiler.EndSample();
        }
        static void UpdateControlStates(float deltaTime)
        {
            for (var i = 0; i < _buttonControlStates.Length; i++)
            {
                UpdateControlState(
                    ref _buttonControlStates[i],
                    _btnStatusInPreviousFrame[i],
                    _btnStatusInThisFrame[i],
                    deltaTime);
            }

            for (var i = 0; i < _sensorControlStates.Length; i++)
            {
                UpdateControlState(
                    ref _sensorControlStates[i],
                    _sensorStatusInPreviousFrame[i],
                    _sensorStatusInThisFrame[i],
                    deltaTime);
            }
        }
        internal static void ResetUIInputForSceneChange()
        {
            for (var i = 0; i < _buttonControlStates.Length; i++)
            {
                _buttonControlStates[i].ResetForSceneChange(
                    _btnStatusInThisFrame[i] == SwitchStatus.On);
            }

            for (var i = 0; i < _sensorControlStates.Length; i++)
            {
                _sensorControlStates[i].ResetForSceneChange(
                    _sensorStatusInThisFrame[i] == SwitchStatus.On);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateControlState(
            ref InputControlState state,
            SwitchStatus previousStatus,
            SwitchStatus currentStatus,
            float deltaTime)
        {
            var isPressed = currentStatus == SwitchStatus.On;
            state.Update(
                isPressed,
                previousStatus == SwitchStatus.Off && isPressed,
                previousStatus == SwitchStatus.On && !isPressed,
                deltaTime);
        }
        public static void BindAnyArea(EventHandler<InputEventArgs> checker) => OnAnyAreaTrigger += checker;
        public static void BindArea(EventHandler<InputEventArgs> checker, ButtonZone sType)
        {
            var sensor = GetSensor(sType.ToSensorArea());
            var button = GetButton(sType);
            if (sensor == null || button is null)
            {
                throw new Exception($"{sType} Sensor or Button not found.");
            }

            sensor.AddSubscriber(checker);
            button.AddSubscriber(checker);
        }
        public static void UnbindAnyArea(EventHandler<InputEventArgs> checker) => OnAnyAreaTrigger -= checker;
        public static void UnbindArea(EventHandler<InputEventArgs> checker, ButtonZone sType)
        {
            var sensor = GetSensor(sType.ToSensorArea());
            var button = GetButton(sType);
            if (sensor is null || button is null)
            {
                throw new Exception($"{sType} Sensor or Button not found.");
            }

            sensor.RemoveSubscriber(checker);
            button.RemoveSubscriber(checker);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckAreaStatus(ButtonZone sType, SwitchStatus targetStatus)
        {
            return CheckSensorStatus(sType.ToSensorArea(),targetStatus) || CheckButtonStatus(sType, targetStatus);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSensorStatus(SensorArea target, SwitchStatus targetStatus)
        {
            ThrowIfSensorIndexOutOfRange(target);

            var sensor = _sensors.Span[(int)target];
            if (sensor is null)
                throw new Exception($"{target} Sensor or Button not found.");

            return sensor.State == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckButtonStatus(ButtonZone target, SwitchStatus targetStatus)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var button = GetButton(target);

            if (button is null)
                throw new Exception($"{target} Button not found.");

            return button.State == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckButtonStatusInThisFrame(ButtonZone target, SwitchStatus targetStatus)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var index = GetButtonIndex(target);

            return _btnStatusInThisFrame[index] == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckButtonStatusInPreviousFrame(ButtonZone target, SwitchStatus targetStatus)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var index = GetButtonIndex(target);

            return _btnStatusInPreviousFrame[index] == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SwitchStatus GetButtonStatusInThisFrame(ButtonZone target)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var index = GetButtonIndex(target);

            return _btnStatusInThisFrame[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SwitchStatus GetButtonStatusInPreviousFrame(ButtonZone target)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var index = GetButtonIndex(target);

            return _btnStatusInPreviousFrame[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsButtonClickedInThisFrame(ButtonZone target)
        {
            ThrowIfButtonIndexOutOfRange(target);
            var index = GetButtonIndex(target);

            return _btnStatusInPreviousFrame[index] == SwitchStatus.Off &&
                   _btnStatusInThisFrame[index] == SwitchStatus.On;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsButtonClickedInThisFrame_OR(params ButtonZone[] targets)
        {
            return IsButtonClickedInThisFrame_OR(targets.AsSpan());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsButtonClickedInThisFrame_OR(ReadOnlySpan<ButtonZone> targets)
        {
            foreach (var target in targets)
            {
                ThrowIfButtonIndexOutOfRange(target);
                var index = GetButtonIndex(target);

                var result = _btnStatusInPreviousFrame[index] == SwitchStatus.Off &&
                             _btnStatusInThisFrame[index] == SwitchStatus.On;
                if (result)
                {
                    return true;
                }
            }
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsButtonClickedInThisFrame_AND(params ButtonZone[] targets)
        {
            return IsButtonClickedInThisFrame_AND(targets.AsSpan());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsButtonClickedInThisFrame_AND(ReadOnlySpan<ButtonZone> targets)
        {
            foreach (var target in targets)
            {
                ThrowIfButtonIndexOutOfRange(target);
                var index = GetButtonIndex(target);

                var result = _btnStatusInPreviousFrame[index] == SwitchStatus.Off &&
                             _btnStatusInThisFrame[index] == SwitchStatus.On;
                if (!result)
                {
                    return false;
                }
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSensorStatusInThisFrame(SensorArea target, SwitchStatus targetStatus)
        {
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInThisFrame[index] == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSensorStatusInPreviousFrame(SensorArea target, SwitchStatus targetStatus)
        {
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInPreviousFrame[index] == targetStatus;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SwitchStatus GetSensorStatusInThisFrame(SensorArea target)
        {
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInThisFrame[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SwitchStatus GetSensorStatusInPreviousFrame(SensorArea target)
        {
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInPreviousFrame[index];
        }
        public static SensorArea GetSensorAreaFromInstanceID(int instanceID)
        {
            if(_instanceID2SensorIndexMappingTable.TryGetValue(instanceID,out var index))
            {
                switch(index)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                    case 16:
                        return (SensorArea)index;
                    case 17:
                        return SensorArea.C;
                    case 18:
                    case 19:
                    case 20:
                    case 21:
                    case 22:
                    case 23:
                    case 24:
                    case 25:
                    case 26:
                    case 27:
                    case 28:
                    case 29:
                    case 30:
                    case 31:
                    case 32:
                    case 33:
                        return (SensorArea)(index - 1);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            throw new InvalidOperationException();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedUpInThisFrame(SensorArea target)
        {
            //when a sensor is pressed and released
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInPreviousFrame[index] == SwitchStatus.On &&
                   _sensorStatusInThisFrame[index] == SwitchStatus.Off;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedInThisFrame(SensorArea target)
        {
            ThrowIfSensorIndexOutOfRange(target);
            var index = (int)target;

            return _sensorStatusInPreviousFrame[index] == SwitchStatus.Off &&
                   _sensorStatusInThisFrame[index] == SwitchStatus.On;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedInThisFrame_OR(params SensorArea[] targets)
        {
            return IsSensorClickedInThisFrame_OR(targets.AsSpan());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedInThisFrame_OR(ReadOnlySpan<SensorArea> targets)
        {
            foreach (var target in targets)
            {
                ThrowIfSensorIndexOutOfRange(target);
                var index = (int)target;

                var result = _sensorStatusInPreviousFrame[index] == SwitchStatus.Off &&
                             _sensorStatusInThisFrame[index] == SwitchStatus.On;
                if (result)
                {
                    return true;
                }
            }
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedInThisFrame_AND(params SensorArea[] targets)
        {
            return IsSensorClickedInThisFrame_AND(targets.AsSpan());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSensorClickedInThisFrame_AND(ReadOnlySpan<SensorArea> targets)
        {
            foreach (var target in targets)
            {
                ThrowIfSensorIndexOutOfRange(target);
                var index = (int)target;

                var result = _sensorStatusInPreviousFrame[index] == SwitchStatus.Off &&
                             _sensorStatusInThisFrame[index] == SwitchStatus.On;
                if (!result)
                {
                    return false;
                }
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Button? GetButton(ButtonZone zone)
        {
            var buttons = _buttons.Span;
            ThrowIfButtonIndexOutOfRange(zone);

            return buttons[(int)zone];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ReadOnlyMemory<Button> GetButtons()
        {
            return _buttons;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Sensor GetSensor(SensorArea target)
        {
            return _sensors.Span[(int)target];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ReadOnlyMemory<Sensor> GetSensors()
        {
            return _sensors;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearAllSubscriber()
        {
            foreach(var sensor in _sensors.Span)
                sensor.ClearSubscriber();
            foreach(var button in _buttons.Span)
                button.ClearSubscriber();
            OnAnyAreaTrigger = null;
        }
        static void OnApplicationQuit(object? sender, EventArgs? e)
        {
            if (_posData is not null)
            {
                UnsafeHelper.Free(_posData);
            }
            GameManager.OnAppQuit -= OnApplicationQuit;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PushEvent(InputEventArgs args)
        {
            if (OnAnyAreaTrigger is not null)
                OnAnyAreaTrigger(null, args);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlyMemory<bool> GetTouchPanelRawData() => _sensorStates;
        /// <summary>
        /// Used to check whether the device activation is caused by abnormal jitter
        /// </summary>
        /// <param name="zone"></param>
        /// <returns>
        /// If the trigger interval is lower than the debounce threshold, returns <see cref="bool">true</see>, otherwise <see cref="bool">false</see>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool JitterDetect(SensorArea zone, TimeSpan now)
        {
            var index = (int)zone;
            TimeSpan lastTriggerTime = _sensorLastTriggerTimes[index];
            TimeSpan debounceTime = _sensorDebounceThresholdMs;

            var diff = now - lastTriggerTime;
            if (diff < debounceTime)
            {
                MajDebug.LogInfo($"[Debounce] Received sensor response\nZone: {zone}\nInterval: {diff.Milliseconds}ms");
                return true;
            }
            return false;
        }
        /// <summary>
        /// Used to check whether the device activation is caused by abnormal jitter
        /// </summary>
        /// <param name="zone"></param>
        /// <returns>
        /// If the trigger interval is lower than the debounce threshold, returns <see cref="bool">true</see>, otherwise <see cref="bool">false</see>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool JitterDetect(ButtonZone zone, TimeSpan now)
        {
            var index = (int)zone;
            TimeSpan lastTriggerTime = _btnLastTriggerTimes[index];
            TimeSpan debounceTime = _btnDebounceThresholdMs;

            var diff = now - lastTriggerTime;
            if (diff < debounceTime)
            {
                MajDebug.LogInfo($"[Debounce] Received button response\nZone: {zone}\nInterval: {diff.Milliseconds}ms");
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ThrowIfButtonIndexOutOfRange(ButtonZone target)
        {
            if (target > ButtonZone.P2 || target < ButtonZone.A1)
            {
                throw new ArgumentOutOfRangeException("Button index cannot greater than A8");
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ThrowIfSensorIndexOutOfRange(SensorArea area)
        {
            if (area < SensorArea.A1 || area > SensorArea.E8)
                throw new ArgumentOutOfRangeException();
        }
        static int GetButtonIndex(ButtonZone area)
        {
            return (int)area;
        }
#if UNITY_STANDALONE
        class IOThreadSynchronization
        {
            public ReadOnlySpan<byte> ReadBuffer
            {
                get
                {
                    return ReadBufferMemory.Span;
                }
            }
            public Span<byte> WriteBuffer
            {
                get
                {
                    return WriteBufferMemory.Span;
                }
            }
            public Memory<byte> WriteBufferMemory { get; set; } = Memory<byte>.Empty;
            public ReadOnlyMemory<byte> ReadBufferMemory { get; set; } = ReadOnlyMemory<byte>.Empty;
            public NamedPipeClientStream PipeClientStream { get; set; }

            readonly EventWaitHandle _readReadyEvent = new(false, EventResetMode.AutoReset);
            readonly EventWaitHandle _readConsumedEvent = new(false, EventResetMode.AutoReset);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool WaitReadReady()
            {
                return _readReadyEvent.WaitOne();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SignalReadReady()
            {
                _readReadyEvent.Set();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool WaitReadConsumed()
            {
                return _readConsumedEvent.WaitOne();
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SignalReadConsumed()
            {
                _readConsumedEvent.Set();
            }
        }
#endif
    }
}
