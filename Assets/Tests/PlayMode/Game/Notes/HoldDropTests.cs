using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
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
    /// Drives <see cref="HoldDrop"/> through the gameplay loop: initialization, skins, the body animation, head and body
    /// judgments in deluxe and classic modes, mines, autoplay and DJ autoplay.
    /// </summary>
    public class HoldDropTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the skin cases as break, each and mine flags with the expected sprite names.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, "Hold", "HoldEndPoint_Normal", "TapLine_Normal", false },
            new object[] { false, true, false, "Hold_Each", "HoldEndPoint_Each", "TapLine_Each", false },
            new object[] { true, false, false, "Hold_Break", "HoldEndPoint_Break", "TapLine_Break", true },
            new object[] { true, true, false, "Hold_Break", "HoldEndPoint_Break", "TapLine_Break", true },
            new object[] { false, false, true, "Hold_Mine", "HoldEndPoint_Normal", "TapLine_Normal", false },
            new object[] { true, false, true, "Hold_Break_Mine", "HoldEndPoint_Break", "TapLine_Break", true }
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

        /// <summary>Checks that Awake hides every renderer and creates the guide line.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesRenderers()
        {
            var hold = _rig.Spawn<HoldDrop>(_rig.HoldTemplate, _rig.HoldParent);
            yield return null;

            Assert.That(hold.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(hold.transform.GetChild(0).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(hold.transform.GetChild(1).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(GuideLine().layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(hold.Distance, Is.EqualTo(-100f));
        }

        /// <summary>Checks that Init copies the pooling information and is ignored while the hold is in play.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_AppliesPoolingInformationOnce()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(startPos: 5, timing: 2f, length: 1.5f, index: 3));
            yield return null;

            hold.Init(GameplayRig.HoldInfo(startPos: 1, timing: 9f));

            Assert.That(hold.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(hold.StartPos, Is.EqualTo(5));
            Assert.That(hold.Timing, Is.EqualTo(2f));
            Assert.That(hold.Length, Is.EqualTo(1.5f));
            Assert.That(hold.QueueInfo.Index, Is.EqualTo(3));
            Assert.That(hold.transform.rotation.eulerAngles.z, Is.EqualTo(157.5f).Within(0.01f));
        }

        /// <summary>Checks the sprites, guide line and material picked for every hold variant.</summary>
        /// <param name="testCase">
        /// The break, each and mine flags followed by the expected body, end point and guide line sprite names and whether
        /// the break material is expected.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var isBreak = (bool)testCase[0];
            var isEach = (bool)testCase[1];
            var isMine = (bool)testCase[2];
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(isBreak: isBreak, isEach: isEach, isMine: isMine));
            yield return null;

            var renderer = hold.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite.name, Is.EqualTo((string)testCase[3]));
            Assert.That(hold.transform.GetChild(1).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo((string)testCase[4]));
            Assert.That(GuideLine().GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo((string)testCase[5]));
            Assert.That(hold.transform.GetChild(0).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Hold_Ex"));
            Assert.That(renderer.sharedMaterial, Is.SameAs((bool)testCase[6] ? _rig.BreakMaterial : _rig.DefaultMaterial));
        }

        /// <summary>Checks the body animation while the head approaches, while it waits at the ring and after it arrives.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_StretchesBodyAndShowsEndPoint()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f, isEx: true));
            var renderer = hold.GetComponent<SpriteRenderer>();
            var endRenderer = hold.transform.GetChild(1).GetComponent<SpriteRenderer>();
            yield return null;

            UpdateAt(hold, 0f);
            Assert.That(hold.State, Is.EqualTo(NoteStatus.Inited));

            UpdateAt(hold, 0.1f);
            Assert.That(hold.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(renderer.enabled, Is.True);
            Assert.That(hold.transform.GetChild(0).GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(GuideLine().layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));

            UpdateAt(hold, 0.5f);
            Assert.That(GuideLine().layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));

            UpdateAt(hold, 0.6f);
            Assert.That(hold.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(hold.Distance, Is.EqualTo(1.8f).Within(1e-3f));
            Assert.That(renderer.size.y, Is.EqualTo(1.975f).Within(1e-3f));
            Assert.That(endRenderer.enabled, Is.False);

            UpdateAt(hold, 1.5f);
            Assert.That(hold.Distance, Is.EqualTo(4.8f).Within(1e-3f));
            Assert.That(renderer.size.y, Is.EqualTo(4.975f).Within(1e-3f));

            UpdateAt(hold, 1.9f);
            Assert.That(endRenderer.enabled, Is.True);
            Assert.That(renderer.size.y, Is.EqualTo(2.15f).Within(1e-3f));

            UpdateAt(hold, 2.05f);
            Assert.That(hold.State, Is.EqualTo(NoteStatus.Arrived));
            Assert.That(hold.Distance, Is.EqualTo(5.175f).Within(1e-3f));
            var expected = NoteHelper.GetTapPosition(1, 4.8f) * (5.175f / 4.8f);
            Assert.That(hold.transform.position.x, Is.EqualTo(expected.x).Within(1e-3f));
        }

        /// <summary>Checks that a short hold shows its end point before the head reaches the ring.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_ShortHoldShowsEndPointEarly()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 0.1f));
            var renderer = hold.GetComponent<SpriteRenderer>();
            yield return null;

            UpdateAt(hold, 0.9f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(hold.transform.GetChild(1).GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(renderer.size.y, Is.EqualTo(2.15f).Within(1e-3f));
        }

        /// <summary>Checks a perfect hold: head click, held body with hold effect and a perfect end judgment.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HeldThroughout_IsPerfect()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(startPos: 2, timing: 1f, length: 1f));
            var renderer = hold.GetComponent<SpriteRenderer>();
            yield return null;

            _rig.SetButton(ButtonZone.A2, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);

            _rig.SetButton(ButtonZone.A2, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.05f);
            Assert.That(HoldEffect(2).Active, Is.True);

            _rig.Step(1.5f);
            Assert.That(renderer.sprite.name, Is.EqualTo("Hold_On"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(_rig.HoldShineMaterial));

            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldJudgedCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
            Assert.That(HoldEffect(2).Active, Is.False);
        }

        /// <summary>Checks that releasing the body after the forgiveness frames downgrades the final grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReleasedBody_DowngradesToGood()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            var renderer = hold.GetComponent<SpriteRenderer>();
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.15f);

            NoteTestEnvironment.SetDeltaTime(0.25f);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.Step(1.2f);
            Assert.That(renderer.sprite.name, Is.EqualTo("Hold_On"));
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.Off);
            _rig.Step(1.4f);
            Assert.That(renderer.sprite.name, Is.EqualTo("Hold_Off"));
            _rig.Step(1.6f);
            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldGoodCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an unplayed hold misses and shows its head result when the option is enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NeverPressed_IsMiss()
        {
            _rig.Settings.Display.DisplayHoldHeadJudgeResult = true;
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            yield return null;

            _rig.Step(1.2f);
            Assert.That(hold.State, Is.Not.EqualTo(NoteStatus.End));
            Assert.That(_rig.NoteManager.IsCurrentNoteJudgeable(new TapQueueInfo() { Index = 1, KeyIndex = 1 }), Is.True);

            NoteTestEnvironment.SetDeltaTime(0.25f);
            _rig.Step(1.4f);
            _rig.Step(1.6f);
            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a break hold requests the break sound on its head and a tap sound on its end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BreakHold_RequestsBreakSoundOnHead()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f, isBreak: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreak], Is.True);

            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreak], Is.False);
            Assert.That(_rig.Counter.JudgeStats.BreakCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an avoided mine hold is perfect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_AvoidedIsPerfect()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f, isMine: true));
            yield return null;

            _rig.Step(1.06f);
            Assert.That(hold.State, Is.Not.EqualTo(NoteStatus.End));
            Assert.That(HoldEffect(1).Active, Is.True);

            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that touching a mine hold during its body is a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_TouchedDuringBodyIsMiss()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f, isMine: true));
            yield return null;
            _rig.Step(1.06f);

            _rig.SetSensor(SensorArea.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.5f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMiss], Is.True);
        }

        /// <summary>Checks that clicking a mine hold head ends it as too fast.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_HeadClickedIsTooFast()
        {
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f, isMine: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that classic mode ends a hold when it is released and judges the release timing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Classic_ReleaseEndsHoldWithReleaseJudgment()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            var renderer = hold.GetComponent<SpriteRenderer>();
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1.05f);
            Assert.That(renderer.sprite.name, Is.EqualTo("Hold_On"));

            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.Step(1.5f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldGoodCount, Is.EqualTo(1));
        }

        /// <summary>Checks that classic mode keeps a perfect when the hold is released at its end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Classic_ReleaseAtEndIsPerfect()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(2.05f);
            Assert.That(hold.State, Is.Not.EqualTo(NoteStatus.End));

            _rig.SetSensor(SensorArea.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.Off);
            _rig.Step(2.1f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that classic mode force-ends a hold held too long, and ends a missed head at once.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Classic_ForceEndsOverlongAndMissedHolds()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var held = _rig.SpawnHold(GameplayRig.HoldInfo(startPos: 1, timing: 1f, length: 1f));
            var missed = _rig.SpawnHold(GameplayRig.HoldInfo(startPos: 2, timing: 1f, length: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1f);
            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);

            _rig.Step(1.2f);
            Assert.That(missed.State, Is.EqualTo(NoteStatus.End));
            Assert.That(held.State, Is.Not.EqualTo(NoteStatus.End));

            _rig.Step(2.4f);

            Assert.That(held.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldMissCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.HoldGoodCount, Is.EqualTo(1));
        }

        /// <summary>Checks that autoplay judges the head, keeps the body pressed and ends with the autoplay grade.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_PlaysWholeHold()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            var renderer = hold.GetComponent<SpriteRenderer>();
            yield return null;

            _rig.Step(0.9f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.False);
            _rig.Step(0.99f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            _rig.Step(1.5f);
            Assert.That(renderer.sprite.name, Is.EqualTo("Hold_On"));

            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that autoplay picks a random grade when the configured grade is invalid.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_RandomGradeForInvalidValue()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = (JudgeGrade)50;
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(timing: 1f, length: 1f));
            yield return null;

            _rig.Step(1f);
            _rig.Step(2f);

            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that DJ autoplay clicks the head and keeps pressing until shortly before the end.</summary>
        /// <param name="mode">The DJ autoplay mode.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_ClicksAndHolds([Values(AutoplayModeOption.DJAuto_ButtonRing_First, AutoplayModeOption.DJAuto_TouchPanel_First)] AutoplayModeOption mode)
        {
            _rig.Controller.SetMods(mode);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var hold = _rig.SpawnHold(GameplayRig.HoldInfo(startPos: 3, timing: 1f, length: 1f));
            yield return null;

            _rig.Step(0.9f);
            _rig.Step(0.98f);
            _rig.Step(0.99f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            for (var frame = 0; frame < 30; frame++)
            {
                _rig.Step(1f + (frame / 60f));
            }
            Assert.That(PressedState(mode), Is.EqualTo(SwitchStatus.On));
            for (var frame = 30; frame < 60; frame++)
            {
                _rig.Step(1f + (frame / 60f));
            }

            _rig.Step(2.02f);

            Assert.That(PressedState(mode), Is.EqualTo(SwitchStatus.Off));
            Assert.That(hold.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.HoldCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Gets the state of the input that a DJ autoplay mode presses for key 3.</summary>
        /// <param name="mode">The DJ autoplay mode.</param>
        /// <returns>The button state for the button-first mode, otherwise the sensor state.</returns>
        private SwitchStatus PressedState(AutoplayModeOption mode)
        {
            return mode == AutoplayModeOption.DJAuto_ButtonRing_First
                ? _rig.NoteManager.GetButtonStatusInThisFrame(ButtonZone.A3)
                : _rig.NoteManager.GetSensorStatusInThisFrame(SensorArea.A3);
        }

        /// <summary>Gets the guide line created by the first spawned hold.</summary>
        /// <returns>The guide line object.</returns>
        private GameObject GuideLine()
        {
            return _rig.NoteManager.transform.GetChild(7).GetChild(0).gameObject;
        }

        /// <summary>Gets the hold particle effect of a key.</summary>
        /// <param name="key">The key from 1 to 8.</param>
        /// <returns>The effect.</returns>
        private HoldEffectDisplayer HoldEffect(int key)
        {
            return _rig.EffectPool.transform.GetChild(0).Find($"HoldEffect_{key}").GetComponent<HoldEffectDisplayer>();
        }

        /// <summary>Runs only the animation update of a hold at a given time.</summary>
        /// <param name="hold">The hold.</param>
        /// <param name="time">The chart time.</param>
        private void UpdateAt(HoldDrop hold, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            hold.OnUpdate();
        }
    }
}
