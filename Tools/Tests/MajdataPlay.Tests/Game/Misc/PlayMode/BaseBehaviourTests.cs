using System.Collections;
using MajdataPlay.Behaviours;
using MajdataPlay.i18n;
using MajdataPlay.Runtime;
using MajdataPlay.Tests;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.PlayMode
{
    /// <summary>Covers the small general purpose behaviours of the Base, Behaviours, Components and i18n folders in Play Mode.</summary>
    public class BaseBehaviourTests
    {
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

        /// <summary>Checks the cached members of a component and the layer used to hide it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MajComponent_CachesMembersAndHides()
        {
            var probe = _objects.Add<MajComponentProbe>();
            yield return null;

            Assert.That(probe.GameObject, Is.SameAs(probe.gameObject));
            Assert.That(probe.Transform, Is.SameAs(probe.transform));
            Assert.That(probe.InstanceID, Is.EqualTo(probe.GetInstanceID()));
            Assert.That(probe.Tag, Is.EqualTo("Untagged"));
            Assert.That(probe.Active, Is.False);
            Assert.That(probe.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));

            probe.SetActive(false);
            Assert.That(probe.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(probe.Active, Is.False);

            probe.SetActive(true);
            Assert.That(probe.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            Assert.That(probe.Active, Is.True);

            probe.Tag = "EditorOnly";
            Assert.That(probe.gameObject.tag, Is.EqualTo("EditorOnly"));
            Assert.That(probe.Tag, Is.EqualTo("EditorOnly"));
        }

        /// <summary>Checks that the instance identifier is computed lazily before Awake.</summary>
        [Test]
        public void MajComponent_InstanceIdBeforeAwake()
        {
            var gameObject = _objects.Create("Inactive");
            gameObject.SetActive(false);
            var probe = gameObject.AddComponent<MajComponentProbe>();

            Assert.That(probe.InstanceID, Is.EqualTo(probe.GetInstanceID()));
            Assert.That(probe.GameObject, Is.Null);
        }

        /// <summary>Checks that behaviours marked with the persistence attribute survive scene loads.</summary>
        [Test]
        public void MajBehaviour_PersistentAttributeMovesObject()
        {
            var probe = _objects.Add<PersistentBehaviourProbe>();

            Assert.That(probe.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        }

        /// <summary>Checks that the persistence component moves its object out of the active scene.</summary>
        [Test]
        public void DontDestroyOnLoadComponent_MovesObject()
        {
            var component = _objects.Add<MajdataPlay.Components.DontDestroyOnLoad>();

            Assert.That(component.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        }

        /// <summary>Checks that a self-destroying object is destroyed only when requested.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DestroySelf_DestroysWhenRequested()
        {
            var keep = _objects.Add<DestroySelf>();
            var destroy = _objects.Add<DestroySelf>();
            destroy.ifDestroy = true;

            yield return null;
            yield return null;

            Assert.That(keep == null, Is.False);
            Assert.That(destroy == null, Is.True);
        }

        /// <summary>Checks that a floating element rotates and oscillates around its start position.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Floating_RotatesAndOscillates()
        {
            var gameObject = _objects.Track(new GameObject("Floating", typeof(RectTransform)));
            var rectTransform = (RectTransform)gameObject.transform;
            rectTransform.localPosition = new Vector3(10f, 20f, 0f);
            var floating = gameObject.AddComponent<Floating>();
            floating.rotate = 5f;
            floating.wx = 1f;
            floating.wy = 2f;

            yield return null;
            yield return null;
            yield return null;

            var offset = rectTransform.localPosition - new Vector3(10f, 20f, 0f);
            Assert.That(Mathf.Abs(offset.x), Is.LessThanOrEqualTo(1.0001f));
            Assert.That(Mathf.Abs(offset.y), Is.LessThanOrEqualTo(1.0001f));
            Assert.That(rectTransform.localEulerAngles.z, Is.GreaterThan(0f));
        }

        /// <summary>Checks that the Live2D controller keeps its object active on supported platforms.</summary>
        [Test]
        public void CubismPlatformController_KeepsSupportedPlatformsActive()
        {
            var controller = _objects.Add<CubismPlatformController>();
            var unsupported = (PlatformInfo.IsAndroid && PlatformInfo.Is32bitProcessor) ||
                              (PlatformInfo.IsWindows && PlatformInfo.IsArm64Processor) ||
                              (PlatformInfo.IsLinux && !PlatformInfo.IsX64Processor);

            Assert.That(controller.gameObject.activeSelf, Is.EqualTo(!unsupported));
        }

        /// <summary>Checks that a localized text component shows the translation of its key on start.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LocalizeTMP_SetsTranslatedText()
        {
            var text = _objects.Add<TextMeshProUGUI>("Text");
            var localize = text.gameObject.AddComponent<LocalizeTMP>();
            localize.local_key = "MAJTEXT_PRESS_ANY_KEY";

            yield return null;

            Assert.That(text.text, Is.EqualTo("MAJTEXT_PRESS_ANY_KEY".i18n()));
        }

        /// <summary>Checks that a legacy text component is localized too.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LocalizeTMP_SetsLegacyText()
        {
            var text = _objects.Add<Text>("Legacy");
            var localize = text.gameObject.AddComponent<LocalizeTMP>();
            localize.local_key = "MAJTEXT_NO_CHARTS";

            yield return null;

            Assert.That(text.text, Is.EqualTo("MAJTEXT_NO_CHARTS".i18n()));
        }

        /// <summary>Checks that the sub display background starts black while it waits for the skins.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSubBg_WaitsForSkins()
        {
            var image = _objects.Add<Image>("SubBg");
            image.color = Color.red;
            image.gameObject.AddComponent<LoadSubBg>();

            yield return null;

            Assert.That(image.color, Is.EqualTo(Color.black));
            Assert.That(image.sprite, Is.Null);
            Assert.That(image.useSpriteMesh, Is.True);
        }
    }
}
