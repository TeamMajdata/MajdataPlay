/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

using System;
using System.Linq;
using Live2D.Cubism.Core;
using UnityEngine;


namespace Live2D.Cubism.Rendering
{
    [ExecuteInEditMode, RequireComponent(typeof(CubismPart))]
    public class CubismPartColorsEditor : MonoBehaviour
    {
        /// <summary>
        /// RenderController.
        /// </summary>
        private CubismRenderController _renderController;

        /// <summary>
        /// Renderer Array.
        /// </summary>
        private CubismRenderer[] _drawableRenderers;

        /// <summary>
        /// Part Array.
        /// </summary>
        private CubismPart _part;

        /// <summary>
        /// <see cref="ChildDrawableRenderers"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismRenderer[] _childDrawableRenderers;

        /// <summary>
        /// Array of art meshes with parts as parents.
        /// </summary>
        public CubismRenderer[] ChildDrawableRenderers
        {
            get { return _childDrawableRenderers; }
            set { _childDrawableRenderers = value; }
        }

        /// <summary>
        /// <see cref="ChildPartColorEditors"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private CubismPartColorsEditor[] _childPartColorEditors;

        /// <summary>
        /// Array of own child parts.
        /// </summary>
        public CubismPartColorsEditor[] ChildPartColorEditors
        {
            get { return _childPartColorEditors; }
            set { _childPartColorEditors = value; }
        }

        /// <summary>
        /// <see cref="PartMultiplyColorEnabled"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private bool _isOverriddenPartMultiplyColors;

        /// <summary>
        /// Whether to override with multiply color from the model.
        /// </summary>
        public bool PartMultiplyColorEnabled
        {
            get { return _isOverriddenPartMultiplyColors; }
            set
            {
                _isOverriddenPartMultiplyColors = value;
                for (int i = 0; i < ChildDrawableRenderers.Length; i++)
                {
                    ChildDrawableRenderers[i].DrawObjectMultiplyColorEnabled = PartMultiplyColorEnabled;
                    ChildDrawableRenderers[i].LastMultiplyColor = PartMultiplyColorEnabled ? MultiplyColor : ChildDrawableRenderers[i].LastMultiplyColor;
                    ChildDrawableRenderers[i].MultiplyColor = PartMultiplyColorEnabled ? MultiplyColor : ChildDrawableRenderers[i].MultiplyColor;
                    ChildDrawableRenderers[i].ApplyMultiplyColor();
                }
                for (int i = 0; i < ChildPartColorEditors.Length; i++)
                {
                    ChildPartColorEditors[i].PartMultiplyColorEnabled = PartMultiplyColorEnabled;
                    ChildPartColorEditors[i].MultiplyColor = MultiplyColor;
                }
            }
        }

        /// <summary>
        /// <see cref="PartScreenColorEnabled"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private bool _isOverriddenPartScreenColors;

        /// <summary>
        /// Whether to override with screen color from the model.
        /// </summary>
        public bool PartScreenColorEnabled
        {
            get { return _isOverriddenPartScreenColors; }
            set
            {
                _isOverriddenPartScreenColors = value;
                for (int i = 0; i < ChildDrawableRenderers.Length; i++)
                {
                    ChildDrawableRenderers[i].DrawObjectScreenColorEnabled = PartScreenColorEnabled;
                    ChildDrawableRenderers[i].LastScreenColor = PartScreenColorEnabled ? ScreenColor : ChildDrawableRenderers[i].LastScreenColor;
                    ChildDrawableRenderers[i].ScreenColor = PartScreenColorEnabled ? ScreenColor : ChildDrawableRenderers[i].ScreenColor;
                    ChildDrawableRenderers[i].ApplyScreenColor();
                }
                for (int i = 0; i < ChildPartColorEditors.Length; i++)
                {
                    ChildPartColorEditors[i].PartScreenColorEnabled = PartScreenColorEnabled;
                    ChildPartColorEditors[i].ScreenColor = ScreenColor;
                }
            }
        }

        /// <summary>
        /// <see cref="MultiplyColor"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _multiplyColor = Color.white;

