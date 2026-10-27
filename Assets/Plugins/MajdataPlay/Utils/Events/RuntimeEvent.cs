using System;
using System.Collections.Generic;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;

#nullable enable
namespace MajdataPlay.Utils.Events
{
    /// <summary>
    /// The non-generic part of <see cref="RuntimeEvent{TDelegate}"/>: the bindings configured in the Inspector. The
    /// Inspector drawer targets this type so that it applies to every <see cref="RuntimeEvent{TDelegate}"/>.
    /// </summary>
    [Serializable]
    public abstract class RuntimeEvent
    {
        /// <summary>The bindings in invocation order.</summary>
        [SerializeField]
        [FormerlySerializedAs("Bindings")]
        private List<RuntimeEventBinding> _bindings = new();

        /// <summary>Initializes an event without bindings; only <see cref="RuntimeEvent{TDelegate}"/> derives from this class.</summary>
        private protected RuntimeEvent()
        {
        }

        /// <summary>Gets the bindings configured in the Inspector, in invocation order.</summary>
        public IReadOnlyList<RuntimeEventBinding> Bindings => _bindings;

        /// <summary>Gets the delegate type that bound methods and runtime listeners must match.</summary>
        public abstract Type DelegateType { get; }

        /// <summary>
        /// Rejects replacing the event instance through a property setter. A property needs a setter only when other
        /// classes should use <c>+=</c> and <c>-=</c>, which pass the same instance back to it; this method accepts that
        /// instance and nothing else. Read-only properties do not need it, because their callers use
        /// <see cref="RuntimeEvent{TDelegate}.Add"/> and <see cref="RuntimeEvent{TDelegate}.Remove"/>.
        /// </summary>
        /// <param name="current">The instance that the property exposes.</param>
        /// <param name="value">The value passed to the setter.</param>
        /// <exception cref="ArgumentNullException"><paramref name="current"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="value"/> is not <paramref name="current"/>.</exception>
        public static void ThrowIfReplaced(RuntimeEvent current, RuntimeEvent? value)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            if (!ReferenceEquals(current, value))
            {
                throw new InvalidOperationException(
                    "A RuntimeEvent cannot be replaced; use Add and Remove, or += and -=, to change its listeners.");
            }
        }
    }

    /// <summary>
    /// An event whose listeners are the <see cref="BindableMethodAttribute"/> methods bound in the Inspector, followed by
    /// the delegates added at runtime with <see cref="Add"/> or <c>+=</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity deserializes the event only into a field it can write: it skips <c>readonly</c> fields and the backing
    /// fields of get-only auto-properties (<c>{ get; }</c>) without a warning, and their Inspector bindings are lost.
    /// Store the event in a private serialized field, or in an auto-property with a private setter marked
    /// <c>[field: SerializeField]</c>, and expose it through a read-only property. Callers change the listeners with
    /// <see cref="Add"/> and <see cref="Remove"/>, which work on read-only members because they change the instance in
    /// place.
    /// </para>
    /// <code>
    /// [SerializeField]
    /// [FormerlySerializedAs("Judged")]
    /// private RuntimeEvent&lt;JudgedHandler&gt; _judged = new();
    ///
    /// public RuntimeEvent&lt;JudgedHandler&gt; Judged => _judged;
    ///
    /// [field: SerializeField]
    /// [field: FormerlySerializedAs("Missed")]
    /// public RuntimeEvent&lt;MissedHandler&gt; Missed { get; private set; } = new();
    ///
    /// // Subscribe: owner.Judged.Add(OnJudged);
    /// // Remove:    owner.Judged.Remove(OnJudged);
    /// // Raise:     _judged.Invoker?.Invoke(in result);
    /// </code>
    /// <para>
    /// The <c>+=</c> and <c>-=</c> operators do the same as <see cref="Add"/> and <see cref="Remove"/>, but C# writes
    /// their result back, so they need an assignable member. To let other classes use them, give the property a setter
    /// that calls <see cref="RuntimeEvent.ThrowIfReplaced"/>, which accepts only the same instance:
    /// <c>set =&gt; RuntimeEvent.ThrowIfReplaced(_judged, value);</c>.
    /// </para>
    /// <para>
    /// The bindings are turned into delegates on the first access to <see cref="Invoker"/> after Unity deserializes the
    /// event; call <see cref="Resolve"/> from <c>Awake</c> to do that work before a hot path raises the event. Invoking
    /// <see cref="Invoker"/> is an ordinary delegate call. Bound targets that are destroyed later are still called.
    /// Use the event from the main thread only.
    /// </para>
    /// </remarks>
    /// <typeparam name="TDelegate">The delegate type of the event; it must be a concrete delegate type.</typeparam>
    [Serializable]
    public sealed class RuntimeEvent<TDelegate> : RuntimeEvent, ISerializationCallbackReceiver
        where TDelegate : Delegate
    {
        /// <summary>The delegates created from the bindings by the last <see cref="Resolve"/>.</summary>
        [NonSerialized]
        private TDelegate? _boundListeners;

        /// <summary>The delegates added with <see cref="Add"/> or <c>+=</c> and not removed yet.</summary>
        [NonSerialized]
        private TDelegate? _runtimeListeners;

        /// <summary>The bound listeners followed by the runtime listeners.</summary>
        [NonSerialized]
        private TDelegate? _invoker;

        /// <summary>Whether <see cref="_boundListeners"/> reflects the current bindings.</summary>
        [NonSerialized]
        private bool _isResolved;

        /// <inheritdoc/>
        public override Type DelegateType => typeof(TDelegate);

        /// <summary>
        /// Gets the delegate that calls every bound method in Inspector order and then every runtime listener in the order
        /// they were added, resolving the bindings first when they changed.
        /// </summary>
        /// <value>The combined delegate, or null when the event has no listeners.</value>
        public TDelegate? Invoker
        {
            get
            {
                if (!_isResolved)
                {
                    Resolve();
                }
                return _invoker;
            }
        }

        /// <summary>
        /// Creates the delegates for the bindings now. Bindings without a target or method are skipped silently; bindings
        /// whose target is missing or whose method is not a matching <see cref="BindableMethodAttribute"/> method are
        /// skipped with a warning.
        /// </summary>
        public void Resolve()
        {
            _boundListeners = CreateBoundListeners();
            _isResolved = true;
            UpdateInvoker();
        }

        /// <summary>
        /// Adds a runtime listener after the bound methods and the listeners added before it. The instance is changed in
        /// place, so this works through read-only fields and properties.
        /// </summary>
        /// <param name="listener">The listener to add; null is ignored.</param>
        public void Add(TDelegate? listener)
        {
            if (listener is null)
            {
                return;
            }
            _runtimeListeners = (TDelegate?)Delegate.Combine(_runtimeListeners, listener);
            UpdateInvoker();
        }

        /// <summary>
        /// Removes the last occurrence of a runtime listener. Methods bound in the Inspector are not affected, even when
        /// <paramref name="listener"/> calls the same method. The instance is changed in place, so this works through
        /// read-only fields and properties.
        /// </summary>
        /// <param name="listener">The listener to remove; null or a listener that was not added is ignored.</param>
        public void Remove(TDelegate? listener)
        {
            if (listener is null)
            {
                return;
            }
            _runtimeListeners = (TDelegate?)Delegate.Remove(_runtimeListeners, listener);
            UpdateInvoker();
        }

        /// <summary>Adds a runtime listener like <see cref="Add"/>, for <c>+=</c> on assignable members.</summary>
        /// <param name="runtimeEvent">The event to add the listener to; it is changed in place.</param>
        /// <param name="listener">The listener to add; null is ignored.</param>
        /// <returns><paramref name="runtimeEvent"/> itself, so that <c>+=</c> keeps the same instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="runtimeEvent"/> is null.</exception>
        public static RuntimeEvent<TDelegate> operator +(RuntimeEvent<TDelegate> runtimeEvent, TDelegate? listener)
        {
            if (runtimeEvent is null)
            {
                throw new ArgumentNullException(nameof(runtimeEvent));
            }
            runtimeEvent.Add(listener);
            return runtimeEvent;
        }

        /// <summary>Removes a runtime listener like <see cref="Remove"/>, for <c>-=</c> on assignable members.</summary>
        /// <param name="runtimeEvent">The event to remove the listener from; it is changed in place.</param>
        /// <param name="listener">The listener to remove; null or a listener that was not added is ignored.</param>
        /// <returns><paramref name="runtimeEvent"/> itself, so that <c>-=</c> keeps the same instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="runtimeEvent"/> is null.</exception>
        public static RuntimeEvent<TDelegate> operator -(RuntimeEvent<TDelegate> runtimeEvent, TDelegate? listener)
        {
            if (runtimeEvent is null)
            {
                throw new ArgumentNullException(nameof(runtimeEvent));
            }
            runtimeEvent.Remove(listener);
            return runtimeEvent;
        }

        /// <summary>Does nothing; the bindings are serialized as they are.</summary>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        /// <summary>
        /// Marks the bindings for resolving on the next access to <see cref="Invoker"/>. Unity may call this off the main
        /// thread, so it does not touch Unity objects; runtime listeners are kept.
        /// </summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _isResolved = false;
        }

        /// <summary>Combines the delegates of all usable bindings in Inspector order.</summary>
        /// <returns>The combined delegate, or null when no binding is usable.</returns>
        private TDelegate? CreateBoundListeners()
        {
            var bindings = Bindings;
            if (bindings.Count == 0)
            {
                return null;
            }
            if (!RuntimeEventMethods.IsSupportedDelegateType(typeof(TDelegate)))
            {
                MajDebug.LogError(
                    nameof(RuntimeEvent),
                    $"Cannot resolve bindings: {typeof(TDelegate)} is not a concrete delegate type.");
                return null;
            }

            var listeners = default(TDelegate);
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var target = binding.Target;
                var methodName = binding.MethodName;
                if (methodName.Length == 0)
                {
                    continue;
                }
                if (target == null)
                {
                    MajDebug.LogWarning(
                        nameof(RuntimeEvent),
                        $"Skipped binding {i} ({methodName}): its target is missing.");
                    continue;
                }
                if (!RuntimeEventMethods.TryCreateDelegate(target, methodName, typeof(TDelegate), out var listener))
                {
                    MajDebug.LogWarning(
                        nameof(RuntimeEvent),
                        $"Skipped binding {i} on \"{target.name}\": {target.GetType().Name}.{methodName} is not a "
                        + $"[BindableMethod] instance method matching {RuntimeEventMethods.FormatSignature(typeof(TDelegate))}.");
                    continue;
                }
                listeners = (TDelegate?)Delegate.Combine(listeners, listener);
            }
            return listeners;
        }

        /// <summary>Rebuilds <see cref="_invoker"/> from the bound and runtime listeners.</summary>
        private void UpdateInvoker()
        {
            _invoker = (TDelegate?)Delegate.Combine(_boundListeners, _runtimeListeners);
        }
    }
}
