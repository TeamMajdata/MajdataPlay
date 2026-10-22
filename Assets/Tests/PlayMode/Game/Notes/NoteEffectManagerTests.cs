using System;
using System.Collections;
using System.Collections.Generic;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
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
    /// Covers <see cref="NoteEffectManager"/> and <see cref="NoteEffectPool"/>: judgment display filtering, button
    /// colours, touch feedback, effect forwarding and the creation of the per-key and per-sensor effects.
    /// </summary>
    public class NoteEffectManagerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Builds the gameplay objects with every effect.</summary>
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

        /// <summary>Checks which grades each judgment display option shows.</summary>
        /// <param name="option">The display option.</param>
        /// <param name="grade">The grade.</param>
        /// <param name="expected">Whether the grade is shown.</param>
        [TestCase(JudgeDisplayOption.All, JudgeGrade.Perfect, true)]
        [TestCase(JudgeDisplayOption.All, JudgeGrade.Miss, true)]
        [TestCase(JudgeDisplayOption.BelowCP, JudgeGrade.Perfect, false)]
        [TestCase(JudgeDisplayOption.BelowCP, JudgeGrade.LatePerfect2nd, true)]
        [TestCase(JudgeDisplayOption.BelowP, JudgeGrade.FastPerfect3rd, false)]
        [TestCase(JudgeDisplayOption.BelowP, JudgeGrade.FastGreat, true)]
        [TestCase(JudgeDisplayOption.BelowGR, JudgeGrade.LateGreat3rd, false)]
        [TestCase(JudgeDisplayOption.BelowGR, JudgeGrade.LateGood, true)]
        [TestCase(JudgeDisplayOption.MissOnly, JudgeGrade.LateGood, false)]
        [TestCase(JudgeDisplayOption.MissOnly, JudgeGrade.Miss, true)]
        [TestCase(JudgeDisplayOption.MissOnly, JudgeGrade.TooFast, true)]
        [TestCase(JudgeDisplayOption.Disable, JudgeGrade.Miss, false)]
        public void CheckJudgeDisplaySetting_FiltersGrades(JudgeDisplayOption option, JudgeGrade grade, bool expected)
        {
            Assert.That(NoteEffectManager.CheckJudgeDisplaySetting(option, new NoteJudgeResult() { Grade = grade }), Is.EqualTo(expected));
        }

        /// <summary>Checks the button light colour for each grade.</summary>
        [Test]
        public void GetColor_MapsGradesToButtonColours()
        {
            var manager = _rig.EffectManager;

            Assert.That(manager.GetColor(JudgeGrade.FastGood), Is.EqualTo(manager.buttonGoodColor));
            Assert.That(manager.GetColor(JudgeGrade.LateGreat2nd), Is.EqualTo(manager.buttonGreatColor));
            Assert.That(manager.GetColor(JudgeGrade.FastPerfect3rd), Is.EqualTo(manager.buttonPerfectColor));
            Assert.That(manager.GetColor(JudgeGrade.Miss), Is.EqualTo(Color.white));
        }

        /// <summary>Checks the sensor range that receives touch feedback for each feedback level.</summary>
        /// <param name="level">The feedback level.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ReadsFeedbackRange([Values(TouchFeedbackLevel.All, TouchFeedbackLevel.Outer_Only, TouchFeedbackLevel.Inner_Only, TouchFeedbackLevel.Disable)] TouchFeedbackLevel level)
        {
            _rig.Settings.Display.TouchFeedback = level;
            var manager = _rig.Objects.Add<NoteEffectManager>("Manager");
            yield return null;

            var range = PrivateMember.GetField<Range<int>>(manager, "_touchFeedbackLevel");
            var expected = level switch
            {
                TouchFeedbackLevel.All => (0, 32),
                TouchFeedbackLevel.Outer_Only => (0, 7),
                TouchFeedbackLevel.Inner_Only => (8, 32),
                _ => (0, 0)
            };
            Assert.That((range.Start, range.End), Is.EqualTo(expected));
            Assert.That(Majdata<NoteEffectManager>.Instance, Is.SameAs(manager));
        }

        /// <summary>Checks that pressing an outer sensor plays its feedback while inner sensors are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AnyAreaClick_PlaysFeedbackInRange()
        {
            AllowFeedback(SensorArea.A3);
            AllowFeedback(SensorArea.B3);
            yield return null;

            Click(new InputEventArgs() { SArea = SensorArea.A3, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On });
            Click(new InputEventArgs() { SArea = SensorArea.B3, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On });

            Assert.That(Feedback(SensorArea.A3).Active, Is.True);
            Assert.That(Feedback(SensorArea.B3).Active, Is.False);
        }

        /// <summary>Checks that button presses map to their outer sensor and invalid events are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AnyAreaClick_FiltersEvents()
        {
            _rig.Settings.Display.TouchFeedback = TouchFeedbackLevel.All;
            var manager = _rig.Objects.Add<NoteEffectManager>("AllFeedback");
            yield return null;
            foreach (SensorArea area in Enum.GetValues(typeof(SensorArea)))
            {
                AllowFeedback(area, manager);
            }

            Click(new InputEventArgs() { BZone = ButtonZone.Test, IsButton = true, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On }, manager);
            Click(new InputEventArgs() { BZone = ButtonZone.A5, IsButton = true, OldStatus = SwitchStatus.On, Status = SwitchStatus.Off }, manager);
            Click(new InputEventArgs() { SArea = SensorArea.D2, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On }, manager);
            Click(new InputEventArgs() { SArea = (SensorArea)40, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On }, manager);
            Assert.That(Feedback(SensorArea.A5).Active, Is.False);
            Assert.That(Feedback(SensorArea.D2).Active, Is.False);

            Click(new InputEventArgs() { BZone = ButtonZone.A5, IsButton = true, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On }, manager);
            Click(new InputEventArgs() { SArea = SensorArea.E4, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On }, manager);

            Assert.That(Feedback(SensorArea.A5).Active, Is.True);
            Assert.That(Feedback(SensorArea.E4).Active, Is.True);
        }

        /// <summary>Checks that a hit judgment suppresses the feedback of its sensor for a short time.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AnyAreaClick_IsSuppressedRightAfterJudgment()
        {
            AllowFeedback(SensorArea.A2);
            yield return null;
            _rig.EffectManager.PlayTapJudgeResult(2, new NoteJudgeResult() { Grade = JudgeGrade.Perfect });

            Click(new InputEventArgs() { SArea = SensorArea.A2, OldStatus = SwitchStatus.Off, Status = SwitchStatus.On });

            Assert.That(Feedback(SensorArea.A2).Active, Is.False);
        }

        /// <summary>Checks that tap judgments play the key effect and only hits reset the feedback timer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayTapJudgeResult_PlaysKeyEffect()
        {
            var lastTrigger = TimeSpan.FromSeconds(-5);
            LastTriggerTimes()[SensorArea.A4] = lastTrigger;
            yield return null;

            _rig.EffectManager.PlayTapJudgeResult(4, new NoteJudgeResult() { Grade = JudgeGrade.Miss });
            Assert.That(LastTriggerTimes()[SensorArea.A4], Is.EqualTo(lastTrigger));
            Assert.That(TapEffect(4).Find("JudgeText").GetComponent<JudgeTextDisplayer>().Active, Is.True);

            _rig.EffectManager.PlayTapJudgeResult(4, new NoteJudgeResult() { Grade = JudgeGrade.LateGreat });
            Assert.That(LastTriggerTimes()[SensorArea.A4], Is.EqualTo(MajTimeline.Time));
            Assert.That(TapEffect(4).Find("Effects/Great").GetComponent<MajComponent>().Active, Is.True);

            _rig.EffectManager.ResetEffect(4);
            Assert.That(TapEffect(4).Find("Effects/Great").GetComponent<MajComponent>().Active, Is.False);
        }

        /// <summary>Checks the touch and touch hold judgment forwarding.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayTouchResults_PlaySensorEffects()
        {
            var lastTrigger = TimeSpan.FromSeconds(-5);
            LastTriggerTimes()[SensorArea.B5] = lastTrigger;
            yield return null;

            _rig.EffectManager.PlayTouchJudgeResult(SensorArea.B5, new NoteJudgeResult() { Grade = JudgeGrade.Miss });
            Assert.That(LastTriggerTimes()[SensorArea.B5], Is.EqualTo(lastTrigger));
            _rig.EffectManager.PlayTouchJudgeResult(SensorArea.B5, new NoteJudgeResult() { Grade = JudgeGrade.Perfect });
            Assert.That(LastTriggerTimes()[SensorArea.B5], Is.EqualTo(MajTimeline.Time));
            Assert.That(_rig.EffectPool.transform.GetChild(1).Find("TouchEffect_B5").GetComponentInChildren<TouchJudgeEffectDisplayer>(true).Active, Is.True);

            _rig.EffectManager.PlayTouchHoldJudgeResult(SensorArea.C, new NoteJudgeResult() { Grade = JudgeGrade.LateGood });
            var touchHoldEffect = _rig.EffectPool.transform.GetChild(2).Find("TouchHoldEffect_C");
            Assert.That(touchHoldEffect.Find("Effects/Good").GetComponent<MajComponent>().Active, Is.True);
        }

        /// <summary>Checks the hold effect forwarding for keys and sensors.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldEffects_PlayAndReset()
        {
            yield return null;
            var keyEffect = _rig.EffectPool.transform.GetChild(0).Find("HoldEffect_7").GetComponent<HoldEffectDisplayer>();
            var sensorEffect = _rig.EffectPool.transform.GetChild(2).Find("TouchHold_HoldingEffect_E6").GetComponent<HoldEffectDisplayer>();

            _rig.EffectManager.PlayHoldEffect(7, JudgeGrade.Perfect);
            _rig.EffectManager.PlayHoldEffect(SensorArea.E6, JudgeGrade.LateGood);
            Assert.That(keyEffect.Active, Is.True);
            Assert.That(sensorEffect.Active, Is.True);

            _rig.EffectManager.ResetHoldEffect(7);
            _rig.EffectManager.ResetHoldEffect(SensorArea.E6);
            Assert.That(keyEffect.Active, Is.False);
            Assert.That(sensorEffect.Active, Is.False);
        }

        /// <summary>Checks that firework requests reach the firework displayer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayFireworkEffect_ShowsFirework()
        {
            yield return null;

            _rig.EffectManager.PlayFireworkEffect(new Vector3(1f, 1f, 0f));
            _rig.EffectPool.OnLateUpdate();

            Assert.That(_rig.Firework.Active, Is.True);
        }

        /// <summary>Checks that destroying the manager frees the singleton.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_FreesSingleton()
        {
            Object.Destroy(_rig.EffectManager.gameObject);
            yield return null;

            Assert.That(Majdata<NoteEffectManager>.IsNull, Is.True);
        }

        /// <summary>Checks that the effect pool creates effects only for the keys and sensors the chart uses.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EffectPool_InitCreatesEffectsForUsedPositions()
        {
            var pool = CreatePartialEffectPool();
            yield return null;

            var tapParent = pool.transform.GetChild(0);
            Assert.That(tapParent.Find("TapEffect_3"), Is.Not.Null);
            Assert.That(tapParent.Find("TapEffect_1"), Is.Null);
            Assert.That(tapParent.Find("HoldEffect_1"), Is.Not.Null);
            Assert.That(pool.transform.GetChild(1).childCount, Is.EqualTo(1));
            Assert.That(pool.transform.GetChild(2).Find("TouchHoldEffect_D1"), Is.Not.Null);
            Assert.That(pool.transform.GetChild(3).childCount, Is.EqualTo(33));
            Assert.That(pool.transform.GetChild(3).GetChild(0).localScale, Is.EqualTo(new Vector3(0.5f, 0.5f, 1f)));

            pool.Init();
            Assert.That(pool.transform.GetChild(3).childCount, Is.EqualTo(33));
        }

        /// <summary>Checks that the effect pool ignores feedback and late updates before it is initialized.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EffectPool_IgnoresCallsBeforeInit()
        {
            var effects = _rig.Objects.Create("UninitializedEffects");
            for (var i = 0; i < 4; i++)
            {
                NoteTemplates.Child($"Layer{i}", effects.transform);
            }
            var pool = effects.AddComponent<NoteEffectPool>();
            yield return null;

            Assert.DoesNotThrow(() => pool.PlayFeedbackEffect(SensorArea.A1));
            Assert.DoesNotThrow(() => pool.ResetFeedbackEffect(SensorArea.A1));
            Assert.DoesNotThrow(() => pool.OnLateUpdate());
        }

        /// <summary>Checks that resetting a fully initialized pool hides every judgment and hold effect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EffectPool_ResetHidesEffects()
        {
            yield return null;
            _rig.EffectManager.PlayTapJudgeResult(1, new NoteJudgeResult() { Grade = JudgeGrade.Perfect });
            _rig.EffectManager.PlayHoldEffect(SensorArea.C, JudgeGrade.Perfect);

            _rig.EffectPool.Reset();

            Assert.That(TapEffect(1).Find("Effects/Perfect").GetComponent<MajComponent>().Active, Is.False);
            Assert.That(_rig.EffectPool.transform.GetChild(2).Find("TouchHold_HoldingEffect_C").GetComponent<HoldEffectDisplayer>().Active, Is.False);
        }

        /// <summary>Checks that resetting a pool initialized for only some positions does not fail.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EffectPool_ResetWithUnusedPositions()
        {
            var pool = CreatePartialEffectPool();
            yield return null;

            Assert.DoesNotThrow(() => pool.Reset());
        }

        /// <summary>Allows feedback for a sensor by moving its last trigger time a second into the past.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="manager">The manager, or the rig manager when null.</param>
        private void AllowFeedback(SensorArea area, NoteEffectManager? manager = null)
        {
            LastTriggerTimes(manager)[area] = MajTimeline.Time - TimeSpan.FromSeconds(1);
        }

        /// <summary>Gets the last trigger times of a manager.</summary>
        /// <param name="manager">The manager, or the rig manager when null.</param>
        /// <returns>The live dictionary.</returns>
        private Dictionary<SensorArea, TimeSpan> LastTriggerTimes(NoteEffectManager? manager = null)
        {
            return PrivateMember.GetField<Dictionary<SensorArea, TimeSpan>>(manager ?? _rig.EffectManager, "_lastTriggerTimes");
        }

        /// <summary>Delivers an input event to the touch feedback handler.</summary>
        /// <param name="args">The event.</param>
        /// <param name="manager">The manager, or the rig manager when null.</param>
        private void Click(InputEventArgs args, NoteEffectManager? manager = null)
        {
            PrivateMember.Invoke(manager ?? _rig.EffectManager, "OnAnyAreaClick", null, args);
        }

        /// <summary>Gets the touch feedback displayer of a sensor.</summary>
        /// <param name="area">The sensor.</param>
        /// <returns>The displayer.</returns>
        private TouchFeedbackDisplayer Feedback(SensorArea area)
        {
            return _rig.EffectPool.transform.GetChild(3).Find(area.ToString()).GetComponent<TouchFeedbackDisplayer>();
        }

        /// <summary>Gets the tap effect of a key.</summary>
        /// <param name="key">The key from 1 to 8.</param>
        /// <returns>The effect transform.</returns>
        private Transform TapEffect(int key)
        {
            return _rig.EffectPool.transform.GetChild(0).Find($"TapEffect_{key}");
        }

        /// <summary>Creates and initializes a second effect pool for tap 3, hold 1, touch C and touch hold D1 only.</summary>
        /// <returns>The pool.</returns>
        private NoteEffectPool CreatePartialEffectPool()
        {
            _rig.SetLoaderFlags(false);
            PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), "_isHasTap")[2] = true;
            PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), "_isHasHold")[0] = true;
            PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), "_isHasTouch")[(int)SensorArea.C] = true;
            PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), "_isHasTouchHold")[(int)SensorArea.D1] = true;
            var effects = _rig.Objects.Create("PartialEffects");
            for (var i = 0; i < 4; i++)
            {
                NoteTemplates.Child($"Layer{i}", effects.transform);
            }
            var pool = effects.AddComponent<NoteEffectPool>();
            PrivateMember.SetField(pool, "_tapEffectPrefab", _rig.TapEffectTemplate);
            PrivateMember.SetField(pool, "_touchHoldEffectPrefab", _rig.TouchHoldEffectTemplate);
            PrivateMember.SetField(pool, "_touchEffectPrefab", _rig.TouchEffectTemplate);
            PrivateMember.SetField(pool, "_holdEffectPrefab", _rig.HoldEffectTemplate);
            PrivateMember.SetField(pool, "_touchFeedbackEffectPrefab", _rig.FeedbackTemplate);
            pool.Init();
            return pool;
        }
    }
}
