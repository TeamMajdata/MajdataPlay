using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>
    /// Covers the shared canvas layout behaviours <see cref="ScreenPosCanvasLayoutProvider"/>,
    /// <see cref="ScreenPosCanvasLayoutApplier"/> and <see cref="RectCornerPositionUpdater"/> in Play Mode.
    /// </summary>
    public class ScreenPosBehaviourTests
    {
        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The display settings before the test, restored afterwards.</summary>
        private (bool Transform, float Offset, float Scale, float SubOffset, float SubScale, float CenterY) _savedDisplay;

        /// <summary>The sub screen edge before the test.</summary>
        private Vector4 _savedEdge;

        /// <summary>Captures the display settings and requires a screen size.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            var display = MajEnv.Settings.Display;
            _savedDisplay = (display.MainScreenTransform, display.MainScreenOffset, display.MainScreenScale,
                             display.SubDisplayOffset, display.SubDisplayScale, display.MainScreenCachedScreenCenterY);
            _savedEdge = InputManager.SubScreenEdge;
            display.MainScreenTransform = true;
            display.MainScreenOffset = 1f;
            display.MainScreenScale = 1f;
            display.SubDisplayOffset = 0f;
            display.SubDisplayScale = 1f;
        }

        /// <summary>Restores the display settings and releases the provider slot.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            Majdata<ScreenPosCanvasLayoutProvider>.Free();
            var display = MajEnv.Settings.Display;
            display.MainScreenTransform = _savedDisplay.Transform;
            display.MainScreenOffset = _savedDisplay.Offset;
            display.MainScreenScale = _savedDisplay.Scale;
            display.SubDisplayOffset = _savedDisplay.SubOffset;
            display.SubDisplayScale = _savedDisplay.SubScale;
            display.MainScreenCachedScreenCenterY = _savedDisplay.CenterY;
            InputManager.SubScreenEdge = _savedEdge;
        }

        /// <summary>Creates the layout provider on a canvas with a 1080x1920 scaler.</summary>
        /// <returns>The provider.</returns>
        private ScreenPosCanvasLayoutProvider CreateProvider()
        {
            var root = _objects.Create("GlobalCanvas");
            root.AddComponent<Canvas>();
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            return root.AddComponent<ScreenPosCanvasLayoutProvider>();
        }

        /// <summary>Gets the layout the provider is expected to compute for the current screen and settings.</summary>
        /// <returns>The expected layout.</returns>
        private static ScreenPosCanvasLayout ExpectedLayout()
        {
            var display = MajEnv.Settings.Display;
            return ScreenPosCanvasLayout.Calculate(new ScreenPosCanvasLayoutInput(Screen.width, Screen.height, new Vector2(1080f, 1920f),
                display.MainScreenTransform, display.MainScreenOffset, display.MainScreenScale, display.SubDisplayOffset, display.SubDisplayScale));
        }

        /// <summary>Creates a named rectangle under a parent.</summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="name">The object name.</param>
        /// <returns>The rectangle.</returns>
        private RectTransform CreateRect(Transform parent, string name)
        {
            var gameObject = _objects.Track(new GameObject(name, typeof(RectTransform)));
            gameObject.transform.SetParent(parent, false);
            return (RectTransform)gameObject.transform;
        }

        /// <summary>Checks that the provider calculates a layout once per change of the screen or display settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Provider_RecalculatesOnChange()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            var provider = CreateProvider();
            Assert.That(Majdata<ScreenPosCanvasLayoutProvider>.Instance, Is.SameAs(provider));
            Assert.That(provider.HasLayout, Is.False);

            yield return null;
            Assert.That(provider.HasLayout, Is.True);
            Assert.That(provider.Version, Is.EqualTo(1));
            Assert.That(provider.Layout.MainDisplayPosition, Is.EqualTo(ExpectedLayout().MainDisplayPosition));
            Assert.That(MajEnv.Settings.Display.MainScreenCachedScreenCenterY, Is.EqualTo(provider.Layout.ScreenCenterY));

            yield return null;
            Assert.That(provider.Version, Is.EqualTo(1));

            MajEnv.Settings.Display.MainScreenScale = 0.5f;
            yield return null;
            Assert.That(provider.Version, Is.EqualTo(2));
            Assert.That(provider.Layout.ReferenceResolution, Is.EqualTo(ExpectedLayout().ReferenceResolution));
        }

        /// <summary>Checks that the provider falls back to the default reference resolution without a canvas scaler.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Provider_WithoutScaler_UsesDefaultReference()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            var provider = _objects.Add<ScreenPosCanvasLayoutProvider>();

            yield return null;

            Assert.That(provider.HasLayout, Is.True);
            Assert.That(provider.Layout.ReferenceResolution, Is.EqualTo(ExpectedLayout().ReferenceResolution));
        }

        /// <summary>Checks that the applier positions the displays, followers and covers of its canvas from the shared layout.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Applier_AppliesSharedLayout()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            MajEnv.Settings.Display.MainScreenScale = 0.8f;
            MajEnv.Settings.Display.SubDisplayScale = 1.5f;
            var provider = CreateProvider();
            var canvas = _objects.Create("Canvas");
            canvas.AddComponent<Canvas>();
            var scaler = canvas.AddComponent<CanvasScaler>();
            var main = CreateRect(canvas.transform, "Main_Display");
            var sub = CreateRect(canvas.transform, "Sub_Display");
            var cover = CreateRect(canvas.transform, "Sub_Cover");
            var bottom = CreateRect(canvas.transform, "Sub_Cover_Bottom");
            var top = CreateRect(canvas.transform, "Sub_Cover_Top");
            var follower = CreateRect(canvas.transform, "Follower");
            bottom.localScale = new Vector3(-2f, 1f, 1f);
            top.localScale = Vector3.zero;
            var applier = canvas.AddComponent<ScreenPosCanvasLayoutApplier>();
            PrivateMember.SetField(applier, "_mainDisplayFollowers", new[] { follower });

            yield return null;

            var layout = provider.Layout;
            Assert.That(scaler.referenceResolution, Is.EqualTo(layout.ReferenceResolution));
            var expectedMain = ScreenPosCanvasLayout.ToAnchoredPosition(layout.MainDisplayPosition, main.anchorMin, main.anchorMax, main.pivot, layout.CanvasSize);
            Assert.That(Vector2.Distance(main.anchoredPosition, expectedMain), Is.LessThan(1e-2f));
            Assert.That(Vector2.Distance(follower.anchoredPosition, expectedMain), Is.LessThan(1e-2f));
            Assert.That(sub.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1f)));
            Assert.That(cover.rect.width, Is.EqualTo(layout.SubCover.width).Within(1e-2f));
            Assert.That(cover.rect.height, Is.EqualTo(layout.SubCover.height).Within(1e-2f));
            Assert.That(bottom.rect.width, Is.EqualTo(layout.SubCoverBottom.width / 2f).Within(1e-2f));
            Assert.That(top.rect.width, Is.EqualTo(100f).Within(1e-2f));

            main.anchoredPosition = new Vector2(123f, 456f);
            yield return null;
            Assert.That(main.anchoredPosition, Is.EqualTo(new Vector2(123f, 456f)));
        }

        /// <summary>Checks that the applier does nothing until a provider has a layout and tolerates missing parts.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Applier_WithoutProvider_DoesNothing()
        {
            var canvas = _objects.Create("Canvas", null);
            canvas.AddComponent<RectTransform>();
            var main = CreateRect(canvas.transform, "Main_Display");
            main.anchoredPosition = new Vector2(1f, 2f);
            canvas.AddComponent<ScreenPosCanvasLayoutApplier>();

            yield return null;

            Assert.That(main.anchoredPosition, Is.EqualTo(new Vector2(1f, 2f)));
        }

        /// <summary>Checks that the corner updater publishes the sub screen edges after its start delay and after changes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CornerUpdater_PublishesSubScreenEdge()
        {
            Assume.That(Screen.width > 0 && Screen.height > 0, "A screen size is required.");
            var rect = CreateRect(_objects.Create("Root").transform, "SubScreen");
            rect.sizeDelta = new Vector2(4f, 2f);
            rect.position = new Vector3(10f, 20f, 0f);
            rect.gameObject.AddComponent<RectCornerPositionUpdater>();
            InputManager.SubScreenEdge = Vector4.zero;

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
            Assert.That(InputManager.SubScreenEdge, Is.EqualTo(new Vector4(8f, 21f, 12f, 19f)));

            rect.position = new Vector3(0f, 0f, 0f);
            MajEnv.Settings.Display.SubDisplayOffset = 0.5f;
            yield return null;
            yield return null;
            yield return null;
            Assert.That(InputManager.SubScreenEdge, Is.EqualTo(new Vector4(-2f, 1f, 2f, -1f)));
        }
    }
}
