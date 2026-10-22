using System.Collections;
using MajdataPlay.Misc.Base;
using MajdataPlay.Tests;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>Covers the scrolling text behaviour <see cref="TMPMarqueeText"/> in Play Mode.</summary>
    public class TMPMarqueeTextTests
    {
        /// <summary>A text far wider than the 100 unit viewport used by the tests.</summary>
        private static readonly string s_longText = new string('W', 80);

        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Creates a marquee with a 100x40 viewport that starts scrolling immediately.</summary>
        /// <returns>The marquee.</returns>
        private TMPMarqueeText CreateMarquee()
        {
            var gameObject = _objects.Track(new GameObject("Marquee", typeof(RectTransform)));
            ((RectTransform)gameObject.transform).sizeDelta = new Vector2(100f, 40f);
            var marquee = gameObject.AddComponent<TMPMarqueeText>();
            marquee.StartDelay = 0f;
            marquee.ScrollSpeed = 300f;
            return marquee;
        }

        /// <summary>Finds a generated child text.</summary>
        /// <param name="marquee">The marquee.</param>
        /// <param name="name">The child name.</param>
        /// <returns>The child text, or null when it was not created.</returns>
        private static TextMeshProUGUI? Child(TMPMarqueeText marquee, string name)
        {
            var child = marquee.transform.Find(name);
            return child == null ? null : child.GetComponent<TextMeshProUGUI>();
        }

        /// <summary>Checks that a short text is shown directly without scrolling copies.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ShortText_DoesNotScroll()
        {
            var marquee = CreateMarquee();
            var source = marquee.GetComponent<TextMeshProUGUI>();

            marquee.SetText("Hi");
            yield return null;

            Assert.That(source.enabled, Is.True);
            Assert.That(Child(marquee, "Marquee Text"), Is.Null);
        }

        /// <summary>Checks that a long text is replaced by two scrolling copies that move left and that a short text restores the source.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LongText_ScrollsAndRestores()
        {
            var marquee = CreateMarquee();
            var source = marquee.GetComponent<TextMeshProUGUI>();

            marquee.SetText(s_longText);
            var main = Child(marquee, "Marquee Text");
            var copy = Child(marquee, "Marquee Copy");

            Assert.That(main, Is.Not.Null);
            Assert.That(copy, Is.Not.Null);
            Assert.That(main!.gameObject.activeSelf, Is.True);
            Assert.That(main.text, Is.EqualTo(s_longText));
            Assert.That(source.enabled, Is.False);
            Assert.That(marquee.GetComponent<RectMask2D>().enabled, Is.True);
            var start = ((RectTransform)main.transform).anchoredPosition.x;
            Assert.That(((RectTransform)copy!.transform).anchoredPosition.x, Is.GreaterThan(start));

            yield return new WaitForSeconds(0.2f);
            Assert.That(((RectTransform)main.transform).anchoredPosition.x, Is.LessThan(start));

            marquee.SetText("Hi");
            Assert.That(source.enabled, Is.True);
            Assert.That(main.gameObject.activeSelf, Is.False);
            Assert.That(copy.gameObject.activeSelf, Is.False);
            Assert.That(marquee.GetComponent<RectMask2D>().enabled, Is.False);
        }

        /// <summary>Checks that a text changed on the source component is picked up during Update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SourceTextChange_IsDetected()
        {
            var marquee = CreateMarquee();
            var source = marquee.GetComponent<TextMeshProUGUI>();
            yield return null;

            source.text = s_longText;
            yield return null;

            Assert.That(Child(marquee, "Marquee Text"), Is.Not.Null);
            Assert.That(source.enabled, Is.False);
        }

        /// <summary>Checks that disabling the marquee restores the source text and enabling it scrolls again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Disable_RestoresSource()
        {
            var marquee = CreateMarquee();
            var source = marquee.GetComponent<TextMeshProUGUI>();
            marquee.SetText(s_longText);
            yield return null;

            marquee.enabled = false;
            Assert.That(source.enabled, Is.True);
            Assert.That(Child(marquee, "Marquee Text")!.gameObject.activeSelf, Is.False);

            marquee.enabled = true;
            Assert.That(source.enabled, Is.False);
            Assert.That(Child(marquee, "Marquee Text")!.gameObject.activeSelf, Is.True);
        }

        /// <summary>Checks that destroying the marquee removes the generated copies.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_RemovesCopies()
        {
            var marquee = CreateMarquee();
            marquee.SetText(s_longText);
            var main = Child(marquee, "Marquee Text")!;
            var copy = Child(marquee, "Marquee Copy")!;

            Object.Destroy(marquee);
            yield return null;

            Assert.That(main == null, Is.True);
            Assert.That(copy == null, Is.True);
        }
    }
}
