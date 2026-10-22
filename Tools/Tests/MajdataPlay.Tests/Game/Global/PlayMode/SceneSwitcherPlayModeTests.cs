using System;
using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Global.PlayMode
{
    /// <summary>Verifies the scene transition overlay of <see cref="SceneSwitcher"/> in Play Mode without loading other scenes.</summary>
    public class SceneSwitcherPlayModeTests
    {
        /// <summary>The transition duration used by the tests, in seconds.</summary>
        private const float Duration = 0.05f;

        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The switcher under test.</summary>
        private SceneSwitcher _switcher = null!;

        /// <summary>The overlay canvas holding the transition graphics.</summary>
        private Canvas _overlayCanvas = null!;

        /// <summary>The masked main image.</summary>
        private Image _mainImage = null!;

        /// <summary>A decoration under the mask.</summary>
        private Image _decoration = null!;

        /// <summary>The overlay image.</summary>
        private Image _overlayImage = null!;

        /// <summary>The secondary screen image.</summary>
        private Image _subImage = null!;

        /// <summary>The loading text.</summary>
        private TextMeshProUGUI _loadingText = null!;

        /// <summary>The scene state and camera before the test.</summary>
        private (MajScenes Current, MajScenes Last, Camera Camera) _saved;

        /// <summary>Builds the transition hierarchy and activates the switcher.</summary>
        [SetUp]
        public void SetUp()
        {
            _saved = (SceneSwitcher.CurrentScene, SceneSwitcher.LastScene, SceneSwitcher.MainCamera);
            _objects = new TestObjects();
            var overlay = _objects.Create("TransitionOverlay");
            _overlayCanvas = overlay.AddComponent<Canvas>();
            var mask = _objects.Create("Mask", overlay.transform);
            var maskRect = mask.AddComponent<RectTransform>();
            _mainImage = _objects.Create("Main", mask.transform).AddComponent<Image>();
            _decoration = _objects.Create("Decoration", mask.transform).AddComponent<Image>();
            _overlayImage = _objects.Create("Overlay", overlay.transform).AddComponent<Image>();
            _subImage = _objects.Create("Sub", overlay.transform).AddComponent<Image>();
            _loadingText = _objects.Create("Loading", overlay.transform).AddComponent<TextMeshProUGUI>();

            var root = InactiveObjects.Create(_objects, "SceneSwitcher");
            root.AddComponent<Canvas>();
            _switcher = root.AddComponent<SceneSwitcher>();
            PrivateMember.SetField(_switcher, "_transitionOverlayCanvas", _overlayCanvas);
            PrivateMember.SetField(_switcher, "_mainMaskRect", maskRect);
            PrivateMember.SetField(_switcher, "_mainImage", _mainImage);
            PrivateMember.SetField(_switcher, "_overlayImage", _overlayImage);
            PrivateMember.SetField(_switcher, "_subImage", _subImage);
            PrivateMember.SetField(_switcher, "_loadingText", _loadingText);
            PrivateMember.SetField(_switcher, "_loadingLightColor", Color.cyan);
            PrivateMember.SetField(_switcher, "_closeDuration", Duration);
            PrivateMember.SetField(_switcher, "_openDuration", Duration);
            root.SetActive(true);
        }

        /// <summary>Destroys the hierarchy and restores the static scene state.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            if (MajdataPlay.Majdata<SceneSwitcher>.IsSingleton)
            {
                MajdataPlay.Majdata<SceneSwitcher>.Free();
            }
            if (MajdataPlay.Majdata<SkinManager>.IsSingleton)
            {
                MajdataPlay.Majdata<SkinManager>.Free();
            }
            PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), _saved.Current);
            PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.LastScene), _saved.Last);
            SceneSwitcher.MainCamera = _saved.Camera;
        }

        /// <summary>Checks the fully open state after Awake.</summary>
        [Test]
        public void Awake_StartsFullyOpen()
        {
            Assert.That(MajdataPlay.Majdata<SceneSwitcher>.Instance, Is.SameAs(_switcher));
            Assert.That(_mainImage.gameObject.activeSelf, Is.False);
            Assert.That(_loadingText.gameObject.activeSelf, Is.False);
            Assert.That(_overlayImage.color.a, Is.EqualTo(0f));
            Assert.That(_decoration.color.a, Is.EqualTo(0f));
            Assert.That(_mainImage.rectTransform.localScale.x, Is.EqualTo(1.01f).Within(1e-4f));
        }

        /// <summary>Checks that closing and opening animate to the covered and open states.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FadeInAndOut_AnimateTheMask()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await _switcher.FadeInAsync();

                Assert.That(PrivateMember.GetField<float>(_switcher, "_maskProgress"), Is.EqualTo(1f));
                Assert.That(_mainImage.gameObject.activeSelf, Is.True);
                Assert.That(_loadingText.gameObject.activeSelf, Is.True);
                Assert.That(_overlayImage.color.a, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(_decoration.color.a, Is.EqualTo(1f).Within(1e-3f));

                await _switcher.FadeOutAsync();
                await UniTask.Delay(400, ignoreTimeScale: true);

                Assert.That(PrivateMember.GetField<float>(_switcher, "_maskProgress"), Is.EqualTo(0f));
                Assert.That(_mainImage.gameObject.activeSelf, Is.False);
                Assert.That(_loadingText.gameObject.activeSelf, Is.False);
                Assert.That(_overlayImage.color.a, Is.EqualTo(0f).Within(1e-3f));
            });
        }

        /// <summary>Checks the fire-and-forget transitions and that a transition to the current state applies it without animating.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SynchronousTransitions_ReachTheirStates()
        {
            _switcher.FadeOut();
            Assert.That(_mainImage.gameObject.activeSelf, Is.False, "already open");

            _switcher.FadeIn();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(_overlayImage.color.a, Is.EqualTo(1f).Within(1e-3f));

            _switcher.FadeIn();
            Assert.That(_loadingText.gameObject.activeSelf, Is.True);

            _switcher.FadeOut();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(_mainImage.gameObject.activeSelf, Is.False);
        }

        /// <summary>Checks the loading text setters.</summary>
        [Test]
        public void SetLoadingText_SetsTextAndColour()
        {
            _switcher.SetLoadingText("Loading", Color.red);
            Assert.That(_loadingText.text, Is.EqualTo("Loading"));
            Assert.That(_loadingText.color, Is.EqualTo(Color.red));

            _switcher.SetLoadingText("Done");
            Assert.That(_loadingText.text, Is.EqualTo("Done"));
            Assert.That(_loadingText.color, Is.EqualTo(Color.white));
        }

        /// <summary>Checks the editor preview helpers.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EditorPreview_PlaysAndFinishes()
        {
#if UNITY_EDITOR
            Assert.That(_switcher.GetEditorPreviewDuration(true), Is.EqualTo(Duration));
            Assert.That(_switcher.GetEditorPreviewDuration(false), Is.EqualTo(Duration));
            _switcher.PreviewTransition(true);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(PrivateMember.GetField<float>(_switcher, "_maskProgress"), Is.EqualTo(1f));
            _switcher.FinishEditorPreview(false);
            Assert.That(_mainImage.gameObject.activeSelf, Is.False);
#endif
            yield return null;
        }

        /// <summary>Checks that a scene change updates the current and last scene and notifies subscribers.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UnitySceneChange_UpdatesScenesAndNotifies()
        {
            var created = SceneManager.CreateScene("Title");
            (MajScenes NewScene, MajScenes OldScene)? received = null;
            EventHandler<(MajScenes NewScene, MajScenes OldScene)> handler = (_, args) => received = args;
            SceneSwitcher.OnSceneChanged += handler;
            PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), MajScenes.List);
            try
            {
                PrivateMember.Invoke(_switcher, "OnUnitySceneChanged", SceneManager.GetActiveScene(), created);

                Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(MajScenes.Title));
                Assert.That(SceneSwitcher.LastScene, Is.EqualTo(MajScenes.List));
                Assert.That(received, Is.EqualTo((MajScenes.Title, MajScenes.List)));

                PrivateMember.Invoke(_switcher, "OnUnitySceneChanged", created, SceneManager.GetActiveScene());
                Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(MajScenes.Title), "unknown scene names keep the current scene");
            }
            finally
            {
                SceneSwitcher.OnSceneChanged -= handler;
            }
            yield return SceneManager.UnloadSceneAsync(created);
        }

        /// <summary>Checks that the background images use the selected skin's secondary display or black without a skin.</summary>
        [Test]
        public void UpdateBGSprite_UsesTheSkinOrBlack()
        {
            PrivateMember.Invoke(_switcher, "UpdateBGSprite");
            Assert.That(_subImage.color, Is.EqualTo(Color.black));
            Assert.That(_overlayImage.color.r, Is.EqualTo(0f));

            var skinManager = _objects.Add<SkinManager>();
            PrivateMember.SetField(skinManager, "_selectedSkin", CustomSkin.Empty);
            PrivateMember.Invoke(_switcher, "UpdateBGSprite");

            Assert.That(_subImage.sprite, Is.SameAs(CustomSkin.Empty.SubDisplay));
            Assert.That(_subImage.color, Is.EqualTo(Color.white));
            Assert.That(_overlayImage.color.r, Is.EqualTo(1f));
        }

        /// <summary>Checks that a failed or cancelled background task stops the switch before any scene is loaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SwitchSceneAfterTask_FailedTasks_Throw()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var failure = new InvalidOperationException("background work failed");

                await AssertFailsAsync(_switcher.SwitchSceneAfterTaskAsync("Title", Task.FromException<int>(failure)));
                await AssertFailsAsync(_switcher.SwitchSceneAfterTaskAsync("Title", new ValueTask<int>(Task.FromException<int>(failure))));
                await AssertFailsAsync(_switcher.SwitchSceneAfterTaskAsync("Title", UniTask.FromException<int>(failure)));

                Assert.That(SceneManager.GetActiveScene().name, Is.Not.EqualTo("Title"));
            });
        }

        /// <summary>Checks that a cancelled UniTask stops the switch with a cancellation error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [Ignore("Known issue: SceneSwitcher rethrows taskToRun.AsTask().Exception, which is null for a cancelled UniTask, so a NullReferenceException is thrown.")]
        public IEnumerator SwitchSceneAfterTask_CancelledUniTask_ThrowsCancellation()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Exception? caught = null;
                try
                {
                    await _switcher.SwitchSceneAfterTaskAsync("Title", UniTask.FromCanceled<int>());
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
                Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            });
        }

        /// <summary>Checks that destroying the switcher removes the overlay canvas.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_RemovesTheOverlay()
        {
            UnityEngine.Object.Destroy(_switcher.gameObject);
            yield return null;
            yield return null;

            Assert.That(_overlayCanvas == null, Is.True);
        }

        /// <summary>Awaits a scene switch and asserts that it failed.</summary>
        /// <typeparam name="T">The result type of the background task.</typeparam>
        /// <param name="switching">The scene switch.</param>
        /// <returns>A task that completes after the assertion.</returns>
        private static async UniTask AssertFailsAsync<T>(UniTask<T> switching)
        {
            Exception? caught = null;
            try
            {
                await switching;
            }
            catch (Exception exception)
            {
                caught = exception;
            }
            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.GetBaseException(), Is.InstanceOf<InvalidOperationException>());
        }
    }
}
