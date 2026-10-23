using System.Collections;
using System.Collections.Generic;
using LitMotion;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>Covers a setting category menu: pooled option views, navigation and rendering option visibility.</summary>
    public class MenuTests : SettingSceneTestBase
    {
        /// <summary>The manager supplying the option pool.</summary>
        private SettingManager _manager = null!;

        /// <summary>The settings instance shown by fixture menus.</summary>
        private SettingFixture _settings = null!;

        /// <summary>Creates the inactive manager and the settings instance.</summary>
        [SetUp]
        public void SetUp()
        {
            _manager = CreateManager();
            _settings = new SettingFixture();
        }

        /// <summary>Gets the number of options the menu currently displays.</summary>
        /// <param name="menu">The menu.</param>
        /// <returns>The displayed option count.</returns>
        private static int DisplayedCount(Menu menu)
        {
            return PrivateMember.GetField<int>(menu, "_displayedOptionCount");
        }

        /// <summary>Checks that initialization builds models for visible properties and fills the manager's option pool.</summary>
        [Test]
        public void Init_BuildsVisibleOptionsAndPool()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.Init();

            Assert.That(DisplayedCount(menu), Is.EqualTo(4));
            Assert.That(PrivateMember.GetField<Stack<Option>>(_manager, "_optionPool").Count, Is.EqualTo(Menu.OPTION_POOL_CAPACITY));
        }

        /// <summary>Checks that operations before initialization are ignored.</summary>
        [Test]
        public void Uninitialized_IgnoresNavigation()
        {
            var menu = AddInactiveUi<Menu>("Uninitialized", MenuListRoot.transform);
            Activate(menu);

            menu.SwitchOption(1);
            menu.HandleInput();
            menu.ToHead();
            menu.ToTail();
            menu.ToOption("Mode");
            menu.RefreshVisibleEnumerators();

            Assert.That(menu.SelectedIndex, Is.EqualTo(0));
            Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.False);
        }

        /// <summary>Checks that moving to the head shows the first two options and records the selection.</summary>
        [Test]
        public void ToHead_ShowsSelectedAndNextOption()
        {
            var menu = CreateMenu(_manager, _settings);

            menu.ToHead();

            Assert.That(menu.SelectedIndex, Is.EqualTo(0));
            Assert.That(NameOf(VisibleOption(menu, 0)!), Is.EqualTo("MAJSETTING_PROPERTY_Toggle"));
            Assert.That(VisibleOption(menu, 1), Is.Not.Null);
            Assert.That(VisibleOption(menu, 2), Is.Null);
            Assert.That(VisibleOption(menu, 0)!.transform.localPosition, Is.EqualTo(new Vector3(0, -220, 0)));
            Assert.That(VisibleOption(menu, 1)!.transform.localPosition, Is.EqualTo(new Vector3(380, -220, 0)));
            Assert.That(VisibleOption(menu, 1)!.transform.localScale.x, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.EqualTo(nameof(SettingFixture.Toggle)));
            Assert.That(Description.text, Is.EqualTo("MAJSETTING_PROPERTY_Toggle_DESC"));
        }

        /// <summary>Checks that moving to the tail selects the last displayed option.</summary>
        [Test]
        public void ToTail_SelectsLastOption()
        {
            var menu = CreateMenu(_manager, _settings);

            menu.ToTail();

            Assert.That(menu.SelectedIndex, Is.EqualTo(3));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.EqualTo(nameof(SettingFixture.AudioOffset)));
        }

        /// <summary>Checks that options can be selected by name, falling back to the first option.</summary>
        [Test]
        public void ToOption_SelectsByNameOrFirst()
        {
            var menu = CreateMenu(_manager, _settings);

            menu.ToOption(nameof(SettingFixture.Quiet));
            Assert.That(menu.SelectedIndex, Is.EqualTo(2));

            menu.ToOption("Missing");
            Assert.That(menu.SelectedIndex, Is.EqualTo(0));

            menu.ToOption(nameof(SettingFixture.Quiet));
            menu.ToOption(string.Empty);
            Assert.That(menu.SelectedIndex, Is.EqualTo(0));
        }

        /// <summary>Checks that switching options animates to the next option and settles there.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SwitchOption_AnimatesToNextOption()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToHead();

            menu.SwitchOption(1);
            yield return null;
            PrivateMember.GetField<MotionHandle>(menu, "_optionAnim").TryComplete();

            Assert.That(menu.SelectedIndex, Is.EqualTo(1));
            Assert.That(VisibleOption(menu, 1)!.transform.localPosition, Is.EqualTo(new Vector3(0, -220, 0)));
            Assert.That(VisibleOption(menu, 0), Is.Not.Null);
            Assert.That(VisibleOption(menu, 2), Is.Not.Null);
        }

        /// <summary>Checks that moving past either end asks the manager to change category without changing the selection.</summary>
        [Test]
        public void SwitchOption_PastEndsDelegatesToManager()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToHead();

            menu.SwitchOption(-1);
            Assert.That(menu.SelectedIndex, Is.EqualTo(0));

            menu.ToTail();
            menu.SwitchOption(1);
            Assert.That(menu.SelectedIndex, Is.EqualTo(3));
        }

        /// <summary>Checks that input reaches the selected option.</summary>
        [Test]
        public void HandleInput_ForwardsToSelectedOption()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToOption(nameof(SettingFixture.Mode));
            menu.HandleInput();

            InputSimulator.SetSensor(SensorArea.E4, true);
            InputSimulator.Step();
            menu.HandleInput();

            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Second));
        }

        /// <summary>Checks that refreshing enumerators shows values changed outside the menu.</summary>
        [Test]
        public void RefreshVisibleEnumerators_ShowsExternalChanges()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToHead();
            _settings.Toggle = true;

            menu.RefreshVisibleEnumerators();

            Assert.That(ValueOf(VisibleOption(menu, 0)!), Is.EqualTo("True"));
        }

        /// <summary>Checks that disabling returns the views to the pool and enabling lays them out again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DisableAndEnable_ReturnsAndRestoresViews()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToOption(nameof(SettingFixture.Mode));

            menu.gameObject.SetActive(false);
            Assert.That(VisibleOption(menu, 1), Is.Null);
            Assert.That(PrivateMember.GetField<Stack<Option>>(_manager, "_optionPool").Count, Is.EqualTo(Menu.OPTION_POOL_CAPACITY));

            menu.gameObject.SetActive(true);
            yield return null;

            Assert.That(VisibleOption(menu, 1), Is.Not.Null);
            Assert.That(menu.SelectedIndex, Is.EqualTo(1));
        }

        /// <summary>Checks that warmup text is collected from every option model.</summary>
        [Test]
        public void CollectWarmupText_CoversEveryOption()
        {
            var menu = CreateMenu(_manager, _settings);
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            menu.CollectWarmupText(warmup, new[] { font }, font);

            var requests = PrivateMember.GetField<Dictionary<TMP_FontAsset, HashSet<uint>>>(warmup, "_requests");
            Assert.That(requests[font], Does.Contain((uint)'Q').And.Contains((uint)'F').And.Contains((uint)'0'));
        }

        /// <summary>Checks that the performance preset only shows the quality row and the common rendering options.</summary>
        [Test]
        public void RenderingPreset_ShowsQualityAndCommonOptions()
        {
            var rendering = new RenderingOptions();
            var menu = CreateMenu(_manager, rendering);

            Assert.That(DisplayedCount(menu), Is.EqualTo(4));
            menu.ToHead();
            Assert.That(NameOf(VisibleOption(menu, 0)!), Is.EqualTo("MAJSETTING_PROPERTY_RenderQuality"));
        }

        /// <summary>Checks that compatibility mode hides everything except the common options and keeps the selected row.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RenderingCompatibilityMode_ShowsOnlyCommonOptions()
        {
            var rendering = new RenderingOptions();
            var menu = CreateMenu(_manager, rendering);
            menu.ToOption(nameof(RenderingOptions.CompatibilityMode));

            rendering.CompatibilityMode = true;
            yield return null;

            Assert.That(DisplayedCount(menu), Is.EqualTo(3));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.EqualTo(nameof(RenderingOptions.CompatibilityMode)));
            Assert.That(NameOf(VisibleOption(menu, menu.SelectedIndex)!), Is.EqualTo("MAJSETTING_PROPERTY_CompatibilityMode"));
        }

        /// <summary>Checks that custom quality shows every rendering option.</summary>
        [Test]
        public void RenderingCustomQuality_ShowsEveryOption()
        {
            var rendering = new RenderingOptions();
            rendering.EnterCustomMode();
            var menu = CreateMenu(_manager, rendering);

            Assert.That(DisplayedCount(menu), Is.GreaterThan(4));
        }

        /// <summary>Checks that the hidden gesture counts presses only on the quality row and resets elsewhere.</summary>
        [Test]
        public void RenderingAdvancedInput_CountsOnQualityRowOnly()
        {
            var rendering = new RenderingOptions();
            var menu = CreateMenu(_manager, rendering);
            menu.ToOption(nameof(RenderingOptions.RenderQuality));

            for (var i = 0; i < 9; i++)
            {
                Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.True);
            }
            Assert.That(menu.TryHandleRenderingAdvancedInput(false), Is.True);
            Assert.That(PrivateMember.GetField<int>(menu, "_renderingAdvancedClickCount"), Is.EqualTo(9));
            Assert.That(rendering.RenderQuality, Is.Not.EqualTo(RenderingQualityOption.Custom));

            menu.ToOption(nameof(RenderingOptions.VSync));
            Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.False);
            Assert.That(PrivateMember.GetField<int>(menu, "_renderingAdvancedClickCount"), Is.EqualTo(0));
        }

        /// <summary>Checks that the tenth press on the quality row unlocks custom rendering and requests a save.</summary>
        [Test]
        public void RenderingAdvancedInput_TenthPressUnlocksCustomMode()
        {
            var vSync = QualitySettings.vSyncCount;
            var level = QualitySettings.GetQualityLevel();
            var mipmapLimit = QualitySettings.globalTextureMipmapLimit;
            try
            {
                var rendering = new RenderingOptions();
                var menu = CreateMenu(_manager, rendering);
                menu.ToOption(nameof(RenderingOptions.RenderQuality));

                for (var i = 0; i < 10; i++)
                {
                    menu.TryHandleRenderingAdvancedInput(true);
                }

                Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
                Assert.That(SaveRequestCount, Is.EqualTo(1));
                Assert.That(DisplayedCount(menu), Is.GreaterThan(4));
                Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.True);
                Assert.That(SaveRequestCount, Is.EqualTo(1));
            }
            finally
            {
                QualitySettings.SetQualityLevel(level, true);
                QualitySettings.vSyncCount = vSync;
                QualitySettings.globalTextureMipmapLimit = mipmapLimit;
            }
        }

        /// <summary>Checks that non-rendering menus and disabled menus reject the hidden gesture.</summary>
        [Test]
        public void RenderingAdvancedInput_RejectedOutsideRenderingMenus()
        {
            var menu = CreateMenu(_manager, _settings);
            Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.False);

            var rendering = CreateMenu(_manager, new RenderingOptions());
            rendering.gameObject.SetActive(false);
            Assert.That(rendering.TryHandleRenderingAdvancedInput(true), Is.False);
        }

        /// <summary>Checks that compatibility mode rejects the hidden gesture.</summary>
        [Test]
        public void RenderingAdvancedInput_RejectedInCompatibilityMode()
        {
            var rendering = new RenderingOptions { CompatibilityMode = true };
            var menu = CreateMenu(_manager, rendering);

            Assert.That(menu.TryHandleRenderingAdvancedInput(true), Is.False);
        }

        /// <summary>Checks that options whose conditions do not hold are left out of the displayed list.</summary>
        [Test]
        public void ConditionalOptions_HiddenWhileConditionFails()
        {
            var menu = CreateMenu(_manager, new ConditionalSettingFixture());

            menu.ToTail();

            Assert.That(DisplayedCount(menu), Is.EqualTo(2));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.EqualTo(nameof(ConditionalSettingFixture.Mode)));
        }

        /// <summary>Checks that editing the controlling option shows the dependent option in the same frame and keeps the selection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalOptions_AppearWhenControllingOptionIsEdited()
        {
            var settings = new ConditionalSettingFixture();
            var menu = CreateMenu(_manager, settings);
            menu.ToOption(nameof(ConditionalSettingFixture.Advanced));
            menu.HandleInput();

            InputSimulator.SetSensor(SensorArea.E4, true);
            InputSimulator.Step();
            menu.HandleInput();
            yield return null;

            Assert.That(settings.Advanced, Is.True);
            Assert.That(DisplayedCount(menu), Is.EqualTo(3));
            Assert.That(menu.SelectedIndex, Is.EqualTo(0));
            Assert.That(NameOf(VisibleOption(menu, 1)!), Is.EqualTo("MAJSETTING_PROPERTY_Detail"));
        }

        /// <summary>Checks that hiding the selected option selects the first displayed option.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ConditionalOptions_HidingSelectedOptionSelectsFirst()
        {
            var settings = new ConditionalSettingFixture { Mode = FixtureMode.Second };
            var menu = CreateMenu(_manager, settings);
            menu.ToOption(nameof(ConditionalSettingFixture.SecondOnly));
            Assert.That(menu.SelectedIndex, Is.EqualTo(2));

            settings.Mode = FixtureMode.Third;
            yield return null;

            Assert.That(DisplayedCount(menu), Is.EqualTo(2));
            Assert.That(menu.SelectedIndex, Is.EqualTo(0));
            Assert.That(MajEnv.RuntimeConfig!.Setting.SelectedOption, Is.EqualTo(nameof(ConditionalSettingFixture.Advanced)));
            Assert.That(NameOf(VisibleOption(menu, 0)!), Is.EqualTo("MAJSETTING_PROPERTY_Advanced"));
        }

        /// <summary>Checks the static layout helpers used for option views.</summary>
        [Test]
        public void LayoutHelpers_InterpolateByDistance()
        {
            Assert.That(PrivateMember.InvokeStatic<Vector3>(typeof(Menu), "GetOptionTransformPosition", -1.5f), Is.EqualTo(new Vector3(-570, -220, 0)));
            Assert.That(PrivateMember.InvokeStatic<Vector3>(typeof(Menu), "GetOptionTransformScale", 0.5f).x, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(PrivateMember.InvokeStatic<Vector3>(typeof(Menu), "GetOptionTransformScale", 3f).x, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(PrivateMember.InvokeStatic<Color>(typeof(Menu), "GetOptionTextColor", 0f), Is.EqualTo(new Color(0.8823529f, 0.8078431f, 0.6392157f, 1f)));
        }

        /// <summary>Checks that destroying the menu cancels its motion and disposes its models.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_CancelsMotion()
        {
            var menu = CreateMenu(_manager, _settings);
            menu.ToHead();
            menu.SwitchOption(1);
            yield return null;
            var handle = PrivateMember.GetField<MotionHandle>(menu, "_optionAnim");

            Object.DestroyImmediate(menu.gameObject);

            Assert.That(handle.IsActive(), Is.False);
        }
    }
}
