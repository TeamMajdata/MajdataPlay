using System;
using System.Collections.Generic;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers <see cref="SettingValueSelection"/>, which translates the values listed by <see cref="TranslateValuesAttribute"/>.</summary>
    public class SettingValueSelectionTests
    {
        /// <summary>A settings section whose members select single values for translation.</summary>
        public sealed class SelectionProbe
        {
            /// <summary>Gets or sets a numeric option with two selected values.</summary>
            [TranslateValues("0", "0.5")]
            public float Distance { get; set; }

            /// <summary>Gets or sets a numeric option with a custom key template and an option name override.</summary>
            [OptionName("Alias")]
            [TranslateValues("1", Key = "MAJTEXT_FX_{0}_{1}")]
            public int Custom { get; set; }

            /// <summary>Gets or sets a numeric option whose selected values are not numbers or empty.</summary>
            [TranslateValues("abc", "")]
            public int Invalid { get; set; }

            /// <summary>Gets or sets a numeric option whose key template is malformed.</summary>
            [TranslateValues("1", Key = "BROKEN_{1")]
            public int BrokenKey { get; set; }

            /// <summary>Gets or sets an untranslated enum option with one selected constant.</summary>
            [NoTranslation(TranslationTarget.OptionValue)]
            [TranslateValues(nameof(MirrorOption.UDMirror))]
            public MirrorOption Mirror { get; set; }

            /// <summary>Gets or sets an option without selected values.</summary>
            public int Plain { get; set; }
        }

        /// <summary>The language active before the test.</summary>
        private Language _savedLanguage = null!;

        /// <summary>Captures the active language.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLanguage = PrivateMember.GetStaticField<Language>(typeof(Localization), "_current");
            PrivateMember.SetStaticField(typeof(Localization), "_current", Language.Default);
        }

        /// <summary>Restores the active language.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", _savedLanguage);
        }

        /// <summary>Resolves the selection of a probe property with its declared type.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The selection.</returns>
        private static SettingValueSelection Resolve(string name)
        {
            var property = typeof(SelectionProbe).GetProperty(name)!;
            return SettingValueSelection.Resolve(SettingReflectionCache.GetAttributes(property), name, property.PropertyType);
        }

        /// <summary>Installs a language with the given translations.</summary>
        /// <param name="translations">The translation pairs.</param>
        private static void UseLanguage(params (string Key, string Value)[] translations)
        {
            var dictionary = new Dictionary<string, string>();
            foreach (var (key, value) in translations)
            {
                dictionary[key] = value;
            }
            PrivateMember.SetStaticField(typeof(Localization), "_current", new Language { Code = "xx-XX", Author = "Test", Translations = dictionary });
        }

        /// <summary>Checks that numeric values match by number and use the value as written in their key.</summary>
        [Test]
        public void TryGetKey_MatchesNumbersByValue()
        {
            var distance = Resolve(nameof(SelectionProbe.Distance));

            Assert.That(distance.TryGetKey(0f, "0", out var zero), Is.True);
            Assert.That(zero, Is.EqualTo("MAJSETTING_PROPERTY_Distance_OPTION_0"));
            Assert.That(distance.TryGetKey(0.5f, "0,5", out var half), Is.True, "the display text of the current culture does not matter");
            Assert.That(half, Is.EqualTo("MAJSETTING_PROPERTY_Distance_OPTION_0.5"));
            Assert.That(distance.TryGetKey(0.0m, "0.0", out _), Is.True);
            Assert.That(distance.TryGetKey(1f, "1", out _), Is.False);
            Assert.That(distance.TryGetKey("text", "0", out _), Is.False);
        }

        /// <summary>Checks the custom key template, the option name override and text matching of non-numeric options.</summary>
        [Test]
        public void TryGetKey_UsesKeyTemplateAndMatchesText()
        {
            var custom = Resolve(nameof(SelectionProbe.Custom));
            var mirror = Resolve(nameof(SelectionProbe.Mirror));

            Assert.That(custom.OptionName, Is.EqualTo("Alias"));
            Assert.That(custom.TryGetKey(1, "1", out var key), Is.True);
            Assert.That(key, Is.EqualTo("MAJTEXT_FX_Alias_1"));
            Assert.That(mirror.TryGetKey(MirrorOption.UDMirror, nameof(MirrorOption.UDMirror), out var mirrorKey), Is.True);
            Assert.That(mirrorKey, Is.EqualTo("MAJSETTING_PROPERTY_Mirror_OPTION_UDMirror"));
            Assert.That(mirror.TryGetKey(MirrorOption.LRMirror, nameof(MirrorOption.LRMirror), out _), Is.False);
        }

        /// <summary>Checks that invalid numbers, empty values and malformed templates are dropped with errors.</summary>
        [Test]
        public void Resolve_DropsInvalidEntries()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.That(Resolve(nameof(SelectionProbe.Invalid)).IsEmpty, Is.True);
                Assert.That(Resolve(nameof(SelectionProbe.BrokenKey)).IsEmpty, Is.True);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.That(Resolve(nameof(SelectionProbe.Plain)), Is.SameAs(SettingValueSelection.Empty));
            Assert.Throws<ArgumentNullException>(() => SettingValueSelection.Resolve(null!, "Name", null));
            Assert.Throws<ArgumentNullException>(() => SettingValueSelection.Resolve(Array.Empty<Attribute>(), null!, null));
        }

        /// <summary>Checks that only selected values are translated and that a missing translation keeps the raw text and is reported.</summary>
        [Test]
        public void Localize_TranslatesSelectedValuesOnly()
        {
            UseLanguage(("MAJSETTING_PROPERTY_Distance_OPTION_0", "OFF"));
            var distance = Resolve(nameof(SelectionProbe.Distance));

            Assert.That(distance.Localize(0f, "0"), Is.EqualTo("OFF"));
            Assert.That(distance.Localize(0.25f, "0.25"), Is.EqualTo("0.25"));
            Assert.That(distance.Localize(0.5f, "0.5"), Is.EqualTo("0.5"));
            var reported = PrivateMember.GetStaticField<HashSet<string>>(typeof(Localization), "_reportedMissingTranslations");
            Assert.That(reported, Is.EquivalentTo(new[] { "MAJSETTING_PROPERTY_Distance_OPTION_0.5" }));
        }
    }
}
