using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Tests.Plugins.Utils.PlayMode;
using MajdataPlay.Utils.Editor;
using MajdataPlay.Utils.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>
    /// Covers the Inspector support of <see cref="RuntimeEvent{TDelegate}"/>: the logic of
    /// <see cref="RuntimeEventDrawerUtility"/> and a drawing pass of <see cref="RuntimeEventDrawer"/> in an editor window.
    /// </summary>
    public class RuntimeEventDrawerTests
    {
        /// <summary>Creates and destroys the test objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The window of the drawing test, if opened.</summary>
        private RuntimeEventDrawerTestWindow? _window;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Closes the window and destroys the test objects.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
            }
            _objects.Dispose();
        }

        /// <summary>Checks that the delegate type is found for event fields, arrays and lists, and only for them.</summary>
        /// <param name="fieldType">The declared field type.</param>
        /// <returns>The delegate type, or null.</returns>
        [TestCase(typeof(RuntimeEvent<Action<int>>), ExpectedResult = typeof(Action<int>))]
        [TestCase(typeof(RuntimeEvent<Action<int>>[]), ExpectedResult = typeof(Action<int>))]
        [TestCase(typeof(List<RuntimeEvent<Func<int, bool>>>), ExpectedResult = typeof(Func<int, bool>))]
        [TestCase(typeof(RuntimeEvent), ExpectedResult = null)]
        [TestCase(typeof(List<int>), ExpectedResult = null)]
        [TestCase(typeof(int), ExpectedResult = null)]
        public Type? GetDelegateType_UnwrapsEventFields(Type fieldType)
        {
            return RuntimeEventDrawerUtility.GetDelegateType(fieldType);
        }

        /// <summary>Checks the labels of empty, bound and missing bindings.</summary>
        [Test]
        public void GetBindingLabel_DescribesTheBinding()
        {
            var listener = _objects.Add<RuntimeEventListener>();

            var noMethod = RuntimeEventDrawerUtility.GetBindingLabel(listener, string.Empty, typeof(Action<int>), out var noMethodIsMissing);
            var bound = RuntimeEventDrawerUtility.GetBindingLabel(listener, "OnValue", typeof(Action<int>), out var boundIsMissing);
            var unmarked = RuntimeEventDrawerUtility.GetBindingLabel(listener, "OnUnmarkedValue", typeof(Action<int>), out var unmarkedIsMissing);
            var noTarget = RuntimeEventDrawerUtility.GetBindingLabel(null, "OnValue", typeof(Action<int>), out var noTargetIsMissing);

            Assert.That(noMethod, Is.EqualTo(RuntimeEventDrawerUtility.NoFunctionLabel));
            Assert.That(noMethodIsMissing, Is.False);
            Assert.That(bound, Is.EqualTo("RuntimeEventListener.OnValue"));
            Assert.That(boundIsMissing, Is.False);
            Assert.That(unmarked, Is.EqualTo("<Missing RuntimeEventListener.OnUnmarkedValue>"));
            Assert.That(unmarkedIsMissing, Is.True);
            Assert.That(noTarget, Is.EqualTo("<Missing OnValue>"));
            Assert.That(noTargetIsMissing, Is.True);
        }

        /// <summary>
        /// Checks that the menu lists the bindable methods of every component on the target's game object, in component
        /// order, numbering repeated component types, whether a component or the game object is assigned.
        /// </summary>
        [Test]
        public void GetMenuEntries_ListsBindableMethodsOfEveryComponent()
        {
            var owner = _objects.Add<RuntimeEventOwner>();
            var gameObject = owner.gameObject;
            var first = gameObject.AddComponent<RuntimeEventListener>();
            var second = gameObject.AddComponent<RuntimeEventListener>();
            var derived = gameObject.AddComponent<RuntimeEventDerivedListener>();

            var fromComponent = RuntimeEventDrawerUtility.GetMenuEntries(owner, typeof(RuntimeEventRefHandler));
            var fromGameObject = RuntimeEventDrawerUtility.GetMenuEntries(gameObject, typeof(Action<int>));

            Assert.That(fromComponent.Select(entry => entry.Path), Is.EqualTo(new[]
            {
                "RuntimeEventListener/OnRefPayload",
                "RuntimeEventListener (1)/OnRefPayload",
                "RuntimeEventDerivedListener/OnRefPayload",
            }));
            Assert.That(fromComponent.Select(entry => entry.Target), Is.EqualTo(new Object[] { first, second, derived }));
            Assert.That(fromGameObject.Where(entry => entry.Target == derived).Select(entry => entry.MethodName), Is.EqualTo(new[]
            {
                "OnPrivateValue",
                "OnValue",
                "OnVirtualValue",
            }));
            Assert.That(fromGameObject.Count(entry => entry.Target == owner), Is.Zero);
        }

        /// <summary>Checks that the menu of a non-component target lists the target's own methods.</summary>
        [Test]
        public void GetMenuEntries_ListsMethodsOfNonComponentTarget()
        {
            var asset = _objects.Track(ScriptableObject.CreateInstance<RuntimeEventListenerAsset>());

            var entries = RuntimeEventDrawerUtility.GetMenuEntries(asset, typeof(Action<int>));

            Assert.That(entries.Select(entry => entry.Path), Is.EqualTo(new[] { "RuntimeEventListenerAsset/OnAssetValue" }));
            Assert.That(entries[0].Target, Is.SameAs(asset));
        }

        /// <summary>Checks that the menu of an unassigned binding is empty.</summary>
        [Test]
        public void GetMenuEntries_ReturnsEmptyForNullTarget()
        {
            Assert.That(RuntimeEventDrawerUtility.GetMenuEntries(null, typeof(Action<int>)), Is.Empty);
        }

        /// <summary>Checks that a menu selection is written into the binding and used by the next invocation.</summary>
        [Test]
        public void AssignBinding_WritesSelectionIntoTheBinding()
        {
            var owner = _objects.Add<RuntimeEventOwner>();
            var listener = owner.gameObject.AddComponent<RuntimeEventListener>();
            RuntimeEventTestBindings.Set(owner, "_valueChanged", (listener, string.Empty));
            using var serializedObject = new SerializedObject(owner);

            RuntimeEventDrawerUtility.AssignBinding(serializedObject, "_valueChanged._bindings.Array.data[0]", listener, "OnValue");
            owner.ValueChanged.Invoker!.Invoke(12);
            RuntimeEventDrawerUtility.AssignBinding(serializedObject, "_valueChanged._bindings.Array.data[0]", listener, string.Empty);

            Assert.That(listener.Calls, Is.EqualTo(new[] { "OnValue:12" }));
            Assert.That(owner.ValueChanged.Bindings[0].MethodName, Is.Empty);
            Assert.That(owner.ValueChanged.Invoker, Is.Null);
        }

        /// <summary>
        /// Checks that an Inspector-style pass draws bound, missing and empty bindings, an unsupported delegate type and a
        /// list of events without errors, and that the event fields use the drawer instead of a collapsed foldout.
        /// </summary>
        [Test]
        public void Drawer_DrawsEventsInAnEditorWindow()
        {
            var owner = _objects.Add<RuntimeEventOwner>();
            var listener = owner.gameObject.AddComponent<RuntimeEventListener>();
            RuntimeEventTestBindings.Set(
                owner,
                "_valueChanged",
                (listener, "OnValue"),
                (listener, "OnUnmarkedValue"),
                (null, "OnValue"),
                (null, string.Empty));
            RuntimeEventTestBindings.Set(owner, "_unsupportedEvent", (listener, "OnValue"));
            owner.EventList.Add(new RuntimeEvent<Action<int>>());

            _window = ScriptableObject.CreateInstance<RuntimeEventDrawerTestWindow>();
            _window.Target = owner;
            _window.MeasuredPropertyPath = "_valueChanged";
            _window.Show();
            PrivateMember.Invoke(_window, "RepaintImmediately");

            Assert.That(_window.DrawCount, Is.GreaterThan(0));
            Assert.That(_window.MeasuredHeight, Is.GreaterThan(EditorGUIUtility.singleLineHeight * 4));
        }
    }
}
