using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Preserves native category order or keeps a button's Image, SVG and TMP foreground above its body.</summary>
    /// <remarks>Instances are bound at runtime and retained for pooling; no source material or serialized asset is changed.</remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class UiBackdropForeground : MonoBehaviour, IMaterialModifier
    {
        /// <summary>Identifies the active button whose visibility and quality policy own this helper.</summary>
        private UiBackdropPanel? _owner;

        /// <summary>Retains the native Graphic without restricting its concrete renderer type.</summary>
        private Graphic? _graphic;

        /// <summary>Identifies a bound category label whose native fallback submeshes can be created later.</summary>
        private TMP_Text? _text;

        /// <summary>Pairs TMP notifications only while this explicitly bound label is enabled.</summary>
        private bool _textSubscribed;

        /// <summary>Prevents material updates during a text-change notification from recursively scanning the label.</summary>
        private bool _refreshingDescendants;

        /// <summary>Reuses storage when this label generates native fallback or sprite submeshes.</summary>
        private readonly List<TMP_SubMeshUI> _scan = new(2);

        /// <summary>Owns only queue modifiers attached to this label's native submeshes.</summary>
        private readonly List<UiBackdropForeground> _descendants = new(2);

        /// <summary>Identifies the native material after earlier stencil or renderer-specific modifiers.</summary>
        private Material? _source;

        /// <summary>Owns only the queue-adjusted clone returned to the native CanvasRenderer.</summary>
        private Material? _material;

        private void OnEnable()
        {
            _graphic = GetComponent<Graphic>();
            _text = _graphic as TMP_Text;
            UpdateTextSubscription();
            RefreshDescendants();
            RefreshMaterial();
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
            ReleaseDescendants();
            _owner = null;
            UpdateTextSubscription();
            ReleaseMaterial();
        }

        /// <summary>Assigns or clears a runtime owner and restores the original material when unbound.</summary>
        /// <param name="owner">The active button panel, or null when its enabled lifetime ends.</param>
        internal void Bind(UiBackdropPanel? owner)
        {
            if (ReferenceEquals(_owner, owner))
            {
                return;
            }
            ReleaseDescendants();
            _owner = owner;
            _graphic ??= GetComponent<Graphic>();
            _text = _graphic as TMP_Text;
            UpdateTextSubscription();
            RefreshDescendants();
            ReleaseMaterial();
            RefreshMaterial();
        }

        /// <summary>Checks ownership before a previous hierarchy owner attempts to unbind a reused helper.</summary>
        /// <param name="owner">The panel checking whether it still owns this helper.</param>
        /// <returns>True only for the currently bound runtime owner.</returns>
        internal bool IsBoundTo(UiBackdropPanel owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        /// <summary>Refreshes native material selection for this Graphic and its existing TMP fallback submeshes.</summary>
        internal void RefreshMaterial()
        {
            if (_graphic == null)
            {
                _graphic = GetComponent<Graphic>();
            }
            if (_graphic != null)
            {
                if (CanvasUpdateRegistry.IsRebuildingGraphics())
                {
                    var renderer = _graphic.canvasRenderer;
                    if (renderer.materialCount == 0)
                    {
                        renderer.materialCount = 1;
                    }
                    renderer.SetMaterial(_graphic.materialForRendering, 0);
                    renderer.SetTexture(_graphic.mainTexture);
                }
                else
                {
                    _graphic.SetMaterialDirty();
                }
            }
            foreach (var descendant in _descendants)
            {
                if (descendant != null && _owner != null && descendant.IsBoundTo(_owner))
                {
                    descendant.RefreshMaterial();
                }
            }
        }

        /// <summary>Observes only an explicitly bound, enabled TMP label, without a per-frame hierarchy scan.</summary>
        private void UpdateTextSubscription()
        {
            var subscribe = _owner != null && isActiveAndEnabled && _text != null;
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

        /// <summary>Keeps newly generated native text submeshes in the same clear foreground as their label.</summary>
        /// <param name="changed">The TMP object that completed native geometry generation.</param>
        private void OnTextChanged(UnityEngine.Object changed)
        {
            if (_owner != null && isActiveAndEnabled && ReferenceEquals(changed, _text))
            {
                RefreshDescendants();
            }
        }

        /// <summary>Binds only this TMP label's fallback Graphics while retaining their shaders, masks and geometry.</summary>
        private void RefreshDescendants()
        {
            var owner = _owner;
            if (owner == null || _text == null || !isActiveAndEnabled || _refreshingDescendants)
            {
                return;
            }
            _refreshingDescendants = true;
            try
            {
                _scan.Clear();
                _text.GetComponentsInChildren(true, _scan);
                foreach (var submesh in _scan)
                {
                    var foreground = submesh.GetComponent<UiBackdropForeground>();
                    if (foreground == null)
                    {
                        foreground = submesh.gameObject.AddComponent<UiBackdropForeground>();
                        foreground.hideFlags = HideFlags.DontSave;
                    }
                    foreground.Bind(owner);
                    if (!_descendants.Contains(foreground))
                    {
                        _descendants.Add(foreground);
                    }
                }
                for (var index = _descendants.Count - 1; index >= 0; index--)
                {
                    var foreground = _descendants[index];
                    if (foreground == null)
                    {
                        _descendants.RemoveAt(index);
                    }
                    else if (!foreground.transform.IsChildOf(transform))
                    {
                        if (foreground.IsBoundTo(owner))
                        {
                            foreground.Bind(null);
                        }
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

        /// <summary>Restores borrowed fallback materials when the parent label is disabled, unbound or pooled.</summary>
        private void ReleaseDescendants()
        {
            foreach (var foreground in _descendants)
            {
                if (foreground != null && _owner != null && foreground.IsBoundTo(_owner))
                {
                    foreground.Bind(null);
                }
            }
            _descendants.Clear();
        }

        /// <summary>Preserves native materials and masks while selecting the owner's category or button stage.</summary>
        /// <param name="baseMaterial">The native Graphic's material after preceding material modifiers.</param>
        /// <returns>A reusable queue-only clone for an active category or button, or the unchanged native material.</returns>
        public Material GetModifiedMaterial(Material baseMaterial)
        {
            var owner = _owner;
            if (!Application.isPlaying || !isActiveAndEnabled || owner == null || !owner.isActiveAndEnabled ||
                !owner.SupportsCurrentStyle ||
                (owner.SourceRole != UiBackdropSourceRole.Peripheral && !owner.IsCategory) ||
                !UiPvBackdropBlur.Enabled || baseMaterial == null)
            {
                ReleaseMaterial();
                return baseMaterial!;
            }
            if (_material == null || _source != baseMaterial || _material.shader != baseMaterial.shader)
            {
                ReleaseMaterial();
                _material = new Material(baseMaterial)
                {
                    name = baseMaterial.name + " (Backdrop Foreground)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _source = baseMaterial;
            }
            else
            {
                // Native TMP/SVG or stencil modifiers may update the same source object during a material rebuild.
                _material.CopyPropertiesFromMaterial(baseMaterial);
            }
            _material.renderQueue = owner.IsCategory ? MajUiBackdropBridge.CategoryQueue
                : MajUiBackdropBridge.ButtonGlyphQueueLowerBound;
            return _material;
        }

        /// <summary>Destroys only this helper's clone while preserving borrowed native materials and pooled components.</summary>
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
