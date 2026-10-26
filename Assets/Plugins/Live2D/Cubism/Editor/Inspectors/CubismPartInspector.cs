/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using System;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Live2D.Cubism.Editor.Inspectors
{
    /// <summary>
    /// Inspector for <see cref="CubismRenderer"/>s.
    /// </summary>
    [CustomEditor(typeof(CubismPart)), CanEditMultipleObjects]
    internal sealed class CubismPartInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Parent <see cref="CubismPart"/> cache.
        /// </summary>
        private CubismPart _parent;

        /// <summary>
        /// Child <see cref="CubismPart"/>s foldout state.
        /// </summary>
        private static bool ChildPartsFoldout { get; set; } = true;

        /// <summary>
        /// Child <see cref="CubismPart"/>s ReorderableList.
        /// </summary>
        private ReorderableList ChildParts { get; set; }

        /// <summary>
        /// Child <see cref="CubismPart"/>'s child <see cref="CubismDrawable"/>s.
        /// </summary>
        private InternalTreeInfo ChildPartDrawables { get; set; }

        private int DrawablesTotalCount { get; set; }

        #region Editor

        /// <summary>
        /// Draws inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (!IsInitialized)
            {
                Initialize();
            }

            EditorGUILayout.ObjectField("Parent Part", _parent, typeof(CubismPart), false);

            {
                var rect = EditorGUILayout.GetControlRect();
                var foldoutRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                var fieldRect = new Rect(rect.x + rect.width - 50f, rect.y, 50f, rect.height);

                ChildPartsFoldout = EditorGUI.Foldout(foldoutRect, ChildPartsFoldout, "Child Parts");
                EditorGUI.IntField(fieldRect, ChildParts.count);
            }
            if (ChildPartsFoldout)
            {
                ChildParts.DoLayoutList();
            }

            {
                var rect = EditorGUILayout.GetControlRect();
                var foldoutRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                var fieldRect = new Rect(rect.x + rect.width - 50f, rect.y, 50f, rect.height);

                ChildPartDrawables.Foldout = EditorGUI.Foldout(foldoutRect, ChildPartDrawables.Foldout, "Child Drawables");
                EditorGUI.IntField(fieldRect, DrawablesTotalCount);
            }
            if (ChildPartDrawables.Foldout)
            {
                for (var i = 0; i < ChildPartDrawables.Drawables.Length; i++)
                {
                    EditorGUILayout.ObjectField(ChildPartDrawables.Drawables[i], typeof(CubismDrawable), true);
                }
                EditorGUI.indentLevel++;
                for (var i = 0; i < ChildPartDrawables.Children.Length; i++)
                {
                    DrawTree(ChildPartDrawables.Children[i]);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawTree(InternalTreeInfo item)
        {
            item.Foldout = EditorGUILayout.Foldout(item.Foldout, item.Part.Id);
            if (item.Foldout)
            {
                for (var i = 0; i < item.Drawables.Length; i++)
                {
                    EditorGUILayout.ObjectField(item.Drawables[i], typeof(CubismDrawable), true);
                }
                for (var i = 0; i < item.Children.Length; i++)
                {
                    DrawTree(item.Children[i]);
                }
            }
        }

        #endregion

        /// <summary>
        /// Gets whether <see langword="this"/> is initialized.
        /// </summary>
        private bool IsInitialized
        {
            get
            {
                return ChildParts != null && ChildPartDrawables != null;
            }
        }

        /// <summary>
        /// Initializes <see langword="this"/>.
        /// </summary>
        private void Initialize()
        {
            var part = target as CubismPart;

            var model = part.FindCubismModel(true);
            var parts = model.Parts;

            _parent = part.UnmanagedParentIndex < 0 ? null : parts[part.UnmanagedParentIndex];

            var childParts = part.ChildParts;

            ChildParts = new ReorderableList(childParts, typeof(CubismPart), false, false, false, false)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.height = EditorGUIUtility.singleLineHeight;
                    var labelRect = new Rect(rect.x, rect.y, rect.width * 0.2f, rect.height);
                    var objRect = new Rect(rect.x + labelRect.width, rect.y, rect.width * 0.8f, rect.height);
                    EditorGUI.LabelField(labelRect, $"{index}");
                    EditorGUI.ObjectField(objRect, childParts[index], typeof(CubismPart), true);
                }
            };

            ChildPartDrawables = new InternalTreeInfo(part);
            DrawablesTotalCount = ChildPartDrawables.Drawables.Length;
        }

        private class InternalTreeInfo
        {
            public bool Foldout = true;
            public readonly CubismPart Part;
            public readonly CubismDrawable[] Drawables;

            public readonly InternalTreeInfo[] Children;


            public InternalTreeInfo(CubismPart part)
            {
                Part = part;
                Drawables = Part.ChildDrawables;

                Children = Array.Empty<InternalTreeInfo>();
                for (var i = 0; i < Part.ChildParts.Length; i++)
                {
                    Array.Resize(ref Children, Children.Length + 1);
                    Children[^1] = new InternalTreeInfo(Part.ChildParts[i]);
                }
            }
        }
    }
}
