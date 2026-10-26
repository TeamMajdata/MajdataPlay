using System;
using System.Collections.Generic;
using MajdataPlay.Utils.Events;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Utils.Editor
{
    /// <summary>
    /// Draws a <see cref="RuntimeEvent{TDelegate}"/> like a UnityEvent: a reorderable list of bindings, each with an
    /// object field and a menu of the <see cref="BindableMethodAttribute"/> methods that match the event's delegate type.
    /// </summary>
    [CustomPropertyDrawer(typeof(RuntimeEvent), true)]
    internal sealed class RuntimeEventDrawer : PropertyDrawer
    {
        /// <summary>The vertical space around each binding row.</summary>
        private const float RowPadding = 2f;

        /// <summary>The horizontal space between the object field and the method button.</summary>
        private const float ColumnSpacing = 4f;

        /// <summary>The share of a row's width taken by the object field.</summary>
        private const float TargetWidthRatio = 0.4f;

        /// <summary>The label of a binding row that differs between the selected objects.</summary>
        private const string MixedValueLabel = "\u2014";

        /// <summary>The tint of the method button when the selected method cannot be resolved.</summary>
        private static readonly Color s_missingColor = new(1f, 0.45f, 0.45f);

        /// <summary>The list state of every drawn property, keyed by property path.</summary>
        private readonly Dictionary<string, DrawerState> _states = new();

        /// <summary>Gets the height of the binding list.</summary>
        /// <param name="property">The <see cref="RuntimeEvent"/> property.</param>
        /// <param name="label">The property label.</param>
        /// <returns>The height of the header, the rows and the add and remove buttons.</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return GetState(property, label).List.GetHeight();
        }

        /// <summary>Draws the binding list.</summary>
        /// <param name="position">The rectangle to draw in.</param>
        /// <param name="property">The <see cref="RuntimeEvent"/> property.</param>
        /// <param name="label">The property label.</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var state = GetState(property, label);
            EditorGUI.BeginProperty(position, label, property);
            state.List.DoList(position);
            EditorGUI.EndProperty();
        }

        /// <summary>Gets or creates the list state of a property and points it at the current serialized data.</summary>
        /// <param name="property">The <see cref="RuntimeEvent"/> property.</param>
        /// <param name="label">The property label shown in the list header.</param>
        /// <returns>The list state.</returns>
        private DrawerState GetState(SerializedProperty property, GUIContent label)
        {
            var bindings = property.FindPropertyRelative(RuntimeEventDrawerUtility.BindingsPropertyName);
            if (!_states.TryGetValue(property.propertyPath, out var state)
                || !ReferenceEquals(state.SerializedObject, property.serializedObject))
            {
                var delegateType = fieldInfo is null ? null : RuntimeEventDrawerUtility.GetDelegateType(fieldInfo.FieldType);
                state = new DrawerState(property.serializedObject, bindings, delegateType);
                _states[property.propertyPath] = state;
            }
            state.List.serializedProperty = bindings;
            // EditorGUI.GetPropertyHeight(property, includeChildren) passes a null label.
            state.SetLabel(label?.text ?? property.displayName);
            return state;
        }

        /// <summary>Draws one binding row: the object field and the method button.</summary>
        /// <param name="state">The list state.</param>
        /// <param name="rect">The row rectangle.</param>
        /// <param name="index">The binding index.</param>
        private static void DrawBinding(DrawerState state, Rect rect, int index)
        {
            var binding = state.List.serializedProperty.GetArrayElementAtIndex(index);
            var targetProperty = binding.FindPropertyRelative(RuntimeEventDrawerUtility.TargetPropertyName);
            var methodNameProperty = binding.FindPropertyRelative(RuntimeEventDrawerUtility.MethodNamePropertyName);

            rect.y += RowPadding / 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            var targetRect = new Rect(rect.x, rect.y, rect.width * TargetWidthRatio, rect.height);
            var methodRect = new Rect(
                targetRect.xMax + ColumnSpacing,
                rect.y,
                rect.width - targetRect.width - ColumnSpacing,
                rect.height);

            EditorGUI.PropertyField(targetRect, targetProperty, GUIContent.none);

            var target = targetProperty.objectReferenceValue;
            var methodName = methodNameProperty.stringValue;
            var isMissing = false;
            string buttonLabel;
            if (targetProperty.hasMultipleDifferentValues || methodNameProperty.hasMultipleDifferentValues)
            {
                buttonLabel = MixedValueLabel;
            }
            else if (state.DelegateType is null)
            {
                buttonLabel = "Unsupported delegate type";
            }
            else
            {
                buttonLabel = RuntimeEventDrawerUtility.GetBindingLabel(target, methodName, state.DelegateType, out isMissing);
            }

            using (new EditorGUI.DisabledScope(target == null || state.DelegateType is null))
            {
                var previousColor = GUI.color;
                if (isMissing)
                {
                    GUI.color = s_missingColor;
                }
                var isClicked = EditorGUI.DropdownButton(methodRect, new GUIContent(buttonLabel), FocusType.Keyboard);
                GUI.color = previousColor;
                if (isClicked && state.DelegateType is not null)
                {
                    ShowMethodMenu(state, methodRect, binding.propertyPath, target, methodName, state.DelegateType);
                }
            }
        }

        /// <summary>Shows the menu that selects the method of a binding.</summary>
        /// <param name="state">The list state.</param>
        /// <param name="buttonRect">The rectangle of the method button, under which the menu opens.</param>
        /// <param name="bindingPath">The property path of the binding.</param>
        /// <param name="target">The object currently assigned to the binding.</param>
        /// <param name="methodName">The method currently selected.</param>
        /// <param name="delegateType">The delegate type of the event.</param>
        private static void ShowMethodMenu(
            DrawerState state,
            Rect buttonRect,
            string bindingPath,
            UnityEngine.Object? target,
            string methodName,
            Type delegateType)
        {
            var serializedObject = state.SerializedObject;
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(RuntimeEventDrawerUtility.NoFunctionLabel),
                methodName.Length == 0,
                () => RuntimeEventDrawerUtility.AssignBinding(serializedObject, bindingPath, target, string.Empty));
            menu.AddSeparator(string.Empty);

            var entries = RuntimeEventDrawerUtility.GetMenuEntries(target, delegateType);
            if (entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent($"No [BindableMethod] methods matching {state.Signature}"));
            }
            foreach (var entry in entries)
            {
                var isSelected = entry.Target == target && entry.MethodName == methodName;
                menu.AddItem(
                    new GUIContent(entry.Path),
                    isSelected,
                    () => RuntimeEventDrawerUtility.AssignBinding(serializedObject, bindingPath, entry.Target, entry.MethodName));
            }
            menu.DropDown(buttonRect);
        }

        /// <summary>The reorderable list and cached labels of one drawn property.</summary>
        private sealed class DrawerState
        {
            /// <summary>The property label that <see cref="HeaderText"/> was built from.</summary>
            private string? _label;

            /// <summary>Initializes the state of a property.</summary>
            /// <param name="serializedObject">The serialized object that owns the property.</param>
            /// <param name="bindings">The binding list property.</param>
            /// <param name="delegateType">The delegate type of the event, or null when it cannot be determined.</param>
            public DrawerState(SerializedObject serializedObject, SerializedProperty bindings, Type? delegateType)
            {
                SerializedObject = serializedObject;
                DelegateType = delegateType is not null && RuntimeEventMethods.IsSupportedDelegateType(delegateType)
                    ? delegateType
                    : null;
                Signature = DelegateType is null ? "(unsupported delegate type)" : RuntimeEventMethods.FormatSignature(DelegateType);
                List = new ReorderableList(serializedObject, bindings, true, true, true, true)
                {
                    elementHeight = EditorGUIUtility.singleLineHeight + RowPadding,
                };
                List.drawHeaderCallback = rect => EditorGUI.LabelField(rect, HeaderText);
                List.drawElementCallback = (rect, index, isActive, isFocused) => DrawBinding(this, rect, index);
            }

            /// <summary>Gets the serialized object that the list edits.</summary>
            public SerializedObject SerializedObject { get; }

            /// <summary>Gets the supported delegate type of the event, or null when it is not supported.</summary>
            public Type? DelegateType { get; }

            /// <summary>Gets the formatted signature of the delegate type.</summary>
            public string Signature { get; }

            /// <summary>Gets the list that draws the bindings.</summary>
            public ReorderableList List { get; }

            /// <summary>Gets the list header: the property label followed by the signature.</summary>
            public string HeaderText { get; private set; } = string.Empty;

            /// <summary>Updates <see cref="HeaderText"/> when the property label changes.</summary>
            /// <param name="label">The property label.</param>
            public void SetLabel(string label)
            {
                if (_label == label)
                {
                    return;
                }
                _label = label;
                HeaderText = $"{label} {Signature}";
            }
        }
    }
}
