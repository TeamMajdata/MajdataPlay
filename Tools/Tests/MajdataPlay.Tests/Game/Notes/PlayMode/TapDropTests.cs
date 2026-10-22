using System.Collections;
using MajdataPlay.Game.Notes;
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
    /// Drives <see cref="TapDrop"/> through the gameplay loop: initialization, skins, approach animation, button and
    /// sensor judgments, misses, mines, EX and break sounds, autoplay and each line bindings.
    /// </summary>
    public class TapDropTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the skin cases as break, each, mine, star and double flags with the expected sprite names.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, false, false, "Tap", "TapLine_Normal", "Tap_Ex", false },
            new object[] { false, true, false, false, false, "Tap_Each", "TapLine_Each", "Tap_Ex", false },
            new object[] { true, false, false, false, false, "Tap_Break", "TapLine_Break", "Tap_Ex", true },
            new object[] { true, true, false, false, false, "Tap_Break", "TapLine_Break", "Tap_Ex", true },
            new object[] { false, false, true, false, false, "Tap_Mine", "TapLine_Normal", "Tap_Ex", false },
            new object[] { false, true, true, false, false, "Tap_Mine", "TapLine_Each", "Tap_Ex", false },
            new object[] { true, false, true, false, false, "Tap_Break_Mine", "TapLine_Break", "Tap_Ex", true },
            new object[] { false, false, false, true, false, "Star", "TapLine_Slide", "Star_Ex", false },
            new object[] { false, true, false, true, false, "Star_Each", "TapLine_Each", "Star_Ex", false },
            new object[] { true, false, false, true, false, "Star_Break", "TapLine_Break", "Star_Ex", true },
            new object[] { false, false, false, true, true, "Star_Double", "TapLine_Slide", "Star_Ex_Double", false },
            new object[] { false, true, false, true, true, "Star_Each_Double", "TapLine_Each", "Star_Ex_Double", false },
            new object[] { true, false, false, true, true, "Star_Break_Double", "TapLine_Break", "Star_Ex_Double", true },
            new object[] { false, false, true, true, false, "Star_Mine", "TapLine_Slide", "Star_Ex", false },
            new object[] { true, false, true, true, false, "Star_Break_Mine", "TapLine_Break", "Star_Ex", true },
            new object[] { false, false, true, true, true, "Star_Double_Mine", "TapLine_Slide", "Star_Ex_Double", false },
            new object[] { true, false, true, true, true, "Star_Break_Double_Mine", "TapLine_Break", "Star_Ex_Double", true }
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

        /// <summary>Checks that Awake hides the note and creates its guide line under the guide line layer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesNoteAndCreatesGuideLine()
        {
            var tap = _rig.Spawn<TapDrop>(_rig.TapTemplate, _rig.TapParent);
            yield return null;

            var guideLines = _rig.NoteManager.transform.GetChild(7);
            Assert.That(guideLines.childCount, Is.EqualTo(1));
            Assert.That(guideLines.GetChild(0).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(tap.Active, Is.False);
            Assert.That(tap.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(tap.transform.localScale, Is.EqualTo(new Vector3(0f, 0f, 1f)));
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Start));
        }

        /// <summary>Checks that Init copies the pooling information and rotates the note to its key.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_AppliesPoolingInformation()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(startPos: 3, timing: 2f, isEach: true, isEx: true, index: 4));
            yield return null;

            Assert.That(tap.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(tap.StartPos, Is.EqualTo(3));
            Assert.That(tap.Timing, Is.EqualTo(2f));
            Assert.That(tap.JudgeTimingWithOffset, Is.EqualTo(2f));
            Assert.That(tap.IsEach, Is.True);
            Assert.That(tap.IsEX, Is.True);
            Assert.That(tap.QueueInfo.Index, Is.EqualTo(4));
            Assert.That(tap.Distance, Is.EqualTo(-100f));
            Assert.That(tap.Active, Is.True);
            Assert.That(tap.transform.rotation.eulerAngles.z, Is.EqualTo(247.5f).Within(0.01f));
        }

        /// <summary>Checks that Init is ignored while the note is still in play.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_IsIgnoredWhileInPlay()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 2f));
            yield return null;

            tap.Init(GameplayRig.TapInfo(startPos: 5, timing: 3f));

            Assert.That(tap.Timing, Is.EqualTo(2f));
            Assert.That(tap.StartPos, Is.EqualTo(1));
        }

        /// <summary>Checks the sprite, guide line, EX sprite and material picked for every tap and star variant.</summary>
        /// <param name="testCase">
        /// The break, each, mine, star and double flags followed by the expected note, guide line and EX sprite names and
        /// whether the break material is expected.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var noteSprite = (string)testCase[5];
            var lineSprite = (string)testCase[6];
            var exSprite = (string)testCase[7];
            var usesBreakMaterial = (bool)testCase[8];
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(isBreak: (bool)testCase[0],
                                                        isEach: (bool)testCase[1],
                                                        isMine: (bool)testCase[2],
                                                        isStar: (bool)testCase[3],
                                                        isDouble: (bool)testCase[4]));
            yield return null;

            var renderer = tap.GetComponent<SpriteRenderer>();
            var exRenderer = tap.transform.GetChild(0).GetComponent<SpriteRenderer>();
            var lineRenderer = _rig.NoteManager.transform.GetChild(7).GetChild(0).GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite.name, Is.EqualTo(noteSprite));
            Assert.That(lineRenderer.sprite.name, Is.EqualTo(lineSprite));
            Assert.That(exRenderer.sprite.name, Is.EqualTo(exSprite));
            Assert.That(renderer.sharedMaterial, Is.SameAs(usesBreakMaterial ? _rig.BreakMaterial : _rig.DefaultMaterial));
        }

        /// <summary>Checks the approach animation from the hidden state through scaling to running.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_ScalesInThenMovesOutwards()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isEx: true));
            var renderer = tap.GetComponent<SpriteRenderer>();
            var exRenderer = tap.transform.GetChild(0).GetComponent<SpriteRenderer>();
            var lineRenderer = _rig.NoteManager.transform.GetChild(7).GetChild(0).GetComponent<SpriteRenderer>();
            yield return null;

            _rig.Step(0f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(renderer.enabled, Is.False);

            _rig.Step(0.1f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(renderer.enabled, Is.True);
            Assert.That(exRenderer.enabled, Is.True);
            Assert.That(lineRenderer.enabled, Is.False);
            Assert.That(tap.Distance, Is.EqualTo(-1.95f).Within(1e-3f));
            Assert.That(tap.transform.localScale.x, Is.EqualTo((-1.95f * 0.265f) + 1f - (0.265f * 1.225f)).Within(1e-3f));

            _rig.Step(0.5f);
            Assert.That(lineRenderer.enabled, Is.True);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Scaling));

            _rig.Step(0.6f);
            Assert.That(tap.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(tap.transform.localScale, Is.EqualTo(new Vector3(1f, 1f, 1f)));
            var expected = NoteHelper.GetTapPosition(1, 4.8f) * (1.8f / 4.8f);
            Assert.That(tap.transform.position.x, Is.EqualTo(expected.x).Within(1e-3f));
            Assert.That(tap.transform.position.y, Is.EqualTo(expected.y).Within(1e-3f));
            Assert.That(lineRenderer.transform.localScale.x, Is.EqualTo(0.375f).Within(1e-3f));
        }

        /// <summary>Checks that star heads rotate while the chart plays and star rotation is enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_RotatesStars()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isStar: true));
            yield return null;
            _rig.Step(0.6f);
            var before = tap.transform.rotation.eulerAngles.z;

            _rig.Step(0.62f);

            Assert.That(Mathf.DeltaAngle(before, tap.transform.rotation.eulerAngles.z), Is.EqualTo(1.5f).Within(0.01f));
        }

        /// <summary>Checks a perfect button judgment and everything the end of the note reports.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonClick_JudgesPerfectAndEndsNote()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(tap.Active, Is.False);
            Assert.That(_rig.Counter.JudgeStats.TapJudgedCount, Is.EqualTo(1));
            Assert.That(_rig.Counter.JudgeStats.TapCriticalCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.True);
            Assert.That(_rig.NoteManager.IsCurrentNoteJudgeable(new TapQueueInfo() { Index = 1, KeyIndex = 1 }), Is.True);
        }

        /// <summary>Checks that a great judgment requests the great sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonClick_LateGreatRequestsGreatSound()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.06f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapGreatCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapGreat], Is.True);
        }

        /// <summary>Checks that a good judgment requests the good sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonClick_FastGoodRequestsGoodSound()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(0.88f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapGoodCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapGood], Is.True);
        }

        /// <summary>Checks that sensor hits are judged with the touch panel offset.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SensorClick_UsesTouchPanelOffset()
        {
            _rig.Settings.Judge.TouchPanelOffset = 3f;
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(startPos: 2, timing: 1f));
            yield return null;
            _rig.SetSensor(SensorArea.A2, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.05f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a held button is not a click and hits outside the window are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Click_IgnoredOutsideWindowOrWithoutEdge()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(0.8f);
            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));

            _rig.SetButton(ButtonZone.A1, SwitchStatus.On, SwitchStatus.On);
            _rig.Step(1f);

            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that a note waits for earlier notes on the same key.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Click_WaitsForEarlierNoteOnKey()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, index: 1));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that one click only judges one of two overlapping notes on the same key.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Click_IsConsumedByOneNote()
        {
            var first = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, index: 0));
            var second = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1.02f, index: 0));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            var ended = (first.State == NoteStatus.End ? 1 : 0) + (second.State == NoteStatus.End ? 1 : 0);
            Assert.That(ended, Is.EqualTo(1));
        }

        /// <summary>Checks that an untouched note becomes a miss after the good window.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TooLate_JudgesMiss()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;

            _rig.Step(1.2f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapMissCount, Is.EqualTo(1));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapPerfect], Is.False);
        }

        /// <summary>Checks that touching a mine is reported as too fast with the miss sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_TouchedIsTooFast()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isMine: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxMiss], Is.True);
            Assert.That(_rig.Counter.JudgeStats.TapMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a mine touched late in its window is reported as a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_TouchedLateIsMiss()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isMine: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.04f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an avoided mine is a perfect once its window passes, and ignores autoplay.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Mine_AvoidedIsPerfect()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isMine: true));
            yield return null;

            _rig.Step(1f);
            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));

            _rig.Step(1.06f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that EX taps request the EX sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ExTap_RequestsExSound()
        {
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, isEx: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1.09f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxTapEx], Is.True);
            Assert.That(_rig.Counter.JudgeStats.TapCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks the break sounds for perfect and non-perfect break judgments.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BreakTap_RequestsBreakSounds()
        {
            var perfect = _rig.SpawnTap(GameplayRig.TapInfo(startPos: 1, timing: 1f, isBreak: true));
            var great = _rig.SpawnTap(GameplayRig.TapInfo(startPos: 2, timing: 1f, isBreak: true));
            yield return null;
            _rig.SetButton(ButtonZone.A1, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            Assert.That(perfect.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakJudge], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreak], Is.True);

            _rig.ClearInput();
            _rig.SetButton(ButtonZone.A2, SwitchStatus.Off, SwitchStatus.On);
            _rig.Step(1.06f);

            Assert.That(great.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreakJudge], Is.True);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxBreak], Is.False);
            Assert.That(_rig.Counter.JudgeStats.BreakJudgedCount, Is.EqualTo(2));
        }

        /// <summary>Checks that autoplay judges the configured grade one frame before the note arrives.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Autoplay_JudgesConfiguredGrade()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = JudgeGrade.LateGreat;
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;

            _rig.Step(0.9f);
            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));

            _rig.Step(0.99f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapGreatCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the button-first DJ autoplay presses the button so the next frame judges the note.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_ButtonFirstClicksButton()
        {
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_ButtonRing_First);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;

            _rig.Step(0.9f);
            _rig.Step(0.98f);
            Assert.That(tap.State, Is.Not.EqualTo(NoteStatus.End));
            Assert.That(_rig.NoteManager.GetButtonStatusInThisFrame(ButtonZone.A1), Is.EqualTo(SwitchStatus.Off));

            _rig.Step(0.99f);

            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.TapCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the touch-first DJ autoplay clicks the sensor of the key.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_TouchFirstClicksSensor()
        {
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_TouchPanel_First);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(startPos: 4, timing: 1f));
            yield return null;

            _rig.Step(0.98f);
            _rig.Step(0.99f);

            Assert.That(_rig.NoteManager.GetSensorStatusInThisFrame(SensorArea.A4), Is.EqualTo(SwitchStatus.On));
            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that the permissive DJ policy falls back to the sensor when the button is already down.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DjAutoplay_PermissivePolicyFallsBackToSensor()
        {
            _rig.Settings.Debug.DJAutoPolicy = DJAutoPolicyOption.Permissive;
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_ButtonRing_First);
            PrivateMember.SetField(_rig.NoteManager, "USERSETTING_IS_AUTOPLAY", true);
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f));
            yield return null;
            _rig.NoteManager.SimulateButtonPress(ButtonZone.A1);
            _rig.Step(0.5f);

            _rig.NoteManager.SimulateButtonPress(ButtonZone.A1);
            _rig.Step(0.98f);
            _rig.Step(0.99f);

            Assert.That(_rig.NoteManager.GetSensorStatusInThisFrame(SensorArea.A1), Is.EqualTo(SwitchStatus.On));
            Assert.That(tap.State, Is.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that a tap binds to its each line binding while alive and unbinds when it ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EachLineBinding_FollowsTapLifetime()
        {
            var binding = new EachLineBinding();
            IEachLineDistanceProvider provider = binding;
            var tap = _rig.SpawnTap(GameplayRig.TapInfo(timing: 1f, binding: binding));
            yield return null;

            _rig.Step(0.6f);
            Assert.That(provider.Distance, Is.EqualTo(tap.Distance));
            Assert.That(provider.IsAnyNoteEnded, Is.False);

            _rig.Step(1.2f);

            Assert.That(provider.IsAnyNoteEnded, Is.True);
            Assert.That(provider.Distance, Is.EqualTo(0f));
        }

        /// <summary>Checks that the judgment effect for the key is played when the tap ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator End_PlaysTapJudgmentEffect()
        {
            _rig.SpawnTap(GameplayRig.TapInfo(startPos: 6, timing: 1f));
            yield return null;
            _rig.SetButton(ButtonZone.A6, SwitchStatus.Off, SwitchStatus.On);

            _rig.Step(1f);

            var effect = _rig.EffectPool.transform.GetChild(0).Find("TapEffect_6");
            Assert.That(effect, Is.Not.Null);
            var perfect = effect!.Find("Effects/Perfect").GetComponent<TapPerfectEffectDisplayer>();
            Assert.That(perfect.Active, Is.True);
        }
    }
}
