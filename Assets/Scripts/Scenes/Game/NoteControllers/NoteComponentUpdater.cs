using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal class NoteComponentUpdater : MonoBehaviour
    {
        public double PreUpdateElapsedMs => IntPreUpdateElapsedMs;
        public double UpdateElapsedMs => IntUpdateElapsedMs;
        public double FixedUpdateElapsedMs => IntFixedUpdateElapsedMs;
        public double LateUpdateElapsedMs => IntLateUpdateElapsedMs;

        protected ReadOnlyMemory<NoteComponentInfo> Components = ReadOnlyMemory<NoteComponentInfo>.Empty;
        protected ReadOnlyMemory<NoteComponentInfo> PreUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
        protected ReadOnlyMemory<NoteComponentInfo> UpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
        protected ReadOnlyMemory<NoteComponentInfo> FixedUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
        protected ReadOnlyMemory<NoteComponentInfo> LateUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;

        PooledArray<NoteComponentInfo> _rentedArrayForComponents = default;
        PooledArray<NoteComponentInfo> _rentedArrayForPreUpdatebleComponents = default;
        PooledArray<NoteComponentInfo> _rentedArrayForUpdatebleComponents = default;
        PooledArray<NoteComponentInfo> _rentedArrayForFixedUpdatebleComponents = default;
        PooledArray<NoteComponentInfo> _rentedArrayForLateUpdatebleComponents = default;

        [ReadOnlyField]
        [SerializeField]
        protected double IntPreUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        protected double IntUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        protected double IntFixedUpdateElapsedMs = 0;
        [ReadOnlyField]
        [SerializeField]
        protected double IntLateUpdateElapsedMs = 0;

        readonly static List<MonoBehaviour> SHARED_CACHE_LIST = new(64);
        public virtual async UniTask InitAsync()
        {
            await UniTask.SwitchToMainThread();
            var children = transform.GetChildren();

            using PooledList<NoteComponentInfo> noteComponents = new();
            using PooledList<NoteComponentInfo> preUpdatableComponents = new();
            using PooledList<NoteComponentInfo> updatableComponents = new();
            using PooledList<NoteComponentInfo> fixedUpdatableComponents = new();
            using PooledList<NoteComponentInfo> lateUpdatableComponents = new();
            using PooledList<MonoBehaviour> components = new();

            foreach (var child in children)
            {
                child.GetComponents<MonoBehaviour>(SHARED_CACHE_LIST);
                if (SHARED_CACHE_LIST.Count != 0)
                {
                    components.AddRange(SHARED_CACHE_LIST);
                }
                SHARED_CACHE_LIST.Clear();
            }
            await UniTask.SwitchToThreadPool();
            foreach (var component in components)
            {
                var noteInfo = new NoteComponentInfo(component);
                if (noteInfo.IsValid)
                {
                    if (noteInfo.IsUpdatable)
                    {
                        updatableComponents.Add(noteInfo);
                    }
                    if (noteInfo.IsFixedUpdatable)
                    {
                        fixedUpdatableComponents.Add(noteInfo);
                    }
                    if (noteInfo.IsLateUpdatable)
                    {
                        lateUpdatableComponents.Add(noteInfo);
                    }
                    if (noteInfo.IsPreUpdatable)
                    {
                        preUpdatableComponents.Add(noteInfo);
                    }
                    noteComponents.Add(noteInfo);
                }
                else
                {
                    noteInfo.Dispose();
                }
            }
            
            _rentedArrayForComponents = Pool<NoteComponentInfo>.Rent(noteComponents.Count, true);
            _rentedArrayForPreUpdatebleComponents = Pool<NoteComponentInfo>.Rent(preUpdatableComponents.Count, true);
            _rentedArrayForUpdatebleComponents = Pool<NoteComponentInfo>.Rent(updatableComponents.Count, true);
            _rentedArrayForFixedUpdatebleComponents = Pool<NoteComponentInfo>.Rent(fixedUpdatableComponents.Count, true);
            _rentedArrayForLateUpdatebleComponents = Pool<NoteComponentInfo>.Rent(lateUpdatableComponents.Count, true);

            noteComponents.CopyTo(_rentedArrayForComponents);
            preUpdatableComponents.CopyTo(_rentedArrayForPreUpdatebleComponents);
            updatableComponents.CopyTo(_rentedArrayForUpdatebleComponents);
            fixedUpdatableComponents.CopyTo(_rentedArrayForFixedUpdatebleComponents);
            lateUpdatableComponents.CopyTo(_rentedArrayForLateUpdatebleComponents);

            Components = _rentedArrayForComponents.AsMemory(0, noteComponents.Count);
            PreUpdatebleComponents = _rentedArrayForPreUpdatebleComponents.AsMemory(0, preUpdatableComponents.Count);
            UpdatebleComponents = _rentedArrayForUpdatebleComponents.AsMemory(0, updatableComponents.Count);
            FixedUpdatebleComponents = _rentedArrayForFixedUpdatebleComponents.AsMemory(0, fixedUpdatableComponents.Count);
            LateUpdatebleComponents = _rentedArrayForLateUpdatebleComponents.AsMemory(0, lateUpdatableComponents.Count);
        }

        protected virtual void OnDestroy()
        {
            Clear();
        }

        internal virtual void Clear()
        {
            foreach (var component in Components.Span)
            {
                component.Dispose();
            }

            Components = ReadOnlyMemory<NoteComponentInfo>.Empty;
            PreUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
            UpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
            FixedUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;
            LateUpdatebleComponents = ReadOnlyMemory<NoteComponentInfo>.Empty;

            _rentedArrayForComponents.Dispose();
            _rentedArrayForPreUpdatebleComponents.Dispose();
            _rentedArrayForUpdatebleComponents.Dispose();
            _rentedArrayForFixedUpdatebleComponents.Dispose();
            _rentedArrayForLateUpdatebleComponents.Dispose();

            _rentedArrayForComponents = default;
            _rentedArrayForPreUpdatebleComponents = default;
            _rentedArrayForUpdatebleComponents = default;
            _rentedArrayForFixedUpdatebleComponents = default;
            _rentedArrayForLateUpdatebleComponents = default;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnPreUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            var preUpdatebleComponents = PreUpdatebleComponents.Span;
            var len = preUpdatebleComponents.Length;
            for (var i = 0; i < len; i++)
            {
                var component = preUpdatebleComponents[i];
                try
                {
                    component.OnPreUpdate();
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }

            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            IntPreUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            var updatebleComponents = UpdatebleComponents.Span;
            var len = updatebleComponents.Length;
            for (var i = 0; i < len; i++)
            {
                var component = updatebleComponents[i];
                try
                {
                    component.OnUpdate();
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }

            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            IntUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnFixedUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            var fixedUpdatebleComponents = FixedUpdatebleComponents.Span;
            var len = fixedUpdatebleComponents.Length;
            for (var i = 0; i < len; i++)
            {
                var component = fixedUpdatebleComponents[i];
                try
                {
                    component.OnFixedUpdate();
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            IntFixedUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnLateUpdate()
        {
            var start = MajTimeline.UnscaledTime;
            var lateUpdatebleComponents = LateUpdatebleComponents.Span;
            var len = lateUpdatebleComponents.Length;
            for (var i = 0; i < len; i++)
            {
                var component = lateUpdatebleComponents[i];
                try
                {
                    component.OnLateUpdate();
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }

            var end = MajTimeline.UnscaledTime;
            var timeSpan = end - start;
            IntLateUpdateElapsedMs = timeSpan.TotalMilliseconds;
        }
    }
}
