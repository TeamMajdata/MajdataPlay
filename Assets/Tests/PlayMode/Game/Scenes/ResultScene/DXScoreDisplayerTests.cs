using System.Collections;
using LitMotion;
using MajdataPlay.Scenes.Result.Components;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ResultScene
{
    /// <summary>Covers the DX score slider and text animation of the result screen.</summary>
    public class DXScoreDisplayerTests : SceneTestBase
    {
        /// <summary>The displayer under test.</summary>
        private DXScoreDisplayer _displayer = null!;

        /// <summary>The slider showing the DX score ratio.</summary>
        private Slider _slider = null!;

        /// <summary>The text showing the DX score.</summary>
        private TextMeshProUGUI _text = null!;

        /// <summary>Creates the displayer with its slider and text under a canvas.</summary>
        [SetUp]
        public void SetUp()
        {
            var canvas = CreateCanvas();
            _slider = Objects.Add<Slider>("Slider", canvas);
            _text = CreateText("Score", canvas);
            _displayer = AddInactive<DXScoreDisplayer>("DXScore", canvas);
            PrivateMember.SetField(_displayer, "_dxScoreSlider", _slider);
            PrivateMember.SetField(_displayer, "_dxScoreDisplayer", _text);
            Activate(_displayer);
        }

        /// <summary>Completes the slider and text motions immediately.</summary>
        private void CompleteAnimations()
        {
            PrivateMember.GetField<MotionHandle>(_displayer, "_slideBarAnim").TryComplete();
            PrivateMember.GetField<MotionHandle>(_displayer, "_scoreTextAnim").TryComplete();
        }

        /// <summary>Checks that a high score animates to its ratio and shows the star rank prefix.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetScore_WithRank_AnimatesToRatioAndRankedText()
        {
            _displayer.SetScore(new GameResult { DXScore = 960, TotalDXScore = 1000 }, 0.5f);
            yield return null;
            CompleteAnimations();

            Assert.That(_slider.value, Is.EqualTo(0.96f).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("<Color=#A2C830>✧4<Color=#4C3A37> 960/1000"));
        }

        /// <summary>Checks that a low score has no rank prefix.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetScore_WithoutRank_ShowsPlainScore()
        {
            _displayer.SetScore(new GameResult { DXScore = 100, TotalDXScore = 1000 }, 0.5f);
            Assert.That(_text.text, Is.EqualTo("<Color=#4C3A37> 0/1000"));
            yield return null;
            CompleteAnimations();

            Assert.That(_slider.value, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("<Color=#4C3A37> 100/1000"));
        }

        /// <summary>Checks that the text animation passes through intermediate floored scores.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetScore_AnimationShowsIntermediateValues()
        {
            _displayer.SetScore(new GameResult { DXScore = 1000, TotalDXScore = 1000 }, 30f);
            yield return null;
            yield return null;

            Assert.That(_text.text, Does.StartWith("<Color=#A2C830>✧5<Color=#4C3A37> "));
            Assert.That(_text.text, Does.Not.EndWith(" 1000/1000"));
        }

        /// <summary>Checks that previewing after a score replays to the stored target and stopping restores it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewAfterScore_ReplaysAndStopRestoresTarget()
        {
            _displayer.SetScore(new GameResult { DXScore = 940, TotalDXScore = 1000 }, 0.5f);
            yield return null;
            CompleteAnimations();

            _displayer.PlayAnimationPreview(30f);
            yield return null;
            Assert.That(_slider.value, Is.LessThan(0.94f));

            _displayer.StopAnimationPreview();

            Assert.That(_slider.value, Is.EqualTo(0.94f).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("<Color=#A2C830>✧3<Color=#4C3A37> 940/1000"));
        }

        /// <summary>Checks that previewing without a score parses the displayed text and stopping restores the previous state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewWithoutScore_ParsesDisplayedTextAndStopRestoresPrevious()
        {
            _slider.value = 0.25f;
            _text.text = "<Color=#4C3A37> 512/2048";
            _text.ForceMeshUpdate(true, true);

            _displayer.PlayAnimationPreview(0.5f);
            yield return null;
            CompleteAnimations();

            Assert.That(_slider.value, Is.EqualTo(_slider.maxValue).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("<Color=#4C3A37> 512/2048"));

            _displayer.StopAnimationPreview();

            Assert.That(_slider.value, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("<Color=#4C3A37> 512/2048"));
        }

        /// <summary>Checks that previewing without a parsable score only animates the slider.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewWithoutScore_UnparsableTextKeepsText()
        {
            _text.text = "no score";
            _text.ForceMeshUpdate(true, true);

            _displayer.PlayAnimationPreview(0.5f);
            yield return null;
            CompleteAnimations();

            Assert.That(_slider.value, Is.EqualTo(_slider.maxValue).Within(1e-4f));
            Assert.That(_text.text, Is.EqualTo("no score"));
        }

        /// <summary>Checks the number scanning helpers used to parse the displayed score.</summary>
        [Test]
        public void NumberParsers_ReadDigitsAroundAnIndex()
        {
            var before = new object?[] { "ab123/45", 5, 0L };
            var after = new object?[] { "ab123/45x", 6, 0L };
            var afterInt = new object?[] { "✧7 x", 1, 0 };
            var empty = new object?[] { "/", 1, 0L };

            Assert.That((bool)PrivateMember.InvokeStatic(typeof(DXScoreDisplayer), "TryParseNumberBefore", before)!, Is.True);
            Assert.That(before[2], Is.EqualTo(123L));
            Assert.That((bool)PrivateMember.InvokeStatic(typeof(DXScoreDisplayer), "TryParseNumberAfter", after)!, Is.True);
            Assert.That(after[2], Is.EqualTo(45L));
            Assert.That((bool)PrivateMember.InvokeStatic(typeof(DXScoreDisplayer), "TryParseNumberAfter", afterInt)!, Is.True);
            Assert.That(afterInt[2], Is.EqualTo(7));
            Assert.That((bool)PrivateMember.InvokeStatic(typeof(DXScoreDisplayer), "TryParseNumberAfter", empty)!, Is.False);
        }

        /// <summary>Checks that the displayed score parser handles ranked, plain and malformed text.</summary>
        [Test]
        public void TryParseDisplayedScore_HandlesRankedPlainAndMalformedText()
        {
            _text.text = "<Color=#4C3A37> 333/444";
            _text.ForceMeshUpdate(true, true);
            var plain = new object?[] { 0L, 0L, 0 };
            Assert.That((bool)PrivateMember.Invoke(_displayer, "TryParseDisplayedScore", plain)!, Is.True);
            Assert.That(plain[0], Is.EqualTo(333L));
            Assert.That(plain[1], Is.EqualTo(444L));
            Assert.That(plain[2], Is.EqualTo(0));

            _text.text = "/12";
            _text.ForceMeshUpdate(true, true);
            var malformed = new object?[] { 0L, 0L, 0 };
            Assert.That((bool)PrivateMember.Invoke(_displayer, "TryParseDisplayedScore", malformed)!, Is.False);
        }

        /// <summary>Checks the score text format with and without a rank.</summary>
        [Test]
        public void FormatScore_IncludesRankOnlyWhenPositive()
        {
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(DXScoreDisplayer), "FormatScore", 5L, 10L, 2), Is.EqualTo("<Color=#A2C830>✧2<Color=#4C3A37> 5/10"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(DXScoreDisplayer), "FormatScore", 5L, 10L, 0), Is.EqualTo("<Color=#4C3A37> 5/10"));
        }

        /// <summary>Checks that destroying the displayer cancels running animations.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_CancelsRunningAnimations()
        {
            _displayer.SetScore(new GameResult { DXScore = 500, TotalDXScore = 1000 }, 30f);
            yield return null;
            var handle = PrivateMember.GetField<MotionHandle>(_displayer, "_slideBarAnim");
            Assert.That(handle.IsActive(), Is.True);

            UnityEngine.Object.DestroyImmediate(_displayer.gameObject);

            Assert.That(handle.IsActive(), Is.False);
        }
    }
}
