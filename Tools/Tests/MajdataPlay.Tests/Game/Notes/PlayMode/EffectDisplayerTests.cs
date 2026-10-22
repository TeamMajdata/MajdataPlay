using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
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
    /// Covers the touch judgment effect, the hold particle effect, the touch feedback and the firework displayers.
    /// </summary>
    public class EffectDisplayerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

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

        /// <summary>Checks that Start rotates the touch effect towards its sensor and lays out its texts.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_StartRotatesAndLaysOut()
        {
            var displayer = CreateTouchEffect(SensorArea.B3);
            yield return null;

            var expected = NoteHelper.GetTouchRoation(NoteHelper.GetTouchAreaPosition(SensorArea.B3), SensorArea.B3);
            Assert.That(Quaternion.Angle(displayer.transform.rotation, expected), Is.LessThan(0.01f));
            Assert.That(displayer.transform.Find("JudgeEffect").localPosition.y, Is.EqualTo(2.2f).Within(1e-4f));
            Assert.That(displayer.transform.Find("JudgeText").localPosition.y, Is.EqualTo(2.2f - 0.66f).Within(1e-4f));
            Assert.That(displayer.transform.Find("FastLateA").localPosition.y, Is.EqualTo(2.2f - 0.66f - 0.56f).Within(1e-4f));
        }

        /// <summary>Checks the judgment effect animations picked by the touch effect for each grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_PlaysJudgmentEffect()
        {
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;
            var effect = displayer.GetComponentInChildren<TouchJudgeEffectDisplayer>(true);

            foreach (var grade in new[] { JudgeGrade.LateGood, JudgeGrade.FastGreat, JudgeGrade.Perfect })
            {
                displayer.Play(new NoteJudgeResult() { Grade = grade, Diff = 5f });
                Assert.That(effect.Active, Is.True, grade.ToString());
                displayer.Reset();
                Assert.That(effect.Active, Is.False);
            }

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Miss });
            Assert.That(effect.Active, Is.False);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, IsMine = true });
            Assert.That(effect.Active, Is.False);
            effect.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.TooFast });
            Assert.That(effect.Active, Is.False);

            effect.PlayEffect(new NoteJudgeResult() { Grade = JudgeGrade.Perfect });
            displayer.OnLateUpdate();
            Assert.That(PrivateMember.GetField<float>(effect, "_animRemainingTime"), Is.EqualTo((20f / 60f) - (1f / 60f)).Within(1e-5f));
        }

        /// <summary>Checks the touch effect texts: plain text, class C text and direction in the text slot.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_ShowsTexts()
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayBreakScore = false;
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;
            var text = displayer.transform.Find("JudgeText");

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat2nd, Diff = 200f });
            Assert.That(text.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Great_Late"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LatePerfect2nd, Diff = 160f, IsBreak = true });
            Assert.That(text.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Perfect_Late"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 0f });
            Assert.That(text.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Perfect"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 5f, IsBreak = true });
            Assert.That(text.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Perfect_Late"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.TooFast, IsMine = true });
            Assert.That(text.GetComponent<JudgeTextDisplayer>().Active, Is.True);

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, IsMine = true, Diff = 0f });
            Assert.That(text.GetComponent<JudgeTextDisplayer>().Active, Is.False);
        }

        /// <summary>Checks the touch effect with break scores and the critical perfect option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_ShowsBreakScoreAndCriticalText()
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayCriticalPerfect = true;
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;
            var sprite = displayer.transform.Find("JudgeText").GetChild(0).GetComponent<SpriteRenderer>();

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat3rd, Diff = 240f, IsBreak = true });
            Assert.That(sprite.sprite.name, Is.EqualTo("Break_1250_Late"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = -5f });
            Assert.That(sprite.sprite.name, Is.EqualTo("CriticalPerfect_Fast"));
        }

        /// <summary>Checks that the direction moves into the text slot when the touch text is hidden.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_DirectionInTextSlotWhenTextHidden()
        {
            _rig.Settings.Display.TouchJudgeType = JudgeDisplayOption.MissOnly;
            _rig.Settings.Display.BreakJudgeType = JudgeDisplayOption.MissOnly;
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.BreakFastLateType = JudgeDisplayOption.All;
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGood, Diff = 280f });
            Assert.That(displayer.transform.Find("FastLateB").GetComponent<FastLateDisplayer>().Active, Is.True);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGood, Diff = 280f, IsBreak = true });
            Assert.That(displayer.transform.Find("FastLateB").GetComponent<FastLateDisplayer>().Active, Is.True);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGood, Diff = 280f, IsMine = true });
            Assert.That(displayer.transform.Find("FastLateB").GetComponent<FastLateDisplayer>().Active, Is.False);
        }

        /// <summary>Checks that the inner judgment distance setting can disable the touch texts.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_HidesTextsWhenDisabled()
        {
            _rig.Settings.Display.InnerJudgeDistance = 0f;
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect });

            Assert.That(displayer.transform.Find("JudgeText").GetComponent<JudgeTextDisplayer>().Active, Is.False);
            displayer.ResetAll();
            Assert.That(displayer.GetComponentInChildren<TouchJudgeEffectDisplayer>(true).Active, Is.False);
        }

        /// <summary>Checks the particle colour picked for each grade and that a playing effect ignores new grades.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldEffect_ColoursByGrade()
        {
            var effect = _rig.Spawn<HoldEffectDisplayer>(_rig.HoldEffectTemplate, _rig.Objects.Create("Parent").transform);
            var material = effect.GetComponent<ParticleSystemRenderer>().material;
            yield return null;
            Assert.That(effect.Active, Is.False);

            effect.Play(JudgeGrade.LateGreat);
            effect.OnLateUpdate();
            Assert.That(effect.Active, Is.True);
            Assert.That(material.color, Is.EqualTo(new Color(1f, 0.70f, 0.94f)));

            effect.Play(JudgeGrade.Perfect);
            effect.OnLateUpdate();
            Assert.That(material.color, Is.EqualTo(new Color(1f, 0.70f, 0.94f)));

            effect.Reset();
            Assert.That(effect.Active, Is.False);
            effect.OnLateUpdate();
            effect.Play(JudgeGrade.FastGood);
            effect.OnLateUpdate();
            Assert.That(material.color, Is.EqualTo(new Color(0.56f, 1f, 0.59f)));

            effect.Reset();
            effect.Play(JudgeGrade.FastPerfect2nd);
            effect.OnLateUpdate();
            Assert.That(material.color, Is.EqualTo(new Color(1f, 227f / 255f, 0f)));

            effect.Reset();
            effect.Play(JudgeGrade.Miss);
            effect.OnLateUpdate();
            Assert.That(material.color, Is.EqualTo(Color.white));
            effect.Reset();
            effect.Play(JudgeGrade.TooFast);
            Assert.That(effect.Active, Is.True);
        }

        /// <summary>Checks the position accessors of the hold effect, which keep the sorting depth.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldEffect_PositionsKeepDepth()
        {
            var effect = _rig.Spawn<HoldEffectDisplayer>(_rig.HoldEffectTemplate, _rig.Objects.Create("Parent").transform);
            effect.transform.localPosition = new Vector3(0f, 0f, -1f);
            yield return null;

            effect.Position = new Vector3(1f, 2f, 5f);
            Assert.That(effect.Position, Is.EqualTo(new Vector3(1f, 2f, -1f)));
            effect.LocalPosition = new Vector3(3f, 4f, 5f);
            Assert.That(effect.LocalPosition, Is.EqualTo(new Vector3(3f, 4f, -1f)));
        }

        /// <summary>Checks that the touch feedback shows on play and hides on reset.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchFeedback_PlaysAndResets()
        {
            var feedback = _rig.Spawn<TouchFeedbackDisplayer>(_rig.FeedbackTemplate, _rig.Objects.Create("Parent").transform);
            yield return null;
            Assert.That(feedback.Active, Is.False);
            Assert.That(feedback.transform.GetChild(0).gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));

            feedback.Play();
            Assert.That(feedback.Active, Is.True);
            Assert.That(feedback.transform.GetChild(0).gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            feedback.Play();

            feedback.Reset();
            Assert.That(feedback.Active, Is.False);
        }

        /// <summary>Checks that a firework request moves the firework, shows it and hides it after its animation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Firework_ShowsAtRequestedPositionThenHides()
        {
            var firework = _rig.Firework;
            var renderer = firework.transform.GetChild(1).GetComponent<SpriteRenderer>();
            yield return null;
            Assert.That(Majdata<FireworkDisplayer>.Instance, Is.SameAs(firework));
            firework.OnLateUpdate();

            firework.Play(new Vector3(1f, 2f, 7f));
            firework.OnLateUpdate();
            Assert.That(firework.Active, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(firework.transform.position, Is.EqualTo(new Vector3(1f, 2f, 0f)));

            NoteTestEnvironment.SetDeltaTime(1f);
            firework.OnLateUpdate();
            firework.OnLateUpdate();
            Assert.That(firework.Active, Is.True);
            firework.OnLateUpdate();

            Assert.That(firework.Active, Is.False);
            Assert.That(renderer.enabled, Is.False);

            Object.Destroy(firework.gameObject);
            yield return null;
            Assert.That(Majdata<FireworkDisplayer>.IsNull, Is.True);
        }

        /// <summary>Checks that every touch hit shows the merged judgment and direction text when fast/late display is on.</summary>
        /// <param name="testCase">The break flag, break score option and critical perfect option.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TouchEffect_ShowsClassCTextForEveryHit([ValueSource(typeof(ClassCCases), nameof(ClassCCases.Options))] object[] testCase)
        {
            _rig.Settings.Display.FastLateType = JudgeDisplayOption.All;
            _rig.Settings.Display.DisplayBreakScore = (bool)testCase[1];
            _rig.Settings.Display.DisplayCriticalPerfect = (bool)testCase[2];
            var displayer = CreateTouchEffect(SensorArea.C);
            yield return null;
            var sprite = displayer.transform.Find("JudgeText").GetChild(0).GetComponent<SpriteRenderer>();

            foreach (var (grade, diff, suffix) in ClassCCases.Hits())
            {
                displayer.Play(new NoteJudgeResult() { Grade = grade, Diff = diff, IsBreak = (bool)testCase[0] });
                Assert.That(sprite.sprite.name, Does.EndWith(suffix), grade.ToString());
            }
        }

        /// <summary>Creates a touch effect displayer for a sensor.</summary>
        /// <param name="area">The sensor.</param>
        /// <returns>The displayer.</returns>
        private TouchEffectDisplayer CreateTouchEffect(SensorArea area)
        {
            var displayer = _rig.Spawn<TouchEffectDisplayer>(_rig.TouchEffectTemplate, _rig.Objects.Create("Parent").transform);
            displayer.SensorPos = area;
            return displayer;
        }
    }
}
