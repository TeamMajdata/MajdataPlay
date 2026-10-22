using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.ScreenPos
{
    /// <summary>Covers the pure layout math of <see cref="ScreenPosCanvasLayout"/> and its input key.</summary>
    public class ScreenPosCanvasLayoutTests
    {
        /// <summary>The reference resolution of the game canvases.</summary>
        private static readonly Vector2 s_reference = new Vector2(1080f, 1920f);

        /// <summary>Creates a layout input for a portrait 1080x1920 screen.</summary>
        /// <param name="enabled">Whether the display transform is enabled.</param>
        /// <param name="mainOffset">The main display offset.</param>
        /// <param name="mainScale">The main display scale.</param>
        /// <param name="subOffset">The sub display offset.</param>
        /// <param name="subScale">The sub display scale.</param>
        /// <param name="width">The screen width.</param>
        /// <param name="height">The screen height.</param>
        /// <returns>The layout input.</returns>
        private static ScreenPosCanvasLayoutInput CreateInput(bool enabled,
                                                              float mainOffset = 1f,
                                                              float mainScale = 1f,
                                                              float subOffset = 0f,
                                                              float subScale = 1f,
                                                              int width = 1080,
                                                              int height = 1920)
        {
            return new ScreenPosCanvasLayoutInput(width, height, s_reference, enabled, mainOffset, mainScale, subOffset, subScale);
        }

        /// <summary>Asserts that a rectangle has the expected edges.</summary>
        /// <param name="rect">The rectangle to check.</param>
        /// <param name="xMin">The expected left edge.</param>
        /// <param name="yMin">The expected bottom edge.</param>
        /// <param name="xMax">The expected right edge.</param>
        /// <param name="yMax">The expected top edge.</param>
        private static void AssertRect(Rect rect, float xMin, float yMin, float xMax, float yMax)
        {
            Assert.That(rect.xMin, Is.EqualTo(xMin).Within(1e-3f), "xMin");
            Assert.That(rect.yMin, Is.EqualTo(yMin).Within(1e-3f), "yMin");
            Assert.That(rect.xMax, Is.EqualTo(xMax).Within(1e-3f), "xMax");
            Assert.That(rect.yMax, Is.EqualTo(yMax).Within(1e-3f), "yMax");
        }

        /// <summary>Checks the template layout used while the display transform is disabled.</summary>
        [Test]
        public void Calculate_TransformDisabled_UsesTemplatePositions()
        {
            var input = CreateInput(false, mainOffset: 3f, mainScale: 0.5f, subOffset: 2f, subScale: 3f);

            var layout = ScreenPosCanvasLayout.Calculate(input);

            Assert.That(layout.ReferenceResolution, Is.EqualTo(s_reference));
            Assert.That(layout.CanvasSize, Is.EqualTo(s_reference));
            Assert.That(layout.ScreenCenterY, Is.EqualTo(960f));
            Assert.That(layout.MainDisplayPosition, Is.EqualTo(new Vector2(0f, 540f)));
            Assert.That(layout.SubDisplayPosition, Is.EqualTo(new Vector2(0f, 1695f)));
            Assert.That(layout.SubDisplayScale, Is.EqualTo(1f));
            AssertRect(layout.SubCover, -540f, 1080f, 540f, 1470f);
            AssertRect(layout.SubCoverBottom, -540f, 0f, 540f, 0f);
            AssertRect(layout.SubCoverTop, -540f, 1920f, 540f, 1920f);
        }

        /// <summary>Checks that the default transform settings reproduce the template position of the main display.</summary>
        [Test]
        public void Calculate_DefaultTransform_MatchesTemplate()
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(true));

            Assert.That(layout.MainDisplayPosition.y, Is.EqualTo(540f).Within(1e-3f));
            Assert.That(layout.SubDisplayPosition.y, Is.EqualTo(1695f).Within(1e-3f));
        }

        /// <summary>Checks that shrinking the main display enlarges the reference resolution and anchors the scale at the screen center.</summary>
        [Test]
        public void Calculate_MainScale_ScalesReferenceResolution()
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(true, mainOffset: 0f, mainScale: 0.5f, subOffset: 2f, subScale: 2f));

            Assert.That(layout.ReferenceResolution, Is.EqualTo(new Vector2(2160f, 3840f)));
            Assert.That(layout.CanvasSize, Is.EqualTo(new Vector2(2160f, 3840f)));
            Assert.That(layout.MainDisplayPosition.y, Is.EqualTo(1770f).Within(1e-3f));
            Assert.That(layout.SubDisplayPosition.y, Is.EqualTo(2855f).Within(1e-3f));
            Assert.That(layout.SubDisplayScale, Is.EqualTo(2f));
            AssertRect(layout.SubCoverBottom, -540f, 0f, 540f, 1230f);
            AssertRect(layout.SubCover, -540f, 2310f, 540f, 2405f);
            AssertRect(layout.SubCoverTop, -540f, 3305f, 540f, 3840f);
        }

        /// <summary>Checks that a non-positive main scale is treated as no scale.</summary>
        /// <param name="scale">The invalid scale.</param>
        [TestCase(0f)]
        [TestCase(-2f)]
        public void Calculate_NonPositiveMainScale_IsIgnored(float scale)
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(true, mainScale: scale));

            Assert.That(layout.ReferenceResolution, Is.EqualTo(s_reference));
            Assert.That(layout.MainDisplayPosition.y, Is.EqualTo(540f).Within(1e-3f));
        }

        /// <summary>Checks that a mirrored sub display uses its absolute height for the covers.</summary>
        [Test]
        public void Calculate_NegativeSubScale_UsesAbsoluteHeight()
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(true, subScale: -1f));

            Assert.That(layout.SubDisplayScale, Is.EqualTo(-1f));
            AssertRect(layout.SubCover, -540f, 1080f, 540f, 1470f);
        }

        /// <summary>Checks that an overlapping main display collapses the middle cover instead of inverting it.</summary>
        [Test]
        public void Calculate_Overlap_CollapsesCover()
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(true, mainOffset: -3f));

            Assert.That(layout.MainDisplayPosition.y, Is.EqualTo(1620f).Within(1e-3f));
            Assert.That(layout.SubCover.height, Is.EqualTo(0f));
            Assert.That(layout.SubCover.yMin, Is.EqualTo(2160f).Within(1e-3f));
            Assert.That(layout.SubCoverTop.height, Is.EqualTo(0f));
        }

        /// <summary>Checks that a wide screen expands the canvas width while keeping the reference height.</summary>
        [Test]
        public void Calculate_WideScreen_ExpandsWidth()
        {
            var layout = ScreenPosCanvasLayout.Calculate(CreateInput(false, width: 2160, height: 1920));

            Assert.That(layout.CanvasSize, Is.EqualTo(new Vector2(2160f, 1920f)));
            Assert.That(layout.ScreenCenterY, Is.EqualTo(960f));
        }

        /// <summary>Checks the conversion of a layout pivot position into an anchored position.</summary>
        [Test]
        public void ToAnchoredPosition_CompensatesAnchorsAndPivot()
        {
            var canvas = new Vector2(1080f, 1920f);

            var bottomCenter = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(10f, 20f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), canvas);
            var topLeft = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(10f, 20f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), canvas);
            var stretched = ScreenPosCanvasLayout.ToAnchoredPosition(new Vector2(0f, 960f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), canvas);

            Assert.That(bottomCenter, Is.EqualTo(new Vector2(10f, 20f)));
            Assert.That(topLeft, Is.EqualTo(new Vector2(550f, -1900f)));
            Assert.That(stretched, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Checks that mirrored covers use the opposite pivot side.</summary>
        [Test]
        public void GetCoverPivotPosition_MirrorsPivot()
        {
            var area = new Rect(0f, 0f, 100f, 50f);
            var pivot = new Vector2(0.25f, 0.2f);

            Assert.That(ScreenPosCanvasLayout.GetCoverPivotPosition(area, pivot, Vector2.one), Is.EqualTo(new Vector2(25f, 10f)));
            Assert.That(ScreenPosCanvasLayout.GetCoverPivotPosition(area, pivot, new Vector2(-1f, 1f)), Is.EqualTo(new Vector2(75f, 10f)));
            Assert.That(ScreenPosCanvasLayout.GetCoverPivotPosition(area, pivot, new Vector2(1f, -1f)), Is.EqualTo(new Vector2(25f, 40f)));
        }

        /// <summary>Checks that the explicit constructor keeps every value.</summary>
        [Test]
        public void Constructor_StoresValues()
        {
            var layout = new ScreenPosCanvasLayout(Vector2.one, Vector2.right, 3f, Vector2.up, Vector2.down, 4f,
                                                   new Rect(1f, 2f, 3f, 4f), new Rect(5f, 6f, 7f, 8f), new Rect(9f, 10f, 11f, 12f));

            Assert.That(layout.ReferenceResolution, Is.EqualTo(Vector2.one));
            Assert.That(layout.CanvasSize, Is.EqualTo(Vector2.right));
            Assert.That(layout.ScreenCenterY, Is.EqualTo(3f));
            Assert.That(layout.MainDisplayPosition, Is.EqualTo(Vector2.up));
            Assert.That(layout.SubDisplayPosition, Is.EqualTo(Vector2.down));
            Assert.That(layout.SubDisplayScale, Is.EqualTo(4f));
            Assert.That(layout.SubCover, Is.EqualTo(new Rect(1f, 2f, 3f, 4f)));
            Assert.That(layout.SubCoverBottom, Is.EqualTo(new Rect(5f, 6f, 7f, 8f)));
            Assert.That(layout.SubCoverTop, Is.EqualTo(new Rect(9f, 10f, 11f, 12f)));
        }

        /// <summary>Checks the value equality and hash code of layout inputs.</summary>
        [Test]
        public void Input_HasValueEquality()
        {
            var a = CreateInput(true, mainOffset: 0.5f);
            var b = CreateInput(true, mainOffset: 0.5f);
            var c = CreateInput(true, mainOffset: 0.6f);
            var d = CreateInput(false, mainOffset: 0.5f);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.Equals(c), Is.False);
            Assert.That(a.Equals(d), Is.False);
            Assert.That(a.Equals((object)"other"), Is.False);
            Assert.That(a.ScreenWidth, Is.EqualTo(1080));
            Assert.That(a.ScreenHeight, Is.EqualTo(1920));
            Assert.That(a.BaseReferenceResolution, Is.EqualTo(s_reference));
            Assert.That(a.IsTransformEnabled, Is.True);
            Assert.That(a.MainScreenOffset, Is.EqualTo(0.5f));
            Assert.That(a.MainScreenScale, Is.EqualTo(1f));
            Assert.That(a.SubDisplayOffset, Is.EqualTo(0f));
            Assert.That(a.SubDisplayScale, Is.EqualTo(1f));
        }
    }
}
