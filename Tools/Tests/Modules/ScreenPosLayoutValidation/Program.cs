#nullable enable
using System;
using MajdataPlay;
using UnityEngine;

/// <summary>
/// Validates the screen position canvas layout calculated for GlobalCanvas and applied to every scene canvas.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The tolerance used when comparing layout values.
    /// </summary>
    const float Tolerance = 0.01f;

    /// <summary>
    /// The reference resolution of CanvasTemplate.
    /// </summary>
    static readonly Vector2 s_referenceResolution = new Vector2(1080f, 1920f);

    /// <summary>
    /// The number of assertions that have passed.
    /// </summary>
    static int s_assertions;

    /// <summary>
    /// Runs every validation and prints a summary line.
    /// </summary>
    /// <returns>0 when every check passes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a check fails.</exception>
    static int Main()
    {
        VerifyDisabledTransformMatchesTemplate();
        VerifyDefaultTransformMatchesTemplate();
        VerifyMainScaleAnchorsAtScreenCenter();
        VerifyTallScreenCoversGaps();
        VerifyWideScreenKeepsVerticalLayout();
        VerifySubDisplayTransform();
        VerifyCollapsedCovers();
        VerifyNonPositiveMainScaleIsIgnored();
        VerifyAnchoredPositionConversion();
        VerifyCoverPivotPosition();
        VerifyInputEquality();
        s_assertions += CornerUpdaterValidation.Run();
        Console.WriteLine($"SCREEN_POS_LAYOUT_VALIDATION_PASSED ({s_assertions} assertions)");
        return 0;
    }

    /// <summary>
    /// A disabled transform reproduces the positions serialized in CanvasTemplate.
    /// </summary>
    static void VerifyDisabledTransformMatchesTemplate()
    {
        var layout = Calculate(1080, 1920, false, 0f, 2f, 3f, 4f);
        CheckVector(layout.ReferenceResolution, 1080f, 1920f, "disabled: reference resolution is unchanged");
        CheckVector(layout.CanvasSize, 1080f, 1920f, "disabled: canvas size");
        CheckFloat(layout.ScreenCenterY, 960f, "disabled: screen center");
        CheckVector(layout.MainDisplayPosition, 0f, 540f, "disabled: Main_Display at its template position");
        CheckVector(layout.SubDisplayPosition, 0f, 1695f, "disabled: Sub_Display at 735 above the canvas center");
        CheckFloat(layout.SubDisplayScale, 1f, "disabled: Sub_Display scale ignores settings");
        // Sub_Cover in CanvasTemplate: anchoredPosition.y 1275, height 390, width 1080.
        CheckRect(layout.SubCover, -540f, 1080f, 1080f, 390f, "disabled: Sub_Cover matches the template");
        CheckRect(layout.SubCoverBottom, -540f, 0f, 1080f, 0f, "disabled: Sub_Cover_Bottom matches the template");
        CheckRect(layout.SubCoverTop, -540f, 1920f, 1080f, 0f, "disabled: Sub_Cover_Top is empty on a 9:16 screen");
    }

    /// <summary>
    /// The default enabled settings produce the same layout as the template.
    /// </summary>
    static void VerifyDefaultTransformMatchesTemplate()
    {
        var layout = Calculate(1080, 1920, true, 1f, 1f, 0f, 1f);
        CheckVector(layout.ReferenceResolution, 1080f, 1920f, "default: reference resolution");
        CheckVector(layout.MainDisplayPosition, 0f, 540f, "default: offset 1 places Main_Display at 540");
        CheckVector(layout.SubDisplayPosition, 0f, 1695f, "default: Sub_Display position");
        CheckRect(layout.SubCover, -540f, 1080f, 1080f, 390f, "default: Sub_Cover");
    }

    /// <summary>
    /// Scaling the main display keeps its distance to the screen center proportional.
    /// </summary>
    static void VerifyMainScaleAnchorsAtScreenCenter()
    {
        var layout = Calculate(1080, 1920, true, 1f, 0.5f, 0f, 1f);
        CheckVector(layout.ReferenceResolution, 2160f, 3840f, "scale 0.5: reference resolution doubles");
        CheckVector(layout.CanvasSize, 2160f, 3840f, "scale 0.5: canvas size doubles");
        CheckVector(layout.MainDisplayPosition, 0f, 1500f, "scale 0.5: Main_Display position");
        // On screen the canvas is drawn at half size: 1500 * 0.5 = 960 - (960 - 540) * 0.5.
        CheckFloat(layout.MainDisplayPosition.y * 0.5f, 960f - (420f * 0.5f), "scale 0.5: Main_Display keeps its distance to the screen center");
        CheckVector(layout.SubDisplayPosition, 0f, 2655f, "scale 0.5: Sub_Display follows the canvas center");
        CheckRect(layout.SubCover, -540f, 2040f, 1080f, 390f, "scale 0.5: Sub_Cover");
        CheckRect(layout.SubCoverBottom, -540f, 0f, 1080f, 960f, "scale 0.5: Sub_Cover_Bottom");
        CheckRect(layout.SubCoverTop, -540f, 2880f, 1080f, 960f, "scale 0.5: Sub_Cover_Top");
    }

    /// <summary>
    /// On screens taller than 9:16 the covers fill the extra height around the displays.
    /// </summary>
    static void VerifyTallScreenCoversGaps()
    {
        var layout = Calculate(1080, 2400, false, 0f, 1f, 0f, 1f);
        CheckVector(layout.CanvasSize, 1080f, 2400f, "tall: canvas size");
        CheckFloat(layout.ScreenCenterY, 1200f, "tall: screen center");
        CheckVector(layout.SubDisplayPosition, 0f, 1935f, "tall: Sub_Display follows the canvas center");
        CheckRect(layout.SubCover, -540f, 1080f, 1080f, 630f, "tall: Sub_Cover reaches the Sub_Display");
        CheckRect(layout.SubCoverTop, -540f, 2160f, 1080f, 240f, "tall: Sub_Cover_Top fills the top gap");
    }

    /// <summary>
    /// On wide screens the expand mode keeps the vertical layout and widens the canvas.
    /// </summary>
    static void VerifyWideScreenKeepsVerticalLayout()
    {
        var layout = Calculate(1920, 1080, true, 1f, 1f, 0f, 1f);
        CheckVector(layout.CanvasSize, 1920f / 0.5625f, 1920f, "wide: canvas size");
        CheckVector(layout.MainDisplayPosition, 0f, 540f, "wide: Main_Display position");
        CheckRect(layout.SubCover, -540f, 1080f, 1080f, 390f, "wide: covers keep the main display width");
    }

    /// <summary>
    /// The sub display offset and scale move and resize the sub display and its covers.
    /// </summary>
    static void VerifySubDisplayTransform()
    {
        var layout = Calculate(1080, 1920, true, 1f, 1f, 1f, 0.8f);
        CheckVector(layout.SubDisplayPosition, 0f, 1795f, "sub: offset 1 moves Sub_Display by 100");
        CheckFloat(layout.SubDisplayScale, 0.8f, "sub: scale is forwarded");
        CheckRect(layout.SubCover, -540f, 1080f, 1080f, 535f, "sub: Sub_Cover ends at the scaled Sub_Display");
        CheckRect(layout.SubCoverTop, -540f, 1975f, 1080f, 0f, "sub: Sub_Cover_Top collapses when Sub_Display leaves the canvas");
    }

    /// <summary>
    /// Covers never get a negative size.
    /// </summary>
    static void VerifyCollapsedCovers()
    {
        var layout = Calculate(1080, 1920, true, 3f, 1f, -10f, 1f);
        CheckVector(layout.MainDisplayPosition, 0f, 0f, "collapsed: offset 3 moves Main_Display to the bottom edge");
        CheckRect(layout.SubCoverBottom, -540f, 0f, 1080f, 0f, "collapsed: Sub_Cover_Bottom is empty below the canvas");
        CheckRect(layout.SubCover, -540f, 540f, 1080f, 0f, "collapsed: Sub_Cover is empty when the displays overlap");
    }

    /// <summary>
    /// A non-positive main display scale behaves like no scale.
    /// </summary>
    static void VerifyNonPositiveMainScaleIsIgnored()
    {
        var layout = Calculate(1080, 1920, true, 0.5f, 0f, 0f, 1f);
        CheckVector(layout.ReferenceResolution, 1080f, 1920f, "scale 0: reference resolution is unchanged");
        CheckVector(layout.MainDisplayPosition, 0f, 675f, "scale 0: only the offset is applied");
    }

    /// <summary>
    /// Layout positions convert to anchored positions for the anchors used by CanvasTemplate and for stretched anchors.
    /// </summary>
    static void VerifyAnchoredPositionConversion()
    {
        var canvasSize = new Vector2(1080f, 1920f);
        var half = new Vector2(0.5f, 0.5f);
        var bottomCenter = new Vector2(0.5f, 0f);
        var bottomLeft = new Vector2(0f, 0f);
        var topRight = new Vector2(1f, 1f);

        var main = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(0f, 540f), bottomCenter, bottomCenter, half, canvasSize);
        CheckVector(main, 0f, 540f, "anchors: bottom-center anchors keep the layout position");

        var sub = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(0f, 1695f), half, half, half, canvasSize);
        CheckVector(sub, 0f, 735f, "anchors: center anchors are relative to the canvas center");

        var stretched = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(0f, 1695f), bottomLeft, topRight, half, canvasSize);
        CheckVector(stretched, 0f, 735f, "anchors: stretched anchors use the pivot-interpolated reference");

        var corner = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(-540f, 0f), bottomLeft, bottomLeft, bottomLeft, canvasSize);
        CheckVector(corner, 0f, 0f, "anchors: bottom-left anchors are relative to the canvas corner");
    }

    /// <summary>
    /// The cover pivot position respects the pivot and mirrors it for negative scales.
    /// </summary>
    static void VerifyCoverPivotPosition()
    {
        var area = Rect.MinMaxRect(-540f, 1080f, 540f, 1470f);
        var centered = ScreenPosCanvasLayout.GetCoverPivotPosition(area, new Vector2(0.5f, 0.5f), new Vector2(1f, 1f));
        CheckVector(centered, 0f, 1275f, "cover: centered pivot is the area center");

        var mirrored = ScreenPosCanvasLayout.GetCoverPivotPosition(area, new Vector2(0.25f, 0f), new Vector2(-1f, 1f));
        CheckVector(mirrored, 270f, 1080f, "cover: negative x scale mirrors the pivot");
    }

    /// <summary>
    /// Inputs compare by value so the provider only recalculates on change.
    /// </summary>
    static void VerifyInputEquality()
    {
        var a = CreateInput(1080, 1920, true, 1f, 1f, 0f, 1f);
        var b = CreateInput(1080, 1920, true, 1f, 1f, 0f, 1f);
        var c = CreateInput(1080, 1920, true, 1f, 1f, 0f, 0.9f);
        var d = CreateInput(1080, 2400, true, 1f, 1f, 0f, 1f);
        Check(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "input: identical inputs are equal");
        Check(!a.Equals(c), "input: a changed sub display scale is detected");
        Check(!a.Equals(d), "input: a changed screen size is detected");
        Check(!a.Equals((object)"other"), "input: other types are not equal");
    }

    /// <summary>
    /// Calculates a layout for the CanvasTemplate reference resolution.
    /// </summary>
    /// <param name="screenWidth">The screen width in pixels.</param>
    /// <param name="screenHeight">The screen height in pixels.</param>
    /// <param name="isTransformEnabled">Whether the display transform is enabled.</param>
    /// <param name="mainOffset">The main display offset.</param>
    /// <param name="mainScale">The main display scale.</param>
    /// <param name="subOffset">The sub display offset.</param>
    /// <param name="subScale">The sub display scale.</param>
    /// <returns>The calculated layout.</returns>
    static ScreenPosCanvasLayout Calculate(int screenWidth,
                                           int screenHeight,
                                           bool isTransformEnabled,
                                           float mainOffset,
                                           float mainScale,
                                           float subOffset,
                                           float subScale)
    {
        var input = CreateInput(screenWidth, screenHeight, isTransformEnabled, mainOffset, mainScale, subOffset, subScale);
        return ScreenPosCanvasLayout.Calculate(input);
    }

    /// <summary>
    /// Creates a layout input for the CanvasTemplate reference resolution.
    /// </summary>
    /// <param name="screenWidth">The screen width in pixels.</param>
    /// <param name="screenHeight">The screen height in pixels.</param>
    /// <param name="isTransformEnabled">Whether the display transform is enabled.</param>
    /// <param name="mainOffset">The main display offset.</param>
    /// <param name="mainScale">The main display scale.</param>
    /// <param name="subOffset">The sub display offset.</param>
    /// <param name="subScale">The sub display scale.</param>
    /// <returns>The layout input.</returns>
    static ScreenPosCanvasLayoutInput CreateInput(int screenWidth,
                                                  int screenHeight,
                                                  bool isTransformEnabled,
                                                  float mainOffset,
                                                  float mainScale,
                                                  float subOffset,
                                                  float subScale)
    {
        return new ScreenPosCanvasLayoutInput(
            screenWidth,
            screenHeight,
            s_referenceResolution,
            isTransformEnabled,
            mainOffset,
            mainScale,
            subOffset,
            subScale);
    }

    /// <summary>
    /// Fails when a condition is false.
    /// </summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">The description of the check.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="condition"/> is false.</exception>
    static void Check(bool condition, string message)
    {
        s_assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Fails when a value differs from the expected value.
    /// </summary>
    /// <param name="actual">The actual value.</param>
    /// <param name="expected">The expected value.</param>
    /// <param name="message">The description of the check.</param>
    /// <exception cref="InvalidOperationException">Thrown when the values differ.</exception>
    static void CheckFloat(float actual, float expected, string message)
    {
        Check(MathF.Abs(actual - expected) <= Tolerance, $"{message}: expected {expected}, got {actual}");
    }

    /// <summary>
    /// Fails when a vector differs from the expected components.
    /// </summary>
    /// <param name="actual">The actual vector.</param>
    /// <param name="x">The expected x component.</param>
    /// <param name="y">The expected y component.</param>
    /// <param name="message">The description of the check.</param>
    /// <exception cref="InvalidOperationException">Thrown when a component differs.</exception>
    static void CheckVector(Vector2 actual, float x, float y, string message)
    {
        var matches = MathF.Abs(actual.x - x) <= Tolerance && MathF.Abs(actual.y - y) <= Tolerance;
        Check(matches, $"{message}: expected ({x}, {y}), got {actual}");
    }

    /// <summary>
    /// Fails when a rectangle differs from the expected edges and size.
    /// </summary>
    /// <param name="actual">The actual rectangle.</param>
    /// <param name="x">The expected left edge.</param>
    /// <param name="y">The expected bottom edge.</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    /// <param name="message">The description of the check.</param>
    /// <exception cref="InvalidOperationException">Thrown when a value differs.</exception>
    static void CheckRect(Rect actual, float x, float y, float width, float height, string message)
    {
        var matches = MathF.Abs(actual.xMin - x) <= Tolerance &&
                      MathF.Abs(actual.yMin - y) <= Tolerance &&
                      MathF.Abs(actual.width - width) <= Tolerance &&
                      MathF.Abs(actual.height - height) <= Tolerance;
        Check(matches, $"{message}: expected (x:{x}, y:{y}, width:{width}, height:{height}), got {actual}");
    }
}
