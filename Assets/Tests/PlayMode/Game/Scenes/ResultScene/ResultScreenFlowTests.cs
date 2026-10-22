using System;
using System.Collections;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.List;
using MajdataPlay.Scenes.Result;
using MajdataPlay.Scenes.Result.Components;
using MajdataPlay.Scenes.Result.Components.Indicators;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ResultScene
{
    /// <summary>Runs the full result screen for a finished game with stub services and fake audio.</summary>
    public class ResultScreenFlowTests : SceneTestBase
    {
        /// <summary>The SFX the result screen plays.</summary>
        private static readonly string[] s_sfx =
        {
            "bgm_result.mp3", "clear.wav", "SSS+.wav", "SSS.wav", "SS+.wav", "SS.wav", "S+.wav", "S.wav",
            "ap_comment.wav", "ap_comment_2.wav", "fc_comment.wav", "fc_comment_2.wav",
            "clear_comment.wav", "clear_comment_2.wav", "clear_comment_3.wav", "clear_comment_4.wav",
            "fail_comment.wav", "fail_comment_2.wav", "fail_comment_3.wav", "fail_comment_4.wav", "fail_comment_5.wav",
            "dian_zan.wav",
        };

        /// <summary>The canvas holding the scene objects.</summary>
        private Transform _canvas = null!;

        /// <summary>The played song.</summary>
        private FakeSongDetail _song = null!;

        /// <summary>Creates the services the result screen uses.</summary>
        [SetUp]
        public void SetUp()
        {
            _canvas = CreateCanvas();
            CreateRuntimeDatabase();
            CreateGameManagerStub();
            CreateSceneSwitcherStub();
            CreateAudioManager(s_sfx);
            _song = new FakeSongDetail("majtest-result-flow", "Result Song", "1", "2", "3", "4", "13+") { Cover = CreateSprite("Cover") };
            MajEnv.RuntimeConfig!.List.SelectedDiff = ChartLevel.Master;
            AddCleanup(() =>
            {
                while (SongStorage.IsInMyFavorites(_song))
                {
                    SongStorage.RemoveFromMyFavorites(_song);
                }
            });
        }

        /// <summary>Installs a finished game.</summary>
        /// <param name="mode">The game mode.</param>
        /// <param name="dx">The DX achievement.</param>
        /// <param name="detail">The judge detail.</param>
        /// <param name="diffs">The note judge differences in milliseconds.</param>
        private void InstallGame(GameMode mode, double dx, JudgeDetail detail, float[] diffs)
        {
            var info = new GameInfo(mode, new ISongDetail[] { _song }, new[] { ChartLevel.Master });
            info.RecordResult(new GameResult
            {
                SongDetail = _song,
                Level = ChartLevel.Master,
                Acc = new Accurate { DX = dx, Classic = dx - 1 },
                JudgeRecord = detail,
                Fast = 3,
                Late = 4,
                DXScore = 900,
                TotalDXScore = 1000,
                NoteJudgeDiffs = diffs,
            });
            Majdata<GameInfo>.Free();
            Majdata<GameInfo>.Instance = info;
        }

        /// <summary>Creates the result screen with every displayer it fills.</summary>
        /// <returns>The inactive manager.</returns>
        private ResultScreenManager CreateScreen()
        {
            var manager = AddInactiveUi<ResultScreenManager>("Result", _canvas);
            foreach (var field in new[]
            {
                "title", "artist", "designer", "level", "accDX", "accHistory", "rank", "criticalCount", "perfectCount", "greatCount",
                "goodCount", "missCount", "criticalLabel", "perfectLabel", "greatLabel", "goodLabel", "missLabel", "fastCount",
                "lateCount", "avgJudgeTime", "omg", "clearText",
            })
            {
                typeof(ResultScreenManager).GetField(field)!.SetValue(manager, CreateText(field, manager.transform));
            }
            var sub = Objects.Add<ResultsSubDisplayManager>("Sub", manager.transform);
            foreach (var row in new[] { "Note", "Hold", "Slide", "Touch", "Break" })
            {
                foreach (var column in new[] { "Critical", "Perfect", "Great", "Good", "Miss" })
                {
                    typeof(ResultsSubDisplayManager).GetField($"{row}_{column}")!.SetValue(sub, CreateText($"{row}_{column}", sub.transform));
                }
            }
            manager.subMonitorManager = sub;
            manager.xxlb = CreateRect("Xxlb", manager.transform);
            manager.xxlb.AddComponent<Animator>();
            var user = AddInactiveUi<UserInfoDisplayer>("User", manager.transform);
            PrivateMember.SetField(user, "_usernameDisplayer", CreateText("Name", user.transform));
            PrivateMember.SetField(user, "_userAvatarDisplayer", CreateImage("Avatar", user.transform));
            Activate(user);
            manager.userInfoDisplayer = user;
            manager.coverImg = CreateImage("Cover", manager.transform);
            var graph = Objects.Add<RawImage>("Graph", manager.transform);
            graph.rectTransform.sizeDelta = new Vector2(144, 40);
            manager._noteJudgeDiffGraph = graph;
            var favorite = CreateImage("Favorite", manager.transform).gameObject.AddComponent<FavoriteAdder>();
            favorite.HeartAdd = CreateSprite("Add");
            favorite.HeartRemove = CreateSprite("Remove");
            manager.favoriteAdder = favorite;
            manager.critColor = Color.yellow;
            manager.perfectColor = Color.red;
            manager.greatColor = Color.magenta;
            manager.goodColor = Color.green;
            PrivateMember.SetField(manager, "_levelCircleDisplayer", CreateImage("Circle", manager.transform));
            PrivateMember.SetField(manager, "_levelBGDisplayer", CreateImage("LevelBG", manager.transform));
            var dx = AddInactiveUi<DXScoreDisplayer>("DX", manager.transform);
            PrivateMember.SetField(dx, "_dxScoreSlider", Objects.Add<Slider>("Slider", dx.transform));
            PrivateMember.SetField(dx, "_dxScoreDisplayer", CreateText("DXText", dx.transform));
            Activate(dx);
            PrivateMember.SetField(manager, "_dxScoreDisplayer", dx);
            var indicator = AddInactiveUi<ScoreUploadIndicator>("Indicator", manager.transform);
            PrivateMember.SetField(indicator, "_uploadingIconDisplayer", Objects.Add<SVGImage>("Up", indicator.transform));
            PrivateMember.SetField(indicator, "_successIconDisplayer", Objects.Add<SVGImage>("Ok", indicator.transform));
            PrivateMember.SetField(indicator, "_errorIconDisplayer", Objects.Add<SVGImage>("Err", indicator.transform));
            PrivateMember.SetField(indicator, "_textDisplayer", CreateText("IndicatorText", indicator.transform));
            Activate(indicator);
            var sender = AddInactiveUi<OnlineInteractionSender>("Sender", manager.transform);
            sender.infotext = CreateText("Info", sender.transform);
            sender.thumb = CreateImage("Thumb", sender.transform);
            PrivateMember.SetField(sender, "_scoreUploadIndicator", indicator);
            Activate(sender);
            PrivateMember.SetField(manager, "_onlineInteractionSender", sender);
            return manager;
        }

        /// <summary>Completes the screen's motions so nothing animates destroyed objects after the test.</summary>
        /// <param name="manager">The manager.</param>
        private static void CompleteAnimations(ResultScreenManager manager)
        {
            PrivateMember.GetField<MotionHandle>(manager, "_achievementRollAnim").TryComplete();
            PrivateMember.GetField<CompositeMotionHandle>(manager, "_judgeTextFadeAnims").Complete();
            PrivateMember.GetField<CompositeMotionHandle>(manager, "_clearMarkAnims").Complete();
            var dx = PrivateMember.GetField<DXScoreDisplayer>(manager, "_dxScoreDisplayer");
            PrivateMember.GetField<MotionHandle>(dx, "_slideBarAnim").TryComplete();
            PrivateMember.GetField<MotionHandle>(dx, "_scoreTextAnim").TryComplete();
        }

        /// <summary>Checks that a perfect game fills every field, saves the score and announces the rank.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ShowsPerfectResultAndSavesScore()
        {
            InstallGame(GameMode.Normal, 100.6, JudgeRecords.Detail(
                tap: JudgeRecords.Info((JudgeGrade.Perfect, 10)),
                breakInfo: JudgeRecords.Info((JudgeGrade.Perfect, 2))), new[] { 16.6f, -16.6f, 33.3f });
            var manager = CreateScreen();

            Activate(manager);
            yield return null;
            CompleteAnimations(manager);
            yield return WaitUntil(() => manager.rank.text.Length > 0, 3f);

            Assert.That(manager.title.text, Is.EqualTo("Result Song"));
            Assert.That(manager.level.text, Is.EqualTo("13+"));
            Assert.That(manager.designer.text, Is.EqualTo("Designer4"));
            Assert.That(manager.omg.text, Is.EqualTo("您赢了"));
            Assert.That(manager.accHistory.text, Is.EqualTo($"{100.6:+0.0000;-0.0000;0}%"));
            Assert.That(manager.criticalCount.text, Is.EqualTo("12"));
            Assert.That(manager.missCount.text, Is.EqualTo("0"));
            Assert.That(manager.fastCount.text, Is.EqualTo("3"));
            Assert.That(manager.lateCount.text, Is.EqualTo("4"));
            Assert.That(manager.avgJudgeTime.text, Is.EqualTo($"{(16.6f - 16.6f + 33.3f) / 3f / MajEnv.FRAME_LENGTH_MSEC:F1}f"));
            Assert.That(manager.clearText.text, Is.EqualTo("AP+"));
            Assert.That(manager.accDX.text, Is.EqualTo((100.6d).ToString("F4") + "%"));
            Assert.That(manager.rank.text, Is.EqualTo("SSS+"));
            Assert.That(PrivateMember.GetField<Image>(manager, "_levelCircleDisplayer").color, Is.EqualTo(DifficultyColors()[4]));
            Assert.That(ScoreManager.GetScore(_song, ChartLevel.Master).PlayCount, Is.EqualTo(1));
            Assert.That(Sfx("bgm_result.mp3").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx("SSS+.wav").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(manager._noteJudgeDiffGraph.texture, Is.Not.Null);
        }

        /// <summary>Checks the clear marks, ranks and failure message of non-perfect results.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ShowsClearMarkAndRankPerResult()
        {
            var cases = new (double Dx, JudgeInfo Tap, JudgeInfo Break, string Clear, string Rank, string Omg)[]
            {
                (100.2, JudgeRecords.Info((JudgeGrade.Perfect, 3)), JudgeRecords.Info((JudgeGrade.FastPerfect2nd, 1)), "AP", "SSS", "您赢了"),
                (99.7, JudgeRecords.Info((JudgeGrade.FastGreat, 1)), JudgeRecords.Info(), "FC+", "SS+", "您赢了"),
                (99.2, JudgeRecords.Info((JudgeGrade.LateGood, 1)), JudgeRecords.Info(), "FC", "SS", "您赢了"),
                (98.1, JudgeRecords.Info((JudgeGrade.Miss, 1), (JudgeGrade.Perfect, 1)), JudgeRecords.Info(), string.Empty, "S+", "您赢了"),
                (97.1, JudgeRecords.Info((JudgeGrade.Miss, 1), (JudgeGrade.Perfect, 1)), JudgeRecords.Info(), string.Empty, "S", "您赢了"),
                (80, JudgeRecords.Info((JudgeGrade.Miss, 1)), JudgeRecords.Info(), string.Empty, string.Empty, "您输了"),
            };
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            foreach (var (dx, tap, breakInfo, clear, rank, omg) in cases)
            {
                InstallGame(GameMode.Normal, dx, JudgeRecords.Detail(tap: tap, breakInfo: breakInfo), Array.Empty<float>());
                var manager = CreateScreen();
                Activate(manager);
                yield return null;
                yield return null;
                yield return null;
                CompleteAnimations(manager);

                Assert.That(manager.clearText.text, Is.EqualTo(clear), $"DX {dx}");
                Assert.That(manager.rank.text, Is.EqualTo(rank), $"DX {dx}");
                Assert.That(manager.omg.text, Is.EqualTo(omg), $"DX {dx}");
                Assert.That(manager.avgJudgeTime.text, Is.EqualTo("0.000s"), $"DX {dx}");
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }

        /// <summary>Checks the frame unit with no judge differences and the classic history difference.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ClassicModeAndEmptyDifferences()
        {
            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;
            InstallGame(GameMode.Normal, 99, JudgeRecords.Detail(tap: JudgeRecords.Info((JudgeGrade.Perfect, 1))), Array.Empty<float>());
            var manager = CreateScreen();

            Activate(manager);
            yield return null;
            CompleteAnimations(manager);

            Assert.That(manager.avgJudgeTime.text, Is.EqualTo("0.0f"));
            Assert.That(manager.accHistory.text, Is.EqualTo($"{98d:+0.0000;-0.0000;0}%"));
            Assert.That(manager.accDX.text, Is.EqualTo((98d).ToString("F2") + "%"));
        }

        /// <summary>Checks that once the save finishes, the favorite button works and the start button returns to the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Update_FavoriteAndLeaveForList()
        {
            InstallGame(GameMode.Normal, 99, JudgeRecords.Detail(tap: JudgeRecords.Info((JudgeGrade.Perfect, 1))), Array.Empty<float>());
            var manager = CreateScreen();
            Activate(manager);
            yield return null;
            CompleteAnimations(manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(manager, "_isAllTaskFinished"), 8f);
            Assert.That(PrivateMember.GetField<bool>(manager, "_isAllTaskFinished"), Is.True);

            InputSimulator.SetButton(ButtonZone.A5, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetButton(ButtonZone.A5, false);
            InputSimulator.Step();
            Assert.That(SongStorage.IsInMyFavorites(_song), Is.True);

            ExpectSceneSwitch();
            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(manager, "_isExited"), Is.True);
            Assert.That(Sfx("bgm_result.mp3").StopCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the last song of a dan course leaves for the course summary.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Update_DanCourseEndLeavesForSummary()
        {
            InstallGame(GameMode.Dan, 99, JudgeRecords.Detail(tap: JudgeRecords.Info((JudgeGrade.Perfect, 1))), Array.Empty<float>());
            var manager = CreateScreen();
            Activate(manager);
            yield return null;
            CompleteAnimations(manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(manager, "_isAllTaskFinished"), 8f);

            ExpectSceneSwitch();
            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(manager, "_isExited"), Is.True);
            Assert.That(Sfx("bgm_result.mp3").StopCount, Is.EqualTo(0));
        }
    }
}
