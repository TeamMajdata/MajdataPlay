using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Slide
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public struct SlideArea: IDisposable
    {
        public bool On
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isOn;
            }
        }
        public bool IsSkippable 
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set; 
        } = true;
        public bool IsFinished
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                if (_areas.Length == 0)
                {
                    return false;
                }
                else
                {
                    return _isFinished;
                }
            }
        }
        public ReadOnlySpan<SensorArea> IncludedAreas
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return _includedAreas.Span;
            }
        }
        public AreaPolicy Policy 
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            init; 
        } = AreaPolicy.OR;
        public int ArrowProgressWhenFinished
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _arrowProgressWhenFinished;
        }
        public int ArrowProgressWhenOn
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _arrowProgressWhenOn;
        }

        bool _isFinished = false;
        bool _isOn = false;
        bool _isDisposed = false;
        int _arrowProgressWhenFinished = 0;
        int _arrowProgressWhenOn = 0;

        Memory<Area> _areas = Memory<Area>.Empty;
        Memory<SensorArea> _includedAreas = Memory<SensorArea>.Empty;

        PooledArray<Area> _rentedArrayForAreas = default;
        PooledArray<SensorArea> _rentedArrayForIncludedAreas = default;
        public SlideArea(ReadOnlySpan<(SensorArea, bool)> types, int progressWhenOn, int progressWhenFinished)
        {
            if (types.Length == 0)
            {
                return;
            }
            Span<SensorArea?> registeredAreas = stackalloc SensorArea?[types.Length];
            Span<Area?> areas = stackalloc Area?[types.Length];
            var i = 0;
            var i2 = 0;
            foreach (var item in types)
            {
                var (area, isLast) = item;
                if (registeredAreas.Any(x => x == area))
                {
                    continue;
                }

                registeredAreas[i++] = area;
                areas[i2++] = new Area()
                {
                    TargetArea = area,
                    IsLast = isLast
                };
            }

            Span<SensorArea> _registeredAreas = stackalloc SensorArea[i];
            Span<Area> _areas = stackalloc Area[i2];

            for (var j = 0; j < i; j++)
            {
                _registeredAreas[j] = (SensorArea)registeredAreas[j]!;
            }
            for (var j = 0; j < i2; j++)
            {
                _areas[j] = (Area)areas[j]!;
            }
            _rentedArrayForAreas = Pool<Area>.Rent(_areas.Length, true);
            _rentedArrayForIncludedAreas = Pool<SensorArea>.Rent(_registeredAreas.Length, true);
            _areas.CopyTo(_rentedArrayForAreas);
            _registeredAreas.CopyTo(_rentedArrayForIncludedAreas);
            this._areas = _rentedArrayForAreas.AsMemory(0, _areas.Length);
            _includedAreas = _rentedArrayForIncludedAreas.AsMemory(0, _registeredAreas.Length);

            _arrowProgressWhenFinished = progressWhenFinished;
            _arrowProgressWhenOn = progressWhenOn;
        }
        public SlideArea(ReadOnlySpan<(SensorArea, bool)> types, int arrowProgress) : this(types, arrowProgress, arrowProgress)
        {

        }
        public SlideArea()
        {

        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Mirror(SensorArea baseLine)
        {
            ThrowIfDisposed();
            var areas = _areas.Span;
            var includedAreas = _includedAreas.Span;
            for (var i = 0; i < areas.Length; i++)
            {
                ref var area = ref areas[i];
                area = new Area()
                {
                    TargetArea = area.TargetArea.Mirror(baseLine),
                    IsLast = area.IsLast
                };
            }
            for (var i = 0; i < includedAreas.Length; i++)
            {
                ref var area = ref includedAreas[i];
                area = area.Mirror(baseLine);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Diff(int diff)
        {
            ThrowIfDisposed();
            var areas = _areas.Span;
            var includedAreas = _includedAreas.Span;
            for (var i = 0; i < areas.Length; i++)
            {
                ref var area = ref areas[i];
                area = new Area()
                {
                    TargetArea = area.TargetArea.Diff(diff),
                    IsLast = area.IsLast
                };
            }
            for (var i = 0; i < includedAreas.Length; i++)
            {
                ref var area = ref includedAreas[i];
                area = area.Diff(diff);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetIsLast()
        {
            ThrowIfDisposed();
            var span = _areas.Span;
            for (var i = 0; i < span.Length; i++)
            {
                ref var area = ref span[i];
                area.IsLast = true;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNonLast()
        {
            ThrowIfDisposed();
            var span = _areas.Span;
            for (var i = 0; i < span.Length; i++)
            {
                ref var area = ref span[i];
                area.IsLast = false;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Check(in SensorArea targetArea, in SwitchStatus state)
        {
            ThrowIfDisposed();
            if (_areas.Length == 0)
            {
                return;
            }
            var span = _areas.Span;
            var isOn = false;
            var isFinished = Policy switch
            {
                AreaPolicy.AND => true,
                _ => false
            };
            for (var i = 0; i < span.Length; i++)
            {
                ref var area = ref span[i];
                area.Check(targetArea, state);

                isOn = isOn || area.On;
                if (Policy == AreaPolicy.AND)
                {
                    isFinished = isFinished && area.IsFinished;
                }
                else
                {
                    isFinished = isFinished || area.IsFinished;
                }
            }
            _isFinished = isFinished;
            _isOn = isOn;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (_rentedArrayForAreas.Length > 0)
            {
                _rentedArrayForAreas.Dispose();
                _rentedArrayForAreas = default;
                _areas = Memory<Area>.Empty;
            }
            if (_rentedArrayForIncludedAreas.Length > 0)
            {
                _rentedArrayForIncludedAreas.Dispose();
                _rentedArrayForIncludedAreas = default;
                _includedAreas = Memory<SensorArea>.Empty;
            }
        }
        void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SlideArea), "This SlideArea has been disposed.");
            }
        }
        struct Area
        {
            /// <summary>
            /// True if the target area has been triggered.
            /// </summary>
            public bool On
            {
                get
                {
                    return _wasOn;
                }
            }
            /// <summary>
            /// True if the target area has been untriggered and has been triggered.
            /// </summary>
            public bool Off
            {
                get
                {
                    return _wasOff;
                }
            }
            public SensorArea TargetArea { get; init; }
            public bool IsLast { get; set; }
            public bool IsFinished
            {
                get
                {
                    if (IsLast)
                    {
                        return _wasOn;
                    }
                    else
                    {
                        return _wasOn && _wasOff;
                    }
                }
            }

            bool _wasOn;
            bool _wasOff;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Check(in SensorArea area, in SwitchStatus status)
            {
                if (area != TargetArea)
                {
                    return;
                }
                if (status == SwitchStatus.Off)
                {
                    if (_wasOn)
                    {
                        _wasOff = true;
                    }
                }
                else
                {
                    _wasOn = true;
                }
            }
        }
    }
}
