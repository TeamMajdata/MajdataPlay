using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks main-display clipping follows the real sibling display bounds without changing layout.</summary>
    public sealed class MainDisplayUiClipTests
    {
        /// <summary>Tracks only temporary test objects, including any display detached from its root.</summary>
        private readonly List<GameObject> _objects = new();
        /// <summary>Retains the common display root.</summary>
        private RectTransform _root = null!;
        /// <summary>Retains the actual main display.</summary>
        private RectTransform _main = null!;
        /// <summary>Retains the sibling secondary display.</summary>
        private RectTransform _sub = null!;
        /// <summary>Allows for inverse-transform float precision at full canvas coordinates.</summary>
        private const float Tolerance = 0.001f;

        /// <summary>Creates the same display names and logical dimensions used by CanvasTemplate.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = CreateRoot("Clip test root");
            _main = CreateDisplay(_root, "Main_Display", new Vector2(1080f, 1080f), 540f);
            _sub = CreateDisplay(_root, "Sub_Display", new Vector2(1080f, 450f), 1695f);
        }

        /// <summary>Removes temporary objects without saving or resaving production assets.</summary>
        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }
            _objects.Clear();
        }

        /// <summary>Checks actual relative display bounds, including a moved main display and overlap.</summary>
        /// <param name="mainOffset">The main display's upward offset from its original center.</param>
        /// <param name="subBottom">The lower edge of the secondary display in root coordinates.</param>
        /// <param name="expectedTopPadding">The resulting upper clipping inset in main-display units.</param>
        [TestCase(0f, 1470f, 0f)]
        [TestCase(0f, 1080f, 0f)]
        [TestCase(0f, 980f, 100f)]
        [TestCase(200f, 1080f, 200f)]
        public void DisplayBounds_OnlyTightenTheNecessaryUpperEdge(float mainOffset, float subBottom, float expectedTopPadding)
        {
            _main.anchoredPosition += Vector2.up * mainOffset;
            _sub.anchoredPosition = new Vector2(0f, subBottom + 225f);
            var originalPosition = _main.anchoredPosition;
            var originalSize = _main.sizeDelta;
            var originalScale = _main.localScale;
            var image = CreateImage(_main);

            MainDisplayUiClip.EnsureFor(image);

            var mask = _main.GetComponent<RectMask2D>();
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.padding.x, Is.Zero);
            Assert.That(mask.padding.y, Is.Zero);
            Assert.That(mask.padding.z, Is.Zero);
            Assert.That(mask.padding.w, Is.EqualTo(expectedTopPadding).Within(Tolerance));
            Assert.That(_main.anchoredPosition, Is.EqualTo(originalPosition));
            Assert.That(_main.sizeDelta, Is.EqualTo(originalSize));
            Assert.That(_main.localScale, Is.EqualTo(originalScale));
        }

        /// <summary>Checks world-corner sampling supports secondary-display scale and axis mirroring.</summary>
        /// <param name="scaleX">The secondary display's horizontal scale.</param>
        /// <param name="scaleY">The secondary display's vertical scale.</param>
        /// <param name="expectedTopPadding">The upper clipping inset after inverse-transforming the bounds.</param>
        [TestCase(1f, 1f, 105f)]
        [TestCase(-1f, -1f, 105f)]
        [TestCase(1f, -2f, 330f)]
        [TestCase(-2f, 0.5f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        public void ScaledOrMirroredSecondaryDisplay_UsesActualWorldBounds(float scaleX, float scaleY, float expectedTopPadding)
        {
            _root.localScale = new Vector3(0.7f, 0.7f, 1f);
            _root.anchoredPosition = new Vector2(135f, -73f);
            _sub.anchoredPosition = new Vector2(0f, 1200f);
            _sub.localScale = new Vector3(scaleX, scaleY, 1f);

            MainDisplayUiClip.EnsureFor(CreateImage(_main));

            Assert.That(_main.GetComponent<RectMask2D>().padding.w,
                Is.EqualTo(expectedTopPadding).Within(Tolerance));
        }

        /// <summary>Checks authored padding survives temporary tightening, inactive secondary display and disable.</summary>
        [Test]
        public void ExistingPadding_IsPreservedAndRestored()
        {
            var original = new Vector4(7f, 11f, 13f, 17f);
            var mask = _main.gameObject.AddComponent<RectMask2D>();
            mask.padding = original;
            _sub.anchoredPosition = new Vector2(0f, 1185f);
            MainDisplayUiClip.EnsureFor(CreateImage(_main));
            var clip = _main.GetComponent<MainDisplayUiClip>();

            Assert.That(_main.GetComponents<RectMask2D>(), Has.Length.EqualTo(1));
            Assert.That(mask.padding, Is.EqualTo(new Vector4(7f, 11f, 13f, 120f)));
            _sub.gameObject.SetActive(false);
            clip.RefreshClip();
            Assert.That(mask.padding, Is.EqualTo(original));
            _sub.gameObject.SetActive(true);
            clip.RefreshClip();
            Assert.That(mask.padding.w, Is.EqualTo(120f));
            clip.enabled = false;
            InvokeMessage(clip, "OnDisable");
            Assert.That(mask.padding, Is.EqualTo(original));
            clip.enabled = true;
            InvokeMessage(clip, "OnEnable");
            Assert.That(mask.padding.w, Is.EqualTo(120f));
        }

        /// <summary>Checks an already tighter authored top inset is never loosened by the automatic boundary.</summary>
        [Test]
        public void ExistingTighterPadding_IsNotRelaxed()
        {
            var mask = _main.gameObject.AddComponent<RectMask2D>();
            mask.padding = new Vector4(2f, 3f, 4f, 160f);
            _sub.anchoredPosition = new Vector2(0f, 1205f);

            MainDisplayUiClip.EnsureFor(CreateImage(_main));

            Assert.That(mask.padding, Is.EqualTo(new Vector4(2f, 3f, 4f, 160f)));
        }

        /// <summary>Checks secondary UI, unrelated panels and unpaired names do not receive a main-display clip.</summary>
        [Test]
        public void EnsureFor_OnlyRecognizesAnActualMainDisplayWithASiblingSubDisplay()
        {
            var subImage = CreateImage(_sub);
            MainDisplayUiClip.EnsureFor(subImage);
            Assert.That(_root.GetComponentsInChildren<MainDisplayUiClip>(true), Is.Empty);
            var unrelated = CreateDisplay(_root, "Other Panel", new Vector2(100f, 100f), 0f);
            MainDisplayUiClip.EnsureFor(CreateImage(unrelated));
            Assert.That(unrelated.GetComponent<MainDisplayUiClip>(), Is.Null);
            var otherRoot = CreateRoot("Unpaired display root");
            var unpaired = CreateDisplay(otherRoot, "Main_Display", new Vector2(1080f, 1080f), 540f);
            MainDisplayUiClip.EnsureFor(CreateImage(unpaired));
            Assert.That(unpaired.GetComponent<MainDisplayUiClip>(), Is.Null);
            Assert.That(unpaired.GetComponent<RectMask2D>(), Is.Null);
        }

        /// <summary>Checks repeated images and refreshes retain only one nonserialized clip owner and mask.</summary>
        [Test]
        public void EnsureFor_ReusesOneRuntimeOwnerPerMainDisplay()
        {
            var first = CreateImage(_main);
            var second = CreateImage(_main);
            MainDisplayUiClip.EnsureFor(first);
            MainDisplayUiClip.EnsureFor(second);
            MainDisplayUiClip.EnsureFor(first);

            Assert.That(_main.GetComponents<MainDisplayUiClip>(), Has.Length.EqualTo(1));
            Assert.That(_main.GetComponents<RectMask2D>(), Has.Length.EqualTo(1));
            Assert.That(_main.GetComponent<MainDisplayUiClip>().hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
            Assert.That(_main.GetComponent<RectMask2D>().hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
        }

        /// <summary>Checks an image reparented into another display binds to that display's independent owner.</summary>
        [Test]
        public void ImageReparenting_EnsuresTheNewDisplayOwner()
        {
            _sub.anchoredPosition = new Vector2(0f, 1205f);
            var image = CreateImage(_main);
            MainDisplayUiClip.EnsureFor(image);
            var root = CreateRoot("Second display root");
            var main = CreateDisplay(root, "Main_Display", new Vector2(1080f, 1080f), 540f);
            CreateDisplay(root, "Sub_Display", new Vector2(1080f, 450f), 1105f);
            image.transform.SetParent(main, false);

            MainDisplayUiClip.EnsureFor(image);

            Assert.That(main.GetComponents<MainDisplayUiClip>(), Has.Length.EqualTo(1));
            Assert.That(main.GetComponent<RectMask2D>().padding.w, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(_main.GetComponent<RectMask2D>().padding.w, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(_sub.GetComponent<MainDisplayUiClip>(), Is.Null);
        }

        /// <summary>Checks a whole main group rebinds after moving to another canvas with a different secondary display.</summary>
        [Test]
        public void MainGroupReparenting_RebindsTheSiblingAndRestoresWhenDetached()
        {
            var original = new Vector4(2f, 3f, 4f, 5f);
            var mask = _main.gameObject.AddComponent<RectMask2D>();
            mask.padding = original;
            MainDisplayUiClip.EnsureFor(CreateImage(_main));
            var clip = _main.GetComponent<MainDisplayUiClip>();
            var newRoot = CreateRoot("Moved display root");
            CreateDisplay(newRoot, "Sub_Display", new Vector2(1080f, 450f), 1105f);
            _main.SetParent(newRoot, false);

            clip.RefreshClip();

            Assert.That(mask.padding.w, Is.EqualTo(200f).Within(Tolerance));
            _main.SetParent(null, false);
            clip.RefreshClip();
            Assert.That(mask.padding, Is.EqualTo(original));
        }

        /// <summary>Creates a parent canvas with deterministic logical coordinates.</summary>
        /// <param name="name">The temporary canvas name.</param>
        /// <returns>The temporary root RectTransform.</returns>
        private RectTransform CreateRoot(string name)
        {
            var gameObject = Track(new GameObject(name, typeof(RectTransform), typeof(Canvas)));
            gameObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = gameObject.GetComponent<RectTransform>();
            root.pivot = Vector2.zero;
            root.sizeDelta = new Vector2(1080f, 1920f);
            return root;
        }

        /// <summary>Creates a display whose pivot is placed relative to the root's bottom edge.</summary>
        /// <param name="root">The temporary parent canvas.</param>
        /// <param name="name">The production display name to use.</param>
        /// <param name="size">The logical display dimensions.</param>
        /// <param name="centerY">The display center measured from the root's bottom edge.</param>
        /// <returns>The temporary display RectTransform.</returns>
        private RectTransform CreateDisplay(RectTransform root, string name, Vector2 size, float centerY)
        {
            var gameObject = Track(new GameObject(name, typeof(RectTransform)));
            var display = gameObject.GetComponent<RectTransform>();
            display.SetParent(root, false);
            display.anchorMin = display.anchorMax = new Vector2(0.5f, 0f);
            display.pivot = new Vector2(0.5f, 0.5f);
            display.sizeDelta = size;
            display.anchoredPosition = new Vector2(0f, centerY);
            return display;
        }

        /// <summary>Creates an atlas-capable UI image without loading game or skin services.</summary>
        /// <param name="parent">The temporary parent display.</param>
        /// <returns>The temporary quality-aware image.</returns>
        private QualityUiImage CreateImage(RectTransform parent)
        {
            var gameObject = Track(new GameObject("Clip source image", typeof(RectTransform), typeof(CanvasRenderer)));
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<QualityUiImage>();
        }

        /// <summary>Tracks one temporary object for teardown.</summary>
        /// <param name="gameObject">The temporary object.</param>
        /// <returns>The same temporary object.</returns>
        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }

        /// <summary>Delivers a lifecycle message explicitly because ordinary behaviours do not update in EditMode tests.</summary>
        /// <param name="clip">The production controller under test.</param>
        /// <param name="name">The private Unity message name.</param>
        private static void InvokeMessage(MainDisplayUiClip clip, string name)
        {
            typeof(MainDisplayUiClip).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(clip, null);
        }
    }
}
