/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using UnityEditor;

namespace Live2D.Cubism.Editor.Inspectors
{
    /// <summary>
    /// Inspector for <see cref="CubismRenderer"/>s.
    /// </summary>
    [CustomEditor(typeof(CubismDrawable)), CanEditMultipleObjects]
    internal sealed class CubismDrawableInspector : UnityEditor.Editor
    {
        /// <summary>
        /// <see cref="CubismPart"/>s cache.
        /// </summary>
        private CubismPart[] Parts { get; set; }

        #region Editor

        /// <summary>
        /// Draws inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var drawable = target as CubismDrawable;

            if (Parts == null)
            {
                Parts = drawable.FindCubismModel(true).Parts;
            }

            if (drawable?.ParentPartIndex < 0)
            {
                return;
            }

            EditorGUILayout.ObjectField("Parent Part", Parts[drawable.ParentPartIndex], typeof(CubismPart), false);
        }

        #endregion
    }
}
