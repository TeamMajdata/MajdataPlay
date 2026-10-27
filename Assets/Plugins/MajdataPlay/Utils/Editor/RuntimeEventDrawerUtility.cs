using System;
using System.Collections.Generic;
using MajdataPlay.Utils.Events;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Utils.Editor
{
    /// <summary>
    /// The Inspector logic of <see cref="RuntimeEventDrawer"/> that does not need a GUI context: finding the delegate
    /// type of a field, labelling bindings, listing the methods a binding can select and writing a selection.
    /// </summary>
    internal static class RuntimeEventDrawerUtility
    {
        /// <summary>The serialized name of <see cref="RuntimeEvent"/>'s binding list.</summary>
        internal const string BindingsPropertyName = "_bindings";

        /// <summary>The serialized name of <see cref="RuntimeEventBinding"/>'s target.</summary>
        internal const string TargetPropertyName = "_target";

        /// <summary>The serialized name of <see cref="RuntimeEventBinding"/>'s method name.</summary>
        internal const string MethodNamePropertyName = "_methodName";

        /// <summary>The label of a binding without a selected method.</summary>
        internal const string NoFunctionLabel = "No Function";

        /// <summary>Gets the delegate type of a <see cref="RuntimeEvent{TDelegate}"/> field, array or list.</summary>
        /// <param name="fieldType">The declared type of the serialized field.</param>
        /// <returns>The <c>TDelegate</c> argument, or null when the field does not hold a <see cref="RuntimeEvent{TDelegate}"/>.</returns>
        internal static Type? GetDelegateType(Type fieldType)
        {
            var eventType = fieldType;
            if (eventType.IsArray)
            {
                eventType = eventType.GetElementType()!;
            }
            else if (eventType.IsGenericType && eventType.GetGenericTypeDefinition() == typeof(List<>))
            {
                eventType = eventType.GetGenericArguments()[0];
            }

            if (eventType.IsGenericType && eventType.GetGenericTypeDefinition() == typeof(RuntimeEvent<>))
            {
                return eventType.GetGenericArguments()[0];
            }
            return null;
        }

        /// <summary>Builds the label that the method button of a binding shows.</summary>
        /// <param name="target">The bound object.</param>
        /// <param name="methodName">The serialized method name.</param>
        /// <param name="delegateType">The delegate type of the event.</param>
        /// <param name="isMissing">Set to true when a method is selected but cannot be resolved.</param>
        /// <returns>
        /// <see cref="NoFunctionLabel"/> without a selected method, <c>Type.Method</c> for a bindable method, and
        /// <c>&lt;Missing Type.Method&gt;</c> when the target or the bindable method is missing.
        /// </returns>
        internal static string GetBindingLabel(Object? target, string methodName, Type delegateType, out bool isMissing)
        {
            isMissing = false;
            if (methodName.Length == 0)
            {
                return NoFunctionLabel;
            }
            if (target == null)
            {
                isMissing = true;
                return $"<Missing {methodName}>";
            }

            var methodLabel = $"{target.GetType().Name}.{methodName}";
            if (RuntimeEventMethods.FindBindableMethod(target.GetType(), methodName, delegateType) is null)
            {
                isMissing = true;
                return $"<Missing {methodLabel}>";
            }
            return methodLabel;
        }

        /// <summary>
        /// Lists the bindable methods that a binding to an object can select: those of every component on the object's
        /// game object for a game object or component, otherwise those of the object itself.
        /// </summary>
        /// <param name="target">The object assigned to the binding.</param>
        /// <param name="delegateType">The delegate type of the event.</param>
        /// <returns>The menu entries grouped by component in component order; empty when <paramref name="target"/> is null.</returns>
        internal static IReadOnlyList<MenuEntry> GetMenuEntries(Object? target, Type delegateType)
        {
            var entries = new List<MenuEntry>();
            if (target == null)
            {
                return entries;
            }

            var gameObject = target switch
            {
                GameObject targetGameObject => targetGameObject,
                Component component => component.gameObject,
                _ => null,
            };
            if (gameObject == null)
            {
                AddMenuEntries(entries, target, target.GetType().Name, delegateType);
                return entries;
            }

            AddMenuEntries(entries, gameObject, nameof(GameObject), delegateType);
            var componentCounts = new Dictionary<Type, int>();
            foreach (var component in gameObject.GetComponents<Component>())
            {
                // Missing scripts appear as destroyed components.
                if (component == null)
                {
                    continue;
                }
                var componentType = component.GetType();
                componentCounts.TryGetValue(componentType, out var count);
                componentCounts[componentType] = count + 1;
                var label = count == 0 ? componentType.Name : $"{componentType.Name} ({count})";
                AddMenuEntries(entries, component, label, delegateType);
            }
            return entries;
        }

        /// <summary>Writes a selection into a binding with undo support.</summary>
        /// <param name="serializedObject">The serialized object that owns the binding.</param>
        /// <param name="bindingPath">The property path of the binding.</param>
        /// <param name="target">The object to bind.</param>
        /// <param name="methodName">The method name to bind; empty to clear the method.</param>
        internal static void AssignBinding(SerializedObject serializedObject, string bindingPath, Object? target, string methodName)
        {
            serializedObject.Update();
            var binding = serializedObject.FindProperty(bindingPath);
            if (binding is null)
            {
                return;
            }
            binding.FindPropertyRelative(TargetPropertyName).objectReferenceValue = target;
            binding.FindPropertyRelative(MethodNamePropertyName).stringValue = methodName;
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Adds a menu entry for every bindable method of an object.</summary>
        /// <param name="entries">The list to add to.</param>
        /// <param name="target">The object whose methods are listed.</param>
        /// <param name="label">The submenu label of the object.</param>
        /// <param name="delegateType">The delegate type of the event.</param>
        private static void AddMenuEntries(List<MenuEntry> entries, Object target, string label, Type delegateType)
        {
            foreach (var method in RuntimeEventMethods.GetBindableMethods(target.GetType(), delegateType))
            {
                entries.Add(new MenuEntry($"{label}/{method.Name}", target, method.Name));
            }
        }

        /// <summary>One selectable method of the method menu.</summary>
        internal readonly struct MenuEntry
        {
            /// <summary>Initializes an entry.</summary>
            /// <param name="path">The menu path, <c>Component/Method</c>.</param>
            /// <param name="target">The object to bind when the entry is selected.</param>
            /// <param name="methodName">The method name to bind when the entry is selected.</param>
            public MenuEntry(string path, Object target, string methodName)
            {
                Path = path;
                Target = target;
                MethodName = methodName;
            }

            /// <summary>Gets the menu path, <c>Component/Method</c>.</summary>
            public string Path { get; }

            /// <summary>Gets the object to bind when the entry is selected.</summary>
            public Object Target { get; }

            /// <summary>Gets the method name to bind when the entry is selected.</summary>
            public string MethodName { get; }
        }
    }
}
