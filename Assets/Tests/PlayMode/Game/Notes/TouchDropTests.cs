using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Utils;
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
    /// Drives <see cref="TouchDrop"/> through the gameplay loop: initialization, skins, the fade-in and fan animation, the
    /// late-only touch judgment windows, groups, mines, fireworks, autoplay and the button ring option.
    /// </summary>
    public class TouchDropTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the late judgment cases as hit offset in seconds and expected grade.</summary>
        private static object[] WindowCases => new object[]
        {
            new object[] { -0.1f, JudgeGrade.Perfect },
            new object[] { 0.1f, JudgeGrade.Perfect },
            new object[] { 0.16f, JudgeGrade.LatePerfect2nd },
            new object[] { 0.19f, JudgeGrade.LatePerfect3rd },
            new object[] { 0.21f, JudgeGrade.LateGreat },
            new object[] { 0.225f, JudgeGrade.LateGreat2nd },
            new object[] { 0.24f, JudgeGrade.LateGreat3rd },
            new object[] { 0.28f, JudgeGrade.LateGood }
        };

        /// <summary>Gets the skin cases as break, each and mine flags with the expected fan and point sprite names.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, "Touch", "TouchPoint", false },
            new object[] { false, true, false, "Touch_Each", "TouchPoint_Each", false },
            new object[] { true, false, false, "Touch_Break", "TouchPoint_Break", true },
            new object[] { true, true, false, "Touch_Break", "TouchPoint_Break", true },
            new object[] { false, false, true, "Touch_Mine", "TouchPoint_Mine", false },
            new object[] { true, false, true, "Touch_Break_Mine", "TouchPoint_Break_Mine", true }
        };

        /// <summary>Builds the gameplay objects and lets Unity run their Start messages.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig();
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that Awake hides the fans, point and border.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesRenderers()
        {
            var touch = _rig.Spawn<TouchDrop>(_rig.TouchTemplate, _rig.TouchParent);
            yield return null;

            foreach (var renderer in touch.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Assert.That(renderer.enabled, Is.False, renderer.name);
            }
            Assert.That(touch.Active, Is.False);
            Assert.That(touch.transform.GetChild(1).GetComponent<SpriteRenderer>().color.a, Is.EqualTo(0f));
        }

        /// <summary>Checks that Init places the touch on its sensor and copies the pooling information once.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_PlacesTouchOnSensor()
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.E3, timing: 2f, isEach: true, index: 2));
            yield return null;

            touch.Init(GameplayRig.TouchInfo(SensorArea.C, timing: 5f));

            var expected = NoteHelper.GetTouchAreaPosition(SensorArea.E3);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(touch.Timing, Is.EqualTo(2f));
            Assert.That(touch.JudgeTimingWithOffset, Is.EqualTo(2f));
            Assert.That(touch.QueueInfo.SensorPos, Is.EqualTo(SensorArea.E3));
            Assert.That(touch.QueueInfo.Index, Is.EqualTo(2));
            Assert.That(touch.transform.position.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(touch.transform.position.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(touch.Active, Is.True);
        }

        /// <summary>Checks the fan and point sprites and material picked for every touch variant.</summary>
        /// <param name="testCase">
        /// The break, each and mine flags followed by the expected fan and point sprite names and whether the break
        /// material is expected.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(isBreak: (bool)testCase[0], isEach: (bool)testCase[1], isMine: (bool)testCase[2]));
            yield return null;

            for (var i = 1; i <= 4; i++)
            {
                var fan = touch.transform.GetChild(i).GetComponent<SpriteRenderer>();
                Assert.That(fan.sprite.name, Is.EqualTo((string)testCase[3]));
                Assert.That(fan.sharedMaterial, Is.SameAs((bool)testCase[5] ? _rig.BreakMaterial : _rig.DefaultMaterial));
            }
            Assert.That(touch.transform.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo((string)testCase[4]));
            Assert.That(touch.transform.GetChild(5).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("TouchJust"));
        }

        /// <summary>Checks the fade-in, the inward fan movement and the arrival with the just border.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_FadesInMovesAndArrives()
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f));
            var fan = touch.transform.GetChild(3).GetComponent<SpriteRenderer>();
            var point = touch.transform.GetChild(0).GetComponent<SpriteRenderer>();
            var just = touch.transform.GetChild(5).GetComponent<SpriteRenderer>();
            yield return null;

            UpdateAt(touch, 0.5f);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(fan.enabled, Is.False);

            UpdateAt(touch, 0.6f);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(fan.enabled, Is.True);
            Assert.That(point.enabled, Is.True);
            Assert.That(fan.color.a, Is.InRange(0.6f, 0.85f));

            UpdateAt(touch, 0.7f);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(fan.color.a, Is.EqualTo(1f));
            Assert.That(fan.transform.localPosition.y, Is.EqualTo(0.226f + 0.2946f).Within(2e-3f));

            UpdateAt(touch, 1f);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Arrived));
            Assert.That(just.enabled, Is.True);
            Assert.That(fan.transform.localPosition.y, Is.EqualTo(0.226f).Within(1e-3f));

            UpdateAt(touch, 1.1f);
            Assert.That(touch.State, Is.EqualTo(NoteStatus.Arrived));
        }

        /// <summary>Checks that the first visible frame registers the touch with the multiple-touch borders.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_RegistersWithMultipleTouchBorders()
        {
            var first = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B2, timing: 1f, index: 0));
            var second = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B2, timing: 1.05f, isBreak: true, index: 1));
            yield return null;

            UpdateAt(first, 0.7f);
            UpdateAt(second, 0.7f);

            var border = _rig.TouchHandler.transform.GetChild((int)SensorArea.B2);
            Assert.That(border.GetChild(0).GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(border.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("TouchBorder_Break[0]"));
        }

        /// <summary>Checks the late-only touch judgment windows through sensor hits.</summary>
        /// <param name="testCase">The hit offset in seconds and the expected grade.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SensorClick_UsesTouchWindows([ValueSource(nameof(WindowCases))] object[] testCase)
        {
            var offset = (float)testCase[0];
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f + offset);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            var stats = _rig.Counter.JudgeStats;
            Assert.That(stats.TouchJudgedCount, Is.EqualTo(1));
            Assert.That(GradeCount(stats, (JudgeGrade)testCase[1]), Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTouch], Is.True);
        }

        /// <summary>Checks that hits earlier than the first perfect segment are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Judge_IgnoresTooEarlyHits()
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f));
            yield return null;

            PrivateMember.Invoke(touch, "Judge", 0.8f);
            Assert.That(PrivateMember.GetField<bool>(touch, "IsJudged"), Is.False);

            Assert.That(touch.State, Is.Not.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that a touch nobody hits is a miss without a touch sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TooLate_IsMiss()
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.D4, timing: 1f));
            yield return null;

            _rig.Step(1.29f);
            Assert.That(touch.State, Is.Not.EqualTo(NoteStatus.End));
            _rig.Step(1.31f);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchMissCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTouch], Is.False);
            Assert.That(_rig.NoteManager.IsCurrentNoteJudgeable(new TouchQueueInfo() { Index = 1, SensorPos = SensorArea.D4 }), Is.True);
        }

        /// <summary>Checks that a touch group judges the remaining members once more than half of it was hit.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Group_JudgesRemainingMembers()
        {
            var group = new TouchGroup();
            var first = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B1, timing: 1f, group: group));
            var second = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B2, timing: 1f, group: group));
            var third = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B3, timing: 1f, group: group));
            group.Members = new object[] { first, second, third };
            yield return null;
            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B2, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.16f);
            _rig.Step(1.17f);

            Assert.That(third.State, Is.EqualTo(NoteStatus.End));
            Assert.That(group.Percent, Is.EqualTo(2f / 3f).Within(1e-4f));
            Assert.That(group.JudgeResult, Is.EqualTo(JudgeGrade.LatePerfect2nd));
            Assert.That(_rig.Counter.JudgeStats.TouchPerfectCount, Is.EqualTo(3));
        }

        /// <summary>Checks that touching a mine is too fast and an avoided mine becomes perfect at its timing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_TouchedOrAvoided()
        {
            var touched = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.E1, timing: 1f, isMine: true));
            var avoided = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.E2, timing: 1f, isMine: true));
            yield return null;
            _rig.SetSensor(SensorArea.E1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(0.95f);
            Assert.That(touched.State, Is.EqualTo(NoteStatus.End));
            Assert.That(avoided.State, Is.Not.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMiss], Is.True);

            _rig.ClearInput();
            _rig.Step(1.01f);

            Assert.That(avoided.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchMissCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.TouchCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a late mine hit is a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_LateTouchIsMiss()
        {
            var touched = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.E1, timing: 1f, isMine: true));
            yield return null;
            _rig.SetSensor(SensorArea.E1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.2f);

            Assert.That(touched.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks the break sounds and the firework of a firework break touch.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BreakFirework_PlaysBreakSoundAndFirework()
        {
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f, isBreak: true, isFirework: true));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakJudge], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxFirework], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTouch], Is.False);
            Assert.That(_rig.Firework.Active, Is.True);
            Assert.That(_rig.Counter.JudgeStats.BreakCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a missed firework touch neither plays the firework nor its sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Firework_NotPlayedOnMiss()
        {
            _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f, isFirework: true));
            yield return null;

            _rig.Step(1.4f);

            Assert.That(_rig.SoundRequests[GameplayRig.SfxFirework], Is.False);
            Assert.That(_rig.Firework.Active, Is.False);
        }

        /// <summary>Checks that the button ring judges outer touches when the modifier allows it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonRing_JudgesOuterTouchWhenEnabled()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Disable, buttonRingForTouch: true);
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.A5, timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A5, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.1f);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that autoplay judges the touch at its timing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_JudgesAtTiming()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.C, timing: 1f));
            yield return null;

            _rig.Step(0.9f);
            Assert.That(touch.State, Is.Not.EqualTo(NoteStatus.End));
            _rig.Step(1f);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that DJ autoplay clicks the sensor so the following frame judges the touch.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_ClicksSensor()
        {
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_TouchPanel_First);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var touch = _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.B4, timing: 1f));
            yield return null;

            _rig.Step(0.9f);
            _rig.Step(0.99f);
            Assert.That(touch.State, Is.Not.EqualTo(NoteStatus.End));
            _rig.Step(1f);

            Assert.That(touch.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TouchCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the touch judgment effect is played for the touch sensor.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator End_PlaysTouchJudgmentEffect()
        {
            _rig.SpawnTouch(GameplayRig.TouchInfo(SensorArea.E7, timing: 1f));
            yield return null;
            _rig.SetSensor(SensorArea.E7, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            var effect = _rig.EffectPool.transform.GetChild(1).Find("TouchEffect_E7");
            Assert.That(effect.Find("JudgeEffect").GetComponent<TouchJudgeEffectDisplayer>().Active, Is.True);
        }

        /// <summary>Gets the judged count of one grade class from the statistics.</summary>
        /// <param name="stats">The statistics.</param>
        /// <param name="grade">The grade.</param>
        /// <returns>The count of the class the grade belongs to.</returns>
        private static long GradeCount(in ObjectCounter.NoteJudgeStatistics stats, JudgeGrade grade)
        {
            return grade switch
            {
                JudgeGrade.Perfect => stats.TouchCriticalCount,
                JudgeGrade.LatePerfect2nd or JudgeGrade.LatePerfect3rd => stats.TouchPerfectCount,
                JudgeGrade.LateGreat or JudgeGrade.LateGreat2nd or JudgeGrade.LateGreat3rd => stats.TouchGreatCount,
                JudgeGrade.LateGood => stats.TouchGoodCount,
                _ => stats.TouchMissCount
            };
        }

        /// <summary>Runs only the animation update of a touch at a given time.</summary>
        /// <param name="touch">The touch.</param>
        /// <param name="time">The chart time.</param>
        private void UpdateAt(TouchDrop touch, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            touch.OnUpdate();
        }
    }
}
