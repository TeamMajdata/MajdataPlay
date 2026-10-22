using System.Collections;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="SlideOK"/>, the slide judgment result: sprite selection per grade, shape and facing, sorting and
    /// its half-second lifetime.
    /// </summary>
    public class SlideOKTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the grade cases as grade and expected judge offset into the result sprite table.</summary>
        private static object[] GradeCases => new object[]
        {
            new object[] { JudgeGrade.Perfect, 6 },
            new object[] { JudgeGrade.FastPerfect2nd, 12 },
            new object[] { JudgeGrade.FastPerfect3rd, 12 },
            new object[] { JudgeGrade.FastGreat, 18 },
            new object[] { JudgeGrade.FastGreat2nd, 18 },
            new object[] { JudgeGrade.FastGreat3rd, 18 },
            new object[] { JudgeGrade.FastGood, 24 },
            new object[] { JudgeGrade.LatePerfect2nd, 30 },
            new object[] { JudgeGrade.LatePerfect3rd, 30 },
            new object[] { JudgeGrade.LateGreat, 36 },
            new object[] { JudgeGrade.LateGreat2nd, 36 },
            new object[] { JudgeGrade.LateGreat3rd, 36 },
            new object[] { JudgeGrade.LateGood, 42 },
            new object[] { JudgeGrade.Miss, 48 },
            new object[] { JudgeGrade.TooFast, 54 }
        };

        /// <summary>Builds the gameplay objects and lets Unity run their Start messages.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializePools: false, initializeEffects: false);
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that Awake hides the result and that Start hands out increasing sorting orders.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Lifecycle_HidesResultAndAssignsSortingOrder()
        {
            var first = NoteTemplates.AddSlideOK(_rig.SlideParent);
            var second = NoteTemplates.AddSlideOK(_rig.SlideParent);

            Assert.That(first.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(first.GetComponent<Animator>().enabled, Is.False);
            Assert.That(first.State, Is.EqualTo(NoteStatus.Start));
            yield return null;

            var firstOrder = PrivateMember.GetField<int>(first, "_sortingOrder");
            Assert.That(PrivateMember.GetField<int>(second, "_sortingOrder"), Is.EqualTo(firstOrder + 1));

            Object.Destroy(first.gameObject);
            yield return null;
            Assert.That(PrivateMember.GetStaticField<int>(typeof(SlideOK), "s_GlobalSortingOrder"), Is.EqualTo(0));
        }

        /// <summary>Checks the sprite picked for every grade with a right-facing curved result.</summary>
        /// <param name="testCase">The grade and the expected judge offset.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayResult_PicksSpriteForGrade([ValueSource(nameof(GradeCases))] object[] testCase)
        {
            var result = NoteTemplates.AddSlideOK(_rig.SlideParent);
            yield return null;
            result.Shape = SlideOKShape.Curv;
            Assert.That(result.SetR(), Is.EqualTo((int)SlideOKShape.Curv));

            result.PlayResult(new NoteJudgeResult() { Grade = (JudgeGrade)testCase[0] });

            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo($"Just[{(int)testCase[1]}]"));
            Assert.That(result.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(result.GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(result.Active, Is.True);
        }

        /// <summary>Checks the critical perfect sprite and the left-facing offset of a straight result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayResult_UsesCriticalPerfectWhenEnabled()
        {
            _rig.Settings.Display.DisplayCriticalPerfect = true;
            var result = NoteTemplates.AddSlideOK(_rig.SlideParent);
            yield return null;
            result.Shape = SlideOKShape.Str;
            Assert.That(result.SetL(), Is.EqualTo((int)SlideOKShape.Str));
            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[4]"));
            result.PlayResult(new NoteJudgeResult() { Grade = JudgeGrade.FastGood });
            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[28]"));

            result.PlayResult(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, IsBreak = true });

            Assert.That(result.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[4]"));
        }

        /// <summary>Checks that classic and modern results are sorted with opposite tie-breaking.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayResult_SortsByStyle()
        {
            var modern = NoteTemplates.AddSlideOK(_rig.SlideParent);
            var classic = NoteTemplates.AddSlideOK(_rig.SlideParent);
            classic.IsClassic = true;
            yield return null;

            modern.PlayResult(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat });
            classic.PlayResult(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat });

            const int Stride = 13107;
            var great = short.MinValue + (Stride * 2);
            var modernOrder = great + Stride - PrivateMember.GetField<int>(modern, "_sortingOrder");
            var classicOrder = great + PrivateMember.GetField<int>(classic, "_sortingOrder");
            Assert.That(modern.transform.localPosition.z, Is.EqualTo(NoteHelper.Sorting.GetZIndex(NoteHelper.Sorting.NoteLayer.SlideOK, modernOrder)).Within(1e-6f));
            Assert.That(classic.transform.localPosition.z, Is.EqualTo(NoteHelper.Sorting.GetZIndex(NoteHelper.Sorting.NoteLayer.SlideOK, classicOrder)).Within(1e-6f));
        }

        /// <summary>Checks that a played result hides itself after half a second.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_EndsAfterHalfASecond()
        {
            var result = NoteTemplates.AddSlideOK(_rig.SlideParent);
            yield return null;
            result.PlayResult(new NoteJudgeResult() { Grade = JudgeGrade.Perfect });

            NoteTestEnvironment.SetDeltaTime(0.3f);
            result.OnUpdate();
            result.OnUpdate();
            Assert.That(result.State, Is.EqualTo(NoteStatus.Running));
            result.OnUpdate();

            Assert.That(result.State, Is.EqualTo(NoteStatus.End));
            Assert.That(result.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(result.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
        }

        /// <summary>Checks that showing or hiding an already shown or hidden result changes nothing.</summary>
        [Test]
        public void SetActive_IgnoresRepeatedState()
        {
            var result = NoteTemplates.AddSlideOK(_rig.SlideParent);
            var renderer = result.GetComponent<SpriteRenderer>();

            result.SetActive(false);
            Assert.That(renderer.enabled, Is.False);
            result.SetActive(true);
            Assert.That(renderer.enabled, Is.True);
            renderer.enabled = false;
            result.SetActive(true);

            Assert.That(renderer.enabled, Is.False);
        }
    }
}
