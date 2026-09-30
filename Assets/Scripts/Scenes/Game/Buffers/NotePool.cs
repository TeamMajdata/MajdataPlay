using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game.Buffers
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal class NotePool<TInfo, TMember> : INotePool<TInfo, TMember>, IDisposable
        where TInfo : NotePoolingInfo where TMember : NoteQueueInfo
    {
        public int Capacity { get; set; } = 64;
        public bool IsStatic { get; } = true;

        protected bool _isDisposed = false;
        protected readonly Transform _parent;
        protected Memory<TimingPoint<TInfo>> _timingPoints = Memory<TimingPoint<TInfo>>.Empty;
        protected Bucket _storage;

        protected uint _flag = 0;

        PooledArray<TimingPoint<TInfo>> _rentedArrayForTimingPoints = default;
        PooledArray<TInfo> _rentedArrayForNotePoolingInfos = default;

        ~NotePool()
        {
            Dispose();
        }
        public NotePool(GameObject prefab, Transform parent, TInfo[] noteInfos, int capacity)
        {
            Capacity = capacity;
            var rentedArray = Pool<IPoolableNote<TInfo, TMember>?>.Rent(capacity);
            _parent = parent;
            for (var i = 0; i < capacity; i++)
            {
                var obj = UnityEngine.Object.Instantiate(prefab, parent);
                obj.SetActive(true);
                var noteObj = obj.GetComponent<IPoolableNote<TInfo, TMember>>();
                if (noteObj is null)
                {
                    throw new NotSupportedException();
                }
                rentedArray[i] = noteObj;
            }
            _storage = new Bucket(rentedArray, capacity);

            using var orderedTimingPoints = new PooledList<IGrouping<float, TInfo>>(noteInfos.GroupBy(x => x.AppearTiming)
                                                                                             .OrderBy(x => x.Key));
            _rentedArrayForTimingPoints = Pool<TimingPoint<TInfo>>.Rent(orderedTimingPoints.Count, true);
            _rentedArrayForNotePoolingInfos = Pool<TInfo>.Rent(noteInfos.Length, true);
            _timingPoints = _rentedArrayForTimingPoints.AsMemory(0, orderedTimingPoints.Count);
            var timingPoints = _timingPoints.Span;
            var notePoolingInfoArrayCursor = 0;
            using var cacheList = new PooledList<TInfo>(16);
            foreach (var (i, timingPoint) in orderedTimingPoints.WithIndex())
            {
                cacheList.AddRange(timingPoint);
                var allocatedArray = _rentedArrayForNotePoolingInfos.AsMemory(notePoolingInfoArrayCursor, cacheList.Count);
                var span = allocatedArray.Span;
                cacheList.CopyTo(span);
                notePoolingInfoArrayCursor += cacheList.Count;
                timingPoints[i] = new TimingPoint<TInfo>()
                {
                    Timing = timingPoint.Key,
                    Infos = allocatedArray
                };
                cacheList.Clear();
            }
        }
        public NotePool(GameObject prefab, Transform parent, TInfo[] noteInfos) : this(prefab, parent, noteInfos, 64)
        {

        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void OnPreUpdate(float currentSec)
        {
            ThrowIfDisposed();
            if (_timingPoints.IsEmpty)
            {
                return;
            }
            var timingPoints = _timingPoints.Span;
            var i = 0;
            try
            {
                for (; i < timingPoints.Length; i++)
                {
                    ref var tp = ref timingPoints[i];
                    var timeDiff = currentSec - tp.Timing;
                    if (timeDiff > -0.15f)
                    {
                        if (!Dequeue(ref tp))
                        {
                            return;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (i != 0)
                {
                    _timingPoints = _timingPoints.Slice(i);
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool Dequeue(ref TimingPoint<TInfo> tp)
        {
            var infos = tp.Infos;
            if (infos.IsEmpty)
                return true;
            var _infos = infos.Span;
            var i = 0;
            try
            {
                for (; i < _infos.Length; i++)
                {
                    var info = _infos[i];
                    var idleNote = Dequeue();
                    if (idleNote is null)
                        return false;
                    ActiveObject(idleNote, info);
                }
            }
            finally
            {
                tp.Infos = infos.Slice(i);
            }
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IPoolableNote<TInfo, TMember>? Dequeue()
        {
            ThrowIfDisposed();
            IPoolableNote<TInfo, TMember>? idleNote;
            if (!_storage.TryRent(out idleNote))
            {
                switch(_flag)
                {
                    case 0:
                        MajDebug.LogWarning($"No more Note can use");
                        _flag = 1;
                        break;
                }
                return null;
            }
            _flag = 0;

            return idleNote;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ActiveObject(IPoolableNote<TInfo, TMember> element, TInfo info)
        {
            info.Instance = element as NoteDrop;
            element.Init(info);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void Collect(in IPoolableNote<TInfo, TMember> endNote)
        {
            ThrowIfDisposed();
            _storage.Return(endNote);
        }
        public virtual void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _storage.Dispose();
            _timingPoints = Memory<TimingPoint<TInfo>>.Empty;

            _rentedArrayForTimingPoints.Dispose();
            _rentedArrayForTimingPoints = default;

            _rentedArrayForNotePoolingInfos.Dispose();
            _rentedArrayForNotePoolingInfos = default;
            
            var childCount = _parent.childCount;
            for (var i = 0; i < childCount; i++)
            {
                try
                {
                    var child = _parent.GetChild(i);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                catch (Exception e)
                {
                    MajDebug.LogWarning($"Cannot destroy note:\n{e}");
                }
            }
        }
        protected void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NotePool<TInfo, TMember>));
            }
        }
        protected struct TimingPoint<T> where T : NotePoolingInfo
        {
            public float Timing { get; init; }
            public Memory<T> Infos { get; set; }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        protected struct Bucket: IDisposable
        {
            public int Size
            {
                get
                {
                    return _size;
                }
            }

            int _cursor = 0;
            bool _isDisposed = false;
            readonly int _size = 0;
            Memory<IPoolableNote<TInfo, TMember>?> _storage = Memory<IPoolableNote<TInfo, TMember>?>.Empty;
            PooledArray<IPoolableNote<TInfo, TMember>?> _rentedArray = default;

            public Bucket(IPoolableNote<TInfo, TMember>?[] rentedArray, int size)
            {
                _size = size;
                _storage = rentedArray.AsMemory();
            }
            public Bucket(IPoolableNote<TInfo, TMember>?[] rentedArray): this(rentedArray, rentedArray.Length)
            {

            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IPoolableNote<TInfo, TMember>? Rent()
            {
                ThrowIfDisposed();
                if (_cursor >= _size)
                {
                    return null;
                }
                var storage = _storage.Span;
                var note = storage[_cursor];
                storage[_cursor] = null;
                _cursor++;
                return note;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryRent([NotNullWhen(true)]out IPoolableNote<TInfo, TMember>? note)
            {
                ThrowIfDisposed();
                if (_cursor >= _size)
                {
                    note = null;
                    return false;
                }
                var storage = _storage.Span;
                note = storage[_cursor]!;
                storage[_cursor] = null;
                _cursor++;
                return true;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Return(IPoolableNote<TInfo, TMember> note)
            {
                ThrowIfDisposed();
                if(note is null)
                {
                    throw new ArgumentNullException(nameof(note));
                }
                if (_cursor <= 0)
                {
                    return;
                }
                _cursor--;
                _storage.Span[_cursor] = note;
            }
            public void Dispose()
            {
                _rentedArray.Dispose();
                _isDisposed = true;
            }
            void ThrowIfDisposed()
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(Bucket));
                }
            }
        }
    }
}
