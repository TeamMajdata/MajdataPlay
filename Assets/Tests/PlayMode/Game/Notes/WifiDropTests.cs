using System.Collections;
using MajdataPlay.IO;
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
    /// Drives <see cref="WifiDrop"/> through the slide updater: the three stars, skins, fade-in, the three judge queues,
    /// bar hiding, too-late misses, autoplay and the classic variant.
    /// </summary>
    public class WifiDropTests
    {
        /// <summary>The modern wifi slide constant.</summary>
        private const float WifiConst = 0.16287f;

        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Gets the skin cases as break, each and mine flags with the expected bar prefix and star sprite name.</summary>
        private static object[] SkinCases => new object[]
        {
            new object[] { false, false, false, "Wifi", "Star", false },
            new object[] { false, true, false, "Wifi_Each", "Star_Each", false },
            new object[] { true, false, false, "Wifi_Break", "Star_Break", true },
            new object[] { false, false, true, "Wifi_Mine", "Star_Mine", false },
            new object[] { true, false, true, "Wifi_Break_Mine", "Star_Break_Mine", true }
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

        /// <summary>Checks that Awake creates three hidden stars and collects the bars and slide result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_CreatesThreeStars()
        {
            var wifi = Spawn();
            yield return null;

            Assert.That(_rig.SlideStarCount, Is.EqualTo(3));
            Assert.That(_rig.SlideStar(0).GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(wifi.EndPos, Is.EqualTo(5));
            Assert.That(wifi.SlideLength, Is.EqualTo(20f));
            Assert.That(wifi.QueueRemaining, Is.EqualTo(4));
            Assert.That(wifi.transform.GetComponentInChildren<SlideOK>(true).Shape, Is.EqualTo(global::MajdataPlay.Scenes.Game.Notes.Slide.SlideOKShape.Wifi));
        }

        /// <summary>Checks the timings, rotation, star placement and slide result set up by Init.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_ComputesTimingsAndPlacesStars()
        {
            var wifi = CreateWifi(startPos: 2);
            yield return null;

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(wifi.transform.rotation.eulerAngles.z, Is.EqualTo(315f).Within(0.01f));
            Assert.That(wifi.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - WifiConst)).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(wifi, "LastWaitTimeSec"), Is.EqualTo(WifiConst).Within(1e-4f));
            Assert.That(PrivateMember.GetField<float>(wifi, "FadeInMaxAlpha"), Is.EqualTo(1f));
            var start = NoteHelper.GetTapPosition(2, 4.8f);
            var star = _rig.SlideStar(0);
            Assert.That(star.position.x, Is.EqualTo(start.x).Within(1e-3f));
            Assert.That(star.localScale.x, Is.EqualTo(0f));
            Assert.That(_rig.SlideParent.GetComponentInChildren<SlideOK>(true).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[5]"));

            wifi.Init();
            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Inited));
        }

        /// <summary>Checks the bar and star sprites and the break material for every wifi variant.</summary>
        /// <param name="testCase">
        /// The break, each and mine flags followed by the expected bar sprite prefix, star sprite name and whether the
        /// stars use the break material.
        /// </param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSkin_PicksSpritesForVariant([ValueSource(nameof(SkinCases))] object[] testCase)
        {
            var wifi = CreateWifi(isBreak: (bool)testCase[0], isEach: (bool)testCase[1], isMine: (bool)testCase[2]);
            yield return null;

            for (var i = 0; i < 11; i++)
            {
                Assert.That(wifi.transform.GetChild(i).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo($"{testCase[3]}[{i}]"));
            }
            var star = _rig.SlideStar(1).GetComponent<SpriteRenderer>();
            Assert.That(star.sprite.name, Is.EqualTo((string)testCase[4]));
            Assert.That(star.sharedMaterial == _rig.BreakMaterial, Is.EqualTo((bool)testCase[5]));
        }

        /// <summary>Checks that the state captured for the slide inspector shows the three lanes of the wifi table.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CaptureDebugInfo_ReportsTheThreeLanes()
        {
            var wifi = CreateWifi();
            yield return null;

            var info = wifi.CaptureDebugInfo();

            Assert.That(info.TableName, Is.EqualTo("wifi"));
            Assert.That(info.TableConst, Is.EqualTo(0.162870f));
            Assert.That(info.TableClassicConst, Is.EqualTo(0.162870f));
            Assert.That(info.Queues, Has.Length.EqualTo(3));
            Assert.That(info.Queues[0].Name, Is.EqualTo("Left"));
            Assert.That(info.Queues[1].Name, Is.EqualTo("Center"));
            Assert.That(info.Queues[2].Name, Is.EqualTo("Right"));
            Assert.That(info.Queues[1].Areas[3].Sensors, Is.EqualTo(new[] { SensorArea.A5, SensorArea.B5 }));
            foreach (var lane in info.Queues)
            {
                Assert.That(lane.Areas, Has.Length.EqualTo(4), lane.Name);
                Assert.That(lane.PassedCount, Is.EqualTo(0), lane.Name);
            }
        }

        /// <summary>
        /// Checks that the state captured for the slide inspector holds the touches of the latest DJAuto run: one narrow
        /// touch for each side lane and a wide one for the centre lane at every step, the last ones at the DJAuto progress.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CaptureDebugInfo_RecordsTheLatestDJAutoTouches()
        {
            _rig.Controller.SetMods(AutoplayModeOption.DJAuto_ButtonRing_First);
            var wifi = CreateWifi();
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            for (var frame = 0; frame <= 30; frame++)
            {
                _rig.Step(1f + (frame / 60f));
            }
            var info = wifi.CaptureDebugInfo();

            Assert.That(info.IsDJAutoEnabled, Is.True);
            Assert.That(info.DJAutoTouchFrame, Is.EqualTo(Time.frameCount));
            Assert.That(info.DJAutoplayProgress, Is.EqualTo(0.5f).Within(0.01f));
            var touches = info.DJAutoTouches;
            Assert.That(touches, Is.Not.Empty);
            Assert.That(touches.Length % 3, Is.EqualTo(0), "Every step touches the three lanes");
            for (var i = 0; i < touches.Length; i++)
            {
                var isCenter = i % 3 == 1;
                Assert.That(touches[i].Radius, Is.EqualTo(isCenter ? 0.8f : 0.15f), $"Touch {i}");
                Assert.That(touches[i].SampledRadius, Is.EqualTo(isCenter ? 0.875f : 0.25f).Within(1e-6f), $"Touch {i}");
            }
            var start = NoteHelper.GetTapPosition(wifi.StartPos, 4.8f);
            var centerEnd = NoteHelper.GetTapPosition(wifi.EndPos, 4.8f);
            var lastCenter = touches[touches.Length - 2].Position;
            Assert.That(Vector3.Distance(lastCenter, Vector3.Lerp(start, centerEnd, info.DJAutoplayProgress)), Is.LessThan(0.1f));
        }

        /// <summary>Checks that the ordinary autoplay, which passes the judge areas without touching, records no DJAuto touches.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CaptureDebugInfo_AutoplayRecordsNoDJAutoTouches()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var wifi = CreateWifi();
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            _rig.Step(1f);
            _rig.Step(1.2f);
            var info = wifi.CaptureDebugInfo();

            Assert.That(info.IsDJAutoEnabled, Is.False);
            Assert.That(info.DJAutoTouchFrame, Is.EqualTo(-1));
            Assert.That(info.DJAutoTouches, Is.Empty);
        }

        /// <summary>Checks the right-hand slide result and the classic star layout.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_ClassicRightHandedWifi()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            _rig.Settings.Game.SlideSkipping = false;
            var wifi = CreateWifi(isJustR: true);
            yield return null;

            Assert.That(wifi.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - 0.2106f)).Within(1e-4f));
            var rightStar = _rig.SlideStar(2);
            var expected = NoteHelper.GetTapPosition(1.11f, 4.55f);
            Assert.That(rightStar.position.x, Is.EqualTo(expected.x).Within(1e-3f));
            Assert.That(_rig.SlideParent.GetComponentInChildren<SlideOK>(true).GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("Just[2]"));
            Assert.That(wifi.QueueRemaining, Is.EqualTo(4));
            var queues = PrivateMember.GetField<System.Memory<global::MajdataPlay.Scenes.Game.Notes.Slide.SlideArea>[]>(wifi, "JudgeQueues");
            Assert.That(queues[1].Span[0].IsSkippable, Is.False);
        }

        /// <summary>Checks the star fade-in, travel towards the three end keys and arrival.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_AnimatesStars()
        {
            var wifi = CreateWifi();
            yield return null;

            UpdateAt(wifi, 0.4f);
            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Inited));

            UpdateAt(wifi, 0.75f);
            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(_rig.SlideStar(1).GetComponent<SpriteRenderer>().color.a, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_rig.SlideStar(1).localScale.x, Is.EqualTo(1f).Within(1e-4f));

            UpdateAt(wifi, 1.5f);
            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(_rig.SlideStar(1).localScale.x, Is.EqualTo(1.5f));

            UpdateAt(wifi, 2.1f);
            Assert.That(wifi.State, Is.EqualTo(NoteStatus.Arrived));
            var center = NoteHelper.GetTapPosition(5, 4.8f);
            Assert.That(_rig.SlideStar(0).position.x, Is.EqualTo(center.x).Within(1e-3f));
            Assert.That(_rig.SlideStar(0).position.y, Is.EqualTo(center.y).Within(1e-3f));
        }

        /// <summary>Checks that classic stars grow from their normal size while fading in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnUpdate_ClassicStarScaling()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var wifi = CreateWifi();
            yield return null;

            UpdateAt(wifi, 0.75f);

            Assert.That(_rig.SlideStar(0).localScale.x, Is.EqualTo(1.25f).Within(1e-4f));
        }

        /// <summary>Plays the whole wifi over its three lanes and checks bars, sound, judgment and result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AllLanesFinishWifi()
        {
            var wifi = CreateWifi();
            var bars = wifi.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            _rig.Step(0.2f);
            Assert.That(bars[0].color.a, Is.EqualTo(1f));
            Press(1f, SensorArea.A1);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            Press(1.1f, SensorArea.B8, SensorArea.B1, SensorArea.B2);
            Assert.That(bars[1].enabled, Is.False);
            Assert.That(bars[2].enabled, Is.True);
            Press(1.2f, SensorArea.B7, SensorArea.C, SensorArea.B3);
            Assert.That(bars[3].enabled, Is.False);
            Press(1.3f, SensorArea.A6, SensorArea.A5, SensorArea.A4);
            Assert.That(wifi.IsFinished, Is.True);
            Assert.That(bars[8].enabled, Is.False);

            for (var frame = 1; frame <= 15; frame++)
            {
                _rig.Step(1.3f + (frame / 60f));
            }

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
            Assert.That(_rig.SlideParent.GetComponentInChildren<SlideOK>(true).State, Is.EqualTo(NoteStatus.Running));
        }

        /// <summary>Checks that a classic wifi is judged with the classic windows once every lane is finished.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ClassicWifiUsesClassicJudgment()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var wifi = CreateWifi();
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            Press(1f, SensorArea.A1);
            Press(1.1f, SensorArea.B8, SensorArea.B1, SensorArea.B2);
            Press(1.2f, SensorArea.B7, SensorArea.C, SensorArea.B3);
            Press(1.3f, SensorArea.A6, SensorArea.A4);
            Assert.That(wifi.IsFinished, Is.True);
            for (var frame = 1; frame <= 20; frame++)
            {
                _rig.Step(1.3f + (frame / 60f));
            }

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(PrivateMember.GetField<float>(wifi, "JudgeDiff"), Is.LessThan(0f));
        }

        /// <summary>Checks the second based fade-in offset and that autoplay with an invalid grade still judges the wifi.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AutoplayRandomGradeAndEarlyBars()
        {
            _rig.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            _rig.Settings.Game.SlideFadeInOffset = 0.1f;
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            _rig.Controller.AutoplayGrade = (JudgeGrade)77;
            var wifi = CreateWifi();
            var bars = wifi.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;
            Assert.That(PrivateMember.GetField<float>(wifi, "FadeInTiming"), Is.EqualTo(0.6f - (3.926913f / 7.5f)).Within(1e-4f));

            _rig.Step(1.1f);
            _rig.Step(1.3f);
            Assert.That(bars[1].enabled, Is.False);
            Assert.That(bars[2].enabled, Is.True);
            _rig.Step(2f);
            _rig.Step(2.02f);

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideJudgedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that an untouched wifi becomes a miss.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_UntouchedWifiIsMiss()
        {
            var wifi = CreateWifi();
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            _rig.Step(1f);
            _rig.Step(2.61f);

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideMissCount, Is.EqualTo(1));
        }

        /// <summary>Checks that autoplay hides bars in three stages and judges the wifi at its end.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_AutoplayFinishesWifi()
        {
            _rig.Controller.SetMods(AutoplayModeOption.Enable);
            var wifi = CreateWifi();
            var bars = wifi.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            _rig.Step(0.6f);
            _rig.Step(1.1f);
            _rig.Step(1.2f);
            Assert.That(_rig.SoundRequests[GameplayRig.SfxSlide], Is.True);
            Assert.That(bars[0].enabled, Is.True);
            _rig.Step(1.5f);
            Assert.That(bars[4].enabled, Is.False);
            Assert.That(bars[5].enabled, Is.True);
            _rig.Step(1.8f);
            Assert.That(bars[8].enabled, Is.False);
            Assert.That(wifi.QueueRemaining, Is.LessThan(4));

            _rig.Step(2f);
            Assert.That(PrivateMember.GetField<bool>(wifi, "IsJudged"), Is.True);
            _rig.Step(2.02f);

            Assert.That(wifi.State, Is.EqualTo(NoteStatus.End));
            Assert.That(_rig.Counter.JudgeStats.SlideCriticalCount, Is.EqualTo(1));
        }

        /// <summary>Checks the classic bar hiding when every lane has at most one area left.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Play_ClassicHidesBarsWhenNearlyDone()
        {
            _rig.Settings.Judge.Mode = JudgeModeOption.Classic;
            var wifi = CreateWifi();
            var bars = wifi.GetComponentsInChildren<SpriteRenderer>(true);
            _rig.RegisterSlides((wifi, 0.3f));
            yield return null;

            Press(1f, SensorArea.A1);
            Press(1.1f, SensorArea.B8, SensorArea.B1, SensorArea.B2);
            Press(1.2f, SensorArea.B7, SensorArea.C, SensorArea.B3);
            Assert.That(bars[3].enabled, Is.False);
            Assert.That(bars[4].enabled, Is.True);

            Press(1.3f, SensorArea.A6, SensorArea.C, SensorArea.A4);

            Assert.That(wifi.QueueRemaining, Is.EqualTo(1));
            Assert.That(bars[7].enabled, Is.False);
            Assert.That(bars[8].enabled, Is.True);
        }

        /// <summary>Spawns a wifi slide.</summary>
        /// <returns>The wifi slide.</returns>
        private WifiDrop Spawn()
        {
            return _rig.SpawnSlide<WifiDrop>(NoteTemplates.Wifi(_rig.Templates, _rig.StarTemplate));
        }

        /// <summary>Spawns, configures and initializes a wifi slide.</summary>
        /// <param name="startPos">The start key.</param>
        /// <param name="isBreak">Whether the slide is a break.</param>
        /// <param name="isEach">Whether the slide is an each slide.</param>
        /// <param name="isMine">Whether the slide is a mine.</param>
        /// <param name="isJustR">Whether the slide result faces right.</param>
        /// <returns>The wifi slide.</returns>
        private WifiDrop CreateWifi(int startPos = 1, bool isBreak = false, bool isEach = false, bool isMine = false, bool isJustR = false)
        {
            var wifi = Spawn();
            wifi.StartPos = startPos;
            wifi.Timing = 0.5f;
            wifi.StartTiming = 1f;
            wifi.Length = 1f;
            wifi.Speed = 7.5f;
            wifi.IsBreak = isBreak;
            wifi.IsEach = isEach;
            wifi.IsMine = isMine;
            wifi.IsJustR = isJustR;
            wifi.Init();
            return wifi;
        }

        /// <summary>Releases every sensor and presses the given ones for one frame.</summary>
        /// <param name="time">The frame time.</param>
        /// <param name="areas">The pressed sensors.</param>
        private void Press(float time, params SensorArea[] areas)
        {
            _rig.ClearInput();
            foreach (var area in areas)
            {
                _rig.SetSensor(area, SwitchStatus.Off, SwitchStatus.On);
            }
            _rig.Step(time);
        }

        /// <summary>Runs only the animation update of a wifi slide at a given time.</summary>
        /// <param name="wifi">The wifi slide.</param>
        /// <param name="time">The chart time.</param>
        private void UpdateAt(WifiDrop wifi, float time)
        {
            _rig.Controller.ThisFrameSec = time;
            wifi.OnUpdate();
        }
    }
}
