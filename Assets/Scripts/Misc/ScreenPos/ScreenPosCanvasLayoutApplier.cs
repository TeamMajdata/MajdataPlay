#nullable enable
using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay
{
    /// <summary>
    /// Applies the layout calculated by <see cref="ScreenPosCanvasLayoutProvider"/> to the display parts of this root canvas.
    /// </summary>
    /// <remarks>
    /// The parts are looked up by name among the direct children of this canvas. Parts that do not exist are skipped.
    /// </remarks>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScreenPosCanvasLayoutApplier : MonoBehaviour
    {
        /// <summary>
        /// The name of the main display child.
        /// </summary>
        private const string MainDisplayName = "Main_Display";

        /// <summary>
        /// The name of the sub display child.
        /// </summary>
        private const string SubDisplayName = "Sub_Display";

        /// <summary>
        /// The name of the cover between the main display and the sub display.
        /// </summary>
        private const string SubCoverName = "Sub_Cover";

        /// <summary>
        /// The name of the cover below the main display.
        /// </summary>
        private const string SubCoverBottomName = "Sub_Cover_Bottom";

        /// <summary>
        /// The name of the cover above the sub display.
        /// </summary>
        private const string SubCoverTopName = "Sub_Cover_Top";

        /// <summary>
        /// The version used before any layout has been applied.
        /// </summary>
        private const int NotAppliedVersion = -1;

        /// <summary>
        /// Additional direct children of this canvas that are placed exactly like the main display.
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("MainDisplayFollowers")]
        private RectTransform[] _mainDisplayFollowers = Array.Empty<RectTransform>();

        /// <summary>
        /// The canvas scaler whose reference resolution applies the main display scale.
        /// </summary>
        private CanvasScaler? _canvasScaler;

        /// <summary>
        /// The main display, or <see langword="null"/> if this canvas has none.
        /// </summary>
        private RectTransform? _mainDisplay;

        /// <summary>
        /// The sub display, or <see langword="null"/> if this canvas has none.
        /// </summary>
        private RectTransform? _subDisplay;

        /// <summary>
        /// The cover between the main display and the sub display, or <see langword="null"/> if this canvas has none.
        /// </summary>
        private RectTransform? _subCover;

        /// <summary>
        /// The cover below the main display, or <see langword="null"/> if this canvas has none.
        /// </summary>
        private RectTransform? _subCoverBottom;

        /// <summary>
        /// The cover above the sub display, or <see langword="null"/> if this canvas has none.
        /// </summary>
        private RectTransform? _subCoverTop;

        /// <summary>
        /// The <see cref="ScreenPosCanvasLayoutProvider.Version"/> that was last applied.
        /// </summary>
        private int _appliedVersion = NotAppliedVersion;

        private void Awake()
        {
            var root = transform;
            _canvasScaler = GetComponent<CanvasScaler>();
            _mainDisplay = FindPart(root, MainDisplayName);
            _subDisplay = FindPart(root, SubDisplayName);
            _subCover = FindPart(root, SubCoverName);
            _subCoverBottom = FindPart(root, SubCoverBottomName);
            _subCoverTop = FindPart(root, SubCoverTopName);
        }

        private void LateUpdate()
        {
            var provider = Majdata<ScreenPosCanvasLayoutProvider>.Instance;
            if (provider == null || !provider.HasLayout)
            {
                return;
            }
            var version = provider.Version;
            if (version == _appliedVersion)
            {
                return;
            }

            Apply(provider.Layout);
            _appliedVersion = version;
        }

        /// <summary>
        /// Applies a layout to every part of this canvas.
        /// </summary>
        /// <param name="layout">The layout to apply.</param>
        private void Apply(in ScreenPosCanvasLayout layout)
        {
            if (_canvasScaler != null)
            {
                _canvasScaler.referenceResolution = layout.ReferenceResolution;
            }

            var canvasSize = layout.CanvasSize;
            ApplyDisplay(_mainDisplay, layout.MainDisplayPosition, 1f, canvasSize);
            foreach (var follower in _mainDisplayFollowers)
            {
                ApplyDisplay(follower, layout.MainDisplayPosition, 1f, canvasSize);
            }
            ApplyDisplay(_subDisplay, layout.SubDisplayPosition, layout.SubDisplayScale, canvasSize);
            ApplyCover(_subCover, layout.SubCover, canvasSize);
            ApplyCover(_subCoverBottom, layout.SubCoverBottom, canvasSize);
            ApplyCover(_subCoverTop, layout.SubCoverTop, canvasSize);
        }

        /// <summary>
        /// Finds a direct child by name.
        /// </summary>
        /// <param name="root">The canvas transform to search.</param>
        /// <param name="name">The name of the child.</param>
        /// <returns>The <see cref="RectTransform"/> of the child, or <see langword="null"/> if it does not exist.</returns>
        private static RectTransform? FindPart(Transform root, string name)
        {
            return root.Find(name) as RectTransform;
        }

        /// <summary>
        /// Moves and scales a display so that its pivot is at a layout position.
        /// </summary>
        /// <param name="display">The display to change; ignored when <see langword="null"/>.</param>
        /// <param name="pivotPosition">The pivot position in layout space.</param>
        /// <param name="scale">The uniform local scale to apply.</param>
        /// <param name="canvasSize">The size of the root canvas.</param>
        private static void ApplyDisplay(RectTransform? display, Vector2 pivotPosition, float scale, Vector2 canvasSize)
        {
            if (display == null)
            {
                return;
            }
            display.anchoredPosition = ScreenPosCanvasLayout.ToAnchoredPosition(
                pivotPosition,
                display.anchorMin,
                display.anchorMax,
                display.pivot,
                canvasSize);
            display.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// Resizes and moves a cover so that it fills a layout area, compensating for its own local scale.
        /// </summary>
        /// <param name="cover">The cover to change; ignored when <see langword="null"/> or scaled to zero.</param>
        /// <param name="area">The area to fill, in layout space.</param>
        /// <param name="canvasSize">The size of the root canvas.</param>
        private static void ApplyCover(RectTransform? cover, Rect area, Vector2 canvasSize)
        {
            if (cover == null)
            {
                return;
            }
            var scale = cover.localScale;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f))
            {
                return;
            }

            // sizeDelta differs from the real size under stretched anchors, so resize through the current anchors.
            cover.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, area.width / Mathf.Abs(scale.x));
            cover.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, area.height / Mathf.Abs(scale.y));

            var pivotPosition = ScreenPosCanvasLayout.GetCoverPivotPosition(area, cover.pivot, scale);
            cover.anchoredPosition = ScreenPosCanvasLayout.ToAnchoredPosition(
                pivotPosition,
                cover.anchorMin,
                cover.anchorMax,
                cover.pivot,
                canvasSize);
        }
    }
}
