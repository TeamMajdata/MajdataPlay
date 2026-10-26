/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEditor;
using UnityEngine;


namespace Live2D.Cubism.Editor.Inspectors
{
    /// <summary>
    /// Inspector for <see cref="CubismRenderer"/>s.
    /// </summary>
    [CustomEditor(typeof(CubismRenderer)), CanEditMultipleObjects]
    internal sealed class CubismRendererInspector : UnityEditor.Editor
    {
        #region Editor

        /// <summary>
        /// Draws inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var renderer = target as CubismRenderer;


            // Fail silently.
            if (renderer == null)
            {
                return;
            }

            if (!renderer.isActiveAndEnabled)
            {
                var model = renderer.FindCubismModel(true);
                var ctrl = model.GetComponent<CubismRenderController>();
                if (!ctrl.IsInitialized)
                {
                    ctrl.TryInitialize();
                }
            }

            // Show settings.
            EditorGUILayout.ObjectField("Mesh", renderer.Mesh, typeof(Mesh), false);


            EditorGUI.BeginChangeCheck();

            // Display DrawObjectMultiplyColorEnabled.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var overrideFlagForDrawObjectMultiplyColors = EditorGUILayout.Toggle("DrawObjectMultiplyColorEnabled", renderer.DrawObjectMultiplyColorEnabled);

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.DrawObjectMultiplyColorEnabled = overrideFlagForDrawObjectMultiplyColors;
                    }
                }
            }

            // Display DrawObjectScreenColorEnabled.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var overrideFlagForDrawObjectScreenColors = EditorGUILayout.Toggle("DrawObjectScreenColorEnabled", renderer.DrawObjectScreenColorEnabled);

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.DrawObjectScreenColorEnabled = overrideFlagForDrawObjectScreenColors;
                    }
                }
            }

            // Display color.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var color = EditorGUILayout.ColorField("Color", renderer.Color);

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.Color = color;
                    }
                }
            }

            // Display multiply color.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                if (renderer.DrawObjectType == CubismModelTypes.DrawObjectType.Drawable
                    || renderer.DrawObjectType == CubismModelTypes.DrawObjectType.Offscreen)
                {
                    var multiplyColor = EditorGUILayout.ColorField("MultiplyColor", renderer.MultiplyColor);

                    if (scope.changed)
                    {
                        foreach (CubismRenderer cubismRenderer in targets)
                        {
                            cubismRenderer.MultiplyColor = multiplyColor;
                        }
                    }
                }
            }

            // Display screen color.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                if (renderer.DrawObjectType == CubismModelTypes.DrawObjectType.Drawable
                    || renderer.DrawObjectType == CubismModelTypes.DrawObjectType.Offscreen)
                {
                    var screenColor = EditorGUILayout.ColorField("ScreenColor", renderer.ScreenColor);

                    if (scope.changed)
                    {
                        foreach (CubismRenderer cubismRenderer in targets)
                        {
                            cubismRenderer.ScreenColor = screenColor;
                        }
                    }
                }
            }

            // Display material (DrawMaterial is the actual rendering material).
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var material = EditorGUILayout.ObjectField("Material", renderer.DrawMaterial, typeof(Material), true) as Material;

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.DrawMaterial = material;
                    }
                }
            }

            // Display main texture.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var mainTexture = EditorGUILayout.ObjectField("Main Texture", renderer.MainTexture, typeof(Texture2D), true) as Texture2D;

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.MainTexture = mainTexture;
                    }
                }
            }

            // Display local sorting order.
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var localSortingOrder = EditorGUILayout.IntField("Local Order", renderer.LocalSortingOrder);

                if (scope.changed)
                {
                    foreach (CubismRenderer cubismRenderer in targets)
                    {
                        cubismRenderer.LocalSortingOrder = localSortingOrder;
                    }
                }
            }


            // Save any changes.
            if (EditorGUI.EndChangeCheck())
            {
                foreach (CubismRenderer cubismRenderer in targets)
                {
                    EditorUtility.SetDirty(cubismRenderer);
                    EditorUtility.SetDirty(cubismRenderer.MeshRenderer);
                }
            }


            // Show backend toggle.
            var showBackends = (renderer.MeshRenderer.hideFlags & HideFlags.HideInInspector) != HideFlags.HideInInspector;
            var toggle = EditorGUILayout.Toggle("Show Mesh Filter & Renderer", showBackends) != showBackends;


            if (toggle)
            {
                foreach (CubismRenderer cubismRenderer in targets)
                {
                    cubismRenderer.MeshRenderer.hideFlags ^= HideFlags.HideInInspector;
                }
            }
        }

        #endregion
    }
}
