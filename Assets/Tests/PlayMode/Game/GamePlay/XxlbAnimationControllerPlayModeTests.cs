using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the dance frames chosen by <see cref="XxlbAnimationController"/> in Play Mode.</summary>
    public class XxlbAnimationControllerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the registered controller.</summary>
        private SingletonScope<XxlbAnimationController> _slot = null!;

        /// <summary>The five dance frames.</summary>
        private Sprite[] _sprites = null!;

        /// <summary>Creates the dance frames.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _slot = new SingletonScope<XxlbAnimationController>();
            var texture = _objects.Track(new Texture2D(5, 1));
            _sprites = new Sprite[5];
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = _objects.Track(Sprite.Create(texture, new Rect(i, 0, 1, 1), Vector2.zero));
            }
        }

        /// <summary>Destroys the controller and restores the registration.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _slot.Dispose();
        }

        /// <summary>Checks that the controller registers itself and unregisters when destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AwakeAndOnDestroy_RegisterTheController()
        {
            var controller = CreateController(out _);
            Assert.That(Majdata<XxlbAnimationController>.Instance, Is.SameAs(controller));

            Object.Destroy(controller.gameObject);
            yield return null;

            Assert.That(Majdata<XxlbAnimationController>.Instance, Is.Null);
        }

        /// <summary>Checks the left, centre, right, centre cycle for perfect judgements.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dance_CyclesThroughThePerfectFrames()
        {
            var controller = CreateController(out var image);
            yield return null;

            var frames = new Sprite[5];
            for (var i = 0; i < frames.Length; i++)
            {
                controller.Dance(i % 2 == 0 ? JudgeGrade.Perfect : JudgeGrade.FastPerfect2nd);
                frames[i] = image.sprite;
            }

            Assert.That(frames, Is.EqualTo(new[] { _sprites[0], _sprites[1], _sprites[2], _sprites[1], _sprites[0] }));
        }

        /// <summary>Checks the frames used for non-perfect judgements and the stepping frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dance_UsesTheOtherFramesForLowerGrades()
        {
            var controller = CreateController(out var image);
            yield return null;

            var frames = new Sprite[4];
            for (var i = 0; i < frames.Length; i++)
            {
                controller.Dance(i % 2 == 0 ? JudgeGrade.Miss : JudgeGrade.LateGreat);
                frames[i] = image.sprite;
            }
            controller.Stepping();

            Assert.That(frames, Is.EqualTo(new[] { _sprites[3], _sprites[1], _sprites[4], _sprites[1] }));
            Assert.That(image.sprite, Is.SameAs(_sprites[1]));
        }

        /// <summary>Creates an active controller with an image and an animator.</summary>
        /// <param name="image">Receives the image showing the frames.</param>
        /// <returns>The controller.</returns>
        private XxlbAnimationController CreateController(out Image image)
        {
            var root = _objects.Create("Xxlb");
            root.SetActive(false);
            image = root.AddComponent<Image>();
            root.AddComponent<Animator>();
            var controller = root.AddComponent<XxlbAnimationController>();
            controller.sprites = _sprites;
            root.SetActive(true);
            return controller;
        }
    }
}
