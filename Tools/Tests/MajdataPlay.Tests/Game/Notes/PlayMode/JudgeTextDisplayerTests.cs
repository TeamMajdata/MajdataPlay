using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="JudgeTextDisplayer"/> and <see cref="FastLateDisplayer"/>: the break shine choice, the judgment
    /// text sprite for every grade, break score and critical perfect option, and the fast/late text.
    /// </summary>
    public class JudgeTextDisplayerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the tap text cases as grade, offset, class C flag and expected sprite name.</summary>
        private static object[] TapTextCases => new object[]
        {
            new object[] { JudgeGrade.LateGood, 1f, false, "Good" },
            new object[] { JudgeGrade.LateGood, 1f, true, "Good_Late" },
            new object[] { JudgeGrade.FastGood, -1f, false, "Good" },
            new object[] { JudgeGrade.FastGood, -1f, true, "Good_Fast" },
            new object[] { JudgeGrade.LateGreat2nd, 1f, false, "Great" },
            new object[] { JudgeGrade.LateGreat3rd, 1f, true, "Great_Late" },
            new object[] { JudgeGrade.FastGreat, -1f, false, "Great" },
            new object[] { JudgeGrade.FastGreat2nd, -1f, true, "Great_Fast" },
            new object[] { JudgeGrade.LatePerfect2nd, 1f, false, "Perfect" },
            new object[] { JudgeGrade.LatePerfect3rd, 1f, true, "Perfect_Late" },
            new object[] { JudgeGrade.FastPerfect2nd, -1f, false, "Perfect" },
            new object[] { JudgeGrade.FastPerfect3rd, -1f, true, "Perfect_Fast" },
            new object[] { JudgeGrade.Perfect, 0f, false, "Perfect" },
            new object[] { JudgeGrade.Perfect, 0f, true, "Perfect" },
            new object[] { JudgeGrade.Perfect, -1f, true, "Perfect_Fast" },
            new object[] { JudgeGrade.Perfect, 1f, true, "Perfect_Late" },
            new object[] { JudgeGrade.Miss, 0f, false, "Miss" },
            new object[] { JudgeGrade.TooFast, 0f, true, "Miss" }
        };

        /// <summary>Gets the critical perfect text cases as offset, class C flag and expected sprite name.</summary>
        private static object[] CriticalCases => new object[]
        {
            new object[] { 0f, false, "CriticalPerfect" },
            new object[] { 0f, true, "CriticalPerfect" },
            new object[] { -1f, true, "CriticalPerfect_Fast" },
            new object[] { 1f, true, "CriticalPerfect_Late" }
        };

        /// <summary>Gets the break score text cases as grade, offset, class C flag and expected sprite name.</summary>
        private static object[] BreakTextCases => new object[]
        {
            new object[] { JudgeGrade.LateGood, 1f, false, "Break_1000" },
            new object[] { JudgeGrade.LateGood, 1f, true, "Break_1000_Late" },
            new object[] { JudgeGrade.FastGood, -1f, true, "Break_1000_Fast" },
            new object[] { JudgeGrade.LateGreat3rd, 1f, true, "Break_1250_Late" },
            new object[] { JudgeGrade.FastGreat3rd, -1f, false, "Break_1250" },
            new object[] { JudgeGrade.LateGreat2nd, 1f, false, "Break_1500" },
            new object[] { JudgeGrade.FastGreat2nd, -1f, true, "Break_1500_Fast" },
            new object[] { JudgeGrade.LateGreat, 1f, true, "Break_2000_Late" },
            new object[] { JudgeGrade.FastGreat, -1f, false, "Break_2000" },
            new object[] { JudgeGrade.LatePerfect3rd, 1f, true, "Break_2500_Late" },
            new object[] { JudgeGrade.FastPerfect3rd, -1f, true, "Break_2500_Fast" },
            new object[] { JudgeGrade.LatePerfect2nd, 1f, false, "Break_2550" },
            new object[] { JudgeGrade.FastPerfect2nd, -1f, true, "Break_2550_Fast" },
            new object[] { JudgeGrade.Perfect, 0f, false, "Break_2600" },
            new object[] { JudgeGrade.Perfect, 0f, true, "Break_2600" },
            new object[] { JudgeGrade.Perfect, -1f, true, "Break_2600_Fast" },
            new object[] { JudgeGrade.Perfect, 1f, true, "Break_2600_Late" },
            new object[] { JudgeGrade.Miss, 0f, false, "Break_0" }
        };

        /// <summary>Gets the shine cases as critical perfect and break score options with the expected shine sprite.</summary>
        private static object[] ShineCases => new object[]
        {
            new object[] { true, true, "Break_2600_Shine" },
            new object[] { true, false, "CriticalPerfect_Shine" },
            new object[] { false, true, "Break_2600_Shine" },
            new object[] { false, false, "Perfect_Shine" }
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

        /// <summary>Checks the break shine sprite chosen from the critical perfect and break score options.</summary>
        /// <param name="testCase">The critical perfect and break score options followed by the expected shine sprite.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_PicksBreakShine([ValueSource(nameof(ShineCases))] object[] testCase)
        {
            _rig.Settings.Display.DisplayCriticalPerfect = (bool)testCase[0];
            _rig.Settings.Display.DisplayBreakScore = (bool)testCase[1];

            var displayer = CreateJudgeText();
            yield return null;

            Assert.That(displayer.transform.GetChild(1).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo((string)testCase[2]));
            Assert.That(displayer.Active, Is.False);
            Assert.That(displayer.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
        }

        /// <summary>Checks the text sprite for tap grades with and without the class C fast/late variant.</summary>
        /// <param name="testCase">The grade, offset, class C flag and expected sprite name.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_PicksTapText([ValueSource(nameof(TapTextCases))] object[] testCase)
        {
            var displayer = CreateJudgeText();
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = (JudgeGrade)testCase[0], Diff = (float)testCase[1] }, (bool)testCase[2]);

            Assert.That(TextSprite(displayer), Is.EqualTo((string)testCase[3]));
            Assert.That(displayer.Active, Is.True);
            Assert.That(displayer.transform.GetChild(0).gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
        }

        /// <summary>Checks the critical perfect text sprites.</summary>
        /// <param name="testCase">The offset, class C flag and expected sprite name.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_PicksCriticalPerfectText([ValueSource(nameof(CriticalCases))] object[] testCase)
        {
            _rig.Settings.Display.DisplayCriticalPerfect = true;
            var displayer = CreateJudgeText();
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = (float)testCase[0] }, (bool)testCase[1]);

            Assert.That(TextSprite(displayer), Is.EqualTo((string)testCase[2]));
        }

        /// <summary>Checks the break score text sprites.</summary>
        /// <param name="testCase">The grade, offset, class C flag and expected sprite name.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_PicksBreakScoreText([ValueSource(nameof(BreakTextCases))] object[] testCase)
        {
            var displayer = CreateJudgeText();
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = (JudgeGrade)testCase[0], Diff = (float)testCase[1], IsBreak = true }, (bool)testCase[2]);

            Assert.That(TextSprite(displayer), Is.EqualTo((string)testCase[3]));
        }

        /// <summary>Checks that breaks use the plain tap text when break scores are hidden.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_BreakUsesTapTextWithoutBreakScore()
        {
            _rig.Settings.Display.DisplayBreakScore = false;
            var displayer = CreateJudgeText();
            yield return null;

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, Diff = 1f, IsBreak = true });

            Assert.That(TextSprite(displayer), Is.EqualTo("Great"));
        }

        /// <summary>Checks that the animation runs for its length after a result is shown and the text can be hidden.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_RunsAnimationOnce()
        {
            var displayer = CreateJudgeText();
            yield return null;
            displayer.OnLateUpdate();
            Assert.That(PrivateMember.GetField<float>(displayer, "_animRemainingTime"), Is.EqualTo(0f));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, IsBreak = true });
            Assert.That(PrivateMember.GetField<long>(displayer, "_expectedTrigger"), Is.Not.EqualTo(long.MaxValue));
            displayer.OnLateUpdate();

            Assert.That(PrivateMember.GetField<long>(displayer, "_expectedTrigger"), Is.EqualTo(long.MaxValue));
            Assert.That(PrivateMember.GetField<float>(displayer, "_animRemainingTime"), Is.EqualTo((21f / 60f) - (1f / 60f)).Within(1e-5f));

            displayer.Reset();
            Assert.That(displayer.Active, Is.False);
        }

        /// <summary>Checks the world and local position accessors, which keep the sorting depth.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Positions_KeepDepth()
        {
            var displayer = CreateJudgeText();
            displayer.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            yield return null;

            displayer.LocalPosition = new Vector3(1f, 2f, 9f);
            Assert.That(displayer.LocalPosition, Is.EqualTo(new Vector3(1f, 2f, -0.5f)));
            displayer.Position = new Vector3(3f, 4f, 9f);
            Assert.That(displayer.Position.x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(displayer.Position.z, Is.EqualTo(-0.5f).Within(1e-5f));
        }

        /// <summary>Checks the fast and late sprites, the hidden cases and the animation of the fast/late text.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FastLate_ShowsDirectionOnlyForOffsetHits()
        {
            var displayer = NoteTemplates.FastLate("FastLate", _rig.Objects.Create("Parent").transform);
            var text = displayer.transform.GetChild(0).GetComponent<SpriteRenderer>();
            yield return null;
            Assert.That(displayer.Active, Is.False);

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.FastGreat, Diff = -10f });
            Assert.That(displayer.Active, Is.True);
            Assert.That(text.sprite.name, Is.EqualTo("Fast"));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.LateGreat, Diff = 10f, IsBreak = true });
            Assert.That(text.sprite.name, Is.EqualTo("Late"));
            displayer.OnLateUpdate();
            Assert.That(PrivateMember.GetField<long>(displayer, "_expectedTrigger"), Is.EqualTo(long.MaxValue));

            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Perfect, Diff = 0f });
            Assert.That(displayer.Active, Is.False);
            displayer.Play(new NoteJudgeResult() { Grade = JudgeGrade.Miss, Diff = 10f });
            Assert.That(displayer.Active, Is.False);

            displayer.LocalPosition = new Vector3(1f, 1f, 0f);
            displayer.Position = new Vector3(2f, 2f, 0f);
            Assert.That(displayer.Position.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(displayer.LocalPosition.y, Is.EqualTo(2f).Within(1e-5f));
        }

        /// <summary>Creates a judgment text displayer under an active parent.</summary>
        /// <returns>The displayer.</returns>
        private JudgeTextDisplayer CreateJudgeText()
        {
            var holder = _rig.Objects.Create("Holder");
            holder.SetActive(false);
            var displayer = NoteTemplates.JudgeText(holder.transform);
            holder.SetActive(true);
            return displayer;
        }

        /// <summary>Gets the name of the text sprite of a judgment text displayer.</summary>
        /// <param name="displayer">The displayer.</param>
        /// <returns>The sprite name.</returns>
        private static string TextSprite(JudgeTextDisplayer displayer)
        {
            return displayer.transform.GetChild(0).GetComponent<SpriteRenderer>().sprite.name;
        }
    }
}
