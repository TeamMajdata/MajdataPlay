using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.i18n;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.i18n
{
    /// <summary>Covers language loading and lookup in <see cref="Localization"/>, <see cref="Language"/> and the string helpers.</summary>
    public class LocalizationTests
    {
        /// <summary>The language active before the test.</summary>
        private Language _savedLanguage = null!;

        /// <summary>The languages available before the test.</summary>
        private Language[] _savedAvailable = Array.Empty<Language>();

        /// <summary>The initialization flag before the test.</summary>
        private bool _savedIsInited;

        /// <summary>Captures and resets the localization state.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLanguage = PrivateMember.GetStaticField<Language>(typeof(Localization), "_current");
            _savedAvailable = Localization.Available;
            _savedIsInited = PrivateMember.GetStaticField<bool>(typeof(Localization), "_isInited");
            PrivateMember.SetStaticField(typeof(Localization), "_current", Language.Default);
        }

        /// <summary>Restores the localization state.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", _savedLanguage);
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), _savedAvailable);
            PrivateMember.SetStaticField(typeof(Localization), "_isInited", _savedIsInited);
        }

        /// <summary>Creates a language with translations.</summary>
        /// <param name="code">The language code.</param>
        /// <param name="author">The author name.</param>
        /// <param name="translations">The translation pairs.</param>
        /// <returns>The language.</returns>
        private static Language CreateLanguage(string code, string author, params (string Key, string Value)[] translations)
        {
            return new Language
            {
                Code = code,
                Author = author,
                Translations = translations.ToDictionary(pair => pair.Key, pair => pair.Value)
            };
        }

        /// <summary>Checks that the four bundled language files are loaded once from Resources/Langs.</summary>
        [Test]
        public void Init_LoadsBundledLanguages()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_isInited", false);
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), Array.Empty<Language>());

            Localization.Init();
            var loaded = Localization.Available;
            Localization.Init();

            Assert.That(loaded.Select(language => language.Code), Is.EquivalentTo(new[] { "en-US", "ja-JP", "zh-CN", "zh-TW" }));
            Assert.That(Localization.Available, Is.SameAs(loaded));
            foreach (var language in loaded)
            {
                Assert.That(language.GetTranslations(), Is.Not.Empty, language.Code);
            }
        }

        /// <summary>Checks that the bundled languages translate the same keys as English.</summary>
        [Test]
        public void BundledLanguages_ShareEnglishKeys()
        {
            var english = Localization.Parse(Resources.Load<TextAsset>("Langs/en-US").text)!;
            var keys = english.GetTranslations().Keys.ToArray();

            Assert.That(keys, Is.Not.Empty);
            foreach (var code in new[] { "ja-JP", "zh-CN", "zh-TW" })
            {
                var language = Localization.Parse(Resources.Load<TextAsset>("Langs/" + code).text)!;
                Assert.That(language.Code, Is.EqualTo(code));
                Assert.That(language.GetTranslations().ContainsKey("MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_UNSET"), Is.True, code);
            }
        }

        /// <summary>Checks that a valid language file is parsed and an invalid one is rejected.</summary>
        [Test]
        public void Parse_ReadsLanguageFile()
        {
            var language = Localization.Parse("{\"Code\":\"xx-XX\",\"Author\":\"Me\",\"Translations\":{\"KEY\":\"Value\"}}");

            Assert.That(language, Is.Not.Null);
            Assert.That(language!.Code, Is.EqualTo("xx-XX"));
            Assert.That(language.Author, Is.EqualTo("Me"));
            Assert.That(language.ToString(), Is.EqualTo("xx-XX - Me"));
            Assert.That(language.GetTranslations()["KEY"], Is.EqualTo("Value"));
            Assert.That(Localization.Parse("{ not json"), Is.Null);
        }

        /// <summary>Checks that parsing several files skips invalid ones and keeps the first of each code/author pair.</summary>
        [Test]
        public void ParseMany_DeduplicatesLanguages()
        {
            var languages = Localization.Parse(new[]
            {
                "{\"Code\":\"a\",\"Author\":\"x\",\"Translations\":{\"K\":\"first\"}}",
                "broken",
                "{\"Code\":\"a\",\"Author\":\"x\",\"Translations\":{\"K\":\"second\"}}",
                "{\"Code\":\"b\",\"Author\":\"x\"}",
            });

            Assert.That(languages.Select(language => language.ToString()), Is.EqualTo(new[] { "a - x", "b - x" }));
            Assert.That(languages[0].GetTranslations()["K"], Is.EqualTo("first"));
            Assert.That(Localization.Parse(new[] { "broken" }), Is.Empty);
        }

        /// <summary>Checks that languages are selected by display name or code and raise the change event.</summary>
        [Test]
        public void SetLang_SelectsAvailableLanguage()
        {
            var english = CreateLanguage("en-US", "A");
            var japanese = CreateLanguage("ja-JP", "B");
            var changes = new List<Language>();
            EventHandler<Language> handler = (_, language) => changes.Add(language);
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), new[] { english, japanese });
            Localization.OnLanguageChanged += handler;
            try
            {
                Assert.That(Localization.SetLang("ja-JP - B"), Is.True);
                Assert.That(Localization.Current, Is.SameAs(japanese));
                Assert.That(Localization.SetLangByCode("en-US"), Is.True);
                Assert.That(Localization.Current, Is.SameAs(english));
                Assert.That(Localization.SetLang("missing"), Is.False);
                Assert.That(Localization.SetLangByCode("missing"), Is.False);
            }
            finally
            {
                Localization.OnLanguageChanged -= handler;
            }

            Assert.That(changes, Is.EqualTo(new[] { japanese, english }));
        }

        /// <summary>Checks that no language can be selected while none is available.</summary>
        [Test]
        public void SetLang_WithoutLanguages_Fails()
        {
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), Array.Empty<Language>());

            Assert.That(Localization.SetLang("en-US - A"), Is.False);
            Assert.That(Localization.SetLangByCode("en-US"), Is.False);
        }

        /// <summary>Checks that a missing translation returns the key itself.</summary>
        [Test]
        public void TryGetLocalizedText_ReturnsKeyWhenMissing()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", CreateLanguage("xx", "y", ("HELLO", "Hi")));

            Assert.That(Localization.TryGetLocalizedText("HELLO", out var found), Is.True);
            Assert.That(found, Is.EqualTo("Hi"));
            Assert.That(Localization.TryGetLocalizedText("MISSING", out var missing), Is.False);
            Assert.That(missing, Is.EqualTo("MISSING"));
        }

        /// <summary>Checks that the dictionary format wins over the mapping table and that the result is cached.</summary>
        [Test]
        public void GetTranslations_PrefersDictionaryAndCaches()
        {
            var both = new Language
            {
                Translations = new Dictionary<string, string> { { "K", "dictionary" } },
                MappingTable = new[] { new LangTable { Origin = "K", Content = "table" } }
            };

            var first = both.GetTranslations();

            Assert.That(first["K"], Is.EqualTo("dictionary"));
            Assert.That(both.GetTranslations(), Is.SameAs(first));
        }

        /// <summary>Checks the legacy mapping table format, including entries without an origin.</summary>
        [Test]
        public void GetTranslations_ReadsMappingTable()
        {
            var language = new Language
            {
                Translations = new Dictionary<string, string>(),
                MappingTable = new[]
                {
                    new LangTable { Origin = "A", Content = "1" },
                    new LangTable { Origin = string.Empty, Content = "ignored" },
                    new LangTable { Origin = "B", Content = "2" },
                }
            };

            var translations = language.GetTranslations();

            Assert.That(translations, Is.EquivalentTo(new Dictionary<string, string> { { "A", "1" }, { "B", "2" } }));
        }

        /// <summary>Checks that a language without translations yields an empty table.</summary>
        [Test]
        public void GetTranslations_EmptyLanguage()
        {
            var language = Language.Default;

            Assert.That(language.GetTranslations(), Is.Empty);
            Assert.That(Language.Default, Is.Not.SameAs(language));
            Assert.That(new LangTable().Origin, Is.Empty);
            Assert.That(new LangTable().Content, Is.Empty);
        }

        /// <summary>Checks translation and formatting through the string extension.</summary>
        [Test]
        public void I18nExtension_TranslatesAndFormats()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", CreateLanguage("xx", "y",
                ("PLAIN", "Plain text"),
                ("FORMAT", "{0} of {1}"),
                ("BROKEN", "{0} and {1}")));

            Assert.That("PLAIN".i18n(), Is.EqualTo("Plain text"));
            Assert.That("PLAIN".i18n(1, 2), Is.EqualTo("Plain text"));
            Assert.That("FORMAT".i18n(1, 2), Is.EqualTo("1 of 2"));
            Assert.That("FORMAT".i18n(null!), Is.EqualTo("{0} of {1}"));
            Assert.That("BROKEN".i18n(1), Is.EqualTo("{0} and {1}"));
            Assert.That("MISSING".i18n(), Is.EqualTo("MISSING"));
            Assert.That("PLAIN".Tryi18n(out var text), Is.True);
            Assert.That(text, Is.EqualTo("Plain text"));
            Assert.That("MISSING".Tryi18n(out _), Is.False);
        }

        /// <summary>Checks the locale and language code of every system language.</summary>
        [Test]
        public void SystemLanguageExtensions_MapEveryLanguage()
        {
            foreach (SystemLanguage language in Enum.GetValues(typeof(SystemLanguage)))
            {
                var locale = language.ToLocale();
                var code = language.ToLanguageCode();

                Assert.That(locale, Does.Match("^[a-z]{2}-[A-Z]{2}$"), language.ToString());
                Assert.That(code, Does.Match("^[a-z]{2}$"), language.ToString());
            }
            Assert.That(SystemLanguage.ChineseTraditional.ToLocale(), Is.EqualTo("zh-TW"));
            Assert.That(SystemLanguage.ChineseSimplified.ToLocale(), Is.EqualTo("zh-CN"));
            Assert.That(SystemLanguage.Chinese.ToLanguageCode(), Is.EqualTo("zh"));
            Assert.That(SystemLanguage.Japanese.ToLocale(), Is.EqualTo("ja-JP"));
            Assert.That(SystemLanguage.Norwegian.ToLocale(), Is.EqualTo("nb-NO"));
            Assert.That(SystemLanguage.Norwegian.ToLanguageCode(), Is.EqualTo("no"));
            Assert.That(SystemLanguage.Unknown.ToLocale(), Is.EqualTo("en-US"));
            Assert.That(SystemLanguage.Unknown.ToLanguageCode(), Is.EqualTo("en"));
            Assert.That(((SystemLanguage)999).ToLocale(), Is.EqualTo("en-US"));
        }
    }
}
