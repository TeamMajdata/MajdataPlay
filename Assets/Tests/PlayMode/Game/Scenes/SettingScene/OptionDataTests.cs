using System.Collections.Generic;
using MajdataPlay.i18n;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>Covers the per-option model of the setting menu: metadata, value enumeration and localization.</summary>
    public class OptionDataTests : SceneTestBase
    {
        /// <summary>The settings instance edited by the options.</summary>
        private SettingFixture _settings = null!;

        /// <summary>Creates the settings instance.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingFixture();
        }

        /// <summary>Creates the model of one fixture property.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The option model.</returns>
        private OptionData Create(string name)
        {
            return new OptionData(typeof(SettingFixture).GetProperty(name)!, _settings);
        }

        /// <summary>Checks the default keys and initial value of a boolean option.</summary>
        [Test]
        public void Constructor_UsesDefaultKeys()
        {
            using var data = Create(nameof(SettingFixture.Toggle));

            Assert.That(data.PropertyInfo.Name, Is.EqualTo(nameof(SettingFixture.Toggle)));
            Assert.That(data.LocalizedName, Is.EqualTo("MAJSETTING_PROPERTY_Toggle"));
            Assert.That(data.HasDescription, Is.True);
            Assert.That(data.DescriptionKey, Is.EqualTo("MAJSETTING_PROPERTY_Toggle_DESC"));
            Assert.That(data.IsOffsetOption, Is.False);
            Assert.That(data.LocalizedValueText, Is.EqualTo("False"));
        }

        /// <summary>Checks that name and description attributes override the keys.</summary>
        [Test]
        public void Constructor_UsesAttributeKeys()
        {
            using var data = Create(nameof(SettingFixture.Mode));

            Assert.That(data.LocalizedName, Is.EqualTo("FIXTURE_MODE_NAME"));
            Assert.That(data.DescriptionKey, Is.EqualTo("FIXTURE_MODE_DESC"));
            Assert.That(data.LocalizedValueText, Is.EqualTo("First"));
        }

        /// <summary>Checks that the translation selection attributes leave the default name, description and value texts.</summary>
        [Test]
        public void Constructor_TranslationSelectionAttributesKeepDefaultKeys()
        {
            var settings = new AnnotatedSettingFixture();
            using var data = new OptionData(typeof(AnnotatedSettingFixture).GetProperty(nameof(AnnotatedSettingFixture.Annotated))!, settings);

            Assert.That(data.LocalizedName, Is.EqualTo("MAJSETTING_PROPERTY_Annotated"));
            Assert.That(data.DescriptionKey, Is.EqualTo("MAJSETTING_PROPERTY_Annotated_DESC"));
            Assert.That(data.HasDescription, Is.True);
            Assert.That(data.LocalizedValueText, Is.EqualTo("False"));
            Assert.That(data.MoveNext(), Is.True);
            Assert.That(settings.Annotated, Is.True);
        }

        /// <summary>Checks the description and offset flags.</summary>
        [Test]
        public void Constructor_ReadsDescriptionAndOffsetFlags()
        {
            using var quiet = Create(nameof(SettingFixture.Quiet));
            using var offset = Create(nameof(SettingFixture.AudioOffset));

            Assert.That(quiet.HasDescription, Is.False);
            Assert.That(offset.IsOffsetOption, Is.True);
        }

        /// <summary>Checks that moving through values edits the setting and wraps around.</summary>
        [Test]
        public void MoveNextAndPrevious_EditSettingAndWrap()
        {
            using var data = Create(nameof(SettingFixture.Mode));

            Assert.That(data.MoveNext(), Is.True);
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Second));
            Assert.That(data.LocalizedValueText, Is.EqualTo("Second"));

            Assert.That(data.MovePrevious(), Is.True);
            Assert.That(data.MovePrevious(), Is.True);
            Assert.That(_settings.Mode, Is.EqualTo(FixtureMode.Third));
            Assert.That(data.LocalizedValueText, Is.EqualTo("Third"));
        }

        /// <summary>Checks that an external change is detected once and refreshes the value text.</summary>
        [Test]
        public void RefreshIfChanged_DetectsExternalChangesOnce()
        {
            using var data = Create(nameof(SettingFixture.Toggle));

            Assert.That(data.RefreshIfChanged(), Is.False);
            _settings.Toggle = true;

            Assert.That(data.RefreshIfChanged(), Is.True);
            Assert.That(data.LocalizedValueText, Is.EqualTo("True"));
            Assert.That(data.RefreshIfChanged(), Is.False);
        }

        /// <summary>Checks that localization refreshes only when the language instance changes.</summary>
        [Test]
        public void RefreshLocalization_UpdatesNameAndValuesForNewLanguage()
        {
            using var data = Create(nameof(SettingFixture.Toggle));
            var language = new Language
            {
                Translations = new Dictionary<string, string>
                {
                    { "MAJSETTING_PROPERTY_Toggle", "Switch" },
                    { "MAJSETTING_GENERAL_OPTION_False", "Off" },
                }
            };

            data.RefreshLocalization();
            Assert.That(data.LocalizedName, Is.EqualTo("MAJSETTING_PROPERTY_Toggle"));

            Localization.Current = language;
            data.RefreshLocalization();

            Assert.That(data.LocalizedName, Is.EqualTo("Switch"));
            Assert.That(data.LocalizedValueText, Is.EqualTo("Off"));
        }

        /// <summary>Checks that warmup collects names, every value label and the description for each option font.</summary>
        [Test]
        public void CollectWarmupText_RequestsLabelsAndDescription()
        {
            using var data = Create(nameof(SettingFixture.Mode));
            var warmup = new SettingFontWarmup();
            var optionFont = CreateDynamicFont();
            var descriptionFont = CreateDynamicFont();

            data.CollectWarmupText(warmup, new[] { optionFont }, descriptionFont);

            var requests = PrivateMember.GetField<Dictionary<TMP_FontAsset, HashSet<uint>>>(warmup, "_requests");
            Assert.That(requests[optionFont], Does.Contain((uint)'T').And.Contains((uint)'h').And.Contains((uint)'N'));
            Assert.That(requests[descriptionFont], Does.Contain((uint)'D'));
        }

        /// <summary>Checks that options without descriptions request no description glyphs.</summary>
        [Test]
        public void CollectWarmupText_SkipsMissingDescription()
        {
            using var data = Create(nameof(SettingFixture.Quiet));
            var warmup = new SettingFontWarmup();
            var optionFont = CreateDynamicFont();
            var descriptionFont = CreateDynamicFont();

            data.CollectWarmupText(warmup, new[] { optionFont }, descriptionFont);

            var requests = PrivateMember.GetField<Dictionary<TMP_FontAsset, HashSet<uint>>>(warmup, "_requests");
            Assert.That(requests.ContainsKey(descriptionFont), Is.False);
            Assert.That(requests[optionFont], Does.Contain((uint)'Q'));
        }

        /// <summary>Checks that numeric options request their current value text.</summary>
        [Test]
        public void CollectWarmupText_RequestsNumericValueText()
        {
            _settings.AudioOffset = 7f;
            using var data = Create(nameof(SettingFixture.AudioOffset));
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            data.CollectWarmupText(warmup, new[] { font }, font);

            var requests = PrivateMember.GetField<Dictionary<TMP_FontAsset, HashSet<uint>>>(warmup, "_requests");
            Assert.That(requests[font], Does.Contain((uint)'7'));
        }
    }
}
