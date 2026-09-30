using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.View;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal sealed class SlideUpdater : NoteUpdater<SlideBase>
    {
        const string UPDATER_NAME = "SlideUpdater";
        const string PRE_UPDATE_METHOD_NAME = UPDATER_NAME + ".PreUpdate";
        const string UPDATE_METHOD_NAME = UPDATER_NAME + ".Update";
        const string FIXED_UPDATE_METHOD_NAME = UPDATER_NAME + ".FixedUpdate";
        const string LATE_UPDATE_METHOD_NAME = UPDATER_NAME + ".LateUpdate";

        ReadOnlyMemory<SlideQueueInfo> _queueInfos = ReadOnlyMemory<SlideQueueInfo>.Empty;

        readonly PooledList<SlideBinding> _slideBindings = new PooledList<SlideBinding>();
        readonly PooledList<SlideBinding> _activatedSlides = new PooledList<SlideBinding>();

        int _slideBindingCursor = 0;

        PooledArray<SlideQueueInfo> _rentedArrayForQueueInfos = default;
        INoteTimeProvider _noteTimeProvider;
        

        void Awake()
        {
            Majdata<SlideUpdater>.Instance = this;
        }
        public override void Init()
        {
            for (var i = 0; i < _queueInfos.Length; i++)
            {
                var queueInfo = _queueInfos.Span[i];
                _slideBindings.Add(new(queueInfo)
                {
                    AppearTiming = queueInfo.AppearTiming - 0.15f,
                    QueueInfo = queueInfo,
                });
            }
        }
        void OnDestroy()
        {
            Majdata<SlideUpdater>.Free();
            Clear();
        }
        private void Start()
        {
            _noteTimeProvider = Majdata<INoteController>.Instance!;
            Clear();
        }
        void Clear()
        {
            _queueInfos = ReadOnlyMemory<SlideQueueInfo>.Empty;
            _rentedArrayForQueueInfos.Dispose();
            _rentedArrayForQueueInfos = default;
        }
        internal void AddSlideQueueInfos(IEnumerable<SlideQueueInfo> infos)
        {
            if (infos is null)
            {
                throw new ArgumentNullException();
            }
            using var buffer = new PooledList<SlideQueueInfo>();
            buffer.AddRange(infos.Where(x => x is not null).OrderBy(x => x.AppearTiming));
            _rentedArrayForQueueInfos = Pool<SlideQueueInfo>.Rent(buffer.Count, true);
            var queueInfos = _rentedArrayForQueueInfos.AsMemory(0, buffer.Count);
            buffer.CopyTo(queueInfos.Span);
            _queueInfos = queueInfos;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnFixedUpdate()
        {

        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create(LATE_UPDATE_METHOD_NAME))
            {
                var len = _activatedSlides.Count;
                for (var i = 0; i < len; i++)
                {
                    var instance = _activatedSlides[i];
                    if (instance.State == NoteStatus.End)
                    {
                        _activatedSlides.RemoveAt(i);
                        i--;
                        len--;
                        continue;
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create(UPDATE_METHOD_NAME))
            {
                ref var activatedSlides = ref MemoryMarshal.GetReference(_activatedSlides.AsSpan());
                var slideCount = _activatedSlides.Count;
                for (var i = 0; i < slideCount; i++)
                {
                    ref readonly var instance = ref Unsafe.Add(ref activatedSlides, i);
                    var instanceState = instance.State;
                    if (instanceState > NoteStatus.Start && instanceState < NoteStatus.End)
                    {
                        instance.OnUpdate();
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create(PRE_UPDATE_METHOD_NAME))
            {
                var thisFrameSec = _noteTimeProvider.ThisFrameSec;
                if (_queueInfos.Length - _slideBindingCursor > 0)
                {
                    for (ref var i = ref _slideBindingCursor; i < _slideBindings.Count; i++)
                    {
                        var binding = _slideBindings[i];
                        var queueInfo = binding.QueueInfo;
                        var appearTiming = binding.AppearTiming;
                        if (thisFrameSec >= appearTiming)
                        {
                            queueInfo.SlideObject.SetActive(true);
                            _activatedSlides.Add(binding);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                ref var activatedSlides = ref MemoryMarshal.GetReference(_activatedSlides.AsSpan());
                var slideCount = _activatedSlides.Count;
                for (var i = 0; i < slideCount; i++)
                {
                    ref readonly var instance = ref Unsafe.Add(ref activatedSlides, i);
                    var instanceState = instance.State;
                    if (instanceState > NoteStatus.Start && instanceState < NoteStatus.End)
                    {
                        instance.OnPreUpdate();
                    }
                }
            }
        }
        readonly struct SlideBinding
        {
            public required float AppearTiming { get; init; }
            public required SlideQueueInfo QueueInfo { get; init; }
            public NoteStatus State
            {
                [Il2CppSetOption(Option.NullChecks, false)]
                [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    switch (_state)
                    {
                        case STATE_SLIDE_DROP:
                            return _slideInstance!.State;
                        case STATE_EX_SLIDE_DROP:
                            return _exSlideInstance!.State;
                        case STATE_WIFI_DROP:
                            return _wifiInstance!.State;
                        default:
                            return NoteStatus.End;
                    }
                }
            }

            readonly int _state = STATE_INVALID;
            readonly SlideDrop? _slideInstance;
            readonly ExtendSlideDrop? _exSlideInstance;
            readonly WifiDrop? _wifiInstance;

            const int STATE_INVALID = 0;
            const int STATE_SLIDE_DROP = 1;
            const int STATE_EX_SLIDE_DROP = 2;
            const int STATE_WIFI_DROP = 3;

            public SlideBinding(SlideQueueInfo queueInfo)
            {
                var instance = queueInfo.SlideObject;
                if (instance is SlideDrop slideDrop)
                {
                    _slideInstance = slideDrop;
                    _state = STATE_SLIDE_DROP;
                }
                else if(instance is ExtendSlideDrop exSlideDrop)
                {
                    _exSlideInstance = exSlideDrop;
                    _state = STATE_EX_SLIDE_DROP;
                }
                else if(instance is WifiDrop wifiDrop)
                {
                    _wifiInstance = wifiDrop;
                    _state = STATE_WIFI_DROP;
                }
            }
            [Il2CppSetOption(Option.NullChecks, false)]
            [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void OnPreUpdate()
            {
                switch(_state)
                {
                    case STATE_SLIDE_DROP:
                        _slideInstance!.OnPreUpdate();
                        break;
                    case STATE_EX_SLIDE_DROP:
                        _exSlideInstance!.OnPreUpdate();
                        break;
                    case STATE_WIFI_DROP:
                        _wifiInstance!.OnPreUpdate();
                        break;
                }
            }
            [Il2CppSetOption(Option.NullChecks, false)]
            [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void OnUpdate()
            {
                switch (_state)
                {
                    case STATE_SLIDE_DROP:
                        _slideInstance!.OnUpdate();
                        break;
                    case STATE_EX_SLIDE_DROP:
                        _exSlideInstance!.OnUpdate();
                        break;
                    case STATE_WIFI_DROP:
                        _wifiInstance!.OnUpdate();
                        break;
                }
            }
        }
    }
}
