using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Settings;
using MajdataPlay.UnsafeKit;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using Unity.IL2CPP.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Profiling;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal class NoteManager : MonoBehaviour
    {

        TapUpdater _tapUpdater;
        HoldUpdater _holdUpdater;
        SlideUpdater _slideUpdater;
        TouchUpdater _touchUpdater;
        TouchHoldUpdater _touchHoldUpdater;
        EachLineUpdater _eachLineUpdater;
        SlideOKUpdater _slideOKUpdater;

        readonly int[] _noteCurrentIndex = new int[8];
        readonly int[] _touchCurrentIndex = new int[33];

        [ReadOnlyField]
        [SerializeField]
        double _preUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        double _updateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        double _fixedUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        double _lateUpdateElapsedMs = 0;

        //DJAuto
        readonly SwitchStatus[] _btnStatusInNextFrame = new SwitchStatus[8];
        readonly SwitchStatus[] _btnStatusInThisFrame = new SwitchStatus[8];
        readonly SwitchStatus[] _btnStatusInPreviousFrame = new SwitchStatus[8];
        //DJAuto
        readonly SwitchStatus[] _sensorStatusInNextFrame = new SwitchStatus[33];
        readonly SwitchStatus[] _sensorStatusInThisFrame = new SwitchStatus[33];
        readonly SwitchStatus[] _sensorStatusInPreviousFrame = new SwitchStatus[33];
        
        readonly bool[] _isBtnClickedInThisFrame = new bool[8];
        readonly bool[] _isSensorClickedInThisFrame = new bool[33];

#if UNITY_ANDROID || UNITY_IOS
        readonly int[] _btnClickedCountInThisFrame = new int[8];
        readonly int[] _btnUsedCountInThisFrame = new int[8];

        readonly int[] _sensorClickedCountInThisFrame = new int[33];
        readonly int[] _sensorUsedCountInThisFrame = new int[33];

        static int _defaultButtonStatusUsage = 0;
        static int _defaultSensorStatusUsage = 0;
#else
        readonly bool[] _isBtnUsedInThisFrame = new bool[8];
        readonly bool[] _isSensorUsedInThisFrame = new bool[33];
        static bool _isUseButtonRingForTouch = false;
#endif

        const string SENSOR_IS_NULL = "Sensor index requested by Note is null";
        const string SENSOR_OUT_OF_RANGE = "Sensor index requested by Note is out of range";
        const string BUTTON_IS_NULL = "Button index requested by Note is null";
        const string BUTTON_OUT_OF_RANGE = "Button index requested by Note is out of range";
        readonly bool USERSETTING_IS_AUTOPLAY = (MajEnv.SystemSettings?.Mod.AutoPlay ?? AutoplayModeOption.Disable) != AutoplayModeOption.Disable;

        void Awake()
        {
            Majdata<NoteManager>.Instance = this;

            Array.Fill(_btnStatusInNextFrame, SwitchStatus.Off);
            Array.Fill(_btnStatusInThisFrame, SwitchStatus.Off);
            Array.Fill(_btnStatusInPreviousFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInNextFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInThisFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInPreviousFrame, SwitchStatus.Off);
            Array.Fill(_isBtnClickedInThisFrame, false);
            Array.Fill(_isSensorClickedInThisFrame, false);
#if UNITY_ANDROID || UNITY_IOS
            Array.Fill(_btnUsedCountInThisFrame, 0);
            Array.Fill(_sensorUsedCountInThisFrame, 0);
#else
            Array.Fill(_isBtnUsedInThisFrame, false);
            Array.Fill(_isSensorUsedInThisFrame, false);
#endif
            Array.Fill(_noteCurrentIndex, 0);
            Array.Fill(_touchCurrentIndex, 0);
            //InputManager.BindAnyArea(OnAnyAreaTrigger);
        }
        void Start()
        {
            _tapUpdater = Majdata<TapUpdater>.Instance!;
            _holdUpdater = Majdata<HoldUpdater>.Instance!;
            _slideUpdater = Majdata<SlideUpdater>.Instance!;
            _touchUpdater = Majdata<TouchUpdater>.Instance!;
            _touchHoldUpdater = Majdata<TouchHoldUpdater>.Instance!;
            _eachLineUpdater = Majdata<EachLineUpdater>.Instance!;
            _slideOKUpdater = Majdata<SlideOKUpdater>.Instance!;
#if !(UNITY_ANDROID || UNITY_IOS)
            _isUseButtonRingForTouch = Majdata<INoteController>.Instance?.ModInfo.ButtonRingForTouch ?? false;
#endif
        }
        void OnDestroy()
        {
            Majdata<NoteManager>.Free();
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("NoteManager.OnPreUpdate"))
            {
                for (var i = 0; i < 8; i++)
                {
#if UNITY_ANDROID || UNITY_IOS
                    _btnUsedCountInThisFrame[i] = 0;
                    _btnClickedCountInThisFrame[i] = 0;
#else
                _isBtnUsedInThisFrame[i] = false;
#endif
                    _isBtnClickedInThisFrame[i] = false;
                }
                for (var i = 0; i < 33; i++)
                {
#if UNITY_ANDROID || UNITY_IOS
                    _sensorUsedCountInThisFrame[i] = 0;
                    _sensorClickedCountInThisFrame[i] = 0;
#else
                _isSensorUsedInThisFrame[i] = false;
#endif
                    _isSensorClickedInThisFrame[i] = false;
                }

                if (USERSETTING_IS_AUTOPLAY)
                {
                    AutoplayIOUpdate();
                }
                else
                {
                    GameIOUpdate();
                }
                _tapUpdater.OnPreUpdate();
                _holdUpdater.OnPreUpdate();
                _slideUpdater.OnPreUpdate();
                _touchUpdater.OnPreUpdate();
                _touchHoldUpdater.OnPreUpdate();
                _eachLineUpdater.OnPreUpdate();
#if UNITY_EDITOR || DEBUG
                _preUpdateElapsedMs = 0;
                _preUpdateElapsedMs += _tapUpdater.PreUpdateElapsedMs;
                _preUpdateElapsedMs += _holdUpdater.PreUpdateElapsedMs;
                _preUpdateElapsedMs += _slideUpdater.PreUpdateElapsedMs;
                _preUpdateElapsedMs += _touchUpdater.PreUpdateElapsedMs;
                _preUpdateElapsedMs += _touchHoldUpdater.PreUpdateElapsedMs;
                _preUpdateElapsedMs += _eachLineUpdater.PreUpdateElapsedMs;
#endif
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("NoteManager.OnUpdate"))
            {
                _tapUpdater.OnUpdate();
                _holdUpdater.OnUpdate();
                _slideUpdater.OnUpdate();
                _touchUpdater.OnUpdate();
                _touchHoldUpdater.OnUpdate();
                _eachLineUpdater.OnUpdate();
                _slideOKUpdater.OnUpdate();
#if UNITY_EDITOR || DEBUG
                _updateElapsedMs = 0;
                _updateElapsedMs += _tapUpdater.UpdateElapsedMs;
                _updateElapsedMs += _holdUpdater.UpdateElapsedMs;
                _updateElapsedMs += _slideUpdater.UpdateElapsedMs;
                _updateElapsedMs += _touchUpdater.UpdateElapsedMs;
                _updateElapsedMs += _touchHoldUpdater.UpdateElapsedMs;
                _updateElapsedMs += _eachLineUpdater.UpdateElapsedMs;
                _updateElapsedMs += _slideOKUpdater.UpdateElapsedMs;
#endif
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("NoteManager.OnLateUpdate"))
            {
                _tapUpdater.OnLateUpdate();
                _holdUpdater.OnLateUpdate();
                _slideUpdater.OnLateUpdate();
                _touchUpdater.OnLateUpdate();
                _touchHoldUpdater.OnLateUpdate();
                _eachLineUpdater.OnLateUpdate();
                _slideOKUpdater.OnLateUpdate();
#if UNITY_EDITOR || DEBUG
                _lateUpdateElapsedMs = 0;
                _lateUpdateElapsedMs += _tapUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _holdUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _slideUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _touchUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _touchHoldUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _eachLineUpdater.LateUpdateElapsedMs;
                _lateUpdateElapsedMs += _slideOKUpdater.LateUpdateElapsedMs;
#endif
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnFixedUpdate()
        {
            using (UnityProfiler.Create("NoteManager.OnFixedUpdate"))
            {
                _tapUpdater.OnFixedUpdate();
                _holdUpdater.OnFixedUpdate();
                _slideUpdater.OnFixedUpdate();
                _touchUpdater.OnFixedUpdate();
                _touchHoldUpdater.OnFixedUpdate();
                _eachLineUpdater.OnFixedUpdate();
#if UNITY_EDITOR || DEBUG
                _fixedUpdateElapsedMs = 0;
                _fixedUpdateElapsedMs += _tapUpdater.FixedUpdateElapsedMs;
                _fixedUpdateElapsedMs += _holdUpdater.FixedUpdateElapsedMs;
                _fixedUpdateElapsedMs += _slideUpdater.FixedUpdateElapsedMs;
                _fixedUpdateElapsedMs += _touchUpdater.FixedUpdateElapsedMs;
                _fixedUpdateElapsedMs += _touchHoldUpdater.FixedUpdateElapsedMs;
                _fixedUpdateElapsedMs += _eachLineUpdater.FixedUpdateElapsedMs;
#endif
            }
        }
        public async UniTask InitAsync()
        {
            _tapUpdater.Init();
            _holdUpdater.Init();
            _slideUpdater.Init();
            _touchUpdater.Init();
            _touchHoldUpdater.Init();
            _eachLineUpdater.Init();
            _slideOKUpdater.Init();
        }
        internal void Clear()
        {
            //_tapUpdater.Clear();
            //_holdUpdater.Clear();
            //_slideUpdater.Clear();
            //_touchUpdater.Clear();
            //_touchHoldUpdater.Clear();
            //_eachLineUpdater.Clear();

            Array.Fill(_btnStatusInNextFrame, SwitchStatus.Off);
            Array.Fill(_btnStatusInThisFrame, SwitchStatus.Off);
            Array.Fill(_btnStatusInPreviousFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInNextFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInThisFrame, SwitchStatus.Off);
            Array.Fill(_sensorStatusInPreviousFrame, SwitchStatus.Off);
#if UNITY_ANDROID || UNITY_IOS
            Array.Fill(_btnUsedCountInThisFrame, 0);
            Array.Fill(_sensorUsedCountInThisFrame, 0);
#else
            Array.Fill(_isBtnUsedInThisFrame, false);
            Array.Fill(_isSensorUsedInThisFrame, false);
#endif
            Array.Fill(_noteCurrentIndex, 0);
            Array.Fill(_touchCurrentIndex, 0);
        }
        public void ResetCounter()
        {
            for (var i = 0; i < 8; i++)
            {
                _noteCurrentIndex[i] = 0;
            }
            for (var i = 0; i < 33; i++)
            {
                _touchCurrentIndex[i] = 0;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void GameIOUpdate()
        {
            var previousButtonStatus = InputManager.ButtonStatusInPreviousFrame;
            var currentButtonStatus = InputManager.ButtonStatusInThisFrame;

            var previousSensorStatus = InputManager.SensorStatusInPreviousFrame;
            var currentSensorStatus = InputManager.SensorStatusInThisFrame;

#if UNITY_ANDROID || UNITY_IOS
            var btnClickedCount = InputManager.ButtonClickedCountInThisFrame;
            var sensorClickedCount = InputManager.SensorClickedCountInThisFrame;
            for (var i = 0; i < 8; i++)
            {
                _btnStatusInPreviousFrame[i] = previousButtonStatus[i];
                _btnStatusInThisFrame[i] = currentButtonStatus[i];

                _isBtnClickedInThisFrame[i] = btnClickedCount[i] != 0;
                _btnClickedCountInThisFrame[i] = btnClickedCount[i];
            }
            for (var i = 0; i < 33; i++)
            {
                _sensorStatusInPreviousFrame[i] = previousSensorStatus[i];
                _sensorStatusInThisFrame[i] = currentSensorStatus[i];

                _isSensorClickedInThisFrame[i] = sensorClickedCount[i] != 0;
                _sensorClickedCountInThisFrame[i] = sensorClickedCount[i];
            }
#else
            for (var i = 0; i < 8; i++)
            {
                _btnStatusInPreviousFrame[i] = previousButtonStatus[i];
                _btnStatusInThisFrame[i] = currentButtonStatus[i];
                _isBtnClickedInThisFrame[i] = _btnStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                              _btnStatusInThisFrame[i] == SwitchStatus.On;
            }
            for (var i = 0; i < 33; i++)
            {
                _sensorStatusInPreviousFrame[i] = previousSensorStatus[i];
                _sensorStatusInThisFrame[i] = currentSensorStatus[i];
                if (_isUseButtonRingForTouch && i < 8)
                {
                    _sensorStatusInThisFrame[i] |= _btnStatusInThisFrame[i];
                }
                _isSensorClickedInThisFrame[i] = _sensorStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                                 _sensorStatusInThisFrame[i] == SwitchStatus.On;
            }
#endif
        }
        void AutoplayIOUpdate()
        {
            for (var i = 0; i < 8; i++)
            {
                var btnState = _btnStatusInNextFrame[i];
                
                _btnStatusInPreviousFrame[i] = _btnStatusInThisFrame[i];
                _btnStatusInThisFrame[i] = btnState;
                _btnStatusInNextFrame[i] = SwitchStatus.Off;
                
#if UNITY_ANDROID || UNITY_IOS
                var isClicked = _btnStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                _btnStatusInThisFrame[i] == SwitchStatus.On;
                _isBtnClickedInThisFrame[i] |= isClicked;
                _btnClickedCountInThisFrame[i] += Convert.ToInt32(_isBtnClickedInThisFrame[i]);
#else
                _isBtnClickedInThisFrame[i] = _btnStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                              _btnStatusInThisFrame[i] == SwitchStatus.On;
#endif
            }
            for (var i = 0; i < 33; i++)
            {
                var senState = _sensorStatusInNextFrame[i];
#if !(UNITY_ANDROID || UNITY_IOS)
                if (_isUseButtonRingForTouch && i < 8)
                {
                    senState |= _btnStatusInThisFrame[i];
                }
#endif
                _sensorStatusInPreviousFrame[i] = _sensorStatusInThisFrame[i];
                _sensorStatusInThisFrame[i] = senState;
                _sensorStatusInNextFrame[i] = SwitchStatus.Off;

#if UNITY_ANDROID || UNITY_IOS
                var isClicked = _sensorStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                _sensorStatusInThisFrame[i] == SwitchStatus.On;
                _isSensorClickedInThisFrame[i] |= isClicked;
                _sensorClickedCountInThisFrame[i] += Convert.ToInt32(isClicked);
#else
                _isSensorClickedInThisFrame[i] = _sensorStatusInPreviousFrame[i] == SwitchStatus.Off &&
                                                 _sensorStatusInThisFrame[i] == SwitchStatus.On;
#endif
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCurrentNoteJudgeable(in TapQueueInfo queueInfo)
        {
            var keyIndex = queueInfo.KeyIndex - 1;
            if (!keyIndex.InRange(0, 7))
            {
                return false;
            }

            var currentIndex = _noteCurrentIndex[keyIndex];
            var index = queueInfo.Index;

            return index <= currentIndex;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCurrentNoteJudgeable(in TouchQueueInfo queueInfo)
        {
            var sensorPos = queueInfo.SensorPos;
            if (sensorPos > SensorArea.E8 || sensorPos < SensorArea.A1)
            {
                return false;
            }
            var pos = (int)sensorPos;
            var index = queueInfo.Index;
            var currentIndex = _touchCurrentIndex[pos];

            return index <= currentIndex;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextNote(in TapQueueInfo queueInfo)
        {
            var keyIndex = queueInfo.KeyIndex - 1;
            if (!keyIndex.InRange(0, 7))
            {
                return;
            }
            ref var currentIndex = ref _noteCurrentIndex[keyIndex];

            currentIndex++;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NextTouch(in TouchQueueInfo queueInfo)
        {
            var sensorPos = queueInfo.SensorPos;
            if (sensorPos > SensorArea.E8 || sensorPos < SensorArea.A1)
            {
                return;
            }
            var pos = (int)sensorPos;
            ref var currentIndex = ref _touchCurrentIndex[pos];

            currentIndex++;
        }
#if UNITY_ANDROID || UNITY_IOS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryUseButtonClickEvent(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }
            ref var btnUsedCount = ref _btnUsedCountInThisFrame[(int)zone];
            var btnClickedCount = _btnClickedCountInThisFrame[(int)zone];

            if (btnUsedCount >= btnClickedCount)
            {
                return false;
            }
            btnUsedCount++;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryUseSensorClickEvent(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return false;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return false;
            }
            ref var sensorUsedCount = ref _sensorUsedCountInThisFrame[(int)area];
            var sensorClickedCount = _sensorClickedCountInThisFrame[(int)area];

            if (sensorUsedCount >= sensorClickedCount)
            {
                return false;
            }
            sensorUsedCount++;
            return true;
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryUseButtonClickEvent(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }
            ref var isUsed = ref _isBtnUsedInThisFrame[(int)zone];
            if (isUsed)
            {
                return false;
            }
            isUsed = true;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryUseSensorClickEvent(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return false;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return false;
            }
            ref var isUsed = ref _isSensorUsedInThisFrame[(int)area];
            if (isUsed)
            {
                return false;
            }
            isUsed = true;
            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckSensorStatusInThisFrame(SensorArea? area, SwitchStatus targetState)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }

            return _sensorStatusInThisFrame[(int)area] == targetState;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckButtonStatusInThisFrame(ButtonZone? zone, SwitchStatus targetState)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            return _btnStatusInThisFrame[(int)zone] == targetState;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckButtonStatusInPreviousFrame(ButtonZone? zone, SwitchStatus targetState)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            return _btnStatusInPreviousFrame[(int)zone] == targetState;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckSensorStatusInPreviousFrame(SensorArea? area, SwitchStatus targetState)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }

            return _sensorStatusInPreviousFrame[(int)area] == targetState;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SwitchStatus GetButtonStatusInThisFrame(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            return _btnStatusInThisFrame[(int)zone];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SwitchStatus GetButtonStatusInPreviousFrame(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            return _btnStatusInPreviousFrame[(int)zone];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SwitchStatus GetSensorStatusInThisFrame(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }

            return _sensorStatusInThisFrame[(int)area];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SwitchStatus GetSensorStatusInPreviousFrame(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }

            return _sensorStatusInPreviousFrame[(int)area];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsButtonClickedInThisFrame(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }
            var i = (int)zone;

            return _isBtnClickedInThisFrame[i];
            //return _btnStatusInPreviousFrame[i] == SwitchStatus.Off &&
            //       _btnStatusInThisFrame[i] == SwitchStatus.On;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSensorClickedInThisFrame(SensorArea? area)
        {
            if(area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }
            var i = (int)area;

            return _isSensorClickedInThisFrame[i];
            //return _sensorStatusInPreviousFrame[i] == SwitchStatus.Off &&
            //       _sensorStatusInThisFrame[i] == SwitchStatus.On;
        }

        public bool SimulateSensorPress(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }
            var i = (int)area;
            var raw = _sensorStatusInNextFrame[i];
            _sensorStatusInNextFrame[i] = SwitchStatus.On;

            return raw == SwitchStatus.Off;
        }
        public bool SimulateButtonPress(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            var i = (int)zone;
            var raw = _btnStatusInNextFrame[i];
            _btnStatusInNextFrame[i] = SwitchStatus.On;

            return raw == SwitchStatus.Off;
        }
        public bool SimulateSensorClick(SensorArea? area)
        {
            if (area is null)
            {
                MajDebug.LogWarning(SENSOR_IS_NULL);
                return default;
            }
            else if (area < SensorArea.A1 || area > SensorArea.E8)
            {
                MajDebug.LogWarning(SENSOR_OUT_OF_RANGE);
                return default;
            }

            var i = (int)area;
            var raw = _sensorStatusInThisFrame[i] == SwitchStatus.Off &&
                      _sensorStatusInNextFrame[i] == SwitchStatus.Off;
            _sensorStatusInNextFrame[i] = SwitchStatus.On;

            return raw;
        }
        public bool SimulateButtonClick(ButtonZone? zone)
        {
            if (zone is null)
            {
                MajDebug.LogWarning(BUTTON_IS_NULL);
                return default;
            }
            else if (zone < ButtonZone.A1 || zone > ButtonZone.A8)
            {
                MajDebug.LogWarning(BUTTON_OUT_OF_RANGE);
                return default;
            }

            var i = (int)zone;
            var raw = _btnStatusInThisFrame[i] == SwitchStatus.Off &&
                      _btnStatusInNextFrame[i] == SwitchStatus.Off;
            _btnStatusInNextFrame[i] = SwitchStatus.On;

            return raw;
        }
        
    }
}