        /// <summary>
        /// Multiply color.
        /// </summary>
        public Color MultiplyColor
        {
            get
            {
                return _multiplyColor;
            }
            set
            {
                // 同じ値が与えられた場合、早めに返す
                if (value == _multiplyColor)
                {
                    return;
                }

                // 値を保存
                _multiplyColor = value;

                for (int i = 0; i < ChildDrawableRenderers.Length; i++)
                {
                    ChildDrawableRenderers[i].MultiplyColor = PartMultiplyColorEnabled ? MultiplyColor : ChildDrawableRenderers[i].MultiplyColor;
                }
                for (int i = 0; i < ChildPartColorEditors.Length; i++)
                {
                    ChildPartColorEditors[i].MultiplyColor = PartMultiplyColorEnabled ? MultiplyColor : ChildPartColorEditors[i].MultiplyColor;
                }
            }
        }

        /// <summary>
        /// <see cref="ScreenColor"/>s backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private Color _screenColor = Color.clear;

        /// <summary>
        /// Screen color.
        /// </summary>
        public Color ScreenColor
        {
            get
            {
                return _screenColor;
            }
            set
            {
                // 同じ値が与えられた場合、早めに返す
                if (value == _screenColor)
                {
                    return;
                }

                // 値を保存
                _screenColor = value;

                for (int i = 0; i < ChildDrawableRenderers.Length; i++)
                {
                    ChildDrawableRenderers[i].ScreenColor = PartScreenColorEnabled ? ScreenColor : ChildDrawableRenderers[i].ScreenColor;
                }
                for (int i = 0; i < ChildPartColorEditors.Length; i++)
                {
                    ChildPartColorEditors[i].ScreenColor = PartScreenColorEnabled ? ScreenColor : ChildPartColorEditors[i].ScreenColor;
                }
            }
        }

        [Obsolete]
        public void TryInitialize(CubismRenderController cubismRenderController, CubismPart part, CubismDrawable[] drawables)
        {
            TryInitialize(gameObject.FindCubismModel(true));
        }

        public void TryInitialize(CubismModel model)
        {
            var drawables = model.Drawables;

            // Initialize.
            _renderController = model.GetComponent<CubismRenderController>();
            _drawableRenderers = _renderController.DrawableRenderers;
            _part = GetComponent<CubismPart>();

            if (_part?.ChildDrawables == null)
            {
                // Fail silently if the part has no child drawables.
                return;
            }

            // Initialize elements.
            ChildDrawableRenderers = Array.Empty<CubismRenderer>();

            for (var drawableIndex = 0; drawableIndex < _part.ChildDrawables.Length; drawableIndex++)
            {
                for (var rendererIndex = 0; rendererIndex < _drawableRenderers.Length; rendererIndex++)
                {
                    var drawableRenderer = _drawableRenderers[rendererIndex];

                    // When this object is the child drawable.
                    if (_part.ChildDrawables[drawableIndex] == drawableRenderer.Drawable)
                    {
                        // Register the corresponding renderers in the dictionary.
                        Array.Resize(ref _childDrawableRenderers, _childDrawableRenderers.Length + 1);
                        ChildDrawableRenderers[^1] = drawableRenderer;
                    }
                }
            }

            if (_part?.ChildParts == null)
            {
                // Fail silently if the part has no child drawables.
                return;
            }

            ChildPartColorEditors = Array.Empty<CubismPartColorsEditor>();

            for (var index = 0; index < _part.ChildParts.Length; index++)
            {
                var cubismPart = _part.ChildParts[index];
                Array.Resize(ref _childPartColorEditors, _childPartColorEditors.Length + 1);
                var colorsEditor = cubismPart.GetComponent<CubismPartColorsEditor>();
                ChildPartColorEditors[^1] = colorsEditor;
            }
        }

#region Unity Events

        private void OnEnable()
        {
            // Early return.
            if ((ChildDrawableRenderers?.Length ?? 0) != 0 && !ChildDrawableRenderers.Contains(null))
            {
                return;
            }

            // Initialize.
            var model = gameObject.FindCubismModel(true);
            TryInitialize(model);
        }

#endregion
    }
}
