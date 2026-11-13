using UnityEngine;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Keeps main-display UI inside its display area and below the sub-display content.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(250)]
    [AddComponentMenu("")]
    public sealed class MainDisplayUiClip : MonoBehaviour
    {
        /// <summary>Identifies the layout-owned main display rather than nested menu panels.</summary>
        private const string MainDisplayName = "Main_Display";
        /// <summary>Identifies the sibling display that main UI must not overdraw.</summary>
        private const string SubDisplayName = "Sub_Display";
        /// <summary>Reuses the sub-display corners without per-frame allocations.</summary>
        private readonly Vector3[] _corners = new Vector3[4];
        /// <summary>Retains the actual main display after layout scaling and positioning.</summary>
        private RectTransform? _main;
        /// <summary>Retains the sibling sub display used to locate the visible upper boundary.</summary>
        private RectTransform? _sub;
        /// <summary>Provides native UGUI clipping without extra draw calls, targets, or copies.</summary>
        private RectMask2D? _mask;
        /// <summary>Preserves any authored padding while temporarily tightening the upper edge.</summary>
        private Vector4 _originalPadding;

        /// <summary>Creates one runtime clip owner only for quality-aware UI under a real main-display group.</summary>
        /// <param name="image">The enabled or reparented quality-aware UI image.</param>
        internal static void EnsureFor(QualityUiImage image)
        {
            for (var current = image.transform; current != null; current = current.parent)
            {
                if (current.name != MainDisplayName || current is not RectTransform ||
                    current.parent == null || current.parent.Find(SubDisplayName) is not RectTransform)
                {
                    continue;
                }
                var clip = current.GetComponent<MainDisplayUiClip>();
                if (clip == null)
                {
                    clip = current.gameObject.AddComponent<MainDisplayUiClip>();
                    clip.hideFlags = HideFlags.DontSave;
                }
                clip.RefreshClip();
                return;
            }
        }

        private void OnEnable()
        {
            RefreshClip();
        }

        private void LateUpdate()
        {
            // ScreenPosCanvasLayoutApplier runs at order 150; sample its final transforms.
            RefreshClip();
        }

        private void OnDisable()
        {
            RestorePadding();
        }

        /// <summary>Updates the clipping edge from real display transforms, including offsets and mirrored scale.</summary>
        internal void RefreshClip()
        {
            if (_main == null)
            {
                _main = transform as RectTransform;
            }
            if (_main == null)
            {
                return;
            }
            if (_main.parent == null)
            {
                _sub = null;
                RestorePadding();
                return;
            }
            if (_sub == null || _sub.parent != _main.parent)
            {
                _sub = _main.parent.Find(SubDisplayName) as RectTransform;
            }
            if (_sub == null)
            {
                RestorePadding();
                return;
            }
            if (_mask == null)
            {
                _mask = GetComponent<RectMask2D>();
                if (_mask == null)
                {
                    _mask = gameObject.AddComponent<RectMask2D>();
                    _mask.hideFlags = HideFlags.DontSave;
                }
                _originalPadding = _mask.padding;
            }

            var padding = _originalPadding;
            if (_sub.gameObject.activeInHierarchy && !Mathf.Approximately(_sub.lossyScale.y, 0f) &&
                !Mathf.Approximately(_sub.lossyScale.x, 0f))
            {
                _sub.GetWorldCorners(_corners);
                var worldToMain = _main.worldToLocalMatrix;
                var lowerEdge = float.PositiveInfinity;
                foreach (var corner in _corners)
                {
                    lowerEdge = Mathf.Min(lowerEdge, worldToMain.MultiplyPoint3x4(corner).y);
                }
                var rect = _main.rect;
                padding.w = Mathf.Max(padding.w, Mathf.Clamp(rect.yMax - lowerEdge, 0f, rect.height));
            }
            if (_mask.padding != padding)
            {
                _mask.padding = padding;
            }
        }

        /// <summary>Restores authored padding when the sibling display is absent or clipping is disabled.</summary>
        private void RestorePadding()
        {
            if (_mask != null && _mask.padding != _originalPadding)
            {
                _mask.padding = _originalPadding;
            }
        }
    }
}
