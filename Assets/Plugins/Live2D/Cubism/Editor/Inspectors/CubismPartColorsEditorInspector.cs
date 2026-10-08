/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEditor;
using Live2D.Cubism.Rendering;


namespace Live2D.Cubism.Editor.Inspectors
{
    [CustomEditor(typeof(CubismPartColorsEditor)), CanEditMultipleObjects]
    internal sealed class PortfolioPartBlendColorEditorInspector : UnityEditor.Editor
    {
        private SerializedProperty childDrawableRenderers;
        private SerializedProperty childParts;

        #region Editor

        /// <summary>
        /// Draws inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var blendColorEditor = target as CubismPartColorsEditor;

            // Fail silently.
            if (blendColorEditor == null)
            {
                return;
            }

            // Obtains a property from a component.
            if (childDrawableRenderers == null)
            {
                childDrawableRenderers = serializedObject.FindProperty("_childDrawableRenderers");
            }
            if (childParts == null)
            {
                childParts = serializedObject.FindProperty("_childParts");
            }

            if (childDrawableRenderers != null)
            {
                // Show renderers.
                EditorGUILayout.PropertyField(childDrawableRenderers);
            }
            if (childParts != null)
            {
                // Show parts.
                EditorGUILayout.PropertyField(childParts);
            }

            EditorGUI.BeginChangeCheck();

            // Display PartMultiplyColorEnabled.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var overrideColorForPartMultiplyColors = EditorGUILayout.Toggle("PartMultiplyColorEnabled", blendColorEditor.PartMultiplyColorEnabled);

                if (scope.changed)
                {
                    foreach (CubismPartColorsEditor partBlendColorEditor in targets)
                    {
                        partBlendColorEditor.PartMultiplyColorEnabled = overrideColorForPartMultiplyColors;
                    }
                }
            }

            // Display PartScreenColorEnabled.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var overrideColorForPartScreenColors = EditorGUILayout.Toggle("PartScreenColorEnabled", blendColorEditor.PartScreenColorEnabled);

                if (scope.changed)
                {
                    foreach (CubismPartColorsEditor partBlendColorEditor in targets)
                    {
                        partBlendColorEditor.PartScreenColorEnabled = overrideColorForPartScreenColors;
                    }
                }
            }

            // Display multiply color.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var multiplyColor = EditorGUILayout.ColorField("MultiplyColor", blendColorEditor.MultiplyColor);

                if (scope.changed)
                {
                    foreach (CubismPartColorsEditor partBlendColorEditor in targets)
                    {
                        partBlendColorEditor.MultiplyColor = multiplyColor;
                    }
                }
            }

            // Display screen color.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var screenColor = EditorGUILayout.ColorField("ScreenColor", blendColorEditor.ScreenColor);

                if (scope.changed)
                {
                    foreach (CubismPartColorsEditor partBlendColorEditor in targets)
                    {
                        partBlendColorEditor.ScreenColor = screenColor;
                    }
                }
            }


            // Save any changes.
            if (EditorGUI.EndChangeCheck())
            {
                foreach (CubismPartColorsEditor partBlendColorEditor in targets)
                {
                    EditorUtility.SetDirty(partBlendColorEditor);

                    foreach (var renderer in partBlendColorEditor.ChildDrawableRenderers)
                    {
                        EditorUtility.SetDirty(renderer);
                        // HACK Get mesh renderer directly.
                        EditorUtility.SetDirty(renderer.MeshRenderer);
                    }
                }
            }
        }

        #endregion
    }
}
