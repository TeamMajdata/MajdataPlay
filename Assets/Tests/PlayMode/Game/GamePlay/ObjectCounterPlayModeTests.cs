using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>
    /// Checks the user interface of <see cref="ObjectCounter"/> in Play Mode: the info displayers for every background
    /// info option, the top info, the judge counts, the judge timing gauge and the note counting of a parsed chart.
    /// </summary>
    public class ObjectCounterPlayModeTests
    {
        /// <summary>A chart with 3 taps, 3 breaks, 2 holds, 1 touch and 3 slides.</summary>
        private const string Fumen = "(120){4}1,2b,3h[4:1],B1,Ch[4:1],1-5[8:1],2b-6[8:1],3-7[8:1]b,4!-8[8:1],E";

        /// <summary>The info displayer palette: critical, perfect and normal combo, dub, bronze, silver and gold achievement, DX score, early and late.</summary>
        private static readonly Color[] s_palette =
        {
            new Color(0.0f, 0.0f, 0.1f), new Color(0.0f, 0.0f, 0.2f), new Color(0.0f, 0.0f, 0.3f), new Color(0.0f, 0.0f, 0.4f),
            new Color(0.0f, 0.0f, 0.5f), new Color(0.0f, 0.0f, 0.6f), new Color(0.0f, 0.0f, 0.7f), new Color(0.0f, 0.0f, 0.8f),
            new Color(0.0f, 0.0f, 0.9f), new Color(0.0f, 0.1f, 1.0f),
        };

        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings and timeline.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>The registrations restored in tear down.</summary>
        private readonly List<IDisposable> _slots = new();

        /// <summary>The registered gameplay manager, never awakened.</summary>
        private GamePlayManager _manager = null!;

        /// <summary>The registered game info.</summary>
        private GameInfo _gameInfo = null!;

        /// <summary>The counter under test.</summary>
        private ObjectCounter _counter = null!;

        /// <summary>A tap used to report judgements.</summary>
        private TapDrop _tap = null!;

        /// <summary>A hold used to report judgements.</summary>
        private HoldDrop _hold = null!;

        /// <summary>Installs default settings and registers the services the counter uses.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _gameInfo = new GameInfo(GameMode.Normal, null!, null!);
            _slots.Add(new SingletonScope<GameInfo>(_gameInfo));
            _slots.Add(new SingletonScope<ObjectCounter>());
            var services = _objects.Create("Services");
            services.SetActive(false);
            _manager = services.AddComponent<GamePlayManager>();
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions()));
            _slots.Add(new SingletonScope<GamePlayManager>(_manager));
            _slots.Add(new SingletonScope<OutlineLoader>(services.AddComponent<OutlineLoader>()));
            var xxlbSlot = new SingletonScope<XxlbAnimationController>();
            _slots.Add(xxlbSlot);
            var xxlb = _objects.Create("Xxlb");
            xxlb.SetActive(false);
            xxlb.AddComponent<Image>();
            xxlb.AddComponent<Animator>();
            var controller = xxlb.AddComponent<XxlbAnimationController>();
            var texture = _objects.Track(new Texture2D(1, 1));
            var sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            controller.sprites = new[] { sprite, sprite, sprite, sprite, sprite };
            xxlb.SetActive(true);
            var notes = _objects.Create("Notes");
            notes.SetActive(false);
            _tap = notes.AddComponent<TapDrop>();
            _hold = notes.AddComponent<HoldDrop>();
        }

        /// <summary>Destroys every object and restores the registrations and settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                _slots[i].Dispose();
            }
            _slots.Clear();
            _settings.Dispose();
        }

        /// <summary>Checks the note sums, scores and DX score of a parsed chart.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CountNoteSumAsync_CountsEveryNoteKind() => UniTask.ToCoroutine(async () =>
        {
            var rig = CreateCounter();

            await _counter.CountNoteSumAsync(SimaiParser.ParseChart(Fumen));

            Assert.That((_counter.TapSum, _counter.BreakSum, _counter.HoldSum, _counter.TouchSum, _counter.SlideSum), Is.EqualTo((3, 3, 2, 1, 3)));
            Assert.That(_counter.NoteSum, Is.EqualTo(12));
            Assert.That(_counter.TotalNoteBaseScore, Is.EqualTo(16000));
            Assert.That(_counter.TotalNoteExtraScore, Is.EqualTo(300));
            Assert.That(PrivateMember.GetField<long>(_counter, "_totalDXScore"), Is.EqualTo(36));
            Assert.That(rig.Critical.text, Is.EqualTo("0"));
            Assert.That(_counter.AllFinished, Is.False);
        });

        /// <summary>Checks that Awake registers the counter, reads the rate text and that OnDestroy releases it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AwakeAndOnDestroy_RegisterTheCounter()
        {
            var rig = CreateCounter();
            yield return null;

            Assert.That(Majdata<ObjectCounter>.Instance, Is.SameAs(_counter));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(_counter, "_rate"), Is.SameAs(rig.Rate));

            UnityEngine.Object.Destroy(_counter.gameObject);
            yield return null;

            Assert.That(Majdata<ObjectCounter>.Instance, Is.Null);
        }

        /// <summary>Checks which top info parent is shown for every top info option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_ShowsTheSelectedTopInfo()
        {
            var expected = new (TopInfoDisplayOption Option, bool Judge, bool Timing)[]
            {
                (TopInfoDisplayOption.Judge, true, false),
                (TopInfoDisplayOption.Timing, false, true),
                (TopInfoDisplayOption.None, false, false),
            };
            foreach (var (option, judge, timing) in expected)
            {
                _settings.Settings.Game.TopInfo = option;
                var rig = CreateCounter();
                Assert.That(rig.TopJudge.activeSelf, Is.EqualTo(judge), option.ToString());
                Assert.That(rig.TopTiming.activeSelf, Is.EqualTo(timing), option.ToString());
                UnityEngine.Object.DestroyImmediate(_counter.gameObject);
            }
            yield return null;
        }

        /// <summary>Checks the header text and colors set for every background info option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_InitializesTheInfoHeaders()
        {
            var expected = new (BGInfoOption Option, string Header, string? Text, int Color)[]
            {
                (BGInfoOption.CPCombo, "CPCombo", null, 0),
                (BGInfoOption.PCombo, "PCombo", null, 1),
                (BGInfoOption.Combo, "Combo", null, 2),
                (BGInfoOption.Achievement, "Achievement", null, 6),
                (BGInfoOption.DXScore, "でらっくす SCORE", null, 7),
                (BGInfoOption.DXScoreRank, "でらっくす SCORE", null, 7),
                (BGInfoOption.S_Border, "S  BORDER", "4.0000%", 5),
                (BGInfoOption.SS_Border, "SS  BORDER", "2.0000%", 6),
                (BGInfoOption.SSS_Border, "SSS  BORDER", "1.0000%", 6),
                (BGInfoOption.MyBest, "MyBestScore BORDER", "101.0000%", 6),
                (BGInfoOption.Diff, string.Empty, string.Empty, 2),
            };
            foreach (var (option, header, text, color) in expected)
            {
                _settings.Settings.Game.BGInfo = option;
                var rig = CreateCounter();
                Assert.That(rig.CenterHeader.text, Is.EqualTo(header), option.ToString());
                Assert.That(rig.CenterHeader.color, Is.EqualTo(s_palette[color]), option.ToString());
                if (text is not null)
                {
                    Assert.That(rig.CenterText.text, Is.EqualTo(text), option.ToString());
                }
                UnityEngine.Object.DestroyImmediate(_counter.gameObject);
            }
            yield return null;
        }

        /// <summary>Checks that the none option hides the displayer and that the gauge replaces the secondary info.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesUnusedDisplayers()
        {
            _settings.Settings.Game.BGInfo = BGInfoOption.None;
            _settings.Settings.Game.SecondaryBGInfo = BGInfoOption.Combo;
            _settings.Settings.Game.EnableJudgeTimingGauge = true;

            var rig = CreateCounter();
            yield return null;

            Assert.That(rig.CenterText.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(rig.CenterHeader.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(PrivateMember.GetField<BGInfoOption>(_counter, "_secondaryInfoDisplayOption"), Is.EqualTo(BGInfoOption.None));
            Assert.That(rig.Gauge.activeSelf, Is.True);
            Assert.That(rig.GaugeScales.TrueForAll(x => !x.enabled), Is.True, "Gauge scales start hidden");
        }

        /// <summary>Checks that the gauge is hidden when disabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesTheGaugeWhenDisabled()
        {
            _settings.Settings.Game.EnableJudgeTimingGauge = false;
            _settings.Settings.Game.SecondaryBGInfo = BGInfoOption.Combo;

            var rig = CreateCounter();
            yield return null;

            Assert.That(rig.Gauge.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<BGInfoOption>(_counter, "_secondaryInfoDisplayOption"), Is.EqualTo(BGInfoOption.Combo));
        }

        /// <summary>Checks the autoplay and dan life headers set in Start, and that view mode leaves them alone.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ShowsAutoplayOrLife()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions { AutoPlay = AutoplayModeOption.Enable }));
            var autoplay = CreateCounter();
            yield return null;
            Assert.That(autoplay.CenterHeader.text, Is.EqualTo("AUTOPLAY"));
            UnityEngine.Object.DestroyImmediate(_counter.gameObject);

            var dan = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 30 },
            };
            dan.CurrentHP = 30;
            Majdata<GameInfo>.Instance = dan;
            var life = CreateCounter();
            yield return null;
            Assert.That(life.CenterHeader.text, Is.EqualTo("LIFE"));
            Assert.That(life.CenterText.text, Is.EqualTo("30"));
            UnityEngine.Object.DestroyImmediate(_counter.gameObject);

            MajEnv.Mode = RunningMode.View;
            var view = CreateCounter();
            yield return null;
            Assert.That(view.CenterHeader.text, Is.EqualTo("Combo"));
        }

        /// <summary>Checks the combo counters and that a zero combo hides the displayer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsCombos() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.BGInfo = BGInfoOption.Combo;
            _settings.Settings.Game.SecondaryBGInfo = BGInfoOption.CPCombo;
            _settings.Settings.Game.SubScreenBGInfo = BGInfoOption.PCombo;
            var rig = await CreateCountedCounterAsync();

            _counter.OnLateUpdate();
            Assert.That(rig.CenterText.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER), "A zero combo is hidden");

            Report(_tap, JudgeGrade.Perfect, 0f);
            Report(_tap, JudgeGrade.FastPerfect2nd, -20f);
            _counter.OnLateUpdate();

            Assert.That(rig.CenterText.text, Is.EqualTo("2"));
            Assert.That(rig.CenterText.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            Assert.That(rig.SecondaryText.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER), "The critical combo was broken");
            Assert.That(rig.SubScreenText.text, Is.EqualTo("2"));
        });

        /// <summary>Checks the achievement texts and colors of every achievement option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsAchievements() => UniTask.ToCoroutine(async () =>
        {
            var expected = new (BGInfoOption Option, string Text, int Color)[]
            {
                (BGInfoOption.Achievement_101, "101.0000%", 6),
                (BGInfoOption.Achievement_100, "100.0000%", 6),
                (BGInfoOption.Achievement, "3.1250%", 3),
                (BGInfoOption.AchievementClassical, "3.12%", 3),
                (BGInfoOption.AchievementClassical_100, "100.00%", 6),
            };
            foreach (var (option, text, color) in expected)
            {
                _settings.Settings.Game.BGInfo = option;
                var rig = await CreateCountedCounterAsync();
                Report(_tap, JudgeGrade.Perfect, 0f);

                _counter.OnLateUpdate();

                Assert.That(rig.CenterText.text, Is.EqualTo(text), option.ToString());
                Assert.That(rig.CenterText.color, Is.EqualTo(s_palette[color]), option.ToString());
                UnityEngine.Object.DestroyImmediate(_counter.gameObject);
            }
        });

        /// <summary>Checks the DX score and the DX score rank displays.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsDxScores() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.BGInfo = BGInfoOption.DXScore;
            _settings.Settings.Game.SecondaryBGInfo = BGInfoOption.DXScoreRank;
            var rig = await CreateCountedCounterAsync();

            Report(_tap, JudgeGrade.LateGreat, 50f);
            _counter.OnLateUpdate();

            Assert.That(rig.CenterText.text, Is.EqualTo("-2"));
            Assert.That(rig.SecondaryHeader.text, Is.EqualTo("✧3"));
            Assert.That(rig.SecondaryText.text, Is.EqualTo("+0"));
            Assert.That(rig.SecondaryText.color, Is.EqualTo(s_palette[6]));

            Report(_tap, JudgeGrade.LateGood, 120f);
            _counter.OnLateUpdate();
            Assert.That(rig.SecondaryHeader.text, Is.EqualTo("✧1"));
            Assert.That(rig.SecondaryText.color, Is.EqualTo(s_palette[7]));

            Report(_tap, JudgeGrade.Miss, 0f);
            _counter.OnLateUpdate();
            Assert.That(rig.SecondaryText.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER), "Below the lowest rank the display is hidden");
        });

        /// <summary>Checks the rank border displays and that a missed border hides the displayer.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsRankBorders() => UniTask.ToCoroutine(async () =>
        {
            var expected = new (BGInfoOption Option, string Text)[]
            {
                (BGInfoOption.S_Border, "4.0000%"),
                (BGInfoOption.SS_Border, "2.0000%"),
                (BGInfoOption.SSS_Border, "1.0000%"),
                (BGInfoOption.MyBest, "101.0000%"),
            };
            foreach (var (option, text) in expected)
            {
                _settings.Settings.Game.BGInfo = option;
                var rig = await CreateCountedCounterAsync();
                Report(_tap, JudgeGrade.Perfect, 0f);

                _counter.OnLateUpdate();

                Assert.That(rig.CenterText.text, Is.EqualTo(text), option.ToString());
                UnityEngine.Object.DestroyImmediate(_counter.gameObject);
            }

            _settings.Settings.Game.BGInfo = BGInfoOption.SSS_Border;
            var missed = await CreateCountedCounterAsync();
            Report(_tap, JudgeGrade.Miss, 0f);
            _counter.OnLateUpdate();
            Assert.That(missed.CenterText.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
        });

        /// <summary>Checks the fast and late difference display and its fading.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsTheLastDifference() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.BGInfo = BGInfoOption.Diff;
            var rig = await CreateCountedCounterAsync();
            _settings.SetDeltaTime(0.5f);

            Report(_tap, JudgeGrade.FastGreat, -12.345f);
            _counter.OnLateUpdate();
            Assert.That(rig.CenterText.text, Is.EqualTo("-12.35"));
            Assert.That(rig.CenterHeader.text, Is.EqualTo("FAST"));
            Assert.That(rig.CenterText.color.a, Is.EqualTo(1f));

            Report(_hold, JudgeGrade.LateGood, 40f);
            _counter.OnLateUpdate();
            _counter.OnLateUpdate();
            Assert.That(rig.CenterHeader.text, Is.EqualTo("LATE"));
            Assert.That(rig.CenterText.color.b, Is.EqualTo(s_palette[9].b));
            Assert.That(rig.CenterText.color.a, Is.EqualTo(1f).Within(1e-5f));

            _counter.OnLateUpdate();
            Assert.That(rig.CenterText.color.a, Is.EqualTo(0f).Within(1e-5f), "The difference fades out");
        });

        /// <summary>Checks the judge counts and the judge top info.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsJudgeCounts() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.TopInfo = TopInfoDisplayOption.Judge;
            _settings.Settings.Display.FastLateType = JudgeDisplayOption.All;
            var rig = await CreateCountedCounterAsync();

            Report(_tap, JudgeGrade.Perfect, -1f);
            Report(_tap, JudgeGrade.LatePerfect2nd, 20f);
            Report(_tap, JudgeGrade.FastGreat, -60f);
            Report(_tap, JudgeGrade.LateGood, 120f);
            Report(_tap, JudgeGrade.Miss, 200f);
            _counter.OnLateUpdate();

            Assert.That(rig.Critical.text, Is.EqualTo("1"));
            Assert.That(rig.Perfect.text, Is.EqualTo("1"));
            Assert.That(rig.Great.text, Is.EqualTo("1"));
            Assert.That(rig.Good.text, Is.EqualTo("1"));
            Assert.That(rig.Miss.text, Is.EqualTo("1"));
            Assert.That(rig.Fast.text, Is.EqualTo("2"));
            Assert.That(rig.Late.text, Is.EqualTo("2"));
            Assert.That(rig.TopPerfect.text, Is.EqualTo("2"));
            Assert.That(rig.TopGreat.text, Is.EqualTo("1"));
            Assert.That(rig.TopGood.text, Is.EqualTo("1"));
            Assert.That(rig.TopMiss.text, Is.EqualTo("1"));
        });

        /// <summary>Checks that the top info leaves zero counts empty and shows fast and late counts for the timing option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsTimingTopInfo() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.TopInfo = TopInfoDisplayOption.Timing;
            _settings.Settings.Display.FastLateType = JudgeDisplayOption.All;
            var rig = await CreateCountedCounterAsync();

            _counter.OnLateUpdate();
            Assert.That(rig.TopFast.text, Is.Empty);
            Assert.That(rig.TopLate.text, Is.Empty);

            Report(_tap, JudgeGrade.FastGreat, -60f);
            _counter.OnLateUpdate();
            Assert.That(rig.TopFast.text, Is.EqualTo("1"));
            Assert.That(rig.TopLate.text, Is.Empty);
        });

        /// <summary>Checks that the judge top info leaves zero counts empty.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_LeavesZeroTopCountsEmpty() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.TopInfo = TopInfoDisplayOption.Judge;
            var rig = await CreateCountedCounterAsync();

            _counter.OnLateUpdate();

            Assert.That(rig.TopPerfect.text, Is.Empty);
            Assert.That(rig.TopGreat.text, Is.Empty);
            Assert.That(rig.TopGood.text, Is.Empty);
            Assert.That(rig.TopMiss.text, Is.Empty);
        });

        /// <summary>Checks that the life display follows dan judgements.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ShowsDanLife() => UniTask.ToCoroutine(async () =>
        {
            var dan = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 30 },
            };
            dan.MaxHP = 30;
            dan.CurrentHP = 30;
            Majdata<GameInfo>.Instance = dan;
            var rig = await CreateCountedCounterAsync();

            Report(_tap, JudgeGrade.Miss, 0f);
            _counter.OnLateUpdate();

            Assert.That(rig.CenterText.text, Is.EqualTo("25"));
            Assert.That(rig.CenterText.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
        });

        /// <summary>Checks the judge timing gauge: scale positions, the average cursor and the fade out.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator JudgeTimingGauge_TracksRecentDifferences() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.EnableJudgeTimingGauge = true;
            var rig = await CreateCountedCounterAsync();

            Report(_tap, JudgeGrade.FastGreat, 30f);
            Report(_hold, JudgeGrade.LateGreat, -300f);
            _counter.OnLateUpdate();

            Assert.That(rig.GaugeScales.TrueForAll(x => x.enabled), Is.True);
            Assert.That(rig.GaugeScales[0].rectTransform.anchoredPosition.x, Is.EqualTo(56.5f).Within(1e-3f));
            Assert.That(rig.GaugeScales[1].rectTransform.anchoredPosition.x, Is.EqualTo(-282.5f).Within(1e-3f));
            Assert.That(rig.Cursor.anchoredPosition.x, Is.EqualTo(282.5f * (-60f / 150f)).Within(1e-3f));
            Assert.That(rig.Cursor.anchoredPosition.y, Is.EqualTo(-30f));

            _settings.SetDeltaTime(1f);
            _counter.OnPreUpdate();
            Assert.That(rig.GaugeScales[0].color.a, Is.EqualTo(0.6f).Within(1e-4f));

            _settings.SetDeltaTime(5f);
            _counter.OnPreUpdate();
            _counter.OnPreUpdate();
            Assert.That(rig.GaugeScales[0].color.a, Is.EqualTo(0f).Within(1e-4f));
        });

        /// <summary>Checks that the gauge cursor stays at a finite position before the first judgement.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator JudgeTimingGauge_CursorIsFiniteBeforeAnyJudgement() => UniTask.ToCoroutine(async () =>
        {
            _settings.Settings.Game.EnableJudgeTimingGauge = true;
            var rig = await CreateCountedCounterAsync();

            _counter.OnLateUpdate();

            Assert.That(float.IsNaN(rig.Cursor.anchoredPosition.x), Is.False);
        });

        /// <summary>Checks that judgements request the outline effect and the dance in the next late update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_ConsumesTheEffectRequests() => UniTask.ToCoroutine(async () =>
        {
            await CreateCountedCounterAsync();
            Report(_tap, JudgeGrade.Perfect, 0f);

            _counter.OnLateUpdate();

            Assert.That(PrivateMember.GetField<bool>(_counter, "_isOutlinePlayRequested"), Is.False);
        });

        /// <summary>Reports a judgement of a note.</summary>
        /// <param name="note">The note.</param>
        /// <param name="grade">The judge grade.</param>
        /// <param name="diff">The judge difference in milliseconds.</param>
        private void Report(NoteDrop note, JudgeGrade grade, float diff)
        {
            switch (note)
            {
                case TapDrop tap:
                    _counter.ReportResult(tap, new NoteJudgeResult { Grade = grade, Diff = diff });
                    break;
                case HoldDrop hold:
                    _counter.ReportResult(hold, new NoteJudgeResult { Grade = grade, Diff = diff });
                    break;
            }
        }

        /// <summary>Creates a counter, waits for Start and counts the notes of the test chart.</summary>
        /// <returns>The counter rig.</returns>
        private async UniTask<CounterRig> CreateCountedCounterAsync()
        {
            var rig = CreateCounter();
            await UniTask.Yield();
            await _counter.CountNoteSumAsync(SimaiParser.ParseChart(Fumen));
            return rig;
        }

        /// <summary>Creates an active counter with every user interface reference it needs.</summary>
        /// <returns>The rig holding the counter's texts and gauge.</returns>
        private CounterRig CreateCounter()
        {
            var root = _objects.Create("ObjectCounter");
            root.SetActive(false);
            var rig = new CounterRig();
            _counter = root.AddComponent<ObjectCounter>();
            var parent = root.transform;
            rig.Rate = Text("ObjectRate", null);
            rig.CenterText = Text("CenterText", parent);
            rig.CenterHeader = Text("CenterHeader", parent);
            rig.SecondaryText = Text("SecondaryText", parent);
            rig.SecondaryHeader = Text("SecondaryHeader", parent);
            rig.SubScreenText = Text("SubScreenText", parent);
            rig.Critical = Text("Critical", parent);
            rig.Perfect = Text("Perfect", parent);
            rig.Great = Text("Great", parent);
            rig.Good = Text("Good", parent);
            rig.Miss = Text("Miss", parent);
            rig.Fast = Text("Fast", parent);
            rig.Late = Text("Late", parent);
            rig.TopJudge = _objects.Create("TopJudge", parent);
            rig.TopTiming = _objects.Create("TopTiming", parent);
            rig.TopPerfect = Text("TopPerfect", rig.TopJudge.transform);
            rig.TopGreat = Text("TopGreat", rig.TopJudge.transform);
            rig.TopGood = Text("TopGood", rig.TopJudge.transform);
            rig.TopMiss = Text("TopMiss", rig.TopJudge.transform);
            rig.TopFast = Text("TopFast", rig.TopTiming.transform);
            rig.TopLate = Text("TopLate", rig.TopTiming.transform);
            rig.Gauge = _objects.Create("Gauge", parent);
            var scales = _objects.Create("GaugeScales", rig.Gauge.transform);
            rig.GaugeScales = new List<RawImage>
            {
                _objects.Add<RawImage>("Scale0", scales.transform),
                _objects.Add<RawImage>("Scale1", scales.transform),
            };
            rig.Cursor = _objects.Add<RawImage>("Cursor", rig.Gauge.transform).rectTransform;

            PrivateMember.SetField(_counter, "_mainScreenInfoDisplayerColorPalette", s_palette);
            PrivateMember.SetField(_counter, "_subScreenInfoDisplayerColorPalette", s_palette);
            PrivateMember.SetField(_counter, "_centerInfoDisplayerText", rig.CenterText);
            PrivateMember.SetField(_counter, "_centerInfoDisplayerHeader", rig.CenterHeader);
            PrivateMember.SetField(_counter, "_secondaryInfoDisplayerText", rig.SecondaryText);
            PrivateMember.SetField(_counter, "_secondaryInfoDisplayerHeader", rig.SecondaryHeader);
            PrivateMember.SetField(_counter, "_subScreenInfoDisplayerText", rig.SubScreenText);
            PrivateMember.SetField(_counter, "_criticalCountDisplayer", rig.Critical);
            PrivateMember.SetField(_counter, "_perfectCountDisplayer", rig.Perfect);
            PrivateMember.SetField(_counter, "_greatCountDisplayer", rig.Great);
            PrivateMember.SetField(_counter, "_goodCountDisplayer", rig.Good);
            PrivateMember.SetField(_counter, "_missCountDisplayer", rig.Miss);
            PrivateMember.SetField(_counter, "_fastCountDisplayer", rig.Fast);
            PrivateMember.SetField(_counter, "_lateCountDisplayer", rig.Late);
            PrivateMember.SetField(_counter, "_topInfoJudgeParent", rig.TopJudge);
            PrivateMember.SetField(_counter, "_topInfoTimingParent", rig.TopTiming);
            PrivateMember.SetField(_counter, "_topInfoPerfect", rig.TopPerfect);
            PrivateMember.SetField(_counter, "_topInfoGreat", rig.TopGreat);
            PrivateMember.SetField(_counter, "_topInfoGood", rig.TopGood);
            PrivateMember.SetField(_counter, "_topInfoMiss", rig.TopMiss);
            PrivateMember.SetField(_counter, "_topInfoFast", rig.TopFast);
            PrivateMember.SetField(_counter, "_topInfoLate", rig.TopLate);
            PrivateMember.SetField(_counter, "_judgeTimingGaugeParent", rig.Gauge);
            PrivateMember.SetField(_counter, "_judgeTimingGaugeScaleParent", scales);
            PrivateMember.SetField(_counter, "_judgeTimingGaugeCursor", rig.Cursor);
            root.SetActive(true);
            return rig;
        }

        /// <summary>Creates a tracked text object.</summary>
        /// <param name="name">The object name.</param>
        /// <param name="parent">The parent transform, or null for a root object.</param>
        /// <returns>The text.</returns>
        private TextMeshProUGUI Text(string name, Transform? parent)
        {
            return _objects.Add<TextMeshProUGUI>(name, parent);
        }

        /// <summary>The user interface objects of one counter.</summary>
        private sealed class CounterRig
        {
            /// <summary>Gets or sets the rate text found by name.</summary>
            public TextMeshProUGUI Rate { get; set; } = null!;

            /// <summary>Gets or sets the centre info text.</summary>
            public TextMeshProUGUI CenterText { get; set; } = null!;

            /// <summary>Gets or sets the centre info header.</summary>
            public TextMeshProUGUI CenterHeader { get; set; } = null!;

            /// <summary>Gets or sets the secondary info text.</summary>
            public TextMeshProUGUI SecondaryText { get; set; } = null!;

            /// <summary>Gets or sets the secondary info header.</summary>
            public TextMeshProUGUI SecondaryHeader { get; set; } = null!;

            /// <summary>Gets or sets the sub screen info text.</summary>
            public TextMeshProUGUI SubScreenText { get; set; } = null!;

            /// <summary>Gets or sets the critical perfect count.</summary>
            public TextMeshProUGUI Critical { get; set; } = null!;

            /// <summary>Gets or sets the perfect count.</summary>
            public TextMeshProUGUI Perfect { get; set; } = null!;

            /// <summary>Gets or sets the great count.</summary>
            public TextMeshProUGUI Great { get; set; } = null!;

            /// <summary>Gets or sets the good count.</summary>
            public TextMeshProUGUI Good { get; set; } = null!;

            /// <summary>Gets or sets the miss count.</summary>
            public TextMeshProUGUI Miss { get; set; } = null!;

            /// <summary>Gets or sets the fast count.</summary>
            public TextMeshProUGUI Fast { get; set; } = null!;

            /// <summary>Gets or sets the late count.</summary>
            public TextMeshProUGUI Late { get; set; } = null!;

            /// <summary>Gets or sets the judge top info parent.</summary>
            public GameObject TopJudge { get; set; } = null!;

            /// <summary>Gets or sets the timing top info parent.</summary>
            public GameObject TopTiming { get; set; } = null!;

            /// <summary>Gets or sets the top perfect count.</summary>
            public TextMeshProUGUI TopPerfect { get; set; } = null!;

            /// <summary>Gets or sets the top great count.</summary>
            public TextMeshProUGUI TopGreat { get; set; } = null!;

            /// <summary>Gets or sets the top good count.</summary>
            public TextMeshProUGUI TopGood { get; set; } = null!;

            /// <summary>Gets or sets the top miss count.</summary>
            public TextMeshProUGUI TopMiss { get; set; } = null!;

            /// <summary>Gets or sets the top fast count.</summary>
            public TextMeshProUGUI TopFast { get; set; } = null!;

            /// <summary>Gets or sets the top late count.</summary>
            public TextMeshProUGUI TopLate { get; set; } = null!;

            /// <summary>Gets or sets the gauge parent.</summary>
            public GameObject Gauge { get; set; } = null!;

            /// <summary>Gets or sets the gauge scale images.</summary>
            public List<RawImage> GaugeScales { get; set; } = null!;

            /// <summary>Gets or sets the gauge cursor.</summary>
            public RectTransform Cursor { get; set; } = null!;
        }
    }
}
