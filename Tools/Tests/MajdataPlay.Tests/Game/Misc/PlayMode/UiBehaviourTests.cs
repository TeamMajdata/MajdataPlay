using System.Collections;
using MajdataPlay.Behaviours.UI;
using MajdataPlay.Behaviours.UI.Inicators;
using MajdataPlay.Tests;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>Covers the circle mask updaters, the UI indicators and the streaming video loader in Play Mode.</summary>
    public class UiBehaviourTests
    {
        /// <summary>The shader property holding the mask radius.</summary>
        private static readonly int s_radius = Shader.PropertyToID("_Radius");

        /// <summary>The shader property holding the mask feather.</summary>
        private static readonly int s_feather = Shader.PropertyToID("_Feather");

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

        /// <summary>Creates a material using the circle mask shader.</summary>
        /// <returns>The tracked material.</returns>
        private Material CreateMaskMaterial()
        {
            var shader = Shader.Find("Maj/UI/Circle Mask");
            Assume.That(shader != null, "The circle mask shader is required.");
            return _objects.Track(new Material(shader));
        }

        /// <summary>Checks that the image mask updater copies its radius and feather into the image material every frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImageCircleMaskUpdater_WritesMaterial()
        {
            var image = _objects.Add<Image>("Image");
            var material = CreateMaskMaterial();
            image.material = material;
            var updater = image.gameObject.AddComponent<ImageCircleMaskUpdater>();
            PrivateMember.SetField(updater, "_radius", 0.3f);
            PrivateMember.SetField(updater, "_feather", 0.05f);

            yield return null;

            Assert.That(updater.enabled, Is.True);
            Assert.That(material.GetFloat(s_radius), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(material.GetFloat(s_feather), Is.EqualTo(0.05f).Within(1e-5f));
        }

        /// <summary>Checks that the raw image mask updater copies its radius and feather into the raw image material every frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RawImageCircleMaskUpdater_WritesMaterial()
        {
            var image = _objects.Add<RawImage>("RawImage");
            var material = CreateMaskMaterial();
            image.material = material;
            var updater = image.gameObject.AddComponent<RawImageCircleMaskUpdater>();
            PrivateMember.SetField(updater, "_radius", 0.4f);
            PrivateMember.SetField(updater, "_feather", 0.1f);

            yield return null;

            Assert.That(material.GetFloat(s_radius), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(material.GetFloat(s_feather), Is.EqualTo(0.1f).Within(1e-5f));
        }

        /// <summary>Creates an inactive object with an indicator whose target is assigned before Awake.</summary>
        /// <typeparam name="T">The indicator type.</typeparam>
        /// <param name="target">The object the indicator toggles, or null to toggle its own object.</param>
        /// <returns>The inactive indicator.</returns>
        private T CreateIndicator<T>(GameObject? target) where T : UIInicator
        {
            var gameObject = _objects.Create(typeof(T).Name);
            gameObject.SetActive(false);
            var indicator = gameObject.AddComponent<T>();
            PrivateMember.SetField(indicator, "ActionTo", target);
            return indicator;
        }

        /// <summary>Checks that the filter indicator shows the search keyword and hides itself without a keyword.</summary>
        [Test]
        public void ListFilteredIndicator_ShowsKeyword()
        {
            var orderBy = MajEnv.RuntimeConfig!.List.OrderBy;
            var savedKeyword = orderBy.Keyword;
            try
            {
                var target = _objects.Create("Target");
                target.SetActive(false);
                var text = _objects.Add<TextMeshProUGUI>("Search");
                var shown = CreateIndicator<ListFilteredInicator>(target);
                shown.SearchText = text;
                orderBy.Keyword = "needle";

                shown.gameObject.SetActive(true);

                Assert.That(target.activeSelf, Is.True);
                Assert.That(text.text, Does.EndWith("needle"));

                var hidden = CreateIndicator<ListFilteredInicator>(null);
                hidden.SearchText = text;
                orderBy.Keyword = string.Empty;
                hidden.gameObject.SetActive(true);

                Assert.That(hidden.gameObject.activeSelf, Is.False);
            }
            finally
            {
                orderBy.Keyword = savedKeyword;
            }
        }

        /// <summary>Checks that the mod indicator follows whether any gameplay mod is active.</summary>
        [Test]
        public void ModEnabledIndicator_FollowsMods()
        {
            var mod = MajEnv.Settings.Mod;
            var savedAllBreak = mod.AllBreak;
            var managerObject = _objects.Create("GameManager");
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent<GameManager>();
            Majdata<GameManager>.Instance = manager;
            try
            {
                var target = _objects.Create("Target");
                mod.AllBreak = false;
                CreateIndicator<ModEnabledInicator>(target).gameObject.SetActive(true);
                Assert.That(target.activeSelf, Is.EqualTo(mod.IsAnyModActive()));

                mod.AllBreak = true;
                var selfToggled = CreateIndicator<ModEnabledInicator>(null);
                selfToggled.gameObject.SetActive(true);
                Assert.That(selfToggled.gameObject.activeSelf, Is.True);
            }
            finally
            {
                mod.AllBreak = savedAllBreak;
                Majdata<GameManager>.Free();
            }
        }

        /// <summary>Checks that invalid streaming video paths are rejected without touching a player.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadVideoFromSA_RejectsInvalidPaths()
        {
            foreach (var path in new[] { string.Empty, "/" })
            {
                var gameObject = _objects.Create("Video");
                gameObject.SetActive(false);
                var loader = gameObject.AddComponent<LoadVideoFromSA>();
                PrivateMember.SetField(loader, "_videoPath", path);
                gameObject.SetActive(true);

                yield return null;
                yield return null;

                Assert.That(PrivateMember.GetField<UnityEngine.Object?>(loader, "_ffmpegPlayer") == null, Is.True);
                Assert.That(PrivateMember.GetField<string>(loader, "_videoPath"), Is.EqualTo(path));
                Assert.That(loader.isActiveAndEnabled, Is.True);
            }
        }
    }
}
