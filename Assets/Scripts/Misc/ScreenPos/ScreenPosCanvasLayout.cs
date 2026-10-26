#nullable enable
using UnityEngine;

namespace MajdataPlay
{
    /// <summary>
    /// Describes where the main display, the sub display and the covers between them are placed on a root canvas.
    /// </summary>
    /// <remarks>
    /// Every position and rectangle is expressed in canvas units relative to the bottom-center of the root canvas:
    /// x grows to the right from the horizontal center and y grows upwards from the bottom edge.
    /// </remarks>
    public readonly struct ScreenPosCanvasLayout
    {
        /// <summary>
        /// The width of the main display in canvas units.
        /// </summary>
        const float MainDisplayWidth = 1080f;

        /// <summary>
        /// The height of the main display in canvas units.
        /// </summary>
        const float MainDisplayHeight = 1080f;

        /// <summary>
        /// The <c>anchoredPosition.y</c> of <c>Main_Display</c> in <c>CanvasTemplate</c>, used while the display transform is disabled.
        /// </summary>
        const float MainDisplayOriginalPosY = 540f;

        /// <summary>
        /// The <c>anchoredPosition.y</c> of <c>Main_Display</c> when <c>MainScreenOffset</c> is zero.
        /// </summary>
        const float MainDisplayZeroOffsetPosY = 810f;

        /// <summary>
        /// The canvas units the main display moves per <c>MainScreenOffset</c> unit.
        /// </summary>
        const float MainDisplayOffsetStep = 270f;

        /// <summary>
        /// The height of the sub display in canvas units.
        /// </summary>
        const float SubDisplayHeight = 450f;

        /// <summary>
        /// The <c>anchoredPosition.y</c> of <c>Sub_Display</c> in <c>CanvasTemplate</c>, relative to the canvas center.
        /// </summary>
        const float SubDisplayOriginalPosY = 735f;

        /// <summary>
        /// The canvas units the sub display moves per <c>SubDisplayOffset</c> unit.
        /// </summary>
        const float SubDisplayOffsetStep = 100f;

        /// <summary>
        /// Gets the reference resolution that the canvas scaler must use to apply the main display scale.
        /// </summary>
        public Vector2 ReferenceResolution { get; }

        /// <summary>
        /// Gets the size of the root canvas, in canvas units, after <see cref="ReferenceResolution"/> is applied.
        /// </summary>
        public Vector2 CanvasSize { get; }

        /// <summary>
        /// Gets the vertical center of the screen, in canvas units of the unmodified reference resolution.
        /// </summary>
        public float ScreenCenterY { get; }

        /// <summary>
        /// Gets the pivot position of the main display.
        /// </summary>
        public Vector2 MainDisplayPosition { get; }

        /// <summary>
        /// Gets the pivot position of the sub display.
        /// </summary>
        public Vector2 SubDisplayPosition { get; }

        /// <summary>
        /// Gets the uniform local scale of the sub display.
        /// </summary>
        public float SubDisplayScale { get; }

        /// <summary>
        /// Gets the area between the top of the main display and the bottom of the sub display.
        /// </summary>
        public Rect SubCover { get; }

        /// <summary>
        /// Gets the area between the bottom of the canvas and the bottom of the main display.
        /// </summary>
        public Rect SubCoverBottom { get; }

        /// <summary>
        /// Gets the area between the top of the sub display and the top of the canvas.
        /// </summary>
        public Rect SubCoverTop { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScreenPosCanvasLayout"/> struct.
        /// </summary>
        /// <param name="referenceResolution">The reference resolution the canvas scaler must use.</param>
        /// <param name="canvasSize">The size of the root canvas after the reference resolution is applied.</param>
        /// <param name="screenCenterY">The vertical screen center in canvas units of the unmodified reference resolution.</param>
        /// <param name="mainDisplayPosition">The pivot position of the main display.</param>
        /// <param name="subDisplayPosition">The pivot position of the sub display.</param>
        /// <param name="subDisplayScale">The uniform local scale of the sub display.</param>
        /// <param name="subCover">The area between the main display and the sub display.</param>
        /// <param name="subCoverBottom">The area below the main display.</param>
        /// <param name="subCoverTop">The area above the sub display.</param>
        public ScreenPosCanvasLayout(Vector2 referenceResolution,
                                     Vector2 canvasSize,
                                     float screenCenterY,
                                     Vector2 mainDisplayPosition,
                                     Vector2 subDisplayPosition,
                                     float subDisplayScale,
                                     Rect subCover,
                                     Rect subCoverBottom,
                                     Rect subCoverTop)
        {
            ReferenceResolution = referenceResolution;
            CanvasSize = canvasSize;
            ScreenCenterY = screenCenterY;
            MainDisplayPosition = mainDisplayPosition;
            SubDisplayPosition = subDisplayPosition;
            SubDisplayScale = subDisplayScale;
            SubCover = subCover;
            SubCoverBottom = subCoverBottom;
            SubCoverTop = subCoverTop;
        }

        /// <summary>
        /// Calculates the layout for a canvas scaler using <c>ScreenMatchMode.Expand</c>.
        /// </summary>
        /// <param name="input">The screen size, base reference resolution and display settings to use.</param>
        /// <returns>The calculated layout.</returns>
        /// <remarks>
        /// The main display is scaled by shrinking the reference resolution and moved so that the scale is anchored at the screen center.
        /// A non-positive main display scale is treated as no scale.
        /// </remarks>
        public static ScreenPosCanvasLayout Calculate(in ScreenPosCanvasLayoutInput input)
        {
            var baseReferenceResolution = input.BaseReferenceResolution;
            var baseScaleFactor = Mathf.Min(
                input.ScreenWidth / baseReferenceResolution.x,
                input.ScreenHeight / baseReferenceResolution.y);
            var baseCanvasSize = new Vector2(
                input.ScreenWidth / baseScaleFactor,
                input.ScreenHeight / baseScaleFactor);
            var screenCenterY = baseCanvasSize.y / 2f;

            var mainScale = 1f;
            var mainPosY = MainDisplayOriginalPosY;
            var subPosY = SubDisplayOriginalPosY;
            var subScale = 1f;
            if (input.IsTransformEnabled)
            {
                if (input.MainScreenScale > 0f)
                {
                    mainScale = input.MainScreenScale;
                }
                mainPosY = MainDisplayZeroOffsetPosY -
                           (input.MainScreenOffset * MainDisplayOffsetStep) +
                           (screenCenterY * ((1f / mainScale) - 1f));
                subPosY = SubDisplayOriginalPosY + (input.SubDisplayOffset * SubDisplayOffsetStep);
                subScale = input.SubDisplayScale;
            }

            var referenceResolution = baseReferenceResolution / mainScale;
            var canvasSize = baseCanvasSize / mainScale;

            var mainLeft = -MainDisplayWidth / 2f;
            var mainRight = MainDisplayWidth / 2f;
            var mainBottom = mainPosY - (MainDisplayHeight / 2f);
            var mainTop = mainPosY + (MainDisplayHeight / 2f);

            // Sub_Display is anchored at the canvas center, so its offset is measured from there.
            var subCenterY = (canvasSize.y / 2f) + subPosY;
            var subHalfHeight = SubDisplayHeight * Mathf.Abs(subScale) / 2f;
            var subBottom = subCenterY - subHalfHeight;
            var subTop = subCenterY + subHalfHeight;

            return new ScreenPosCanvasLayout(
                referenceResolution,
                canvasSize,
                screenCenterY,
                new Vector2(0f, mainPosY),
                new Vector2(0f, subCenterY),
                subScale,
                CreateArea(mainLeft, mainRight, mainTop, subBottom),
                CreateArea(mainLeft, mainRight, 0f, mainBottom),
                CreateArea(mainLeft, mainRight, subTop, canvasSize.y));
        }

        /// <summary>
        /// Converts a pivot position in layout space into the <c>RectTransform.anchoredPosition</c> of a direct child of the root canvas.
        /// </summary>
        /// <param name="pivotPosition">The pivot position in layout space.</param>
        /// <param name="anchorMin">The <c>anchorMin</c> of the child.</param>
        /// <param name="anchorMax">The <c>anchorMax</c> of the child.</param>
        /// <param name="pivot">The <c>pivot</c> of the child.</param>
        /// <param name="canvasSize">The size of the root canvas.</param>
        /// <returns>The anchored position that places the pivot of the child at <paramref name="pivotPosition"/>.</returns>
        public static Vector2 ToAnchoredPosition(Vector2 pivotPosition,
                                                 Vector2 anchorMin,
                                                 Vector2 anchorMax,
                                                 Vector2 pivot,
                                                 Vector2 canvasSize)
        {
            var anchorReferenceX = (Mathf.Lerp(anchorMin.x, anchorMax.x, pivot.x) - 0.5f) * canvasSize.x;
            var anchorReferenceY = Mathf.Lerp(anchorMin.y, anchorMax.y, pivot.y) * canvasSize.y;
            return new Vector2(pivotPosition.x - anchorReferenceX, pivotPosition.y - anchorReferenceY);
        }

        /// <summary>
        /// Gets the pivot position that makes a scaled rectangle cover an area exactly.
        /// </summary>
        /// <param name="area">The area to cover, in layout space.</param>
        /// <param name="pivot">The <c>pivot</c> of the rectangle.</param>
        /// <param name="localScale">The local scale of the rectangle; a negative component mirrors the pivot on that axis.</param>
        /// <returns>The pivot position in layout space.</returns>
        public static Vector2 GetCoverPivotPosition(Rect area, Vector2 pivot, Vector2 localScale)
        {
            var pivotX = localScale.x > 0f ? pivot.x : 1f - pivot.x;
            var pivotY = localScale.y > 0f ? pivot.y : 1f - pivot.y;
            return new Vector2(area.xMin + (area.width * pivotX), area.yMin + (area.height * pivotY));
        }

        /// <summary>
        /// Creates an area from its edges.
        /// </summary>
        /// <param name="left">The left edge.</param>
        /// <param name="right">The right edge.</param>
        /// <param name="bottom">The bottom edge.</param>
        /// <param name="top">The top edge.</param>
        /// <returns>The area; an inverted area collapses to zero size at its left or bottom edge.</returns>
        static Rect CreateArea(float left, float right, float bottom, float top)
        {
            return Rect.MinMaxRect(left, bottom, Mathf.Max(left, right), Mathf.Max(bottom, top));
        }
    }
}
