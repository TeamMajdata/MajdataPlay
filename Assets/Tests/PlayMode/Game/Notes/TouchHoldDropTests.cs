using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Rendering;
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
    /// Drives <see cref="TouchHoldDrop"/> through the gameplay loop: initialization, skins, the fan animation and progress
    /// border, head and body judgments, touch and body groups, mines, fireworks, autoplay and DJ autoplay.
    /// </summary>
    public class TouchHoldDropTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the skin cases as break, each and mine flags with the expected fan prefix, border and point names.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, "TouchHold", "TouchHold[4]", "TouchPoint", false },
            new object[] { false, true, false, "TouchHold", "TouchHold[4]", "TouchPoint_Each", false },
            new object[] { true, false, false, "TouchHold_Break", "TouchHold_Break[4]", "TouchPoint_Break", true },
            new object[] { false, false, true, "TouchHold_Mine", "TouchHold_Mine[4]", "TouchPoint_Mine", false },
            new object[] { true, false, true, "TouchHold_Break_Mine", "TouchHold_Break_Mine[4]", "TouchPoint_Break_Mine", true }
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

        /// <summary>Checks that Awake hides the fans, point and progress border.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesRenderers()
        {
            var touchHold = _rig.Spawn<TouchHoldDrop>(_rig.TouchHoldTemplate, _rig.TouchHoldParent);
            yield return null;

            foreach (var renderer in touchHold.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Assert.That(renderer.enabled, Is.False, renderer.name);
            }
            Assert.That(Border(touchHold).enabled, Is.False);
            Assert.That(touchHold.Active, Is.False);
        }

        /// <summary>Checks that Init places the note, copies the information once and resets the border progress.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_AppliesPoolingInformationOnce()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B6, timing: 2f, length: 1.5f, isFirework: true, index: 1));
            yield return null;

            touchHold.Init(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 7f));

            var expected = NoteHelper.GetTouchAreaPosition(SensorArea.B6);
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(touchHold.Timing, Is.EqualTo(2f));
            Assert.That(touchHold.Length, Is.EqualTo(1.5f));
            Assert.That(touchHold.isFirework, Is.True);
            Assert.That(touchHold.areaPosition, Is.EqualTo('B'));
            Assert.That(touchHold.QueueInfo.Index, Is.EqualTo(1));
            Assert.That(touchHold.transform.position.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(Progress(touchHold), Is.EqualTo(0f));
        }

        /// <summary>Checks the fan, border and point sprites and the fan material for every variant.</summary>
        /// <param name="testCase">
        /// The break, each and mine flags followed by the expected fan sprite prefix, border and point sprite names and
        /// whether the break material is expected.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(isBreak: (bool)testCase[0], isEach: (bool)testCase[1], isMine: (bool)testCase[2]));
            yield return null;

            for (var i = 0; i < 4; i++)
            {
                var fan = touchHold.transform.GetChild(4 - i).GetComponent<SpriteRenderer>();
                Assert.That(fan.sprite.name, Is.EqualTo($"{testCase[3]}[{i}]"));
                Assert.That(fan.sharedMaterial, Is.SameAs((bool)testCase[6] ? _rig.BreakMaterial : _rig.DefaultMaterial));
            }
            Assert.That(Border(touchHold).Sprite.name, Is.EqualTo((string)testCase[4]));
            Assert.That(touchHold.transform.GetChild(5).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo((string)testCase[5]));
        }

        /// <summary>Checks the fade-in, the fan movement, the arrival and the progress of the border.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_FadesInArrivesAndFillsBorder()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f));
            var fan = touchHold.transform.GetChild(4).GetComponent<SpriteRenderer>();
            yield return null;

            UpdateAt(touchHold, 0.5f);
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Inited));

            UpdateAt(touchHold, 0.6f);
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(fan.enabled, Is.True);
            Assert.That(fan.color.a, Is.InRange(0.6f, 0.85f));

            UpdateAt(touchHold, 0.7f);
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(fan.color.a, Is.EqualTo(1f));

            UpdateAt(touchHold, 1.5f);
            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.Arrived));
            Assert.That(Border(touchHold).enabled, Is.True);
            Assert.That(Progress(touchHold), Is.EqualTo(0.5f).Within(1e-4f));

            UpdateAt(touchHold, 3f);
            Assert.That(Progress(touchHold), Is.EqualTo(1f));
        }

        /// <summary>Checks a perfect touch hold: head hit, pressed body with riser sound and border, perfect end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HeldThroughout_IsPerfect()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTouchHold], Is.True);
            Assert.That(TouchHoldEffect(SensorArea.C).Active, Is.True);

            _rig.SetSensor(SensorArea.C, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.5f);
            Assert.That(Border(touchHold).Sprite.name, Is.EqualTo("TouchHold[4]"));

            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTouchHold], Is.False);
            Assert.That(TouchHoldEffect(SensorArea.C).Active, Is.False);
        }

        /// <summary>Checks that releasing the body swaps in the off border and downgrades the final grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReleasedBody_DowngradesToGood()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetSensor(SensorArea.C, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.3f);

            NoteTestEnvironment.SetDeltaTime(0.25f);
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.Off);
            _rig.Step(1.4f);
            _rig.Step(1.5f);
            Assert.That(Border(touchHold).Sprite.name, Is.EqualTo("TouchHold_Off"));
            _rig.Step(1.6f);
            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldGoodCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an untouched touch hold misses its head and ends as a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NeverTouched_IsMiss()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.E2, timing: 1f, length: 1f));
            yield return null;

            _rig.Step(1.31f);
            Assert.That(_rig.NoteManager.IsCurrentNoteJudgeable(new TouchQueueInfo() { Index = 1, SensorPos = SensorArea.E2 }), Is.True);
            NoteTestEnvironment.SetDeltaTime(0.25f);
            _rig.Step(1.5f);
            _rig.Step(1.7f);
            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a short touch hold skips the body check and keeps its head grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ShortHold_KeepsHeadGrade()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 0.3f));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.1f);
            _rig.ClearInput();

            _rig.Step(1.32f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a touch group judges a member head once most of the group was hit.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Group_JudgesRemainingHead()
        {
            var group = new TouchGroup();
            var first = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B1, timing: 1f, group: group));
            var second = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B2, timing: 1f, group: group));
            var third = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B3, timing: 1f, group: group));
            group.Members = new object[] { first, second, third };
            yield return null;
            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B2, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);
            _rig.Step(1.01f);

            Assert.That(PrivateMember.GetField<bool>(third, "IsJudged"), Is.True);
            Assert.That(_rig.NoteManager.IsCurrentNoteJudgeable(new TouchQueueInfo() { Index = 1, SensorPos = SensorArea.B3 }), Is.True);
            Assert.That(third.GroupInfo, Is.SameAs(group));
        }

        /// <summary>Checks that a body group keeps an unpressed member alive while most members are pressed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BodyGroup_CoversUnpressedMember()
        {
            var bodyGroup = new TouchHoldGroup();
            var first = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B1, timing: 1f, bodyGroup: bodyGroup));
            var second = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B2, timing: 1f, bodyGroup: bodyGroup));
            var third = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.B3, timing: 1f, bodyGroup: bodyGroup));
            bodyGroup.Members = new object[] { first, second, third };
            yield return null;
            _rig.SetSensor(SensorArea.B1, SwitchStatus.Off, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B2, SwitchStatus.Off, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B3, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            Assert.That(bodyGroup.Percent, Is.EqualTo(1f));

            _rig.SetSensor(SensorArea.B1, SwitchStatus.On, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B2, SwitchStatus.On, SwitchStatus.On);
            _rig.SetSensor(SensorArea.B3, SwitchStatus.On, SwitchStatus.Off);
            for (var frame = 0; frame < 35; frame++)
            {
                _rig.Step(1.2f + (frame / 60f));
            }
            Assert.That(bodyGroup.Percent, Is.EqualTo(2f / 3f).Within(1e-4f));
            _rig.Step(2f);

            Assert.That(third.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(3));
        }

        /// <summary>Checks that an avoided mine touch hold is perfect and a touched mine body is a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_AvoidedOrTouched()
        {
            var avoided = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.D1, timing: 1f, length: 1f, isMine: true));
            var touched = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.D2, timing: 1f, length: 1f, isMine: true));
            yield return null;

            _rig.Step(1.01f);
            Assert.That(PrivateMember.GetField<bool>(avoided, "IsJudged"), Is.True);
            _rig.SetSensor(SensorArea.D2, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.5f);
            Assert.That(touched.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMiss], Is.True);

            _rig.ClearInput();
            _rig.Step(2.01f);

            Assert.That(avoided.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that touching a mine touch hold head ends it as too fast.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_HeadTouchedIsTooFast()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f, isMine: true));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(0.95f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a firework touch hold plays the firework and its sound when it ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Firework_PlayedAtEnd()
        {
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f, isFirework: true));
            yield return null;
            _rig.SetSensor(SensorArea.C, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetSensor(SensorArea.C, SwitchStatus.On, SwitchStatus.On);

            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxFirework], Is.True);
            Assert.That(_rig.Firework.Active, Is.True);
        }

        /// <summary>Checks that the button ring judges an outer touch hold head when the modifier allows it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonRing_JudgesOuterHeadWhenEnabled()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Disable, buttonRingForTouch: true);
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.A3, timing: 1f, length: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A3, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(PrivateMember.GetField<bool>(touchHold, "IsJudged"), Is.True);
        }

        /// <summary>Checks that autoplay judges the head, holds the body and ends with the autoplay grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_PlaysWholeTouchHold()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = JudgeGrade.LatePerfect2nd;
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f));
            yield return null;

            _rig.Step(0.9f);
            Assert.That(PrivateMember.GetField<bool>(touchHold, "IsJudged"), Is.False);
            _rig.Step(1f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            _rig.Step(1.5f);
            Assert.That(Border(touchHold).Sprite.name, Is.EqualTo("TouchHold[4]"));
            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldPerfectCount, Is.EqualTo(1));
        }

        /// <summary>Checks that autoplay picks a valid random grade for an invalid configured grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_RandomGradeForInvalidValue()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = (JudgeGrade)42;
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.C, timing: 1f, length: 1f));
            yield return null;

            _rig.Step(1f);
            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that DJ autoplay clicks the head and keeps the sensor pressed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_ClicksAndPresses()
        {
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_TouchPanel_First);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var touchHold = _rig.SpawnTouchHold(GameplayRig.TouchHoldInfo(SensorArea.E4, timing: 1f, length: 1f));
            yield return null;

            _rig.Step(0.9f);
            _rig.Step(0.99f);
            _rig.Step(1f);
            Assert.That(PrivateMember.GetField<bool>(touchHold, "IsJudged"), Is.True);
            for (var frame = 1; frame < 60; frame++)
            {
                _rig.Step(1f + (frame / 60f));
            }
            Assert.That(_rig.NoteManager.GetSensorStatusInThisFrame(SensorArea.E4), Is.EqualTo(SwitchStatus.On));
            _rig.Step(2f);

            Assert.That(touchHold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Gets the progress border renderer of a touch hold.</summary>
        /// <param name="touchHold">The touch hold.</param>
        /// <returns>The border renderer.</returns>
        private static RawSpriteRenderer Border(TouchHoldDrop touchHold)
        {
            return touchHold.transform.GetChild(0).GetComponent<RawSpriteRenderer>();
        }

        /// <summary>Reads the progress written into the border property block.</summary>
        /// <param name="touchHold">The touch hold.</param>
        /// <returns>The progress from 0 to 1.</returns>
        private static float Progress(TouchHoldDrop touchHold)
        {
            var block = new MaterialPropertyBlock();
            Border(touchHold).GetPropertyBlock(block);
            return block.GetFloat("_Progress");
        }

        /// <summary>Gets the hold particle effect of a touch hold sensor.</summary>
        /// <param name="area">The sensor.</param>
        /// <returns>The effect.</returns>
        private HoldEffectDisplayer TouchHoldEffect(SensorArea area)
        {
            return _rig.EffectPool.transform.GetChild(2).Find($"TouchHold_HoldingEffect_{area}").GetComponent<HoldEffectDisplayer>();
        }

        /// <summary>Runs only the animation update of a touch hold at a given time.</summary>
        /// <param name="touchHold">The touch hold.</param>
        /// <param name="time">The chart time.</param>
        private void UpdateAt(TouchHoldDrop touchHold, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            touchHold.OnUpdate();
        }
    }
}
