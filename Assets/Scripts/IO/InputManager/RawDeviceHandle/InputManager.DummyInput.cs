using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace MajdataPlay.IO
{
    internal static unsafe partial class InputManager
    {
        public const int TOUCH_ANGLE_SMAPLE_COUNT = 128;
        public const float FINGER_RADIUS_SEGMENT_LENGTH = 0.5f / 4;

        public static float? Override_TouchSimulationRadius { get; set; } = 0.1f;
        public static float? Override_TouchAAreaExtraRadius { get; set; } = 0f;
        public static float? Override_TouchBAreaExtraRadius { get; set; } = 0f;
        public static float? Override_TouchCAreaExtraRadius { get; set; } = 0f;
        public static float? Override_TouchDAreaExtraRadius { get; set; } = 0f;
        public static float? Override_TouchEAreaExtraRadius { get; set; } = 0f;

        // uint64 TouchPosData
        //
        // Button bit (12bit)
        // 1 2 3 4 5 6 7 8 9 10 11 12
        // 0 0 0 0 0 0 0 0 0 0  0  0
        // Sensor bit (34bit)
        // A1 A2 A3 A4 A5 A6 A7 A8 B1 B2 B3 B4 B5 B6 B7 B8 C1 C2 D1 D2 D3 D4 D5 D6 D7 D8 E1 E2 E3 E4 E5 E6 E7 E8
        // 0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0  0
        // Version bit (16bit)
        // uint16
        // Flag bit (2 bit)
        // 0: ButtonRing only
        // 1: Sensor only

        static ReadOnlyMemory<Vector4> _unitCircle = ReadOnlyMemory<Vector4>.Empty;
        static ulong* _posData = null;
        readonly static Dictionary<int, ulong> _touchRecorder = new(32);        

        static ushort _version = 0;
        
        static int _lastScreenWidth = -1;
        static int _lastScreenHeight = -1;
        static float _lastFingerRadius = 0.5f;
        static float _lastTouchRadiusAdjust = 1f;
        static float _lastAAreaExtraRadius = 1f;
        static float _lastBAreaExtraRadius = 1f;
        static float _lastCAreaExtraRadius = 1f;
        static float _lastDAreaExtraRadius = 1f;
        static float _lastEAreaExtraRadius = 1f;
        static float _lastMainScreenOffset = 1f;
        static bool _lastMainScreenTransform = false;
        static float _maxTouchRadius = -1f;
        static float _lastTouchButtonRingEdge = 5.4f;
        //readonly static Dictionary<SensorArea, HashSet<int>> _touchRecords = new(8);
        public static bool UseOuterTouchAsSensor { get; set; }
        public static bool UseGameplayTouchEnhancementFeatures { get; set; } = false;
        static void UpdateMousePosition()
        {
            Profiler.BeginSample("ButtonRing.OnPreUpdate.UpdateMousePosition");
            var sensors = _sensors.Span;
            var mainCamera = SceneSwitcher.MainCamera;

            Span<int> buttonClickedCount = stackalloc int[8];
            Span<int> sensorClickedCount = stackalloc int[34];
            Span<bool> newStates = stackalloc bool[34];
            Span<bool> extraButtonStates = stackalloc bool[12];

            var touches = Touch.activeTouches;

            if (touches.Count > 0)
            {
                FromTouchPanel(touches, buttonClickedCount, sensorClickedCount, newStates, extraButtonStates, mainCamera);
            }
#if UNITY_STANDALONE || UNITY_EDITOR
            else if (Mouse.current != null)
            {
                FromMouse(Mouse.current, buttonClickedCount, sensorClickedCount, newStates, extraButtonStates, mainCamera);
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            // OR physical input with phone touches before queuing. One source must never
            // release a zone still held by the other. No Android touch/key injection.
            OniimaiController.MergeInput(newStates, extraButtonStates, sensorClickedCount, buttonClickedCount);
#endif
            var now = MajTimeline.UnscaledTime;
            foreach (var (i, state) in newStates.WithIndex())
            {
                var _state = state ? SwitchStatus.On : SwitchStatus.Off;
                _touchPanelInputBuffer.Enqueue(new InputDeviceReport()
                {
                    Index = i,
                    State = _state,
                    Timestamp = now
                });
            }
#if UNITY_ANDROID || UNITY_IOS
            for (var i = 0; i < buttonClickedCount.Length; i++)
            {
                _btnClickedCountInThisFrame[i] += buttonClickedCount[i];
            }
            for (var i = 0; i < sensorClickedCount.Length; i++) 
            {
                var clickedCount = sensorClickedCount[i];
                if (i == 16)
                {
                    clickedCount = Mathf.Max(clickedCount, sensorClickedCount[17]);
                    i++;
                }
                if(i >= 16)
                {
                    _sensorClickedCountInThisFrame[i - 1] = clickedCount;
                }
                else
                {
                    _sensorClickedCountInThisFrame[i] = clickedCount;
                }
            }
#endif
            foreach (var (i, state) in extraButtonStates.WithIndex())
            {
                var _state = state ? SwitchStatus.On : SwitchStatus.Off;
                //if (i < 8 && UseOuterTouchAsSensor) continue;
                _buttonRingInputBuffer.Enqueue(new InputDeviceReport()
                {
                    Index = i,
                    State = _state,
                    Timestamp = now
                });
            }
            Profiler.EndSample();
        }
        static void FromTouchPanel(in ReadOnlyArray<Touch> touches,
                                   Span<int> buttonClickedCount,
                                   Span<int> sensorClickedCount,
                                   Span<bool> sensorStates, 
                                   Span<bool> extraButton, Camera mainCamera)
        {
#if UNITY_IOS
            const float PLATFORM_TOUCH_RADIUS_ADJUST = 78f * 4f;
#else
            const float PLATFORM_TOUCH_RADIUS_ADJUST = 1f;
#endif
            for (var j = 0; j < touches.Count; j++)
            {
                var touch = touches[j];
                if(!touch.valid)
                {
                    continue;
                }
                var touchPosData = 0UL;
                var touchRadius = touch.radius.magnitude;
                _touchRecorder.TryGetValue(touch.touchId, out var lastTouchPosData);
                var isSensorOnly = (lastTouchPosData & (1UL << 63)) != 0;
                PositionToSensorState(sensorStates,
                    extraButton,
                    mainCamera, 
                    touch.screenPosition, 
                    touchRadius / PLATFORM_TOUCH_RADIUS_ADJUST, 
                    ref touchPosData,
                    ref isSensorOnly);
                if (touchRadius > _maxTouchRadius)
                {
                    MajDebug.LogInfo($"Touch radius: {touchRadius}");
                    _maxTouchRadius = touchRadius;
                }
#if UNITY_ANDROID || UNITY_IOS

                if (UseOuterTouchAsSensor || isSensorOnly)
                {
                    for (var i = 0; i < 34; i++)
                    {
                        var lastState = false;
                        var currentState = false;

                        if (i < 8)
                        {
                            lastState = ((lastTouchPosData & (1UL << (i + 12))) | (lastTouchPosData & (1UL << i))) != 0;
                            currentState = ((touchPosData & (1UL << (i + 12))) | (touchPosData & (1UL << i))) != 0;
                        }
                        else
                        {
                            lastState = (lastTouchPosData & (1UL << (i + 12))) != 0;
                            currentState = (touchPosData & (1UL << (i + 12))) != 0;
                        }

                        if (!lastState && currentState)
                        {
                            sensorClickedCount[i]++;
                        }
                    }
                }
                else
                {                    
                    for (var i = 0; i < 34; i++)
                    {
                        var lastState = (lastTouchPosData & (1UL << (i + 12))) != 0;
                        var currentState = (touchPosData & (1UL << (i + 12))) != 0;
                        var isClicked = !lastState && currentState;
                        if (i < 8)
                        {
                            var isPreviousFrameInButton = (lastTouchPosData & (1UL << i)) != 0;
                            isClicked = isClicked && !isPreviousFrameInButton;
                        }

                        if(isClicked)
                        {
                            sensorClickedCount[i]++;
                        }
                    }
                    for (var i = 0; i < 8; i++)
                    {
                        var lastState = (lastTouchPosData & (1UL << i)) != 0;
                        var currentState = (touchPosData & (1UL << i)) != 0;
                        var isPreviousFrameInSensor = (lastTouchPosData & (1UL << (i + 12))) != 0;
                        var isClicked = (!lastState && currentState) && !isPreviousFrameInSensor;

                        if (isClicked)
                        {
                            buttonClickedCount[i]++;
                        }
                    }
                }
#endif
                if (touch.ended)
                {
                    _touchRecorder.Remove(touch.touchId);
                }
                else
                {
                    _touchRecorder[touch.touchId] = touchPosData;
                }
            }
        }


        static void FromMouse(Mouse mouse,
            Span<int> buttonClickedCount,
            Span<int> sensorClickedCount,
            Span<bool> sensorStates, 
            Span<bool> extraButton, 
            Camera mainCamera)
        {
            var leftButton = mouse.leftButton;
            if(!leftButton.isPressed)
            {
                _touchRecorder.Remove(1);
                return;
            }
            _touchRecorder.TryGetValue(1, out var lastTouchPosData);
            var touchPosData = 0UL;
            var isSensorOnly = (lastTouchPosData & (1UL << 63)) != 0;
            PositionToSensorState(sensorStates, 
                extraButton, 
                mainCamera, 
                mouse.position.value, 
                0, 
                ref touchPosData,
                ref isSensorOnly);
#if UNITY_ANDROID || UNITY_IOS
            if (UseOuterTouchAsSensor || isSensorOnly)
            {
                for (var i = 0; i < 34; i++)
                {
                    var lastState = false;
                    var currentState = false;

                    if (i < 8)
                    {
                        lastState = ((lastTouchPosData & (1UL << (i + 12))) | (lastTouchPosData & (1UL << i))) != 0;
                        currentState = ((touchPosData & (1UL << (i + 12))) | (touchPosData & (1UL << i))) != 0;
                    }
                    else
                    {
                        lastState = (lastTouchPosData & (1UL << (i + 12))) != 0;
                        currentState = (touchPosData & (1UL << (i + 12))) != 0;
                    }

                    if (!lastState && currentState)
                    {
                        sensorClickedCount[i]++;
                    }
                }
            }
            else
            {
                for (var i = 0; i < 34; i++)
                {
                    var lastState = (lastTouchPosData & (1UL << (i + 12))) != 0;
                    var currentState = (touchPosData & (1UL << (i + 12))) != 0;
                    var isClicked = !lastState && currentState;

                    if (i < 8)
                    {
                        var isPreviousFrameInButton = (lastTouchPosData & (1UL << i)) != 0;
                        isClicked = isClicked && !isPreviousFrameInButton;
                    }

                    if (isClicked)
                    {
                        sensorClickedCount[i]++;
                    }
                }
                for (var i = 0; i < 8; i++)
                {
                    var lastState = (lastTouchPosData & (1UL << i)) != 0;
                    var currentState = (touchPosData & (1UL << i)) != 0;
                    var isPreviousFrameInSensor = (lastTouchPosData & (1UL << (i + 12))) != 0;
                    var isClicked = (!lastState && currentState) && !isPreviousFrameInSensor;

                    if (isClicked)
                    {
                        buttonClickedCount[i]++;
                    }
                }
            }

            _touchRecorder[1] = touchPosData;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PositionToSensorState(Span<bool> sensorStates, Span<bool> buttonStates, Camera mainCamera, Vector3 position)
        {
            var _ = 0UL;
            var isSensorOnly = false;
            PositionToSensorState(sensorStates, buttonStates, mainCamera, position, 0, ref _, ref isSensorOnly);
        }
        /// <summary>
        /// return extra button pos 0-7, if none return -1
        /// </summary>
        /// <param name="sensorStates"></param>
        /// <param name="mainCamera"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PositionToSensorState(Span<bool> sensorStates,
            Span<bool> buttonStates,
            Camera mainCamera, 
            Vector3 position, 
            float touchRadius, 
            ref ulong rawPositionData,
            ref bool isSensorOnly)
        {
            const ulong BUTTON_BIT_MASK = 0b0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_1111_1111_1111;
            const ulong SENSOR_BIT_MASK = 0b0000_0000_0000_0000_0011_1111_1111_1111_1111_1111_1111_1111_1111_0000_0000_0000;
            const ulong VERSION_BIT_MASK = 0b0011_1111_1111_1111_1100_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000;
            const ulong FLAG_BIT_MASK = 0b1100_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000_0000;

            var x = (int)position.x;
            var y = (int)position.y;
            if(x < 0 || y < 0)
            {
                return;
            }
            var useGameplayTouchEnhancementFeatures = UseGameplayTouchEnhancementFeatures;
            var cubeRay = mainCamera.ScreenToWorldPoint(position);
            var versionBit = ((ulong)_version) << (12 + 34);
            var newP = 0UL;
            var rayToCenter = cubeRay - new Vector3(0, 0, -10);
            var radToCenter = rayToCenter.magnitude;
            var subScreenEdge = SubScreenEdge;
            var edgeYPosition = Mathf.Max(5.08f + (1.5f + (_lastMainScreenOffset * 2.7f)), 5.4f);
            var extraButtonStates = (stackalloc bool[12]);
            var extraSensorStates = (stackalloc bool[34]);
            var isAnyExtraButtonTriggered = false;
            var isAnySensorTriggered = false;
            var isInSubScreenRect = (cubeRay.x > subScreenEdge.x && cubeRay.x < subScreenEdge.z) &&
                                    (cubeRay.y > subScreenEdge.w && cubeRay.y < subScreenEdge.y);
            if (isInSubScreenRect)
            {
                //P1 Select
                // we directly set the output, not using isAnyExtraButtonTriggered..
                buttonStates[9] |= true;
                newP |= 1UL << 9;
            }
            if(radToCenter > _lastTouchButtonRingEdge)
            {
                const float SENSOR_GROUP_A_DEG = 14.5f;
                const float SENSOR_GROUP_D_DEG = 8f;
                
                // out of the screen area to the button area
                var degree = (-Mathf.Atan2(rayToCenter.y, rayToCenter.x) * Mathf.Rad2Deg) + 180;
                var pos = (int)(degree / 22.5f);
                var degDiff = degree - (22.5f * pos);

                var center = pos;
                var left = (pos + 15) & 15;
                var right = (pos + 1) & 15;

                var isSensorCenter = (pos & 1) == 0;
                var edge = isSensorCenter ? SENSOR_GROUP_D_DEG : SENSOR_GROUP_A_DEG;
                var targetPos = 0;

                if (Mathf.Abs(degDiff) <= edge)
                {
                    targetPos = center;
                }
                else
                {
                    targetPos = degDiff > 0 ? right : left;
                }
                var isSensor = (targetPos & 1) == 0;
                var index = ((targetPos >> 1) + 6) & 7;

                if (isSensor)
                {
                    extraSensorStates[(int)SensorArea.D1 + index] = true;
                }
                else
                {
                    extraButtonStates[(int)ButtonZone.A1 + index] = true;
                    isAnyExtraButtonTriggered = true;
                }
            }
            var circleSamples = _unitCircle.Span;
            var userRad = _lastFingerRadius * (1 + (touchRadius * _lastTouchRadiusAdjust));
            var a_extraRad = _lastAAreaExtraRadius;
            var b_extraRad = _lastBAreaExtraRadius;
            var c_extraRad = _lastCAreaExtraRadius;
            var d_extraRad = _lastDAreaExtraRadius;
            var e_extraRad = _lastEAreaExtraRadius;
            //var lastCircular = cubeRay + new Vector3(0, userRad);
            fixed(Vector4* circleSamplesPtr = &circleSamples.GetPinnableReference())
            {
                MobileTouchPanelHelper.PositionHandle(cubeRay,
                    userRad,
                    a_extraRad,
                    b_extraRad,
                    c_extraRad,
                    d_extraRad,
                    e_extraRad,
                    FINGER_RADIUS_SEGMENT_LENGTH,
                    TOUCH_ANGLE_SMAPLE_COUNT,
                    _posData,
                    circleSamplesPtr,
                    ref newP);
            }
            isAnySensorTriggered |= (newP & SENSOR_BIT_MASK) > (1 << 11);
            // if there is any sensor bit triggered,
            // we consider it as sensor only.
            if (useGameplayTouchEnhancementFeatures)
            {
                isSensorOnly |= isAnySensorTriggered;
            }


            if (UseOuterTouchAsSensor || isSensorOnly)
            {
                newP |= (1UL << 63);
                newP |= versionBit;
                for (var i = 0; i < 8; i++)
                {
                    var state = extraButtonStates[i];
                    sensorStates[i] |= state;
                    if(state)
                    {
                        newP |= 1UL << i;
                    }
                }
                for (var i = 8; i < 12; i++)
                {
                    var state = extraButtonStates[i];
                    buttonStates[i] |= state;
                    if (state)
                    {
                        newP |= 1UL << i;
                    }
                }
                for (var i = (int)SensorArea.D1; i < (int)SensorArea.E1; i++)
                {
                    var state = extraSensorStates[i];
                    sensorStates[i + 1] |= state;
                    if (state)
                    {
                        newP |= 1UL << (i + 1 + 12);
                    }
                }
                for (var i = 0; i < 34; i++)
                {
                    var result = (newP & (1UL << (i + 12))) != 0;
                    sensorStates[i] |= result;
                }
            }
            else // is using outer touch as buttons or not in game
            {
                if (isAnyExtraButtonTriggered)
                {
                    newP = versionBit;
                    for (var i = 0; i < extraButtonStates.Length; i++)
                    {
                        var state = extraButtonStates[i];
                        buttonStates[i] |= extraButtonStates[i];
                        if (state)
                        {
                            newP |= 1UL << i;
                        }
                    }
                }
                else if(isAnySensorTriggered)
                {
                    for (var i = 0; i < 34; i++)
                    {
                        var result = (newP & (1UL << (i + 12))) != 0;
                        sensorStates[i] |= result;
                    }
                }
            }

            rawPositionData = newP;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void RaycastNow(in Vector3 pos, in Span<bool> newStates, ref ulong newP)
        {
            var ray = new Ray(pos, Vector3.forward);
            var ishit = Physics.Raycast(ray, out var hitInfom);
            if (ishit)
            {
                var id = hitInfom.colliderInstanceID;
                if (_instanceID2SensorIndexMappingTable.TryGetValue(id, out var index))
                {
                    newP |= 1UL << (index + 12);
                    newStates[index] = true;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void RaycastNow(in Vector3 pos, ref ulong newP)
        {
            var ray = new Ray(pos, Vector3.forward);
            var ishit = Physics.Raycast(ray, out var hitInfom);
            if (ishit)
            {
                var id = hitInfom.colliderInstanceID;
                if (_instanceID2SensorIndexMappingTable.TryGetValue(id, out var index))
                {
                    newP |= 1UL << (index + 12);
                }
            }
        }
    }
}
