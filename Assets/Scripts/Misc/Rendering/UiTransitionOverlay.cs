using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Keeps explicitly bound transition artwork and text above ordinary UI and glass buttons.</summary>
    /// <remarks>Only material queue is changed; native shaders, masks, geometry and animation remain unchanged.</remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class UiTransitionOverlay : MonoBehaviour, IMaterialModifier
    {
        /// <summary>Places transitions after button bodies and glyphs without introducing another pipeline stage.</summary>
        internal const int OverlayQueue = 4000;

        /// <summary>Tracks explicit ownership independently of the optional background-blur feature.</summary>
        private bool _bound;

        /// <summary>Retains the actual native Graphic whose material is being modified.</summary>
        private Graphic? _graphic;

        /// <summary>Retains the text component whose dynamically generated submeshes must join this overlay.</summary>
        private TMP_Text? _text;

        /// <summary>Pairs the global TMP callback only while this bound text is enabled.</summary>
        private bool _textSubscribed;

        /// <summary>Prevents a text rebuild triggered during binding from recursively scanning the same hierarchy.</summary>
        private bool _refreshingDescendants;

        /// <summary>Reuses hierarchy-query storage only when the bound text changes.</summary>
        private readonly List<Graphic> _scan = new(4);

        /// <summary>Retains modifiers created for this text's native fallback and sprite submeshes.</summary>
        private readonly List<UiTransitionOverlay> _descendants = new(2);

        /// <summary>Identifies the native source after preceding material modifiers.</summary>
        private Material? _source;

        /// <summary>Owns the queue-only clone, never the source font, shader or artwork material.</summary>
        private Material? _material;

        /// <summary>Binds one explicit transition Graphic, reusing an existing runtime modifier without marking it dirty again.</summary>
        /// <param name="graphic">The transition image, decoration, loading label or native text submesh.</param>
        /// <returns>The bound modifier retained by the transition owner.</returns>
        /// <exception cref="ArgumentNullException">The Graphic is null or already destroyed.</exception>
        internal static UiTransitionOverlay Ensure(Graphic graphic)
        {
            if (graphic == null)
            {
                throw new ArgumentNullException(nameof(graphic));
            }
            var overlay = graphic.GetComponent<UiTransitionOverlay>();
            if (overlay == null)
            {
                overlay = graphic.gameObject.AddComponent<UiTransitionOverlay>();
                overlay.hideFlags = HideFlags.DontSave;
            }
            if (overlay._bound)
            {
                return overlay;
            }
            overlay._graphic = graphic;
            overlay._text = graphic as TMP_Text;
            overlay._bound = true;
            overlay.UpdateTextSubscription();
            overlay.RefreshDescendants();
            overlay.RefreshMaterial();
            return overlay;
        }

        private void OnEnable()
        {
            _graphic = GetComponent<Graphic>();
            _text = _graphic as TMP_Text;
            UpdateTextSubscription();
            if (_bound)
            {
                RefreshDescendants();
                RefreshMaterial();
            }
        }

        private void OnDisable()
        {
            UpdateTextSubscription();
            ReleaseDescendants();
            ReleaseMaterial();
            RefreshMaterial();
        }

        private void OnDestroy()
        {
            _bound = false;
            UpdateTextSubscription();
            ReleaseDescendants();
            ReleaseMaterial();
        }

        /// <summary>Restores borrowed native materials and releases text subscriptions without destroying pooled helpers.</summary>
        internal void Unbind()
        {
            if (!_bound)
            {
                return;
            }
            _bound = false;
            UpdateTextSubscription();
            ReleaseDescendants();
            ReleaseMaterial();
            RefreshMaterial();
        }

        /// <summary>Preserves the complete native material while placing an enabled transition after other UI.</summary>
        /// <param name="baseMaterial">The native material after earlier stencil or renderer-specific modifiers.</param>
        /// <returns>A reusable overlay clone, or the unchanged native material when unbound or disabled.</returns>
        public Material GetModifiedMaterial(Material baseMaterial)
        {
            if (!_bound || !isActiveAndEnabled || baseMaterial == null)
            {
                ReleaseMaterial();
                return baseMaterial!;
            }
            if (_material == null || _source != baseMaterial || _material.shader != baseMaterial.shader)
            {
                ReleaseMaterial();
                _material = new Material(baseMaterial)
                {
                    name = baseMaterial.name + " (Transition Overlay)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _source = baseMaterial;
            }
            else
            {
                _material.CopyPropertiesFromMaterial(baseMaterial);
            }
            _material.renderQueue = OverlayQueue;
            return _material;
        }

        /// <summary>Pairs text callbacks only for a bound, enabled TMP owner, never for unrelated scene text.</summary>
        private void UpdateTextSubscription()
        {
            var subscribe = _bound && isActiveAndEnabled && _text != null;
            if (subscribe == _textSubscribed)
            {
                return;
            }
            _textSubscribed = subscribe;
            if (subscribe)
            {
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            }
            else
            {
                TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            }
        }

        /// <summary>Discovers new fallback submeshes only when this exact loading label changes.</summary>
        /// <param name="changed">The TMP object whose native text generation has just completed.</param>
        private void OnTextChanged(UnityEngine.Object changed)
        {
            if (_bound && isActiveAndEnabled && ReferenceEquals(changed, _text))
            {
                RefreshDescendants();
            }
        }

        /// <summary>Binds newly created native text Graphics without rebuilding existing modifiers or scanning every frame.</summary>
        private void RefreshDescendants()
        {
            if (_text == null || _refreshingDescendants)
            {
                return;
            }
            _refreshingDescendants = true;
            try
            {
                _scan.Clear();
                _text.GetComponentsInChildren(true, _scan);
                foreach (var graphic in _scan)
                {
                    if (graphic == null || graphic == _graphic)
                    {
                        continue;
                    }
                    var overlay = Ensure(graphic);
                    if (!_descendants.Contains(overlay))
                    {
                        _descendants.Add(overlay);
                    }
                }
                for (var index = _descendants.Count - 1; index >= 0; index--)
                {
                    var overlay = _descendants[index];
                    if (overlay == null)
                    {
                        _descendants.RemoveAt(index);
                    }
                    else if (!overlay.transform.IsChildOf(transform))
                    {
                        overlay.Unbind();
                        _descendants.RemoveAt(index);
                    }
                }
            }
            finally
            {
                _scan.Clear();
                _refreshingDescendants = false;
            }
        }

        /// <summary>Restores native materials on fallback children when their text owner is disabled or released.</summary>
        private void ReleaseDescendants()
        {
            foreach (var overlay in _descendants)
            {
                if (overlay != null)
                {
                    overlay.Unbind();
                }
            }
            _descendants.Clear();
        }

        /// <summary>Updates material selection safely even when TMP reports a new submesh during a Canvas rebuild.</summary>
        private void RefreshMaterial()
        {
            var graphic = _graphic;
            if (graphic == null || !graphic.isActiveAndEnabled)
            {
                return;
            }
            if (CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                // Match native Graphic.UpdateMaterial without recursively registering another graphic rebuild.
                var renderer = graphic.canvasRenderer;
                if (renderer.materialCount == 0)
                {
                    renderer.materialCount = 1;
                }
                renderer.SetMaterial(graphic.materialForRendering, 0);
                renderer.SetTexture(graphic.mainTexture);
            }
            else
            {
                graphic.SetMaterialDirty();
            }
        }

        /// <summary>Destroys only the owned queue clone, leaving native font and artwork materials untouched.</summary>
        private void ReleaseMaterial()
        {
            if (_material != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_material);
                }
                else
                {
                    DestroyImmediate(_material);
                }
            }
            _material = null;
            _source = null;
        }
    }
}
