using System.Collections;
using System.Collections.Generic;
using MajdataPlay.i18n;
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
    /// <summary>Covers a single setting option view: binding, selection, input and refresh behaviour.</summary>
    public class OptionTests : SettingSceneTestBase
    {
        /// <summary>The settings instance edited by the option.</summary>
        private SettingFixture _settings = null!;

        /// <summary>The manager receiving description texts.</summary>
        private SettingManager _manager = null!;

        /// <summary>The option under test.</summary>
        private Option _option = null!;

        /// <summary>Creates an active option and an inactive manager.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingFixture();
            _manager = CreateManager();
            _option = CreateOptionTemplate(Canvas);
        }

        /// <summary>Binds the option to one fixture property.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The option model.</returns>
        private OptionData Bind(string name)
        {
            var data = new OptionData(typeof(SettingFixture).GetProperty(name)!, _settings);
            _option.Bind(_manager, data);
            return data;
        }

        /// <summary>Checks that binding shows the localized name and value.</summary>
        [Test]
        public void Bind_ShowsNameAndValue()
        {
            Bind(nameof(SettingFixture.Mode));

            Assert.That(NameOf(_option), Is.EqualTo("FIXTURE_MODE_NAME"));
            Assert.That(ValueOf(_option), Is.EqualTo("First"));
        }

        /// <summary>Checks that selecting shows the description with line breaks replaced by commas.</summary>
        [Test]
        public void SetSelected_ShowsDescription()
        {
            Bind(nameof(SettingFixture.Toggle));

            _option.SetSelected(true);
            _option.SetSelected(true);

            Assert.That(Description.text, Is.EqualTo("MAJSETTING_PROPERTY_Toggle_DESC"));
        }

        /// <summary>Checks that an option without description clears the description.</summary>
        [Test]
        public void SetSelected_WithoutDescription_ClearsDescription()
        {
            _manager.SetDescriptionText("previous");
            Bind(nameof(SettingFixture.Quiet));

            _option.SetSelected(true);

            Assert.That(Description.text, Is.Empty);
        }

        /// <summary>Checks that an offset option appends the offset unit to its description.</summary>
        [Test]
        public void SetSelected_OffsetOption_AppendsUnit()
        {
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            Bind(nameof(SettingFixture.AudioOffset));

            _option.SetSelected(true);

            Assert.That(Description.text, Is.EqualTo("MAJSETTING_PROPERTY_AudioOffset_DESC,MAJTEXT_SETTING_OFFSETUNIT_Second"));
        }

        /// <summary>Checks that a changed offset unit refreshes the selected option's description on the next late update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_RefreshesDescriptionWhenOffsetUnitChanges()
        {
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
            Bind(nameof(SettingFixture.AudioOffset));
            _option.SetSelected(true);

            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            yield return null;

            Assert.That(Description.text, Does.EndWith("MAJTEXT_SETTING_OFFSETUNIT_Second"));
        }

        /// <summary>Checks that a setting changed elsewhere is shown on the next late update.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_ShowsExternalChanges()
        {
            Bind(nameof(SettingFixture.Toggle));

            _settings.Toggle = true;
            yield return null;

            Assert.That(ValueOf(_option), Is.EqualTo("True"));
        }

        /// <summary>Checks that the next and previous sensors change the selected option's value.</summary>
        [Test]
        public void HandleInput_SensorsChangeValue()
        {
            Bind(nameof(SettingFixture.Mode));
            _option.SetSelected(true);
            _option.HandleInput();

            InputSimulator.SetSensor(SensorArea.E4, true);
            InputSimulator.Step();
            _option.HandleInput();
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Second));
            Assert.That(ValueOf(_option), Is.EqualTo("Second"));

            InputSimulator.SetSensor(SensorArea.E4, false);
            InputSimulator.Step();
            _option.HandleInput();
            InputSimulator.SetSensor(SensorArea.B6, true);
            InputSimulator.Step();
            _option.HandleInput();

            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.First));
        }

        /// <summary>Checks that input waits for release after selection and is ignored while unselected.</summary>
        [Test]
        public void HandleInput_IgnoredUntilSelectedAndReleased()
        {
            Bind(nameof(SettingFixture.Mode));
            InputSimulator.SetSensor(SensorArea.B4, true);
            InputSimulator.Step();

            _option.HandleInput();
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.First));

            _option.SetSelected(true);
            _option.HandleInput();
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.First));

            InputSimulator.SetSensor(SensorArea.B4, false);
            InputSimulator.Step();
            _option.HandleInput();
            InputSimulator.SetSensor(SensorArea.B3, true);
            InputSimulator.Step();
            _option.HandleInput();
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Second));
        }

        /// <summary>Checks that a held sensor repeats after the initial delay using the configured iteration speed.</summary>
        [Test]
        public void HandleInput_RepeatsWhileHeld()
        {
            MajEnv.Settings.Debug.MenuOptionIterationSpeed = 0;
            Bind(nameof(SettingFixture.Mode));
            _option.SetSelected(true);
            _option.HandleInput();
            InputSimulator.SetSensor(SensorArea.E4, true);
            InputSimulator.Step();
            _option.HandleInput();

            SetDeltaTime(0.5f);
            _option.HandleInput();

            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Third));
        }

        /// <summary>Checks that a language change refreshes the name and the selected description.</summary>
        [Test]
        public void LanguageChange_RefreshesTexts()
        {
            Bind(nameof(SettingFixture.Toggle));
            _option.SetSelected(true);

            Localization.Current = new Language
            {
                Translations = new Dictionary<string, string>
                {
                    { "MAJSETTING_PROPERTY_Toggle", "Switch" },
                    { "MAJSETTING_PROPERTY_Toggle_DESC", "Line1\nLine2" },
                }
            };

            Assert.That(NameOf(_option), Is.EqualTo("Switch"));
            Assert.That(Description.text, Is.EqualTo("Line1,Line2"));
        }

        /// <summary>Checks that a disabled option ignores language changes and catches up when enabled.</summary>
        [Test]
        public void DisableAndEnable_ResubscribeAndRefresh()
        {
            Bind(nameof(SettingFixture.Toggle));
            _option.gameObject.SetActive(false);

            Localization.Current = new Language
            {
                Translations = new Dictionary<string, string> { { "MAJSETTING_PROPERTY_Toggle", "Switch" } }
            };
            Assert.That(NameOf(_option), Is.EqualTo("MAJSETTING_PROPERTY_Toggle"));
            _settings.Toggle = true;

            _option.gameObject.SetActive(true);

            Assert.That(NameOf(_option), Is.EqualTo("Switch"));
            Assert.That(ValueOf(_option), Is.EqualTo("True"));
        }

        /// <summary>Checks that an unbound option ignores input and enumerator refreshes.</summary>
        [Test]
        public void Unbind_StopsReactingToTheModel()
        {
            Bind(nameof(SettingFixture.Toggle));
            _option.SetSelected(true);
            _option.Unbind();
            _settings.Toggle = true;

            _option.RefreshEnumerator();
            _option.HandleInput();
            _option.SetSelected(true);
            Localization.Current = new Language();

            Assert.That(ValueOf(_option), Is.EqualTo("False"));
        }

        /// <summary>Checks that refreshing the enumerator shows the current setting value.</summary>
        [Test]
        public void RefreshEnumerator_ShowsCurrentValue()
        {
            Bind(nameof(SettingFixture.Mode));
            _settings.Mode = FixtureMode.Third;

            _option.RefreshEnumerator();

            Assert.That(ValueOf(_option), Is.EqualTo("Third"));
        }

        /// <summary>Checks that the option reports the fonts of both texts and tints both texts.</summary>
        [Test]
        public void FontsAndColor_CoverBothTexts()
        {
            Bind(nameof(SettingFixture.Toggle));
            var fonts = new HashSet<TMP_FontAsset>();

            _option.CollectFontAssets(fonts);
            _option.SetTextColor(Color.magenta);

            var name = PrivateMember.GetField<TextMeshProUGUI>(_option, "_nameTextDisplayer");
            var value = PrivateMember.GetField<TextMeshProUGUI>(_option, "_valueTextDisplayer");
            Assert.That(fonts, Does.Contain(name.font));
            Assert.That(name.canvasRenderer.GetColor(), Is.EqualTo(Color.magenta));
            Assert.That(value.canvasRenderer.GetColor(), Is.EqualTo(Color.magenta));
        }

        /// <summary>Checks that destroying a bound option unsubscribes from language changes.</summary>
        [Test]
        public void Destroy_UnsubscribesFromLanguageChanges()
        {
            Bind(nameof(SettingFixture.Toggle));

            Object.DestroyImmediate(_option.gameObject);

            Assert.DoesNotThrow(() => Localization.Current = new Language());
        }
    }
}
