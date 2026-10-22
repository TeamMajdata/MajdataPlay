using System.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.TotalResult;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.TotalResultScene
{
    /// <summary>Covers the dan course summary scene and its song displayers.</summary>
    public class TotalResultTests : SceneTestBase
    {
        /// <summary>The canvas holding the scene objects.</summary>
        private Transform _canvas = null!;

        /// <summary>Creates the services the scene uses.</summary>
        [SetUp]
        public void SetUp()
        {
            _canvas = CreateCanvas();
            CreateRuntimeDatabase();
            CreateGameManagerStub();
            CreateSceneSwitcherStub();
            CreateAudioManager("bgm_result.mp3", "bgm_dan.mp3", "challenge_clear.wav", "challenge_fail.wav");
        }

        /// <summary>Creates an inactive dan song displayer.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The displayer.</returns>
        private DanSongCoverDisplayer CreateDanSong(Transform parent)
        {
            var displayer = AddInactiveUi<DanSongCoverDisplayer>("DanSong", parent);
            foreach (var field in new[] { "_titleDisplayer", "_artistDisplayer", "_achievementDisplayer", "_levelDisplayer" })
            {
                PrivateMember.SetField(displayer, field, CreateText(field, displayer.transform));
            }
            foreach (var field in new[] { "_coverDisplayer", "_levelColorDisplayer", "_levelRingColorDisplayer" })
            {
                PrivateMember.SetField(displayer, field, CreateImage(field, displayer.transform));
            }
            PrivateMember.SetField(displayer, "_loadingIndicator", CreateRect("Loading", displayer.transform));
            return displayer;
        }

        /// <summary>Gets a text field of a component.</summary>
        /// <param name="component">The component.</param>
        /// <param name="field">The field name.</param>
        /// <returns>The text.</returns>
        private static string TextOf(Component component, string field)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(component, field).text;
        }

        /// <summary>Checks that a song with a result shows its title, level, achievement and cover.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DanSong_ShowsResult()
        {
            var displayer = CreateDanSong(_canvas);
            Activate(displayer);
            var song = new FakeSongDetail("majtest-dan-song", "Dan Song", "1", "2", "3", "4", "12+") { Cover = CreateSprite("DanCover") };

            displayer.SetSongDetail(song, ChartLevel.Master, new GameResult { Acc = new Accurate { DX = 100.1234, Classic = 99.5 } });
            yield return WaitUntil(() => PrivateMember.GetField<Image>(displayer, "_coverDisplayer").sprite == song.Cover);

            Assert.That(TextOf(displayer, "_titleDisplayer"), Is.EqualTo("Dan Song"));
            Assert.That(TextOf(displayer, "_artistDisplayer"), Is.EqualTo("Artist"));
            Assert.That(TextOf(displayer, "_levelDisplayer"), Is.EqualTo("12+"));
            Assert.That(TextOf(displayer, "_achievementDisplayer"), Is.EqualTo($"{100.1234:F4}%"));
            Assert.That(PrivateMember.GetField<Image>(displayer, "_levelColorDisplayer").color, Is.EqualTo(DifficultyColors()[4]));
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_loadingIndicator").activeSelf, Is.False);
        }

        /// <summary>Checks the classic format, a missing level and a missing result.</summary>
        [Test]
        public void DanSong_ClassicMissingLevelAndMissingResult()
        {
            var displayer = CreateDanSong(_canvas);
            Activate(displayer);
            var song = new FakeSongDetail("majtest-dan-missing", "Missing", "1") { Cover = CreateSprite("Cover") };
            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;

            displayer.SetSongDetail(song, ChartLevel.Expert, new GameResult { Acc = new Accurate { Classic = 98.765 } });
            Assert.That(TextOf(displayer, "_achievementDisplayer"), Is.EqualTo($"{98.765:F2}%"));
            Assert.That(TextOf(displayer, "_levelDisplayer"), Is.EqualTo("-"));

            displayer.SetSongDetail(song, ChartLevel.Expert, null);
            Assert.That(TextOf(displayer, "_achievementDisplayer"), Is.EqualTo("---.----%"));
        }

        /// <summary>Checks that the small result displayer shows the result of one chart.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SmallDisplayer_ShowsResult()
        {
            var displayer = Objects.Add<TotalResultSmallDisplayer>("Small", _canvas);
            displayer.title = CreateText("Title", _canvas);
            displayer.artist = CreateText("Artist", _canvas);
            displayer.designer = CreateText("Designer", _canvas);
            displayer.level = CreateText("Level", _canvas);
            displayer.accDX = CreateText("Acc", _canvas);
            displayer.coverImg = CreateImage("Cover", _canvas);
            var song = new FakeSongDetail("majtest-small", "Small", "1", "2", "3", "4", "13") { Cover = CreateSprite("Cover") };

            displayer.DisplayResult(song, new GameResult { Acc = new Accurate { DX = 97.5 } }, ChartLevel.Master);
            yield return WaitUntil(() => displayer.coverImg.sprite == song.Cover);

            Assert.That(displayer.title.text, Is.EqualTo("Small"));
            Assert.That(displayer.designer.text, Is.EqualTo("Designer4"));
            Assert.That(displayer.level.text, Is.EqualTo("Master 13"));
            Assert.That(displayer.accDX.text, Is.EqualTo($"{97.5:F4}%"));

            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;
            displayer.DisplayResult(song, new GameResult { Acc = new Accurate { Classic = 96.25 } }, ChartLevel.Master);
            Assert.That(displayer.accDX.text, Is.EqualTo($"{96.25:F2}%"));
        }

        /// <summary>Creates the summary manager for a dan course.</summary>
        /// <param name="startHp">The course start HP; negative disables life.</param>
        /// <param name="currentHp">The remaining HP.</param>
        /// <param name="displayers">The number of song displayers.</param>
        /// <returns>The inactive manager.</returns>
        private TotalResultManager CreateManager(int startHp, int currentHp, int displayers = 3)
        {
            var songs = new ISongDetail[]
            {
                new FakeSongDetail("majtest-total-1", "First", "1", "2", "3", "4", "5") { Cover = CreateSprite("A") },
                new FakeSongDetail("majtest-total-2", "Second", "1", "2", "3", "4", "5") { Cover = CreateSprite("B") },
            };
            var info = new GameInfo(GameMode.Dan, songs, new[] { ChartLevel.Expert, ChartLevel.Master })
            {
                DanInfo = new DanInfo { Name = "Course", SongLevels = new[] { 3, 4 }, StartHP = startHp },
                MaxHP = 100,
                CurrentHP = currentHp,
            };
            info.Results[0] = new GameResult { SongDetail = songs[0], Acc = new Accurate { DX = 99.5 } };
            Majdata<GameInfo>.Free();
            Majdata<GameInfo>.Instance = info;

            var manager = AddInactiveUi<TotalResultManager>("TotalResult", _canvas);
            var root = CreateRect("Songs", _canvas);
            for (var i = 0; i < displayers; i++)
            {
                CreateDanSong(root.transform).gameObject.SetActive(true);
            }
            PrivateMember.SetField(manager, "_songCoverDisplayerListRoot", root);
            PrivateMember.SetField(manager, "_hpDisplayer", CreateRect("HP", manager.transform));
            PrivateMember.SetField(manager, "_hpSlider", Objects.Add<Slider>("HPSlider", manager.transform));
            PrivateMember.SetField(manager, "_hpTextDisplayer", CreateText("HPText", manager.transform));
            PrivateMember.SetField(manager, "_totalAchievementDisplayer", CreateText("Total", manager.transform));
            PrivateMember.SetField(manager, "_titleDisplayer", CreateText("Title", manager.transform));
            return manager;
        }

        /// <summary>Checks that the summary shows the course, life, total achievement and each song.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Manager_ShowsCourseSummary()
        {
            var manager = CreateManager(startHp: 100, currentHp: 40);
            Activate(manager);
            yield return null;

            Assert.That(TextOf(manager, "_titleDisplayer"), Is.EqualTo("Course"));
            Assert.That(TextOf(manager, "_hpTextDisplayer"), Is.EqualTo("40/<size=75%>100"));
            Assert.That(PrivateMember.GetField<Slider>(manager, "_hpSlider").value, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(TextOf(manager, "_totalAchievementDisplayer"), Is.EqualTo($"{99.5:F4}%"));
            var root = PrivateMember.GetField<GameObject>(manager, "_songCoverDisplayerListRoot").transform;
            Assert.That(root.GetChild(0).gameObject.activeSelf, Is.True);
            Assert.That(root.GetChild(1).gameObject.activeSelf, Is.True);
            Assert.That(root.GetChild(2).gameObject.activeSelf, Is.False);
            Assert.That(TextOf(root.GetChild(1).GetComponent<DanSongCoverDisplayer>(), "_achievementDisplayer"), Is.EqualTo("---.----%"));
            Assert.That(Sfx("bgm_dan.mp3").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx("challenge_clear.wav").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx("bgm_result.mp3").StopCount, Is.EqualTo(1));
            Assert.That(MajdataPlay.IO.InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f).Within(1e-4f));
        }

        /// <summary>Checks that a failed course without life plays the failure voice and hides life.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Manager_FailedOrLifelessCourses()
        {
            var failed = CreateManager(startHp: 100, currentHp: 0, displayers: 1);
            Activate(failed);
            yield return null;
            Assert.That(Sfx("challenge_fail.wav").PlayOneShotCount, Is.EqualTo(1));
            Object.DestroyImmediate(failed.gameObject);

            MajEnv.Settings.Judge.Mode = JudgeModeOption.Classic;
            var lifeless = CreateManager(startHp: -1, currentHp: 0, displayers: 1);
            Activate(lifeless);
            yield return null;

            Assert.That(TextOf(lifeless, "_hpTextDisplayer"), Is.EqualTo("  --"));
            Assert.That(PrivateMember.GetField<GameObject>(lifeless, "_hpDisplayer").activeSelf, Is.False);
            Assert.That(TextOf(lifeless, "_totalAchievementDisplayer"), Is.EqualTo($"{0d:F2}%"));
            Assert.That(Sfx("challenge_clear.wav").PlayOneShotCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the start button returns to the list once the summary accepts input.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Manager_StartButtonReturnsToList()
        {
            var manager = CreateManager(startHp: 100, currentHp: 50);
            Activate(manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(manager, "_isInited"), 5f);
            Assert.That(PrivateMember.GetField<bool>(manager, "_isInited"), Is.True);
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(MajdataPlay.IO.ButtonZone.A4);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(manager, "_isExited"), Is.True);
            Assert.That(Sfx("bgm_dan.mp3").StopCount, Is.EqualTo(1));

            Object.DestroyImmediate(manager.gameObject);
            Assert.That(MajdataPlay.IO.InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f).Within(1e-4f));
        }
    }
}
