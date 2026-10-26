using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="TapEffectDisplayer"/> and <see cref="TouchHoldEffectDisplayer"/> with their perfect, great and good
    /// effects: effect selection, judgment text and fast/late choices for every display setting, and the text layout.
    /// </summary>
    public class TapEffectDisplayerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the effect cases as grade, break flag and the expected active effect name, or null for none.</summary>
        private static object[] EffectCases => new object[]
        {
            new object[] { JudgeGrade.Perfect, false, "Perfect" },
            new object[] { JudgeGrade.LatePerfect3rd, false, "Perfect" },
            new object[] { JudgeGrade.FastGreat2nd, false, "Great" },
            new object[] { JudgeGrade.LateGreat, false, "Great" },
            new object[] { JudgeGrade.FastGood, false, "Good" },
            new object[] { JudgeGrade.LateGood, false, "Good" },
            new object[] { JudgeGrade.LateGood, true, "Perfect" },
            new object[] { JudgeGrade.FastGreat3rd, true, "Perfect" },
            new object[] { JudgeGrade.FastPerfect2nd, true, "Perfect" },
            new object[] { JudgeGrade.Miss, false, "" },
            new object[] { JudgeGrade.TooFast, true, "" }
        };

        /// <summary>Builds the gameplay objects.</summary>
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

        /// <summary>Checks that Start lays out the judgment text and fast/late texts exactly their distance below a centred effect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_LaysOutTexts()
        {
            var displayer = Create(false);
            yield return null;

            Assert.That(Child(displayer, "JudgeText").localPosition.y, Is.EqualTo(-1f).Within(1e-3f));
            Assert.That(Child(displayer, "FastLateA").localPosition.y, Is.EqualTo(-1.66f).Within(1e-3f));
            Assert.That(Child(displayer, "FastLateB").localPosition.y, Is.EqualTo(-1f).Within(1e-3f));
            Assert.That(PrivateMember.GetField<bool>(displayer, "_isEnabled"), Is.True);
        }

        /// <summary>Checks that moving the effect moves the texts towards the centre, scaled by the distance ratio.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LocalPosition_MovesTextsTowardsCentre()
        {
            var displayer = Create(false);
            yield return null;

            displayer.DistanceRatio = 0.5f;
            displayer.LocalPosition = new Vector3(0f, 4f, 0f);
            Assert.That(displayer.LocalPosition.y, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(displayer.Position.y, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(Child(displayer, "JudgeText").localPosition.y, Is.EqualTo(2.5f).Within(1e-3f));
            Assert.That(Child(displayer, "FastLateA").localPosition.y, Is.EqualTo(1.84f).Within(1e-3f));

            displayer.LocalPosition = Vector3.zero;
            Assert.That(Child(displayer, "JudgeText").localPosition.y, Is.EqualTo(-1.5f).Within(1e-3f));
        }

        /// <summary>Checks which effect plays for each grade and break flag.</summary>
        /// <param name="testCase">The grade, break flag and expected active effect name, empty for none.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_PicksEffect([ValueSource(nameof(EffectCases))] object[] testCase)
        {
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = (JudgeGrade)testCase[0], IsBreak = (bool)testCase[1], Diff = 5f });

            var expected = (string)testCase[2];
            Assert.That(Effect(displayer, "Perfect").Active, Is.EqualTo(expected == "Perfect"));
            Assert.That(Effect(displayer, "Great").Active, Is.EqualTo(expected == "Great"));
            Assert.That(Effect(displayer, "Good").Active, Is.EqualTo(expected == "Good"));
            Assert.That(Text(displayer).Active, Is.True);
        }

        /// <summary>Checks that mines play no effect and show a text only when they were touched.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_MineShowsTextOnlyWhenTouched()
        {
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, IsMine = true, Diff = 5f });
            Assert.That(Effect(displayer, "Perfect").Active, Is.False);
            Assert.That(Text(displayer).Active, Is.False);
            Assert.That(FastLate(displayer, "FastLateB").Active, Is.False);

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.TooFast, IsMine = true, Diff = -5f });
            Assert.That(Text(displayer).Active, Is.True);
        }

        /// <summary>Checks the class C text, which merges the judgment and its direction, when fast/late display is on.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ShowsClassCTextWhenFastLateIsShown()
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, Diff = 60f });
            Assert.That(TextSprite(displayer), Is.EqualTo("Great_Late"));
            Assert.That(FastLate(displayer, "FastLateA").Active, Is.False);

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = -5f });
            Assert.That(TextSprite(displayer), Is.EqualTo("Perfect_Fast"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 0f });
            Assert.That(TextSprite(displayer), Is.EqualTo("Perfect"));
            Assert.That(FastLate(displayer, "FastLateA").Active, Is.False);
        }

        /// <summary>Checks the class C break text with break scores and the critical perfect variants.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ShowsClassCBreakAndCriticalText()
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.BreakFastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayCriticalPerfect = true;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.FastGreat2nd, Diff = -60f, IsBreak = true });
            Assert.That(TextSprite(displayer), Is.EqualTo("Break_1500_Fast"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 5f });
            Assert.That(TextSprite(displayer), Is.EqualTo("CriticalPerfect_Late"));
        }

        /// <summary>Checks the class C break text when break scores are hidden.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ShowsClassCBreakTextWithoutBreakScore()
        {
            _rig.Settings.Display.BreakFastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayBreakScore = false;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.FastPerfect2nd, Diff = -20f, IsBreak = true });
            Assert.That(TextSprite(displayer), Is.EqualTo("Perfect_Fast"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 0f, IsBreak = true });
            Assert.That(TextSprite(displayer), Is.EqualTo("Perfect"));
        }

        /// <summary>Checks that the direction moves into the text slot when the text itself is hidden.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ShowsDirectionInTextSlotWhenTextIsHidden()
        {
            _rig.Settings.Display.NoteJudgeType = JudgeDisplayOption.MissOnly;
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.BelowCP;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, Diff = 60f });

            Assert.That(Text(displayer).Active, Is.False);
            Assert.That(FastLate(displayer, "FastLateB").Active, Is.True);
            Assert.That(FastLate(displayer, "FastLateA").Active, Is.False);
        }

        /// <summary>
        /// Checks that breaks below the break fast/late threshold show the plain text and breaks above it show the merged
        /// class C text instead of a separate direction.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_BreakFastLateThresholdSelectsText()
        {
            _rig.Settings.Display.BreakFastLateType = JudgeDisplayOption.BelowP;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LatePerfect2nd, Diff = 20f, IsBreak = true });
            Assert.That(TextSprite(displayer), Is.EqualTo("Break_2550"));
            Assert.That(FastLate(displayer, "FastLateA").Active, Is.False);

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, Diff = 60f, IsBreak = true });
            Assert.That(TextSprite(displayer), Is.EqualTo("Break_2000_Late"));
            Assert.That(FastLate(displayer, "FastLateA").Active, Is.False);
        }

        /// <summary>Checks that no text is shown when the outer judgment distance is zero.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_HidesTextsWhenDisabled()
        {
            _rig.Settings.Display.OuterJudgeDistance = 0f;
            var displayer = Create(false);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect });

            Assert.That(Effect(displayer, "Perfect").Active, Is.True);
            Assert.That(Text(displayer).Active, Is.False);
        }

        /// <summary>Checks that the touch hold variant uses the touch judgment display setting.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchHoldVariant_UsesTouchJudgeSetting()
        {
            _rig.Settings.Display.TouchJudgeType = JudgeDisplayOption.Disable;
            var displayer = Create(true);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 5f });
            Assert.That(Text(displayer).Active, Is.False);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 5f, IsBreak = true });
            Assert.That(Text(displayer).Active, Is.True);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 5f, IsMine = true });
            Assert.That(Text(displayer).Active, Is.False);
        }

        /// <summary>Checks that resetting hides every effect and text and that late updates advance the animations.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResetAndLateUpdate_ManageEffects()
        {
            var displayer = Create(false);
            yield return null;
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect });
            var perfect = Effect(displayer, "Perfect");

            displayer.OnLateUpdate();
            Assert.That(PrivateMember.GetField<float>(perfect, "_animRemainingTime"), Is.EqualTo((48f / 60f) - (1f / 60f)).Within(1e-5f));
            Assert.That(perfect.transform.GetChild(0).gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));

            displayer.ResetAll();

            Assert.That(perfect.Active, Is.False);
            Assert.That(perfect.transform.GetChild(0).gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(Text(displayer).Active, Is.False);
        }

        /// <summary>Checks the great and good effects' animation bookkeeping and their reset.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GreatAndGoodEffects_Animate()
        {
            var displayer = Create(false);
            yield return null;
            var great = displayer.GetComponentInChildren<TapGreatEffectDisplayer>(true);
            var good = displayer.GetComponentInChildren<TapGoodEffectDisplayer>(true);

            great.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, IsBreak = true });
            good.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.LateGood, IsBreak = true });
            Assert.That(great.Active, Is.False);
            Assert.That(good.Active, Is.False);

            great.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat });
            good.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.LateGood });
            great.OnLateUpdate();
            good.OnLateUpdate();

            Assert.That(PrivateMember.GetField<float>(great, "_animRemainingTime"), Is.EqualTo((26f / 60f) - (1f / 60f)).Within(1e-5f));
            Assert.That(PrivateMember.GetField<float>(good, "_animRemainingTime"), Is.EqualTo((32f / 60f) - (1f / 60f)).Within(1e-5f));
            great.Reset();
            good.Reset();
            Assert.That(great.Active, Is.False);
            Assert.That(good.Active, Is.False);
        }

        /// <summary>Checks that every hit shows the merged judgment and direction text when fast/late display is on.</summary>
        /// <param name="testCase">The break flag, break score option and critical perfect option.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ShowsClassCTextForEveryHit([ValueSource(typeof(ClassCCases), nameof(ClassCCases.Options))] object[] testCase)
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.BreakFastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayBreakScore = (bool)testCase[1];
            _rig.Settings.Display.DisplayCriticalPerfect = (bool)testCase[2];
            var displayer = Create(false);
            yield return null;

            foreach (var (grade, diff, suffix) in ClassCCases.Hits())
            {
                displayer.Play(new NoteJudgeResult() { Grade = grade, Diff = diff, IsBreak = (bool)testCase[0] });
                Assert.That(TextSprite(displayer), Does.EndWith(suffix), grade.ToString());
            }
        }

        /// <summary>Creates a tap or touch hold effect displayer.</summary>
        /// <param name="isTouchHold">Whether to create the touch hold variant.</param>
        /// <returns>The displayer.</returns>
        private TapEffectDisplayer Create(bool isTouchHold)
        {
            var template = isTouchHold ? _rig.TouchHoldEffectTemplate : _rig.TapEffectTemplate;
            return _rig.Spawn<TapEffectDisplayer>(template, _rig.Objects.Create("Parent").transform);
        }

        /// <summary>Finds a direct child transform.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <param name="name">The child name.</param>
        /// <returns>The child transform.</returns>
        private static Transform Child(TapEffectDisplayer displayer, string name)
        {
            return displayer.transform.Find(name);
        }

        /// <summary>Gets an effect component below the effect parent.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <param name="name">The effect name.</param>
        /// <returns>The effect component.</returns>
        private static MajComponent Effect(TapEffectDisplayer displayer, string name)
        {
            return displayer.transform.Find($"Effects/{name}").GetComponent<MajComponent>();
        }

        /// <summary>Gets the judgment text displayer.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <returns>The judgment text displayer.</returns>
        private static JudgeTextDisplayer Text(TapEffectDisplayer displayer)
        {
            return Child(displayer, "JudgeText").GetComponent<JudgeTextDisplayer>();
        }

        /// <summary>Gets the sprite name of the judgment text.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <returns>The sprite name.</returns>
        private static string TextSprite(TapEffectDisplayer displayer)
        {
            return Child(displayer, "JudgeText").GetChild(0).GetComponent<SpriteRenderer>().sprite.name;
        }

        /// <summary>Gets a fast/late displayer.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <param name="name">The fast/late displayer name.</param>
        /// <returns>The fast/late displayer.</returns>
        private static FastLateDisplayer FastLate(TapEffectDisplayer displayer, string name)
        {
            return Child(displayer, name).GetComponent<FastLateDisplayer>();
        }
    }
}
