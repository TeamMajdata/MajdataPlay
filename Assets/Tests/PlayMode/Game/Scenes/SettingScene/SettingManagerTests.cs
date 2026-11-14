using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>
    /// Runs the setting scene manager with stub audio, skin and scene services over the real <see cref="UserSetting"/> and
    /// <see cref="SystemSetting"/> categories, and drives it through simulated cabinet input.
    /// </summary>
    public class SettingManagerTests : SettingSceneTestBase
    {
        /// <summary>The manager under test.</summary>
        private SettingManager _manager = null!;

        /// <summary>Installs the services the scene needs and creates the inactive manager.</summary>
        [SetUp]
        public void SetUp()
        {
            CreateAudioManager("settings.wav");
            CreateSkinManagerStub();
            CreateSceneSwitcherStub();
            _manager = CreateManager();
            var mode = MajEnv.Mode;
            AddCleanup(() => MajEnv.Mode = mode);
        }

        /// <summary>Gets the menus created by the manager.</summary>
        private Menu[] Menus => PrivateMember.GetField<Menu[]>(_manager, "_menus");

        /// <summary>Gets the menu of the selected category.</summary>
        private Menu Current => Menus[_manager.Index];

        /// <summary>Starts the manager and waits until it accepts input.</summary>
        /// <returns>The coroutine.</returns>
        private IEnumerator StartManager()
        {
            Activate(_manager);
            yield return WaitUntil(() => PrivateMember.GetField<bool>(_manager, "_isInited"), 30);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.True, "The manager did not finish initializing.");

            // One idle frame releases the input repeaters that initialization suppresses.
            yield return null;
        }

        /// <summary>Gets the category titles created by the manager, in menu order.</summary>
        private MenuTitleDisplayer[] Titles => PrivateMember.GetField<MenuTitleDisplayer[]>(_manager, "_menuTitleDisplayers");

        /// <summary>Gets the number of menus built from the user and system setting categories.</summary>
        private static int CategoryMenuCount => SettingReflectionCache.GetVisibleProperties(typeof(UserSetting)).Length +
            SettingReflectionCache.GetVisibleProperties(typeof(SystemSetting)).Length;

        /// <summary>Gets the position of the title cursor, which counts listed menus only.</summary>
        private float CursorPosition => PrivateMember.GetField<float>(_manager, "_listCursorPos");

        /// <summary>Finds a menu by name.</summary>
        /// <param name="name">The menu name.</param>
        /// <returns>The index of the menu.</returns>
        private int MenuIndex(string name)
        {
            return Array.FindIndex(Menus, menu => menu.Name == name);
        }

        /// <summary>Makes a menu listed only while sensor display is enabled, as <see cref="ShowInSettingUIWhenAttribute"/> on its category would.</summary>
        /// <param name="name">The menu name.</param>
        /// <returns>The index of the menu.</returns>
        private int ListOnlyWithSensorDisplay(string name)
        {
            var index = MenuIndex(name);
            var attributes = new Attribute[] { new ShowInSettingUIWhenAttribute("User.Display.DisplaySensor", true) };
            Menus[index].Visibility = SettingVisibility.Resolve(typeof(SettingRoot), name, attributes);
            return index;
        }

        /// <summary>Publishes one collection holding a chart and saves both as the list selection.</summary>
        /// <param name="selectedSongId">The saved song identity, or null for the identity of the published chart.</param>
        /// <returns>The published chart.</returns>
        private FakeSongDetail SelectChart(string? selectedSongId = null)
        {
            var song = new FakeSongDetail("majtest-setting-chart");
            var collection = new SongCollection("Selected", new ISongDetail[] { song })
            {
                Id = Guid.NewGuid()
            };
            var collections = SongStorage.Collections;
            PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), new[] { collection });
            AddCleanup(() => PrivateMember.SetStaticProperty(typeof(SongStorage), nameof(SongStorage.Collections), collections));
            MajEnv.RuntimeConfig!.List.SelectedDirGuid = collection.Id;
            MajEnv.RuntimeConfig.List.SelectedSongId = selectedSongId ?? song.Id;
            return song;
        }

        /// <summary>Holds a button for one frame of manager updates, then releases it for another frame.</summary>
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

        /// <summary>Touches a sensor for one update frame, then releases it for another frame.</summary>
        /// <param name="area">The sensor area.</param>
        /// <returns>The press and release frames.</returns>
        private static IEnumerator PressSensor(SensorArea area)
        {
            InputSimulator.SetSensor(area, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(area, false);
            InputSimulator.Step();
            yield return null;
        }

        /// <summary>Binds only the navigation-arrow group, leaving the existing scene fixture and title templates intact.</summary>
        /// <returns>The navigation-only CanvasGroup controlled by the manager.</returns>
        private CanvasGroup BindNavigationArrows()
        {
            var arrows = CreateRect("CategoryNavigation", TitleListRoot.transform).AddComponent<CanvasGroup>();
            PrivateMember.SetField(_manager, "_navigationArrows", arrows);
            return arrows;
        }

        /// <summary>Checks disabling cancels the delayed initialization and re-enabling finishes the existing menus.</summary>
        /// <returns>The initialization restart regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Initialization_DisableCancelsAndEnableRestarts()
        {
            Activate(_manager);
            yield return null;
            var pending = PrivateMember.GetField<System.Threading.CancellationTokenSource>(_manager, "_initializationCancellationSource").Token;
            _manager.enabled = false;
            yield return null;
            yield return null;
            yield return null;
            Assert.That(pending.IsCancellationRequested, Is.True);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.False);
            _manager.enabled = true;
            yield return StartManager();
            Assert.That(Menus.Length, Is.EqualTo(CategoryMenuCount));
        }

        /// <summary>Checks destruction cancels the pending delay without restoring initialization afterward.</summary>
        /// <returns>The destroyed-owner regression coroutine.</returns>
        [UnityTest]
        public IEnumerator Initialization_DestroyDoesNotCompleteThePendingDelay()
        {
            Activate(_manager);
            yield return null;
            var pending = PrivateMember.GetField<System.Threading.CancellationTokenSource>(_manager, "_initializationCancellationSource").Token;
            UnityEngine.Object.Destroy(_manager.gameObject);
            yield return null;
            yield return null;
            yield return null;
            Assert.That(_manager == null, Is.True);
            Assert.That(pending.IsCancellationRequested, Is.True);
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isInited"), Is.False);
        }

        /// <summary>Checks that Start builds one menu and title per category, selects Game and plays the scene sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_BuildsMenusAndSelectsGame()
        {
            yield return StartManager();

            var expected = CategoryMenuCount;
            Assert.That(Menus.Length, Is.EqualTo(expected));
            Assert.That(Current.Name, Is.EqualTo(nameof(UserSetting.Game)));
            Assert.That(Current.gameObject.activeSelf, Is.True);
            Assert.That(Menus.Count(menu => menu.gameObject.activeSelf), Is.EqualTo(1));
            Assert.That(Menus.Any(menu => menu.Name == "Volume"), Is.True);
            Assert.That(TitleListRoot.transform.childCount, Is.EqualTo(expected));
            Assert.That(TitleListRoot.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(Sfx("settings.wav").PlayOneShotCount, Is.EqualTo(1));
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(4.8f).Within(1e-4f));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.Not.Empty);
            Assert.That(Description.text, Is.Not.Empty);
        }

        /// <summary>Checks that the remembered category and option are restored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_RestoresRememberedMenuAndOption()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Judge);
            MajEnv.RuntimeConfig.Setting.SelectedOption = "JudgeOffset";

            yield return StartManager();

            Assert.That(Current.Name, Is.EqualTo(nameof(UserSetting.Judge)));
            Assert.That(MajEnv.RuntimeConfig.Setting.SelectedOption, Is.EqualTo("JudgeOffset"));
        }

        /// <summary>Checks that an unknown remembered category falls back to Game and forgets the option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_UnknownMenuFallsBackToGame()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = "Missing";
            MajEnv.RuntimeConfig.Setting.SelectedOption = "Missing";

            yield return StartManager();

            Assert.That(Current.Name, Is.EqualTo(nameof(UserSetting.Game)));
            Assert.That(Current.SelectedIndex, Is.EqualTo(0));
        }

        /// <summary>Checks that a selected chart adds its chart settings as the first category.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_WithSelectedChart_AddsChartSettingMenu()
        {
            var song = SelectChart();

            yield return StartManager();

            Assert.That(Menus[0].Name, Is.EqualTo(nameof(ChartSetting)));
            Assert.That(Menus[0].Instance, Is.SameAs(ChartSettingStorage.GetSetting(song.Hash)));
            Assert.That(Menus.Length, Is.EqualTo(CategoryMenuCount + 1));
        }

        /// <summary>Checks that a saved song identity matching no published chart adds no chart setting menu.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_WithUnavailableSelectedChart_SkipsChartSettingMenu()
        {
            var song = SelectChart("missing-song");

            yield return StartManager();

            Assert.That(song.Id, Is.Not.EqualTo("missing-song"));
            Assert.That(Menus.Any(menu => menu.Name == nameof(ChartSetting)), Is.False);
        }

        /// <summary>Checks that the chart setting page can be suppressed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_IgnoringChartPage_SkipsChartSettingMenu()
        {
            SelectChart();
            MajEnv.RuntimeConfig!.Setting.IgnoreChartSettingPage = true;

            yield return StartManager();

            Assert.That(Menus.Any(menu => menu.Name == nameof(ChartSetting)), Is.False);
        }

        /// <summary>Checks that the next category touch selects the following menu and animates the titles.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NextCategoryTouch_SelectsFollowingMenu()
        {
            yield return StartManager();
            var start = _manager.Index;

            yield return PressSensor(SensorArea.B1);
            PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").TryComplete();

            Assert.That(_manager.Index, Is.EqualTo(start + 1));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedMenu, Is.EqualTo(Current.Name));
            Assert.That(Menus[start].gameObject.activeSelf, Is.False);
            Assert.That(Current.SelectedIndex, Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<float>(_manager, "_listCursorPos"), Is.EqualTo(_manager.Index));
        }

        /// <summary>Checks that the previous category touch wraps to the last menu while its title cursor skips unlisted menus.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviousCategoryTouch_WrapsToLastMenu()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Game);
            yield return StartManager();
            Assert.That(_manager.Index, Is.EqualTo(0));

            yield return PressSensor(SensorArea.B8);

            Assert.That(_manager.Index, Is.EqualTo(Menus.Length - 1));
            var listedMenuCount = Menus.Count(menu => menu.Visibility.IsVisible(_manager.Setting));
            Assert.That(CursorPosition, Is.EqualTo(listedMenuCount - 1));
            Assert.That(PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").IsActive(), Is.False);
            Assert.That(Current.SelectedIndex, Is.EqualTo(PrivateMember.GetField<int>(Current, "_displayedOptionCount") - 1));
        }

        /// <summary>Checks B1/B8 navigate on press edges, without repeating while the touch remains held.</summary>
        /// <returns>The initialized manager and deterministic touch-frame sequence.</returns>
        [UnityTest]
        public IEnumerator CategoryTouches_UsePressEdgesWithoutRepeating()
        {
            yield return StartManager();
            var start = _manager.Index;
            InputSimulator.SetSensor(SensorArea.B1, true);
            InputSimulator.Step();
            yield return null;
            Assert.That(_manager.Index, Is.EqualTo(start + 1));

            SetDeltaTime(0.8f);
            for (var frame = 0; frame < 3; frame++)
            {
                InputSimulator.Step(0.8f);
                yield return null;
                Assert.That(_manager.Index, Is.EqualTo(start + 1), "A held category touch must not become cabinet repeat input.");
            }
            InputSimulator.SetSensor(SensorArea.B1, false);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetSensor(SensorArea.B8, true);
            InputSimulator.Step();
            yield return null;
            Assert.That(_manager.Index, Is.EqualTo(start));
            InputSimulator.SetSensor(SensorArea.B8, false);
            InputSimulator.Step();
            yield return null;
        }

        /// <summary>Checks former physical category keys do nothing and cannot block the top category touch controls.</summary>
        /// <returns>The press, hold and simultaneous touch sequences for both physical keys.</returns>
        [UnityTest]
        public IEnumerator PhysicalCategoryButtons_AreIgnoredAndDoNotBlockCategoryTouches()
        {
            yield return StartManager();
            var start = _manager.Index;
            SetDeltaTime(0.8f);
            foreach (var zone in new[] { ButtonZone.A2, ButtonZone.A7 })
            {
                InputSimulator.SetButton(zone, true);
                for (var frame = 0; frame < 3; frame++)
                {
                    InputSimulator.Step(0.8f);
                    yield return null;
                    Assert.That(_manager.Index, Is.EqualTo(start), "Physical category keys must remain inactive on press and hold.");
                }
                yield return PressSensor(SensorArea.B1);
                Assert.That(_manager.Index, Is.EqualTo(start + 1), "A held physical key cannot block the next-category touch.");
                yield return PressSensor(SensorArea.B8);
                Assert.That(_manager.Index, Is.EqualTo(start), "A held physical key cannot block the previous-category touch.");
                InputSimulator.SetButton(zone, false);
                InputSimulator.Step();
                yield return null;
            }
        }

        /// <summary>Checks arrow opacity follows the same 250ms transition while the category cursor retains OutQuad easing.</summary>
        /// <returns>The initialized manager and exact motion-time samples.</returns>
        [UnityTest]
        public IEnumerator CategoryNavigationArrows_FadeAcrossTheSharedTransitionAndCompleteVisible()
        {
            var arrows = BindNavigationArrows();
            yield return StartManager();
            var start = CursorPosition;
            _manager.NextMenu();
            var motion = PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim");
            Assert.That(motion.Duration, Is.EqualTo(0.25f));
            foreach (var sample in new (double Time, float Alpha, float Cursor)[]
            {
                (0, 1f, 0f),
                (0.025, 0.5f, 0.19f),
                (0.05, 0f, 0.36f),
                (0.125, 0f, 0.75f),
                (0.2, 0f, 0.96f),
                (0.225, 0.5f, 0.99f),
                (0.25, 1f, 1f)
            })
            {
                motion.Time = sample.Time;
                Assert.That(arrows.alpha, Is.EqualTo(sample.Alpha).Within(0.0001f));
                Assert.That(CursorPosition, Is.EqualTo(start + sample.Cursor).Within(0.0001f));
            }
            motion.TryComplete();
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(CursorPosition, Is.EqualTo(start + 1f));
        }

        /// <summary>Checks interruption keeps partial opacity and disable/enable or boundary wrapping restores visible arrows.</summary>
        /// <returns>The category reversal, disable and wrapping sequence.</returns>
        [UnityTest]
        public IEnumerator CategoryNavigationArrows_ReverseWithoutFlashingAndRestoreAfterDisableOrWrap()
        {
            var arrows = BindNavigationArrows();
            yield return StartManager();
            _manager.NextMenu();
            var first = PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim");
            first.Time = 0.025;
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            var retainedCursor = CursorPosition;
            _manager.PreviousMenu();
            var reversed = PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim");
            reversed.Time = 0;
            Assert.That(first.IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(CursorPosition, Is.EqualTo(retainedCursor).Within(0.0001f));
            reversed.Time = 0.025;
            Assert.That(arrows.alpha, Is.EqualTo(0.25f).Within(0.0001f));

            _manager.enabled = false;
            Assert.That(reversed.IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(CursorPosition, Is.EqualTo(0f));
            _manager.enabled = true;
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(CursorPosition, Is.EqualTo(0f));
            _manager.PreviousMenu();
            Assert.That(_manager.Index, Is.EqualTo(Menus.Length - 1));
            Assert.That(PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").IsActive(), Is.False);
            Assert.That(arrows.alpha, Is.EqualTo(1f), "Instant wrapping must not leave the navigation group faded out.");
        }

        /// <summary>Checks that an unlisted menu is skipped with its title hidden, and listed again once its condition holds.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_SkippedUntilConditionHolds()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Game);
            yield return StartManager();
            var game = MenuIndex(nameof(UserSetting.Game));
            var judge = ListOnlyWithSensorDisplay(nameof(UserSetting.Judge));
            var display = MenuIndex(nameof(UserSetting.Display));
            yield return null;

            Assert.That(Titles[judge].gameObject.activeSelf, Is.False);

            yield return PressSensor(SensorArea.B1);
            PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").TryComplete();

            Assert.That((game, judge, display), Is.EqualTo((0, 1, 2)));
            Assert.That(_manager.Index, Is.EqualTo(display));
            Assert.That(CursorPosition, Is.EqualTo(1f));

            MajEnv.UserSettings.Display.DisplaySensor = true;
            yield return null;

            Assert.That(Titles[judge].gameObject.activeSelf, Is.True);
            Assert.That(CursorPosition, Is.EqualTo(2f));

            yield return PressSensor(SensorArea.B8);
            Assert.That(_manager.Index, Is.EqualTo(judge));
        }

        /// <summary>Checks that the selected menu stays listed while its condition fails and leaves the list once another menu is selected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_SelectedMenuStaysListedUntilLeft()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Judge);
            yield return StartManager();
            var game = MenuIndex(nameof(UserSetting.Game));
            var judge = ListOnlyWithSensorDisplay(nameof(UserSetting.Judge));
            var display = MenuIndex(nameof(UserSetting.Display));
            yield return null;

            Assert.That((game, judge, display), Is.EqualTo((0, 1, 2)));
            Assert.That(_manager.Index, Is.EqualTo(judge));
            Assert.That(Titles[judge].gameObject.activeSelf, Is.True);

            yield return PressSensor(SensorArea.B1);

            // Judge left the list, so Display moved into its position without a title animation.
            Assert.That(_manager.Index, Is.EqualTo(display));
            Assert.That(Titles[judge].gameObject.activeSelf, Is.False);
            Assert.That(CursorPosition, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").IsActive(), Is.False);

            yield return PressSensor(SensorArea.B8);
            Assert.That(_manager.Index, Is.EqualTo(game));
        }

        /// <summary>Checks that a remembered menu that is not listed falls back to Game and forgets the option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_UnlistedRememberedMenuFallsBackToGame()
        {
            yield return StartManager();
            ListOnlyWithSensorDisplay(nameof(UserSetting.Judge));
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Judge);
            MajEnv.RuntimeConfig.Setting.SelectedOption = "JudgeOffset";

            PrivateMember.Invoke(_manager, "RestoreSelectedMenu");

            Assert.That(Current.Name, Is.EqualTo(nameof(UserSetting.Game)));
            Assert.That(MajEnv.RuntimeConfig.Setting.SelectedOption, Is.Empty);
            Assert.That(CursorPosition, Is.EqualTo(0f));
        }

        /// <summary>Checks that the option buttons move the selection within the current menu.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OptionButtons_MoveSelection()
        {
            yield return StartManager();

            yield return PressButton(ButtonZone.A3);
            Assert.That(Current.SelectedIndex, Is.EqualTo(1));

            yield return PressButton(ButtonZone.A6);
            Assert.That(Current.SelectedIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// Checks that a category switch returns the option views of an inactive menu, as left behind by a switch that
        /// threw before activating its menu, so that the shared pool still covers the slide of the next menu.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CategorySwitch_ReleasesViewsOfInactiveMenu()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Game);
            yield return StartManager();
            var stuck = Menus[_manager.Index + 1];

            // The state a failed UpdateMenu leaves: the old menu is deactivated and the selected menu rented its
            // views while inactive, but was never activated.
            Current.gameObject.SetActive(false);
            stuck.ToHead();
            PrivateMember.SetProperty(_manager, nameof(SettingManager.Index), _manager.Index + 1);
            Assert.That(stuck.gameObject.activeSelf, Is.False);
            Assert.That(VisibleOption(stuck, 0), Is.Not.Null);

            yield return PressSensor(SensorArea.B1);

            Assert.That(VisibleOption(stuck, 0), Is.Null);
            Assert.That(PooledOptionCount + HeldOptionCount(Current), Is.EqualTo(Menu.OPTION_POOL_CAPACITY));

            // Sliding to the next option needs a third view while the menu is at its head.
            yield return PressButton(ButtonZone.A3);
            PrivateMember.GetField<MotionHandle>(Current, "_optionAnim").TryComplete();

            Assert.That(Current.SelectedIndex, Is.EqualTo(1));
            Assert.That(VisibleOption(Current, 1), Is.Not.Null);
            Assert.That(PooledOptionCount + HeldOptionCount(Current), Is.EqualTo(Menu.OPTION_POOL_CAPACITY));
        }

        /// <summary>Gets the number of option views waiting in the shared pool.</summary>
        private int PooledOptionCount => PrivateMember.GetField<Stack<Option>>(_manager, "_optionPool").Count;

        /// <summary>Gets the number of option views a menu holds.</summary>
        /// <param name="menu">The menu.</param>
        /// <returns>The number of views rented by the menu.</returns>
        private static int HeldOptionCount(Menu menu)
        {
            return PrivateMember.GetField<Option?[]>(menu, "_visibleOptions").Count(option => option != null);
        }

        /// <summary>Checks that moving before the first option of a menu switches to the previous category.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviousOptionAtHead_SwitchesCategory()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(UserSetting.Judge);
            yield return StartManager();
            var start = _manager.Index;

            yield return PressButton(ButtonZone.A6);

            Assert.That(_manager.Index, Is.EqualTo(start - 1));
        }

        /// <summary>Checks that changing the offset unit converts every offset setting.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OffsetUnitChange_ConvertsOffsets()
        {
            yield return StartManager();
            var settings = MajEnv.UserSettings;
            settings.Judge.AudioOffset = 6f;
            settings.Judge.JudgeOffset = -3f;
            MajEnv.SystemSettings.Debug.DisplayOffset = 60f;

            settings.Judge.OffsetUnit = OffsetUnitOption.Second;
            yield return null;

            Assert.That(settings.Judge.AudioOffset, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(settings.Judge.JudgeOffset, Is.EqualTo(-0.05f).Within(1e-6f));
            Assert.That(MajEnv.SystemSettings.Debug.DisplayOffset, Is.EqualTo(1f).Within(1e-6f));

            settings.Judge.OffsetUnit = OffsetUnitOption.Frame;
            yield return null;

            Assert.That(settings.Judge.AudioOffset, Is.EqualTo(6f).Within(1e-6f));
        }

        /// <summary>Checks the decimal offset conversion between frames and seconds.</summary>
        [Test]
        public void ConvertOffsetUnit_RoundsPerUnit()
        {
            Assert.That(PrivateMember.InvokeStatic<float>(typeof(SettingManager), "ConvertOffsetUnit", 1f, true), Is.EqualTo(0.017f).Within(1e-6f));
            Assert.That(PrivateMember.InvokeStatic<float>(typeof(SettingManager), "ConvertOffsetUnit", 0.017f, false), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PrivateMember.InvokeStatic<float>(typeof(SettingManager), "ConvertOffsetUnit", 0f, false), Is.EqualTo(0f));
        }

        /// <summary>
        /// Checks that category metadata is cached, lists the user categories before the system categories and resolves
        /// each menu to its category instance; hidden categories such as Audio get no menu.
        /// </summary>
        [Test]
        public void GetMenuMetadata_CachesAndListsUserThenSystemCategories()
        {
            var first = (Array)PrivateMember.InvokeStatic(typeof(SettingManager), "GetMenuMetadata", typeof(SettingRoot))!;
            var second = (Array)PrivateMember.InvokeStatic(typeof(SettingManager), "GetMenuMetadata", typeof(SettingRoot))!;
            var setting = new SettingRoot(new UserSetting(), new SystemSetting());

            Assert.That(second, Is.SameAs(first));
            var names = new List<string>();
            var instances = new Dictionary<string, object?>();
            foreach (var metadata in first)
            {
                var name = PrivateMember.GetProperty<string>(metadata!, "Name");
                names.Add(name);
                instances[name] = PrivateMember.Invoke(metadata!, "GetInstance", setting);
            }
            Assert.That(names, Is.EqualTo(new[]
            {
                nameof(UserSetting.Game), nameof(UserSetting.Judge), nameof(UserSetting.Display), nameof(UserSetting.Volume),
                nameof(UserSetting.Touch), nameof(SystemSetting.Common), nameof(SystemSetting.Mod),
                nameof(SystemSetting.Rendering), nameof(SystemSetting.Encoding), nameof(SystemSetting.Decoding), nameof(SystemSetting.Debug)
            }));
            Assert.That(instances[nameof(UserSetting.Game)], Is.SameAs(setting.User.Game));
            Assert.That(instances[nameof(UserSetting.Volume)], Is.SameAs(setting.User.Volume));
            Assert.That(instances[nameof(SystemSetting.Rendering)], Is.SameAs(setting.System.Rendering));
            Assert.That(instances[nameof(SystemSetting.Debug)], Is.SameAs(setting.System.Debug));
        }

        /// <summary>Checks that the hidden A8 gesture is counted on the rendering quality row and interrupted by navigation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AdvancedGesture_CountsPressesAndResetsOnNavigation()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(SystemSetting.Rendering);
            MajEnv.RuntimeConfig.Setting.SelectedOption = nameof(RenderingOptions.RenderQuality);
            yield return StartManager();
            Assert.That(Current.Name, Is.EqualTo(nameof(SystemSetting.Rendering)));

            yield return PressButton(ButtonZone.A8);
            yield return PressButton(ButtonZone.A8);
            Assert.That(PrivateMember.GetField<int>(Current, "_renderingAdvancedClickCount"), Is.EqualTo(2));

            InputSimulator.SetButton(ButtonZone.A8, true);
            InputSimulator.SetButton(ButtonZone.A3, true);
            InputSimulator.Step();
            yield return null;
            InputSimulator.SetButton(ButtonZone.A8, false);
            InputSimulator.SetButton(ButtonZone.A3, false);
            InputSimulator.Step();
            yield return null;

            Assert.That(PrivateMember.GetField<int>(Current, "_renderingAdvancedClickCount"), Is.EqualTo(0));
        }

        /// <summary>Checks that the exit button restores volumes and leaves for the list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ExitButton_RestoresVolumesAndSwitchesScene()
        {
            yield return StartManager();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A4);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            Assert.That(float.IsNaN(Sfx("settings.wav").LastSetVolume), Is.False);
        }

        /// <summary>Checks that the exit button leaves for the view scene in view mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ExitButton_InViewMode_SwitchesToView()
        {
            MajEnv.Mode = RunningMode.View;
            yield return StartManager();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A5);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }

        /// <summary>Checks that the calibration button leaves for the calibrator and stops handling input.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CalibratorButton_SwitchesSceneAndStopsInput()
        {
            yield return StartManager();
            ExpectSceneSwitch();

            InputSimulator.StepUntilButtonClickCompleted(ButtonZone.A1);
            yield return null;
            var index = _manager.Index;
            yield return PressSensor(SensorArea.B1);

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            Assert.That(_manager.Index, Is.EqualTo(index));
        }

        /// <summary>Checks the visible A1 sensor keeps the same completed-click calibration action as physical A1.</summary>
        /// <returns>The initialized manager and calibration touch.</returns>
        [UnityTest]
        public IEnumerator CalibratorTouch_SwitchesSceneAndStopsInput()
        {
            yield return StartManager();
            ExpectSceneSwitch();
            InputSimulator.StepUntilSensorClickCompleted(SensorArea.A1);
            yield return null;
            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
        }

        /// <summary>Checks that a language change rewarms fonts without errors.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LanguageChange_RewarmsFonts()
        {
            yield return StartManager();

            Assert.DoesNotThrow(() => Localization.Current = new Language { Code = "fx-FX" });
        }

        /// <summary>Checks that repeated description texts and pool initialization are ignored.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DescriptionAndPool_IgnoreRepeats()
        {
            yield return StartManager();
            var pool = PrivateMember.GetField<Transform>(_manager, "_optionPoolRoot");

            _manager.SetDescriptionText("A\nB");
            Description.text = "changed";
            _manager.SetDescriptionText("A\nB");
            _manager.InitializeOptionPool(OptionTemplate);

            Assert.That(Description.text, Is.EqualTo("changed"));
            Assert.That(PrivateMember.GetField<Transform>(_manager, "_optionPoolRoot"), Is.SameAs(pool));
        }

        /// <summary>Checks that destroying the manager restores the ring edge and requests a save.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_RequestsSaveAndRestoresRingEdge()
        {
            yield return StartManager();
            var saves = SaveRequestCount;

            UnityEngine.Object.DestroyImmediate(_manager.gameObject);

            Assert.That(SaveRequestCount, Is.EqualTo(saves + 1));
            Assert.That(InputManager.TouchButtonRingEdge, Is.EqualTo(5.4f).Within(1e-4f));
        }
    }
}
