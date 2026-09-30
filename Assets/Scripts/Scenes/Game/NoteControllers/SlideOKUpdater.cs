using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.IL2CPP.CompilerServices;

namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal sealed class SlideOKUpdater : NoteUpdater<SlideOK>
    {
        const string UPDATER_NAME = "SlideOKUpdater";
        const string PRE_UPDATE_METHOD_NAME = UPDATER_NAME + ".PreUpdate";
        const string UPDATE_METHOD_NAME = UPDATER_NAME + ".Update";
        const string FIXED_UPDATE_METHOD_NAME = UPDATER_NAME + ".FixedUpdate";
        const string LATE_UPDATE_METHOD_NAME = UPDATER_NAME + ".LateUpdate";

        readonly PooledList<SlideOK> _activeSlideOKs = new();
        readonly PooledList<SlideOK> _inactiveSlideOKs = new();

        void Awake()
        {
            Majdata<SlideOKUpdater>.Instance = this;
        }
        void OnDestroy()
        {
            Majdata<SlideOKUpdater>.Free();
        }

        public override void Init()
        {
            base.Init();
            _inactiveSlideOKs.AddRange(NoteInstances.AsSpan());
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
        internal void OnUpdate()
        {
            using (UnityProfiler.Create(UPDATE_METHOD_NAME))
            {
                ref var activatedSlideOKs = ref MemoryMarshal.GetReference(_activeSlideOKs.AsSpan());
                var slideOKCount = _activeSlideOKs.Count;
                for (var i = 0; i < slideOKCount; i++)
                {
                    ref readonly var instance = ref Unsafe.Add(ref activatedSlideOKs, i);
                    instance.OnUpdate();
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create(LATE_UPDATE_METHOD_NAME))
            {
                var len = _activeSlideOKs.Count;
                for (var i = 0; i < len; i++)
                {
                    var instance = _activeSlideOKs[i];
                    if (instance.State == NoteStatus.End)
                    {
                        _activeSlideOKs.RemoveAt(i);
                        i--;
                        len--;
                        continue;
                    }
                }

                for (var i = 0; i < _inactiveSlideOKs.Count; i++)
                {
                    var instance = _inactiveSlideOKs[i];
                    if (instance.State == NoteStatus.Running)
                    {
                        _activeSlideOKs.Add(instance);
                        _inactiveSlideOKs.RemoveAt(i);
                        i--;
                    }
                }
            }
        }
    }
}
