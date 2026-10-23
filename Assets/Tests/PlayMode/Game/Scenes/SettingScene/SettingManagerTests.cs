using System;
using System.Collections;
using System.Linq;
using LitMotion;
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
    /// Runs the setting scene manager with stub audio, skin and scene services over the real <see cref="GameSetting"/>
    /// categories, and drives it through simulated cabinet input.
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
            var attributes = new Attribute[] { new ShowInSettingUIWhenAttribute("Debug.DisplaySensor", true) };
            Menus[index].Visibility = SettingVisibility.Resolve(typeof(GameSetting), name, attributes);
            return index;
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

        /// <summary>Checks that Start builds one menu and title per category, selects Game and plays the scene sound.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_BuildsMenusAndSelectsGame()
        {
            yield return StartManager();

            var expected = SettingReflectionCache.GetVisibleProperties(typeof(GameSetting)).Length;
            Assert.That(Menus.Length, Is.EqualTo(expected));
            Assert.That(Current.Name, Is.EqualTo(nameof(GameSetting.Game)));
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
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Judge);
            MajEnv.RuntimeConfig.Setting.SelectedOption = "JudgeOffset";

            yield return StartManager();

            Assert.That(Current.Name, Is.EqualTo(nameof(GameSetting.Judge)));
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

            Assert.That(Current.Name, Is.EqualTo(nameof(GameSetting.Game)));
            Assert.That(Current.SelectedIndex, Is.EqualTo(0));
        }

        /// <summary>Checks that a selected chart adds its chart settings as the first category.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_WithSelectedChart_AddsChartSettingMenu()
        {
            MajEnv.RuntimeConfig!.List.SelectedSongHash = "majtest-setting-chart";

            yield return StartManager();

            Assert.That(Menus[0].Name, Is.EqualTo(nameof(ChartSetting)));
            Assert.That(Menus[0].Instance, Is.SameAs(ChartSettingStorage.GetSetting("majtest-setting-chart")));
            Assert.That(Menus.Length, Is.EqualTo(SettingReflectionCache.GetVisibleProperties(typeof(GameSetting)).Length + 1));
        }

        /// <summary>Checks that the chart setting page can be suppressed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_IgnoringChartPage_SkipsChartSettingMenu()
        {
            MajEnv.RuntimeConfig!.List.SelectedSongHash = "majtest-setting-chart";
            MajEnv.RuntimeConfig.Setting.IgnoreChartSettingPage = true;

            yield return StartManager();

            Assert.That(Menus.Any(menu => menu.Name == nameof(ChartSetting)), Is.False);
        }

        /// <summary>Checks that the next category button selects the following menu and animates the titles.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NextCategoryButton_SelectsFollowingMenu()
        {
            yield return StartManager();
            var start = _manager.Index;

            yield return PressButton(ButtonZone.A2);
            PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").TryComplete();

            Assert.That(_manager.Index, Is.EqualTo(start + 1));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedMenu, Is.EqualTo(Current.Name));
            Assert.That(Menus[start].gameObject.activeSelf, Is.False);
            Assert.That(Current.SelectedIndex, Is.EqualTo(0));
            Assert.That(PrivateMember.GetField<float>(_manager, "_listCursorPos"), Is.EqualTo(_manager.Index));
        }

        /// <summary>Checks that the previous category button wraps from the first menu to the last without animation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviousCategoryButton_WrapsToLastMenu()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Game);
            yield return StartManager();
            Assert.That(_manager.Index, Is.EqualTo(0));

            yield return PressButton(ButtonZone.A7);

            Assert.That(_manager.Index, Is.EqualTo(Menus.Length - 1));
            Assert.That(PrivateMember.GetField<float>(_manager, "_listCursorPos"), Is.EqualTo(Menus.Length - 1));
            Assert.That(Current.SelectedIndex, Is.EqualTo(PrivateMember.GetField<int>(Current, "_displayedOptionCount") - 1));
        }

        /// <summary>Checks that an unlisted menu is skipped with its title hidden, and listed again once its condition holds.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_SkippedUntilConditionHolds()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Game);
            yield return StartManager();
            var game = MenuIndex(nameof(GameSetting.Game));
            var judge = ListOnlyWithSensorDisplay(nameof(GameSetting.Judge));
            var display = MenuIndex(nameof(GameSetting.Display));
            yield return null;

            Assert.That(Titles[judge].gameObject.activeSelf, Is.False);

            yield return PressButton(ButtonZone.A2);
            PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").TryComplete();

            Assert.That((game, judge, display), Is.EqualTo((0, 1, 2)));
            Assert.That(_manager.Index, Is.EqualTo(display));
            Assert.That(CursorPosition, Is.EqualTo(1f));

            MajEnv.Settings.Debug.DisplaySensor = true;
            yield return null;

            Assert.That(Titles[judge].gameObject.activeSelf, Is.True);
            Assert.That(CursorPosition, Is.EqualTo(2f));

            yield return PressButton(ButtonZone.A7);
            Assert.That(_manager.Index, Is.EqualTo(judge));
        }

        /// <summary>Checks that the selected menu stays listed while its condition fails and leaves the list once another menu is selected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_SelectedMenuStaysListedUntilLeft()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Judge);
            yield return StartManager();
            var game = MenuIndex(nameof(GameSetting.Game));
            var judge = ListOnlyWithSensorDisplay(nameof(GameSetting.Judge));
            var display = MenuIndex(nameof(GameSetting.Display));
            yield return null;

            Assert.That((game, judge, display), Is.EqualTo((0, 1, 2)));
            Assert.That(_manager.Index, Is.EqualTo(judge));
            Assert.That(Titles[judge].gameObject.activeSelf, Is.True);

            yield return PressButton(ButtonZone.A2);

            // Judge left the list, so Display moved into its position without a title animation.
            Assert.That(_manager.Index, Is.EqualTo(display));
            Assert.That(Titles[judge].gameObject.activeSelf, Is.False);
            Assert.That(CursorPosition, Is.EqualTo(1f));
            Assert.That(PrivateMember.GetField<MotionHandle>(_manager, "_menuTitleDisplayerAnim").IsActive(), Is.False);

            yield return PressButton(ButtonZone.A7);
            Assert.That(_manager.Index, Is.EqualTo(game));
        }

        /// <summary>Checks that a remembered menu that is not listed falls back to Game and forgets the option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalMenu_UnlistedRememberedMenuFallsBackToGame()
        {
            yield return StartManager();
            ListOnlyWithSensorDisplay(nameof(GameSetting.Judge));
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Judge);
            MajEnv.RuntimeConfig.Setting.SelectedOption = "JudgeOffset";

            PrivateMember.Invoke(_manager, "RestoreSelectedMenu");

            Assert.That(Current.Name, Is.EqualTo(nameof(GameSetting.Game)));
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

        /// <summary>Checks that moving before the first option of a menu switches to the previous category.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PreviousOptionAtHead_SwitchesCategory()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Judge);
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
            var settings = MajEnv.Settings;
            settings.Judge.AudioOffset = 6f;
            settings.Judge.JudgeOffset = -3f;
            settings.Debug.DisplayOffset = 60f;

            settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            yield return null;

            Assert.That(settings.Judge.AudioOffset, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(settings.Judge.JudgeOffset, Is.EqualTo(-0.05f).Within(1e-6f));
            Assert.That(settings.Debug.DisplayOffset, Is.EqualTo(1f).Within(1e-6f));

            settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
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

        /// <summary>Checks that category metadata is cached and maps Audio to its Volume page.</summary>
        [Test]
        public void GetMenuMetadata_CachesAndMapsAudioToVolume()
        {
            var first = (Array)PrivateMember.InvokeStatic(typeof(SettingManager), "GetMenuMetadata", typeof(GameSetting))!;
            var second = (Array)PrivateMember.InvokeStatic(typeof(SettingManager), "GetMenuMetadata", typeof(GameSetting))!;
            var setting = new GameSetting();

            Assert.That(second, Is.SameAs(first));
            object? volume = null;
            object? game = null;
            foreach (var metadata in first)
            {
                var name = PrivateMember.GetProperty<string>(metadata!, "Name");
                if (name == "Volume")
                {
                    volume = PrivateMember.Invoke(metadata!, "GetInstance", setting);
                }
                else if (name == nameof(GameSetting.Game))
                {
                    game = PrivateMember.Invoke(metadata!, "GetInstance", setting);
                }
            }
            Assert.That(volume, Is.SameAs(setting.Audio.Volume));
            Assert.That(game, Is.SameAs(setting.Game));
        }

        /// <summary>Checks that the hidden A8 gesture is counted on the rendering quality row and interrupted by navigation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AdvancedGesture_CountsPressesAndResetsOnNavigation()
        {
            MajEnv.RuntimeConfig!.Setting.SelectedMenu = nameof(GameSetting.Rendering);
            MajEnv.RuntimeConfig.Setting.SelectedOption = nameof(RenderingOptions.RenderQuality);
            yield return StartManager();
            Assert.That(Current.Name, Is.EqualTo(nameof(GameSetting.Rendering)));

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
            yield return PressButton(ButtonZone.A2);

            Assert.That(PrivateMember.GetField<bool>(_manager, "_isExited"), Is.True);
            Assert.That(_manager.Index, Is.EqualTo(index));
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
