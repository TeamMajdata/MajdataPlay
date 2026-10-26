using System;
using System.Collections;
using System.Linq;
using LitMotion;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Result;
using MajdataPlay.Scenes.Result.Components;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ResultScene
{
    /// <summary>
    /// Covers the result screen's animations, achievement formatting and judge-difference graph. The manager runs in its
    /// debug mode, which draws a sample graph instead of reading a finished game.
    /// </summary>
    public class ResultScreenManagerTests : SceneTestBase
    {
        /// <summary>The manager under test.</summary>
        private ResultScreenManager _manager = null!;

        /// <summary>The raw image receiving the judge-difference graph.</summary>
        private RawImage _graph = null!;

        /// <summary>The DX score displayer used by previews.</summary>
        private DXScoreDisplayer _dxScore = null!;

        /// <summary>Creates the manager with every text it animates.</summary>
        [SetUp]
        public void SetUp()
        {
            var canvas = CreateCanvas();
            CreateRuntimeDatabase();
            _graph = Objects.Add<RawImage>("Graph", canvas);
            _graph.rectTransform.sizeDelta = new Vector2(144, 48);
            _dxScore = AddInactive<DXScoreDisplayer>("DXScore", canvas);
            PrivateMember.SetField(_dxScore, "_dxScoreSlider", Objects.Add<Slider>("Slider", canvas));
            PrivateMember.SetField(_dxScore, "_dxScoreDisplayer", CreateText("DXScoreText", canvas));
            Activate(_dxScore);

            _manager = AddInactive<ResultScreenManager>("ResultScreen", canvas);
            _manager.IsDebug = true;
            _manager.accDX = CreateText("Acc", canvas);
            _manager.criticalLabel = CreateText("CriticalLabel", canvas);
            _manager.perfectLabel = CreateText("PerfectLabel", canvas);
            _manager.greatLabel = CreateText("GreatLabel", canvas);
            _manager.goodLabel = CreateText("GoodLabel", canvas);
            _manager.missLabel = CreateText("MissLabel", canvas);
            _manager.criticalCount = CreateText("CriticalCount", canvas);
            _manager.perfectCount = CreateText("PerfectCount", canvas);
            _manager.greatCount = CreateText("GreatCount", canvas);
            _manager.goodCount = CreateText("GoodCount", canvas);
            _manager.missCount = CreateText("MissCount", canvas);
            _manager.clearText = CreateText("Clear", canvas);
            _manager.critColor = Color.yellow;
            _manager.perfectColor = Color.red;
            _manager.greatColor = Color.magenta;
            _manager.goodColor = Color.green;
            _manager._noteJudgeDiffGraph = _graph;
            PrivateMember.SetField(_manager, "_dxScoreDisplayer", _dxScore);
        }

        /// <summary>Activates the manager and lets Start draw the debug graph.</summary>
        /// <returns>The coroutine.</returns>
        private IEnumerator StartManager()
        {
            Activate(_manager);
            yield return null;
        }

        /// <summary>Completes every motion the manager owns.</summary>
        private void CompleteAnimations()
        {
            PrivateMember.GetField<MotionHandle>(_manager, "_achievementRollAnim").TryComplete();
            PrivateMember.GetField<CompositeMotionHandle>(_manager, "_judgeTextFadeAnims").Complete();
            PrivateMember.GetField<CompositeMotionHandle>(_manager, "_clearMarkAnims").Complete();
            PrivateMember.GetField<MotionHandle>(_dxScore, "_slideBarAnim").TryComplete();
            PrivateMember.GetField<MotionHandle>(_dxScore, "_scoreTextAnim").TryComplete();
        }

        /// <summary>Checks that Awake narrows the touch ring edge and debug Start draws a graph matching the image size.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DebugStart_DrawsGraphOfImageSize()
        {
            yield return StartManager();

            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f).Within(1e-4f));
            Assert.That(_graph.texture, Is.Not.Null);
            Assert.That(_graph.texture.width, Is.EqualTo(144));
            Assert.That(_graph.texture.height, Is.EqualTo(48));
        }

        /// <summary>Checks that destroying the manager releases the graph texture and restores the ring edge.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_ReleasesGraphAndRestoresRingEdge()
        {
            yield return StartManager();
            var texture = _graph.texture;

            UnityEngine.Object.DestroyImmediate(_manager.gameObject);

            Assert.That(texture == null, Is.True);
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f).Within(1e-4f));
        }

        /// <summary>Checks that the achievement is truncated, not rounded, with the precision of the judge mode.</summary>
        [Test]
        public void FormatAchievement_TruncatesToModePrecision()
        {
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(ResultScreenManager), "FormatAchievement", 99.99999d, false), Is.EqualTo((99.9999d).ToString("F4") + "%"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(ResultScreenManager), "FormatAchievement", 99.999d, true), Is.EqualTo((99.99d).ToString("F2") + "%"));
            Assert.That(PrivateMember.InvokeStatic<string>(typeof(ResultScreenManager), "FormatAchievement", 0d, true), Is.EqualTo((0d).ToString("F2") + "%"));
        }

        /// <summary>Checks that the achievement roll starts at zero and ends at the truncated target.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayAchievementRollAnimation_RollsFromZeroToTarget()
        {
            yield return StartManager();

            _manager.PlayAchievementRollAnimation(100.51239d, false);
            Assert.That(_manager.accDX.text, Is.EqualTo((0d).ToString("F4") + "%"));
            yield return null;
            CompleteAnimations();

            Assert.That(_manager.accDX.text, Is.EqualTo((100.5123d).ToString("F4") + "%"));
        }

        /// <summary>Checks that previewing reuses the last roll target and stopping shows the final state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewAfterRoll_ReplaysTargetAndStopShowsFinalState()
        {
            yield return StartManager();
            _manager.clearText.text = "AP";
            _manager.PlayAchievementRollAnimation(97.5d, true);
            yield return null;

            Assert.That(_manager.PreviewAchievementRollAnimation(), Is.True);
            yield return null;
            Assert.That(_manager.criticalLabel.color.a, Is.LessThan(1f));

            _manager.StopResultAnimationPreview();

            Assert.That(_manager.accDX.text, Is.EqualTo((97.5d).ToString("F2") + "%"));
            Assert.That(_manager.criticalLabel.color.a, Is.EqualTo(1f));
            Assert.That(_manager.missCount.color.a, Is.EqualTo(1f));
            Assert.That(_manager.clearText.color.a, Is.EqualTo(1f));
        }

        /// <summary>Checks that previewing without a target parses the displayed achievement and infers the mode from its decimals.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewWithoutTarget_ParsesDisplayedAchievement()
        {
            yield return StartManager();
            _manager.accDX.text = (98.5d).ToString("F4") + "%";

            Assert.That(_manager.PreviewAchievementRollAnimation(), Is.True);
            yield return null;
            CompleteAnimations();

            Assert.That(_manager.accDX.text, Is.EqualTo((98.5d).ToString("F4") + "%"));

            PrivateMember.SetField(_manager, "_hasAchievementRollTarget", false);
            _manager.accDX.text = (95.5d).ToString("F2") + "%";
            Assert.That(_manager.PreviewAchievementRollAnimation(), Is.True);
            yield return null;
            CompleteAnimations();

            Assert.That(_manager.accDX.text, Is.EqualTo((95.5d).ToString("F2") + "%"));
        }

        /// <summary>Checks that previewing fails when no target exists and the text is not a number.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviewWithoutTarget_RejectsNonNumericText()
        {
            yield return StartManager();
            _manager.accDX.text = "N/A";

            Assert.That(_manager.PreviewAchievementRollAnimation(), Is.False);
        }

        /// <summary>Checks that judge rows fade in one after another and end fully opaque.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator JudgeTextFade_StartsTransparentAndEndsOpaque()
        {
            yield return StartManager();

            PrivateMember.Invoke(_manager, "PlayJudgeTextFadeInAnimation");
            Assert.That(_manager.missLabel.color.a, Is.EqualTo(0f));
            Assert.That(_manager.criticalCount.color.a, Is.EqualTo(0f));
            yield return null;
            CompleteAnimations();

            Assert.That(_manager.missLabel.color.a, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(_manager.criticalCount.color.a, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>Checks that the clear mark shrinks from an enlarged scale to its original scale and fades in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ClearMark_ScalesDownAndFadesIn()
        {
            yield return StartManager();
            _manager.clearText.text = "FC+";
            _manager.clearText.transform.localScale = new Vector3(2f, 2f, 1f);

            PrivateMember.Invoke(_manager, "PlayClearMarkAnimation");
            Assert.That(_manager.clearText.transform.localScale.x, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(_manager.clearText.color.a, Is.EqualTo(0f));
            yield return null;
            CompleteAnimations();

            Assert.That(_manager.clearText.transform.localScale.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(_manager.clearText.color.a, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>Checks that an empty clear mark stays hidden without starting motions.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ClearMark_EmptyTextStaysHidden()
        {
            yield return StartManager();
            _manager.clearText.text = string.Empty;

            PrivateMember.Invoke(_manager, "PlayClearMarkAnimation");
            yield return null;

            Assert.That(PrivateMember.GetField<CompositeMotionHandle>(_manager, "_clearMarkAnims").Count, Is.EqualTo(0));
            Assert.That(_manager.clearText.color.a, Is.EqualTo(0f));

            _manager.StopResultAnimationPreview();
            Assert.That(_manager.clearText.color.a, Is.EqualTo(0f));
        }

        /// <summary>Checks that the judge-difference graph is transparent without samples and draws bars with samples.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DrawNoteJudgeDiffGraph_DrawsOnlyNonEmptyBins()
        {
            yield return StartManager();

            var empty = DrawGraph(Array.Empty<float>(), 20, 144);
            var samples = new[] { 0f, 1f, -1f, 30f, -45f, 80f, 140f, -149f, 150f, 400f };
            var drawn = DrawGraph(samples, 20, 144);
            try
            {
                Assert.That(empty.GetPixels32().All(pixel => pixel.a == 0), Is.True);
                Assert.That(drawn.GetPixels32().Any(pixel => pixel.a > 0), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(empty);
                UnityEngine.Object.DestroyImmediate(drawn);
            }
        }

        /// <summary>Draws the graph through reflection; spans cannot be boxed, so a delegate invokes the private method.</summary>
        /// <param name="samples">The judge differences in milliseconds.</param>
        /// <param name="height">The texture height.</param>
        /// <param name="width">The texture width.</param>
        /// <returns>The drawn texture.</returns>
        private Texture2D DrawGraph(float[] samples, int height, int width)
        {
            var method = typeof(ResultScreenManager).GetMethod("DrawNoteJudgeDiffGraph", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var draw = (GraphDrawer)Delegate.CreateDelegate(typeof(GraphDrawer), _manager, method);
            return (Texture2D)draw(samples, height, width);
        }

        /// <summary>Matches the private graph drawing method.</summary>
        /// <param name="dataset">The judge differences.</param>
        /// <param name="height">The texture height.</param>
        /// <param name="width">The texture width.</param>
        /// <returns>The drawn texture.</returns>
        private delegate Texture GraphDrawer(ReadOnlySpan<float> dataset, int height, int width);
    }
}
