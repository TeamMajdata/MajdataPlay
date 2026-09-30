using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.Game.Misc.Notes;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using static MajdataPlay.PlayerLoopCallbackAttribute;
#nullable enable
namespace MajdataPlay.Scenes.Game.Buffers
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class NoteComponentInfo: IDisposable
    {
        public IStateful<NoteStatus>? Component
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return _noteObject;
            }
        }
        public bool IsPreUpdatable { get; init; }
        public bool IsUpdatable { get; init; }
        public bool IsFixedUpdatable { get; init; }
        public bool IsLateUpdatable { get; init; }
        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isValid;
            }
        }
        public NoteStatus State
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowIfDisposed();
                return _noteObject?.State ?? NoteStatus.End;
            }
        }

        IStateful<NoteStatus>? _noteObject;
        IMajComponent? _component;
        GameObject? _gameObject;

        bool _isDisposed = false;
        readonly bool _isValid = false;

        ReadOnlyMemory<PlayerLoopFunction> _onPreUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
        ReadOnlyMemory<PlayerLoopFunction> _onUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
        ReadOnlyMemory<PlayerLoopFunction> _onFixedUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
        ReadOnlyMemory<PlayerLoopFunction> _onLateUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;

        PooledArray<PlayerLoopFunction> _rentedArrayForOnPreUpdateFunctions = default;
        PooledArray<PlayerLoopFunction> _rentedArrayForOnUpdateFunctions = default;
        PooledArray<PlayerLoopFunction> _rentedArrayForOnFixedUpdateFunctions = default;
        PooledArray<PlayerLoopFunction> _rentedArrayForOnLateUpdateFunctions = default;

        delegate void PlayerLoopFunction();
        public NoteComponentInfo(object component)
        {
            if (component is null)
            {
                _noteObject = default;
                IsUpdatable = false;
                IsFixedUpdatable = false;
                IsLateUpdatable = false;
                _isValid = false;
                return;
            }
            _gameObject = component as GameObject;
            _noteObject = component as IStateful<NoteStatus>;
            _component = component as IMajComponent;

            var componentType = component.GetType();
            if(componentType.GetCustomAttribute<NoteComponentAttribute>() is null)
            {
                _noteObject = default;
                IsUpdatable = false;
                IsFixedUpdatable = false;
                IsLateUpdatable = false;
                _isValid = false;
                return;
            }
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
            if(!MajCache<(Type, BindingFlags), MethodInfo[]>.TryGetValue((componentType, flags),out var methods))
            {
                methods = MajCache<(Type, BindingFlags), MethodInfo[]>.GetOrAdd((componentType, flags), componentType.GetMethods(flags));
            }
            var delegateType = typeof(PlayerLoopFunction);
            _rentedArrayForOnPreUpdateFunctions = Pool<PlayerLoopFunction>.Rent(methods.Length, true);
            _rentedArrayForOnUpdateFunctions = Pool<PlayerLoopFunction>.Rent(methods.Length, true);
            _rentedArrayForOnFixedUpdateFunctions = Pool<PlayerLoopFunction>.Rent(methods.Length, true);
            _rentedArrayForOnLateUpdateFunctions = Pool<PlayerLoopFunction>.Rent(methods.Length, true);

            var onPreUpdateFuncCount = 0;
            var onUpdateFuncCount = 0;
            var onLateUpdateFuncCount = 0;
            var onFixedUpdateFuncCount = 0;

            foreach (var method in methods)
            {
                var paramCount = 0;
                if (!MajCache<MethodInfo, ParameterInfo[]>.TryGetValue(method, out var paramInfos))
                {
                    paramInfos = MajCache<MethodInfo, ParameterInfo[]>.GetOrAdd(method, method.GetParameters());
                }
                paramCount = paramInfos.Length;
                if (paramCount != 0 || method.ReturnType != typeof(void))
                {
                    continue;
                }
                if (!MajCache<MethodInfo, PlayerLoopCallbackAttribute[]>.TryGetValue(method, out var attributes))
                {
                    attributes = (PlayerLoopCallbackAttribute[])Attribute.GetCustomAttributes(method, typeof(PlayerLoopCallbackAttribute));
                    attributes = MajCache<MethodInfo, PlayerLoopCallbackAttribute[]>.GetOrAdd(method, attributes);
                }
                if (attributes.Length == 0)
                {
                    continue;
                }
                var timing = attributes.Max(x => x.Timing);

                if ((timing & LoopTiming.PreUpdate) == LoopTiming.PreUpdate)
                {
                    var func = (PlayerLoopFunction?)method.CreateDelegate(delegateType, component);
                    if (func is not null)
                    {
                        _rentedArrayForOnPreUpdateFunctions[onPreUpdateFuncCount++] = func;
                    }
                }
                if ((timing & LoopTiming.Update) == LoopTiming.Update)
                {
                    var func = (PlayerLoopFunction?)method.CreateDelegate(delegateType, component);
                    if (func is not null)
                    {
                        _rentedArrayForOnUpdateFunctions[onUpdateFuncCount++] = func;
                    }
                }
                if ((timing & LoopTiming.LateUpdate) == LoopTiming.LateUpdate)
                {
                    var func = (PlayerLoopFunction?)method.CreateDelegate(delegateType, component);
                    if (func is not null)
                    {
                        _rentedArrayForOnLateUpdateFunctions[onLateUpdateFuncCount++] = func;
                    }
                }
                if ((timing & LoopTiming.FixedUpdate) == LoopTiming.FixedUpdate)
                {
                    var func = (PlayerLoopFunction?)method.CreateDelegate(delegateType, component);
                    if (func is not null)
                    {
                        _rentedArrayForOnFixedUpdateFunctions[onFixedUpdateFuncCount++] = func;
                    }
                }

            }
            IsPreUpdatable = onPreUpdateFuncCount != 0;
            IsUpdatable = onUpdateFuncCount != 0;
            IsFixedUpdatable = onFixedUpdateFuncCount != 0;
            IsLateUpdatable = onLateUpdateFuncCount != 0;

            _onPreUpdateFunctions = _rentedArrayForOnPreUpdateFunctions.AsMemory(0, onPreUpdateFuncCount);
            _onUpdateFunctions = _rentedArrayForOnUpdateFunctions.AsMemory(0, onUpdateFuncCount);
            _onFixedUpdateFunctions = _rentedArrayForOnFixedUpdateFunctions.AsMemory(0, onFixedUpdateFuncCount);
            _onLateUpdateFunctions = _rentedArrayForOnLateUpdateFunctions.AsMemory(0, onLateUpdateFuncCount);

            _isValid = !_onUpdateFunctions.IsEmpty ||
                       !_onFixedUpdateFunctions.IsEmpty ||
                       !_onLateUpdateFunctions.IsEmpty;
        }
        ~NoteComponentInfo()
        {
            Dispose();
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnPreUpdate()
        {
            ThrowIfDisposed();
            var onPreUpdateFunctions = _onPreUpdateFunctions.Span;
            var funcCount = onPreUpdateFunctions.Length;
            if (funcCount == 0)
            {
                return;
            }
            if (IsExecutable())
            {
                for (var i = 0; i < funcCount; i++)
                {
                    var func = onPreUpdateFunctions[i];
                    try
                    {
                        func();
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnUpdate()
        {
            ThrowIfDisposed();
            var onUpdateFunctions = _onUpdateFunctions.Span;
            var funcCount = onUpdateFunctions.Length;
            if (funcCount == 0)
            {
                return;
            }
            if (IsExecutable())
            {
                for (var i = 0; i < funcCount; i++)
                {
                    var func = onUpdateFunctions[i];
                    try
                    {
                        func();
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnLateUpdate()
        {
            ThrowIfDisposed();
            var onLateUpdateFunctions = _onLateUpdateFunctions.Span;
            var funcCount = onLateUpdateFunctions.Length;
            if (funcCount == 0)
            {
                return;
            }
            if (IsExecutable())
            {
                for (var i = 0; i < funcCount; i++)
                {
                    var func = onLateUpdateFunctions[i];
                    try
                    {
                        func();
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnFixedUpdate()
        {
            ThrowIfDisposed();
            var onFixedUpdateFunctions = _onFixedUpdateFunctions.Span;
            var funcCount = onFixedUpdateFunctions.Length;
            if (funcCount == 0)
            {
                return;
            }
            if (IsExecutable())
            {
                for (var i = 0; i < funcCount; i++)
                {
                    var func = onFixedUpdateFunctions[i];
                    try
                    {
                        func();
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsExecutable()
        {
            ThrowIfDisposed();
            if(((object?)_noteObject ?? _component) is null)
            {
                return _gameObject?.activeSelf ?? false;
            }
            return State is not (NoteStatus.Start or NoteStatus.End) &&
                   (_component?.Active ?? false);
        }
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _noteObject = default;
            _component = default;
            _gameObject = default;

            _rentedArrayForOnFixedUpdateFunctions.Dispose();
            _rentedArrayForOnLateUpdateFunctions.Dispose();
            _rentedArrayForOnPreUpdateFunctions.Dispose();
            _rentedArrayForOnUpdateFunctions.Dispose();

            _onPreUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
            _onUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
            _onFixedUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;
            _onLateUpdateFunctions = ReadOnlyMemory<PlayerLoopFunction>.Empty;

            _rentedArrayForOnPreUpdateFunctions = default;
            _rentedArrayForOnUpdateFunctions = default;
            _rentedArrayForOnFixedUpdateFunctions = default;
            _rentedArrayForOnLateUpdateFunctions = default;
        }
        void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NoteComponentInfo));
            }
        }
    }
}
