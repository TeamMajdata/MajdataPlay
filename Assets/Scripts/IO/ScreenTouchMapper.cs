using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MajdataPlay.Diagnostics;
using MajdataPlay.UnsafeKit;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace MajdataPlay.IO
{
    /// <summary>Shared screen geometry and touch-radius mapping, updated on Unity's main thread.</summary>
    internal static unsafe class ScreenTouchMapper
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
        static float _lastTouchButtonRingEdge = 5.4f;
        public static bool UseOuterTouchAsSensor { get; set; }
        public static bool UseGameplayTouchEnhancementFeatures { get; set; } = false;

        static IReadOnlyDictionary<int, int> _instanceID2SensorIndexMappingTable = new Dictionary<int, int>();
        public static bool IsInitialized => _posData != null;
        public static ReadOnlySpan<Vector4> UnitCircle => _unitCircle.Span;
        public static ReadOnlySpan<ulong> TouchPanelPositionSamples => IsInitialized
            ? new ReadOnlySpan<ulong>(_posData, 1280 * 1280) : ReadOnlySpan<ulong>.Empty;
        public static float FingerRadius => MajEnv.Settings.Debug.TouchSimulationRadius;
        public static float TouchButtonRingEdge
        {
            get => _lastTouchButtonRingEdge;
            set => _lastTouchButtonRingEdge = value;
        }
        /// <summary>Left, top, right and bottom edges of the secondary screen.</summary>
        public static Vector4 SubScreenEdge { get; set; }

        public static void Init(IReadOnlyDictionary<int, int> instanceID2SensorIndexMappingTable)
        {
            if (IsInitialized) return;
            Input.multiTouchEnabled = true;
            EnhancedTouchSupport.Enable();
            _posData = UnsafeHelper.Alloc<ulong>(1280 * 1280);
            new Span<ulong>(_posData, 1280 * 1280).Clear();
            var samples = new Vector4[TOUCH_ANGLE_SMAPLE_COUNT];
            var step = 2f * Mathf.PI / TOUCH_ANGLE_SMAPLE_COUNT;

            for (int i = 0; i < TOUCH_ANGLE_SMAPLE_COUNT; i++)
            {
                var angle = step * i;
                samples[i] = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
            }

            _unitCircle = samples;
            _instanceID2SensorIndexMappingTable = instanceID2SensorIndexMappingTable;
            _lastScreenHeight = Screen.height;
            _lastScreenWidth = Screen.width;
            MajDebug.LogDebug("[ScreenTouchMapper]Screen dimensions initialized");
            MajDebug.LogDebug("[ScreenTouchMapper]Start generating sensor map");
            for (var x = -540; x <= 540; x++)
            {
                if ((x + 540) % 100 == 0)
                {
                    MajDebug.LogDebug($"[ScreenTouchMapper]Progress: {x + 540}/1080");
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
                            MajDebug.LogWarning($"[ScreenTouchMapper]Unknown collider instance id: {id}");
                        }
                    }
                }
            }
            MajDebug.LogDebug($"[ScreenTouchMapper]Sensor map generate finished");
            OnPreUpdate();
        }

        public static void OnPreUpdate()
        {
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
            var mainScreenTransform = displayOptions.MainScreenTransform;
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
        }

        public static void Dispose()
        {
            if (!IsInitialized) return;
            UnsafeHelper.Free(_posData);
            _posData = null;
            _unitCircle = ReadOnlyMemory<Vector4>.Empty;
            EnhancedTouchSupport.Disable();
        }

        /// <summary>Accumulates sensor and button states for one pointer and records its region flags.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PositionToSensorState(Span<bool> sensorStates,
            Span<bool> buttonStates,
            Camera mainCamera,
            Vector3 position,
            float touchRadius,
            ref ulong rawPositionData,
            ref bool isSensorOnly)
        {
            const ulong SENSOR_BIT_MASK = 0b0000_0000_0000_0000_0011_1111_1111_1111_1111_1111_1111_1111_1111_0000_0000_0000;

            if (!IsInitialized || mainCamera == null)
                return;
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
            Span<bool> extraButtonStates = stackalloc bool[12];
            Span<bool> extraSensorStates = stackalloc bool[34];
            extraButtonStates.Clear();
            extraSensorStates.Clear();
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

    }
}
