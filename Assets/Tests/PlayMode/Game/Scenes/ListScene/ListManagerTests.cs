using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Drives the song list scene manager with simulated cabinet input.</summary>
    public class ListManagerTests : ListSceneTestBase
    {
        /// <summary>The SFX the list scene plays.</summary>
        private static readonly string[] s_sfx =
        {
            "select_song.wav", "select_song_2.wav", "select_song_3.wav", "select_song_4.wav",
            "no_touch.wav", "no_touch_2.wav", "no_touch_3.wav", "no_touch_4.wav", "no_touch_5.wav", "no_touch_6.wav", "no_touch_7.wav",
            "easy.wav", "basic.wav", "advanced.wav", "expert.wav", "master.wav", "remaster.wav", "original.wav",
            "track_start.wav", "track_start_2.wav", "track_start_3.wav", "bgm_explosion.mp3", "challenge_mode.wav",
        };

        /// <summary>The manager under test.</summary>
        private ListManager _manager = null!;

        /// <summary>The cover list.</summary>
        private CoverListManager _covers = null!;

        /// <summary>The collection list.</summary>
        private CollectionListManager _collections = null!;

        /// <summary>The songs of the first collection.</summary>
        private FakeSongDetail[] _songs = null!;

        /// <summary>Creates the scene objects; the managers start on the first frame of each test.</summary>
        [SetUp]
        public void SetUp()
        {
            foreach (var name in s_sfx)
            {
                AddSfx(name);
            }
            CreateSceneSwitcherStub();
            var xxlb = AddInactive<XxlbAnimation>("Xxlb", Canvas);
            xxlb.gameObject.AddComponent<Animator>();
            Activate(xxlb);
            (_covers, _collections, _) = CreateListManagers(covers: 7, thumbnails: 13, collectionDisplayers: 3);
            _songs = CreateSongs("list", 12);
            PublishCollections(CreateCollection("Main", _songs), CreateCollection("Second", CreateSongs("list-second", 2)));
        }

        /// <summary>Creates the list manager, activates the scene and waits until it accepts input.</summary>
        /// <param name="onlineEnabled">Whether online features are enabled.</param>
        /// <returns>The coroutine.</returns>
        private IEnumerator StartScene(bool onlineEnabled = false)
        {
            MajEnv.Settings.Online.Enable = onlineEnabled;
            _manager = AddInactiveUi<ListManager>("List", Canvas);
            var user = AddInactiveUi<UserInfoDisplayer>("User", Canvas);
            PrivateMember.SetField(user, "_usernameDisplayer", CreateText("Name", user.transform));
            PrivateMember.SetField(user, "_userAvatarDisplayer", CreateImage("Avatar", user.transform));
            PrivateMember.SetField(user, "_errorIcon", CreateRect("Error", user.transform));
            Activate(user);
            PrivateMember.SetField(_manager, "_userProfileDisplayer", user);
            PrivateMember.SetField(_manager, "_favoriteAdder", CreateFavoriteAdder(Canvas));
            ActivateManagers(_covers, _collections);
            Activate(_manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(_manager, "_isInited"), 5f);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.True, "The list did not initialize.");
            yield return null;
        }

        /// <summary>Holds a button for one manager update, then releases it for another.</summary>
        /// <param name="zone">The button.</param>
        /// <returns>The coroutine.</returns>
        private static IEnumerator PressButton(ButtonZone zone)
        {
            InputSimulator.SetButton(zone, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetButton(zone, false);
            InputSimulator.Step();
            yield return null;
        }

        /// <summary>Touches a sensor for one manager update, then releases it for another.</summary>
        /// <param name="area">The sensor.</param>
        /// <returns>The coroutine.</returns>
        private static IEnumerator TouchSensor(SensorArea area)
        {
            InputSimulator.SetSensor(area, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(area, false);
            InputSimulator.Step();
            yield return null;
        }

        /// <summary>Gets whether the manager has left the scene.</summary>
        private bool IsExited => PrivateMember.GetField<bool>(_manager, "_isExited");

        /// <summary>Checks that starting registers the scene, plays the list music and selects the first song.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_InitializesScene()
        {
            yield return StartScene();

            Assert.That(Majdata<ListManager>.Instance, Is.SameAs(_manager));
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f).Within(1e-4f));
            Assert.That(Sfx("bgm_select.mp3").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Sfx("bgm_select.mp3").IsLoop, Is.True);
            Assert.That(_covers.SelectedSong, Is.SameAs(_songs[0]));
            Assert.That(_manager.CancellationToken.IsCancellationRequested, Is.False);
            var favorite = PrivateMember.GetField<FavoriteAdder>(_manager, "_favoriteAdder");
            Assert.That(favorite.PressToAddTime, Is.EqualTo(0.5f));
        }

        /// <summary>Checks that already playing list music is not restarted.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_KeepsPlayingMusic()
        {
            Sfx("bgm_select.mp3").Playing = true;

            yield return StartScene();

            Assert.That(Sfx("bgm_select.mp3").PlayOneShotCount, Is.EqualTo(0));
        }

        /// <summary>Checks that the outer B sensors change the collection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator BSensors_ChangeCollection()
        {
            yield return StartScene();

            yield return TouchSensor(SensorArea.B1);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Second"));

            yield return TouchSensor(SensorArea.B8);
            Assert.That(_collections.SelectedCollection.Name, Is.EqualTo("Main"));
        }

        /// <summary>Checks that the slide buttons move through the songs.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SlideButtons_MoveSelection()
        {
            yield return StartScene();

            yield return PressButton(ButtonZone.A3);
            Assert.That(_covers.SelectedSong, Is.SameAs(_songs[1]));

            yield return PressButton(ButtonZone.A6);
            Assert.That(_covers.SelectedSong, Is.SameAs(_songs[0]));
        }

        /// <summary>Checks that the outer buttons change the difficulty and announce it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DifficultyButtons_ChangeAndAnnounceDifficulty()
        {
            yield return StartScene();

            yield return PressButton(ButtonZone.A8);
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDiff, Is.EqualTo(ChartLevel.UTAGE));
            Assert.That(Sfx("original.wav").PlayOneShotCount, Is.EqualTo(1));

            yield return PressButton(ButtonZone.A1);
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDiff, Is.EqualTo(ChartLevel.Easy));
            Assert.That(Sfx("easy.wav").PlayOneShotCount, Is.EqualTo(1));
        }

        /// <summary>Checks that touching the lower sensors plays a no-touch voice.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LowerSensor_PlaysNoTouchVoice()
        {
            yield return StartScene();

            yield return TouchSensor(SensorArea.D5);

            Assert.That(s_sfx.Where(name => name.StartsWith("no_touch", StringComparison.Ordinal)).Sum(name => Sfx(name).PlayOneShotCount), Is.EqualTo(1));
        }

        /// <summary>Checks that B2 arms the favorite toggle while touched.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator B2_ArmsFavoriteWhileTouched()
        {
            yield return StartScene();
            var favorite = PrivateMember.GetField<FavoriteAdder>(_manager, "_favoriteAdder");

            InputSimulator.SetSensor(SensorArea.B2, true);
            InputSimulator.Step();
            yield return null;
            Assert.That(favorite.State, Is.True);

            InputSimulator.SetSensor(SensorArea.B2, false);
            InputSimulator.Step();
            yield return null;
            Assert.That(favorite.State, Is.False);
        }

        /// <summary>Checks that holding the center sensor for a second picks a random song.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingCenter_PicksRandomSong()
        {
            yield return StartScene();
            SetDeltaTime(0.5f);

            InputSimulator.SetSensor(SensorArea.C, true);
            for (var i = 0; i < 5; i++)
            {
                InputSimulator.Step();
                yield return null;
            }
            var pressTime = PrivateMember.GetField<float>(_manager, "_cAreaPressTime");
            InputSimulator.SetSensor(SensorArea.C, false);
            InputSimulator.Step();
            yield return null;

            Assert.That(pressTime, Is.LessThan(1f));
            Assert.That(PrivateMember.GetField<float>(_manager, "_cAreaPressTime"), Is.EqualTo(-1f));
            Assert.That(Array.IndexOf(_songs, _covers.SelectedSong), Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>Checks that a left swipe across the screen scrolls several songs forward.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator QuickSlide_SwipeScrollsForward()
        {
            yield return StartScene();

            yield return TouchSensor(SensorArea.C);
            InputSimulator.SetSensor(SensorArea.C, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(SensorArea.C, false);
            InputSimulator.SetSensor(SensorArea.B7, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(SensorArea.B7, false);
            InputSimulator.SetSensor(SensorArea.E7, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(SensorArea.E7, false);
            InputSimulator.Step();
            yield return WaitUntil(() => PrivateMember.GetField<int>(_manager, "_quickSlideDirection") == 0, 3f);
            yield return new WaitForSecondsRealtime(0.4f);

            Assert.That(Array.IndexOf(_songs, _covers.SelectedSong), Is.GreaterThan(0));
            Assert.That(PrivateMember.GetField<int>(_manager, "_quickSlideRemaining"), Is.EqualTo(0));
        }

        /// <summary>Checks that the sort button leaves for the sort and search scene.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SortButton_LeavesForSortFind()
        {
            yield return StartScene();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A2);
            yield return null;

            Assert.That(IsExited, Is.True);
            Assert.That(_manager.CancellationToken.IsCancellationRequested, Is.True);
        }

        /// <summary>Checks that the settings button leaves for the setting scene.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SettingButton_LeavesForSetting()
        {
            yield return StartScene();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A7);
            yield return null;

            Assert.That(IsExited, Is.True);
        }

        /// <summary>Checks that the P1 release leaves for the sort and search scene.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator P1Release_LeavesForSortFind()
        {
            yield return StartScene();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonReleaseCompleted(ButtonZone.P1, 0.1f);
            yield return null;

            Assert.That(IsExited, Is.True);
        }

        /// <summary>Checks that a short start press starts the selected song in normal mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartButton_StartsGame()
        {
            yield return StartScene();
            yield return PressButton(ButtonZone.A3);
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonReleaseCompleted(ButtonZone.A4, 0.2f);
            yield return null;

            Assert.That(IsExited, Is.True);
            var info = Majdata<GameInfo>.Instance!;
            Assert.That(info.Mode, Is.EqualTo(GameMode.Normal));
            Assert.That(info.Current, Is.SameAs(_songs[1]));
            Assert.That(Sfx("bgm_select.mp3").StopCount, Is.EqualTo(1));
        }

        /// <summary>Checks that holding the start button plays the explosion and then starts practice mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingStart_StartsPractice()
        {
            yield return StartScene();
            SetDeltaTime(0.5f);
            ExpectSceneSwitch();

            InputSimulator.SetButton(ButtonZone.A4, true);
            for (var i = 0; i < 8 && !IsExited; i++)
            {
                InputSimulator.Step(0.5f);
                yield return null;
            }

            Assert.That(IsExited, Is.True);
            Assert.That(Sfx("bgm_explosion.mp3").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(Majdata<GameInfo>.Instance!.Mode, Is.EqualTo(GameMode.Practice));
        }

        /// <summary>Checks that a dan collection without dan information cannot be started.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartButton_DanWithoutInfoDoesNothing()
        {
            PublishCollections(new SongCollection("Dan", _songs) { Id = Guid.NewGuid(), Type = ChartStorageType.Dan });
            yield return StartScene();

            InputSimulator.StepUntilButtonReleaseCompleted(ButtonZone.A4, 0.2f);
            yield return null;

            Assert.That(IsExited, Is.False);
            Assert.That(_manager.CancellationToken.IsCancellationRequested, Is.True);
        }

        /// <summary>Checks that a complete dan collection starts the course with its life settings.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartButton_DanStartsCourse()
        {
            var dan = new DanInfo { Name = "Course", SongLevels = new[] { 3, 4 }, StartHP = 80, RestoreHP = 5 };
            var songs = CreateSongs("list-dan", 2);
            PublishCollections(new SongCollection("Dan", songs) { Id = Guid.NewGuid(), Type = ChartStorageType.Dan, DanInfo = dan });
            yield return StartScene();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonReleaseCompleted(ButtonZone.A4, 0.2f);
            yield return null;

            var info = Majdata<GameInfo>.Instance!;
            Assert.That(IsExited, Is.True);
            Assert.That(info.Mode, Is.EqualTo(GameMode.Dan));
            Assert.That(info.MaxHP, Is.EqualTo(80));
            Assert.That(info.HPRecover, Is.EqualTo(5));
            Assert.That(info.Levels, Is.EqualTo(new[] { ChartLevel.Expert, ChartLevel.Master }));
            Assert.That(Sfx("challenge_mode.wav").PlayOneShotCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a dan collection with mismatched levels cannot be started.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator StartButton_DanWithMismatchedLevelsDoesNothing()
        {
            var dan = new DanInfo { Name = "Course", SongLevels = new[] { 3 } };
            PublishCollections(new SongCollection("Dan", CreateSongs("list-dan-bad", 2)) { Id = Guid.NewGuid(), Type = ChartStorageType.Dan, DanInfo = dan });
            yield return StartScene();

            InputSimulator.StepUntilButtonReleaseCompleted(ButtonZone.A4, 0.2f);
            yield return null;

            Assert.That(IsExited, Is.False);
        }

        /// <summary>Checks that holding P1 for three seconds refreshes the library.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingP1_RefreshesLibrary()
        {
            yield return StartScene();
            SetDeltaTime(0.5f);
            ExpectSceneSwitch();

            InputSimulator.SetButton(ButtonZone.P1, true);
            for (var i = 0; i < 10 && !IsExited; i++)
            {
                InputSimulator.Step(0.5f);
                yield return null;
            }
            yield return null;

            Assert.That(IsExited, Is.True);
            Assert.That(Sfx("bgm_select.mp3").StopCount, Is.EqualTo(1));
        }

        /// <summary>Checks that holding A5 logs out only when online features are enabled.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingLogout_RequiresOnline()
        {
            yield return StartScene(onlineEnabled: false);
            SetDeltaTime(0.5f);

            InputSimulator.SetButton(ButtonZone.A5, true);
            for (var i = 0; i < 4; i++)
            {
                InputSimulator.Step(0.5f);
                yield return null;
            }

            Assert.That(IsExited, Is.False);
        }

        /// <summary>Checks that holding A5 with online features logs out.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HoldingLogout_LogsOut()
        {
            yield return StartScene(onlineEnabled: true);
            SetDeltaTime(0.5f);
            ExpectSceneSwitch();

            InputSimulator.SetButton(ButtonZone.A5, true);
            for (var i = 0; i < 6 && !IsExited; i++)
            {
                InputSimulator.Step(0.5f);
                yield return null;
            }
            yield return null;

            Assert.That(IsExited, Is.True);
        }

        /// <summary>Checks that input resets the inactivity timer and inactivity logs out when online.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Inactivity_LogsOutWhenOnline()
        {
            yield return StartScene(onlineEnabled: true);
            PrivateMember.SetField(_manager, "_inactiveTimeSec", 100f);
            PrivateMember.Invoke(_manager, "OnAnyInput", null, default(InputEventArgs));
            Assert.That(PrivateMember.GetField<float>(_manager, "_inactiveTimeSec"), Is.LessThan(1f));
            ExpectSceneSwitch();

            PrivateMember.SetField(_manager, "_inactiveTimeSec", 301f);
            yield return null;
            yield return null;

            Assert.That(IsExited, Is.True);
        }

        /// <summary>Checks that destroying the manager releases the scene registration and restores the ring edge.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_ReleasesScene()
        {
            yield return StartScene();

            UnityEngine.Object.DestroyImmediate(_manager.gameObject);

            Assert.That(Majdata<ListManager>.IsNull, Is.True);
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f).Within(1e-4f));
        }

        /// <summary>Checks that waiting for background tasks completes immediately unless a task is running.</summary>
        [Test]
        public void WaitForBackgroundTaskSuspend_WaitsOnlyForRunningTasks()
        {
            ListManager.AllBackgroundTasks.Clear();
            Assert.That(ListManager.WaitForBackgroundTaskSuspendAsync().IsCompleted, Is.True);

            ListManager.AllBackgroundTasks.Add(Task.CompletedTask);
            Assert.That(ListManager.WaitForBackgroundTaskSuspendAsync().IsCompleted, Is.True);

            var running = new TaskCompletionSource<bool>();
            ListManager.AllBackgroundTasks.Add(running.Task);
            var wait = ListManager.WaitForBackgroundTaskSuspendAsync();
            Assert.That(wait.IsCompleted, Is.False);

            running.SetResult(true);
            Assert.That(wait.Wait(1000), Is.True);
        }

        /// <summary>Checks that Awake drops completed and missing background tasks and keeps running ones.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_PrunesCompletedBackgroundTasks()
        {
            var running = new TaskCompletionSource<bool>();
            ListManager.AllBackgroundTasks.Clear();
            ListManager.AllBackgroundTasks.Add(Task.CompletedTask);
            ListManager.AllBackgroundTasks.Add(null!);
            ListManager.AllBackgroundTasks.Add(running.Task);

            yield return StartScene();

            Assert.That(ListManager.AllBackgroundTasks, Does.Contain(running.Task));
            Assert.That(ListManager.AllBackgroundTasks, Has.No.Member(Task.CompletedTask));
            running.SetResult(true);
        }

        /// <summary>Checks that pruning a large background task list keeps exactly the running tasks.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_PrunesLargeBackgroundTaskList()
        {
            var running = new TaskCompletionSource<bool>();
            // A distinct completed task: the scene's own components register tasks while starting, and some of
            // them complete before the assertion, so only the tasks inserted here are checked.
            var completed = Task.FromResult(true);
            ListManager.AllBackgroundTasks.Clear();
            for (var i = 0; i < 2100; i++)
            {
                ListManager.AllBackgroundTasks.Add(running.Task);
                ListManager.AllBackgroundTasks.Add(completed);
                ListManager.AllBackgroundTasks.Add(null!);
            }

            yield return StartScene();

            Assert.That(ListManager.AllBackgroundTasks.Count(task => ReferenceEquals(task, running.Task)), Is.EqualTo(2100));
            Assert.That(ListManager.AllBackgroundTasks.Any(task => ReferenceEquals(task, completed)), Is.False);
            Assert.That(ListManager.AllBackgroundTasks.Any(task => task is null), Is.False);
            running.SetResult(true);
        }
    }
}
