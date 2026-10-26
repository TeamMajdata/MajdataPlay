using System;
using MajdataPlay.Tests.Plugins.Utils.PlayMode;
using MajdataPlay.Utils.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>
    /// Covers <see cref="RuntimeEvent{TDelegate}"/>: resolving serialized bindings, the order of bound and runtime
    /// listeners, the <c>+</c> and <c>-</c> operators, re-resolving after deserialization, cloning with
    /// <see cref="Object.Instantiate(Object)"/> and the replacement guard of exposing properties.
    /// </summary>
    public class RuntimeEventTests
    {
        /// <summary>The property path of <see cref="RuntimeEventOwner.ValueChanged"/>.</summary>
        private const string ValueChangedPath = "_valueChanged";

        /// <summary>The property path of <see cref="RuntimeEventOwner.PayloadChanged"/>.</summary>
        private const string PayloadChangedPath = "_payloadChanged";

        /// <summary>Creates and destroys the test objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The component that owns the events.</summary>
        private RuntimeEventOwner _owner = null!;

        /// <summary>A listener on the owner's game object.</summary>
        private RuntimeEventListener _listener = null!;

        /// <summary>Creates an owner and a listener on the same game object.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _owner = _objects.Add<RuntimeEventOwner>();
            _listener = _owner.gameObject.AddComponent<RuntimeEventListener>();
        }

        /// <summary>Destroys the test objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that an event without bindings or listeners has no invoker.</summary>
        [Test]
        public void Invoker_IsNullWithoutListeners()
        {
            Assert.That(new RuntimeEvent<Action<int>>().Invoker, Is.Null);
            Assert.That(_owner.ValueChanged.Invoker, Is.Null);
        }

        /// <summary>Checks that the delegate type is the type argument.</summary>
        [Test]
        public void DelegateType_ReturnsTypeArgument()
        {
            Assert.That(_owner.ValueChanged.DelegateType, Is.EqualTo(typeof(Action<int>)));
            Assert.That(_owner.PayloadChanged.DelegateType, Is.EqualTo(typeof(RuntimeEventRefHandler)));
        }

        /// <summary>Checks that serialized bindings are visible through <see cref="RuntimeEvent.Bindings"/>.</summary>
        [Test]
        public void Bindings_ReflectSerializedData()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"), (null, string.Empty));

            var bindings = _owner.ValueChanged.Bindings;

            Assert.That(bindings.Count, Is.EqualTo(2));
            Assert.That(bindings[0].Target, Is.SameAs(_listener));
            Assert.That(bindings[0].MethodName, Is.EqualTo("OnValue"));
            Assert.That(bindings[1].Target, Is.Null);
            Assert.That(bindings[1].MethodName, Is.Empty);
        }

        /// <summary>Checks that bound methods, public and private, are called in Inspector order.</summary>
        [Test]
        public void Invoker_CallsBoundMethodsInInspectorOrder()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnPrivateValue"), (_listener, "OnValue"));

            _owner.ValueChanged.Invoker!.Invoke(5);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnPrivateValue:5", "OnValue:5" }));
        }

        /// <summary>Checks that runtime listeners run after the bound methods, even when they were added first.</summary>
        [Test]
        public void Invoker_CallsRuntimeListenersAfterBoundMethods()
        {
            _owner.ValueChanged += value => _listener.Calls.Add($"Runtime:{value}");
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));

            _owner.ValueChanged.Invoker!.Invoke(2);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:2", "Runtime:2" }));
        }

        /// <summary>Checks that both operators change the event in place and return the same instance.</summary>
        [Test]
        public void Operators_ReturnSameInstance()
        {
            var runtimeEvent = new RuntimeEvent<Action<int>>();
            Action<int> listener = _ => { };

            Assert.That(runtimeEvent + listener, Is.SameAs(runtimeEvent));
            Assert.That(runtimeEvent.Invoker, Is.Not.Null);
            Assert.That(runtimeEvent - listener, Is.SameAs(runtimeEvent));
            Assert.That(runtimeEvent.Invoker, Is.Null);
        }

        /// <summary>Checks that <c>-=</c> removes a runtime listener but never a binding to the same method.</summary>
        [Test]
        public void SubtractionOperator_RemovesRuntimeListenerButKeepsBinding()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));
            Action<int> runtimeListener = _listener.OnValue;
            _owner.ValueChanged += runtimeListener;
            _owner.ValueChanged.Invoker!.Invoke(1);

            _owner.ValueChanged -= runtimeListener;
            _owner.ValueChanged -= _listener.OnValue;
            _owner.ValueChanged.Invoker!.Invoke(2);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:1", "OnValue:1", "OnValue:2" }));
        }

        /// <summary>Checks that null listeners are ignored by both operators.</summary>
        [Test]
        public void Operators_IgnoreNullListeners()
        {
            var runtimeEvent = new RuntimeEvent<Action<int>>();

            runtimeEvent += null;
            runtimeEvent -= null;

            Assert.That(runtimeEvent.Invoker, Is.Null);
        }

        /// <summary>Checks that both operators reject a null event.</summary>
        [Test]
        public void Operators_ThrowForNullEvent()
        {
            var runtimeEvent = default(RuntimeEvent<Action<int>>);
            Action<int> listener = _ => { };

            Assert.Throws<ArgumentNullException>(() => _ = runtimeEvent! + listener);
            Assert.Throws<ArgumentNullException>(() => _ = runtimeEvent! - listener);
        }

        /// <summary>
        /// Checks that bindings without a method are skipped silently and bindings with a missing target or an unbindable
        /// method are skipped, while the usable binding still runs.
        /// </summary>
        [Test]
        public void Invoker_SkipsEmptyAndUnusableBindings()
        {
            RuntimeEventTestBindings.Set(
                _owner,
                ValueChangedPath,
                (null, string.Empty),
                (_listener, string.Empty),
                (null, "OnValue"),
                (_listener, "OnUnmarkedValue"),
                (_listener, "OnValueReturningDouble"),
                (_listener, "OnValue"));

            _owner.ValueChanged.Invoker!.Invoke(4);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:4" }));
        }

        /// <summary>Checks that a binding whose target was destroyed after it was assigned is skipped.</summary>
        [Test]
        public void Invoker_SkipsDestroyedTarget()
        {
            var otherListener = _objects.Add<RuntimeEventListener>();
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (otherListener, "OnValue"), (_listener, "OnValue"));
            Object.DestroyImmediate(otherListener.gameObject);

            _owner.ValueChanged.Invoker!.Invoke(6);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:6" }));
        }

        /// <summary>
        /// Checks that changing the serialized bindings keeps the event instance and its runtime listeners and that the
        /// next invocation uses the new bindings.
        /// </summary>
        [Test]
        public void Invoker_ResolvesAgainAfterDeserializationAndKeepsRuntimeListeners()
        {
            var runtimeEvent = _owner.ValueChanged;
            _owner.ValueChanged += value => _listener.Calls.Add($"Runtime:{value}");
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));
            _owner.ValueChanged.Invoker!.Invoke(1);

            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnPrivateValue"));
            _owner.ValueChanged.Invoker!.Invoke(2);

            Assert.That(_owner.ValueChanged, Is.SameAs(runtimeEvent));
            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:1", "Runtime:1", "OnPrivateValue:2", "Runtime:2" }));
        }

        /// <summary>Checks that resolving repeatedly does not duplicate the bound listeners.</summary>
        [Test]
        public void Resolve_IsIdempotent()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));

            _owner.ValueChanged.Resolve();
            _owner.ValueChanged.Resolve();
            _owner.ValueChanged.Invoker!.Invoke(8);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "OnValue:8" }));
        }

        /// <summary>
        /// Checks that a clone made with <see cref="Object.Instantiate(Object)"/> calls the clone's listener and does not
        /// copy runtime listeners.
        /// </summary>
        [Test]
        public void Instantiate_RetargetsBindingsToCloneAndDropsRuntimeListeners()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));
            _owner.ValueChanged += value => _listener.Calls.Add($"Runtime:{value}");

            var clone = _objects.Track(Object.Instantiate(_owner.gameObject));
            var cloneOwner = clone.GetComponent<RuntimeEventOwner>();
            var cloneListener = clone.GetComponent<RuntimeEventListener>();
            cloneOwner.ValueChanged.Invoker!.Invoke(9);

            Assert.That(cloneOwner.ValueChanged.Bindings[0].Target, Is.SameAs(cloneListener));
            Assert.That(cloneListener.Calls, Is.EqualTo(new[] { "OnValue:9" }));
            Assert.That(_listener.Calls, Is.Empty);
        }

        /// <summary>Checks that a bound method receives <c>ref</c> arguments by reference.</summary>
        [Test]
        public void Invoker_PassesRefArgumentsToBoundMethods()
        {
            RuntimeEventTestBindings.Set(_owner, PayloadChangedPath, (_listener, "OnRefPayload"));
            var payload = new RuntimeEventTestPayload { Value = 1 };

            _owner.PayloadChanged.Invoker!.Invoke(ref payload);

            Assert.That(payload.Value, Is.EqualTo(2));
        }

        /// <summary>Checks that bindings to virtual methods dispatch to the override of the target's type.</summary>
        [Test]
        public void Invoker_CallsTheBoundOverride()
        {
            var derivedListener = _owner.gameObject.AddComponent<RuntimeEventDerivedListener>();
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnVirtualValue"), (derivedListener, "OnVirtualValue"));

            _owner.ValueChanged.Invoker!.Invoke(3);

            Assert.That(_listener.Calls, Is.EqualTo(new[] { "Base.OnVirtualValue:3" }));
            Assert.That(derivedListener.Calls, Is.EqualTo(new[] { "Derived.OnVirtualValue:3" }));
        }

        /// <summary>Checks that a binding to a scriptable object calls its method.</summary>
        [Test]
        public void Invoker_CallsMethodsOfNonComponentTargets()
        {
            var asset = _objects.Track(ScriptableObject.CreateInstance<RuntimeEventListenerAsset>());
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (asset, "OnAssetValue"));

            _owner.ValueChanged.Invoker!.Invoke(11);

            Assert.That(asset.Calls, Is.EqualTo(new[] { "OnAssetValue:11" }));
        }

        /// <summary>Checks that the guard accepts the exposed instance and rejects any other value.</summary>
        [Test]
        public void ThrowIfReplaced_AcceptsOnlyTheSameInstance()
        {
            var runtimeEvent = new RuntimeEvent<Action<int>>();

            Assert.DoesNotThrow(() => RuntimeEvent.ThrowIfReplaced(runtimeEvent, runtimeEvent));
            Assert.Throws<InvalidOperationException>(() => RuntimeEvent.ThrowIfReplaced(runtimeEvent, new RuntimeEvent<Action<int>>()));
            Assert.Throws<InvalidOperationException>(() => RuntimeEvent.ThrowIfReplaced(runtimeEvent, null));
            Assert.Throws<ArgumentNullException>(() => RuntimeEvent.ThrowIfReplaced(null!, runtimeEvent));
        }

        /// <summary>Checks that a guarded property supports <c>+=</c> and <c>-=</c> but cannot be replaced.</summary>
        [Test]
        public void GuardedProperty_SupportsCompoundAssignmentAndRejectsReplacement()
        {
            var calls = 0;
            Action<int> listener = _ => calls++;

            _owner.ValueChanged += listener;
            _owner.ValueChanged.Invoker!.Invoke(0);
            _owner.ValueChanged -= listener;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_owner.ValueChanged.Invoker, Is.Null);
            Assert.Throws<InvalidOperationException>(() => _owner.ValueChanged = new RuntimeEvent<Action<int>>());
            Assert.Throws<InvalidOperationException>(() => _owner.ValueChanged = null!);
        }

        /// <summary>Checks that the bindings are stored in the serialized data of the owner.</summary>
        [Test]
        public void Serialization_WritesBindingsIntoTheOwner()
        {
            RuntimeEventTestBindings.Set(_owner, ValueChangedPath, (_listener, "OnValue"));

            var json = EditorJsonUtility.ToJson(_owner);

            Assert.That(json, Does.Contain("\"_valueChanged\":{\"_bindings\":[{\"_target\":"));
            Assert.That(json, Does.Contain("\"_methodName\":\"OnValue\""));
        }
    }
}
