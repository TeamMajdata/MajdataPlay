using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.View;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal class NotePoolManager : MonoBehaviour
    {
        public ComponentState State { get; private set; } = ComponentState.Idle;

        NotePool<TapPoolingInfo, TapQueueInfo> tapPool;
        NotePool<HoldPoolingInfo, TapQueueInfo> holdPool;
        NotePool<TouchPoolingInfo, TouchQueueInfo> touchPool;
        NotePool<TouchHoldPoolingInfo, TouchQueueInfo> touchHoldPool;
        EachLinePool eachLinePool;

        [SerializeField]
        GameObject tapPrefab;
        [SerializeField]
        GameObject holdPrefab;
        [SerializeField]
        GameObject touchPrefab;
        [SerializeField]
        GameObject touchHoldPrefab;
        [SerializeField]
        GameObject eachLinePrefab;

        INoteTimeProvider _noteTimeProvider;
        PooledList<TapPoolingInfo> _tapInfos = new();
        PooledList<HoldPoolingInfo> _holdInfos = new();
        PooledList<TouchPoolingInfo> _touchInfos = new();
        PooledList<TouchHoldPoolingInfo> _touchHoldInfos = new();
        PooledList<EachLinePoolingInfo> _eachLineInfos = new();
        void Awake()
        {
            Majdata<NotePoolManager>.Instance = this;
        }
        public void Init()
        {
            var noteLoader = Majdata<NoteLoader>.Instance!;
            var debugOptions = MajEnv.Settings.Debug;
            var isHadTap = false;
            var isHadHold = false;
            var isHadTouch = false;
            var isHadTouchHold = false;
            var isHadEachLine = false;

            for (var i = 0; i < noteLoader.IsHasTap.Length; i++) 
            {
                isHadTap |= noteLoader.IsHasTap[i];
                isHadHold |= noteLoader.IsHasHold[i];
            }
            for (var i = 0; i < noteLoader.IsHasTouch.Length; i++)
            {
                isHadTouch |= noteLoader.IsHasTouch[i];
                isHadTouchHold |= noteLoader.IsHasTouchHold[i];
            }
            isHadEachLine = isHadTap || isHadHold;
            var tapPoolCapacity = isHadTap ? debugOptions.TapPoolCapacity : 0;
            var holdPoolCapacity = isHadHold ? debugOptions.HoldPoolCapacity : 0;
            var touchPoolCapacity = isHadTouch ? debugOptions.TouchPoolCapacity : 0;
            var touchHoldPoolCapacity = isHadTouchHold ? debugOptions.TouchHoldPoolCapacity : 0;
            var eachLinePoolCapacity = isHadEachLine ? debugOptions.EachLinePoolCapacity : 0;

            var tapParent = transform.GetChild(0);
            var holdParent = transform.GetChild(1);
            var touchParent = transform.GetChild(4);
            var touchHoldParent = transform.GetChild(5);
            var eachLineParent = transform.GetChild(6);
            
            tapPool = new(tapPrefab, tapParent, _tapInfos.ToArray(), Math.Max(tapPoolCapacity, 0));
            holdPool = new(holdPrefab, holdParent, _holdInfos.ToArray(), Math.Max(holdPoolCapacity, 0));
            touchPool = new(touchPrefab, touchParent, _touchInfos.ToArray(), Math.Max(touchPoolCapacity, 0));
            touchHoldPool = new(touchHoldPrefab, touchHoldParent, _touchHoldInfos.ToArray(), Math.Max(touchHoldPoolCapacity, 0));
            eachLinePool = new(eachLinePrefab, eachLineParent, _eachLineInfos.ToArray(), Math.Max(eachLinePoolCapacity, 0));
            State = ComponentState.Running;
        }
        void Start()
        {
            _noteTimeProvider = Majdata<INoteController>.Instance!;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("NotePoolManager.PreUpdate"))
            {
                switch (State)
                {
                    case ComponentState.Idle:
                    case ComponentState.Scanning:
                    case ComponentState.Loading:
                    case ComponentState.Parsing:
                        return;
                }
                var currentSec = _noteTimeProvider.ThisFrameSec;
                tapPool.OnPreUpdate(currentSec);
                holdPool.OnPreUpdate(currentSec);
                touchPool.OnPreUpdate(currentSec);
                touchHoldPool.OnPreUpdate(currentSec);
                eachLinePool.OnPreUpdate(currentSec);
            }
        }
        public void AddTap(TapPoolingInfo tapInfo)
        {
            _tapInfos.Add(tapInfo);
        }
        public void AddHold(HoldPoolingInfo holdInfo)
        {
            _holdInfos.Add(holdInfo);
        }
        public void AddTouch(TouchPoolingInfo touchInfo)
        {
            _touchInfos.Add(touchInfo);
        }
        public void AddTouchHold(TouchHoldPoolingInfo touchHoldInfo)
        {
            _touchHoldInfos.Add(touchHoldInfo);
        }
        public void AddEachLine(EachLinePoolingInfo eachLineInfo)
        {
            _eachLineInfos.Add(eachLineInfo);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Collect<TNote>(TNote endNote)
        {
            switch (endNote)
            {
                case TapDrop tap:
                    tapPool.Collect(tap);
                    break;
                case HoldDrop hold:
                    holdPool.Collect(hold);
                    break;
                case EachLineDrop eachLine:
                    eachLinePool.Collect(eachLine);
                    break;
                case TouchDrop touch:
                    touchPool.Collect(touch);
                    break;
                case TouchHoldDrop touchHold:
                    touchHoldPool.Collect(touchHold);
                    break;
            }
        }
        void OnDestroy()
        {
            _tapInfos.Dispose();
            _holdInfos.Dispose();
            _touchInfos.Dispose();
            _touchHoldInfos.Dispose();
            _eachLineInfos.Dispose();

            tapPool?.Dispose();
            holdPool?.Dispose();
            touchPool?.Dispose();
            touchHoldPool?.Dispose();
            eachLinePool?.Dispose();
            Majdata<NotePoolManager>.Free();
        }
        internal void Clear()
        {
            tapPool?.Dispose();
            holdPool?.Dispose();
            touchPool?.Dispose();
            touchHoldPool?.Dispose();
            eachLinePool?.Dispose();

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var childCountInChild = child.childCount;
                for (var ii = 0; ii < childCountInChild; ii++)
                {
                    var childInChild = child.GetChild(ii);
                    Destroy(childInChild.gameObject);
                }
            }

            _tapInfos.Clear();
            _holdInfos.Clear();
            _touchInfos.Clear();
            _touchHoldInfos.Clear();
            _eachLineInfos.Clear();
        }
    }
}
