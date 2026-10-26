using System.Collections;
using LitMotion;
using MajdataPlay.Scenes.Result.Components.Indicators;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using Unity.VectorGraphics;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ResultScene
{
    /// <summary>Covers the icon states and text of the online score upload indicator.</summary>
    public class ScoreUploadIndicatorTests : SceneTestBase
    {
        /// <summary>The indicator under test.</summary>
        private ScoreUploadIndicator _indicator = null!;

        /// <summary>The uploading icon.</summary>
        private SVGImage _uploading = null!;

        /// <summary>The success icon.</summary>
        private SVGImage _success = null!;

        /// <summary>The error icon.</summary>
        private SVGImage _error = null!;

        /// <summary>The status text.</summary>
        private TextMeshProUGUI _text = null!;

        /// <summary>Creates the indicator and its icons.</summary>
        [SetUp]
        public void SetUp()
        {
            _uploading = Objects.Add<SVGImage>("Uploading");
            _success = Objects.Add<SVGImage>("Success");
            _error = Objects.Add<SVGImage>("Error");
            _text = CreateText();
            _indicator = AddInactive<ScoreUploadIndicator>();
            PrivateMember.SetField(_indicator, "_uploadingIconDisplayer", _uploading);
            PrivateMember.SetField(_indicator, "_successIconDisplayer", _success);
            PrivateMember.SetField(_indicator, "_errorIconDisplayer", _error);
            PrivateMember.SetField(_indicator, "_textDisplayer", _text);
            Activate(_indicator);
        }

        /// <summary>Checks that uploading shows only the uploading icon and pulses its alpha.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetUploading_ShowsUploadingIconAndAnimatesAlpha()
        {
            _indicator.SetUploading();
            yield return null;
            yield return null;

            Assert.That(_uploading.gameObject.activeSelf, Is.True);
            Assert.That(_success.gameObject.activeSelf, Is.False);
            Assert.That(_error.gameObject.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<MotionHandle>(_indicator, "_iconAnimHandle").IsActive(), Is.True);
            Assert.That(_uploading.color.r, Is.EqualTo(0.3098039f).Within(1e-4f));

            // The looping motion is not bound to the indicator's lifetime, so the test stops it explicitly.
            _indicator.SetError();
        }

        /// <summary>Checks that success replaces the uploading state and stops the animation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSuccess_ShowsSuccessIconAndStopsAnimation()
        {
            _indicator.SetUploading();
            yield return null;

            _indicator.SetSuccess();

            Assert.That(_uploading.gameObject.activeSelf, Is.False);
            Assert.That(_success.gameObject.activeSelf, Is.True);
            Assert.That(_error.gameObject.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<MotionHandle>(_indicator, "_iconAnimHandle").IsActive(), Is.False);
        }

        /// <summary>Checks that an error replaces the uploading state and stops the animation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetError_ShowsErrorIconAndStopsAnimation()
        {
            _indicator.SetUploading();
            yield return null;

            _indicator.SetError();

            Assert.That(_uploading.gameObject.activeSelf, Is.False);
            Assert.That(_success.gameObject.activeSelf, Is.False);
            Assert.That(_error.gameObject.activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<MotionHandle>(_indicator, "_iconAnimHandle").IsActive(), Is.False);
        }

        /// <summary>Checks that destroying the indicator while uploading stops its looping icon motion.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_WhileUploading_StopsLoopingMotion()
        {
            _indicator.SetUploading();
            yield return null;
            var handle = PrivateMember.GetField<MotionHandle>(_indicator, "_iconAnimHandle");

            UnityEngine.Object.DestroyImmediate(_indicator.gameObject);
            var isStillActive = handle.IsActive();
            handle.TryCancel();

            Assert.That(isStillActive, Is.False);
        }

        /// <summary>Checks that the status text is forwarded unchanged.</summary>
        [Test]
        public void SetText_UpdatesStatusText()
        {
            _indicator.SetText("Uploading score");

            Assert.That(_text.text, Is.EqualTo("Uploading score"));
        }
    }
}
