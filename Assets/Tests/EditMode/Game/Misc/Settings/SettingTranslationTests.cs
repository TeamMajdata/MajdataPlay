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
    /// <summary>Covers <see cref="SettingTranslation"/>, the localization rule the setting UI derives from the translation attributes.</summary>
    public class SettingTranslationTests
    {
        /// <summary>A settings section whose members select translation rules.</summary>
        public sealed class RuleProbe
        {
            /// <summary>Gets or sets an option without translation attributes.</summary>
            public bool Plain { get; set; }

            /// <summary>Gets or sets an option with custom templates and both value tables.</summary>
            [OptionName("Alias")]
            [UseGeneralTranslation(TranslationTarget.OptionValue)]
            [UseTranslation(TranslationTarget.OptionName, "MAJTEXT_FX_{0}")]
            [UseTranslation(TranslationTarget.OptionValue, "MAJTEXT_FX_{0}_{1}")]
            public bool Custom { get; set; }

            /// <summary>Gets or sets an option that is never translated, even though a template is also declared.</summary>
            [NoTranslation(TranslationTarget.OptionName)]
            [NoTranslation(TranslationTarget.OptionValue)]
            [UseTranslation(TranslationTarget.OptionValue, "IGNORED_{1}")]
            public bool Silent { get; set; }

            /// <summary>Gets or sets an option with a malformed key template.</summary>
            [UseTranslation(TranslationTarget.OptionName, "BROKEN_{0")]
            public bool Broken { get; set; }

            /// <summary>Gets or sets an option whose name key is overridden without translation attributes.</summary>
            [OptionName("LEGACY_{KEY}")]
            public bool Legacy { get; set; }

            /// <summary>Gets or sets an option whose name override is empty.</summary>
            [OptionName("")]
            public bool EmptyName { get; set; }
        }

        /// <summary>A settings root whose categories select title rules.</summary>
        public sealed class RootProbe
        {
            /// <summary>Gets a category whose title uses a custom template.</summary>
            [UseTranslation(TranslationTarget.MenuName, "MAJTEXT_MENU_{0}")]
            public RuleProbe Titled { get; } = new();

            /// <summary>Gets a category whose title uses the general table.</summary>
            [UseGeneralTranslation(TranslationTarget.MenuName)]
            public RuleProbe General { get; } = new();

            /// <summary>Gets a category whose title uses the general table under an option name override.</summary>
            [OptionName("Shared")]
            [UseGeneralTranslation(TranslationTarget.MenuName)]
            public RuleProbe Named { get; } = new();
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

        /// <summary>Gets the cached attributes of a probe property.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The attributes.</returns>
        private static Attribute[] Attributes(string name)
        {
            return SettingReflectionCache.GetAttributes(typeof(RuleProbe).GetProperty(name)!);
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

        /// <summary>Checks the built-in templates used without translation attributes.</summary>
        [Test]
        public void Resolve_WithoutAttributes_UsesDefaultTemplates()
        {
            var name = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Plain)), TranslationTarget.OptionName, nameof(RuleProbe.Plain));
            var value = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Plain)), TranslationTarget.OptionValue, nameof(RuleProbe.Plain));
            var menu = SettingTranslation.Resolve(Array.Empty<Attribute>(), TranslationTarget.MenuName, "Menu");

            Assert.That(name.IsExplicit, Is.False);
            Assert.That(name.IsTranslated, Is.True);
            Assert.That(name.OptionName, Is.EqualTo(nameof(RuleProbe.Plain)));
            Assert.That(name.KeyTemplates, Is.EqualTo(new[] { SettingTranslation.DefaultOptionNameTemplate }));
            Assert.That(value.KeyTemplates, Is.EqualTo(new[] { SettingTranslation.DefaultOptionValueTemplate, SettingTranslation.GeneralOptionValueTemplate }));
            Assert.That(value.GetKey(0, "True"), Is.EqualTo("MAJSETTING_PROPERTY_Plain_OPTION_True"));
            Assert.That(value.GetKey(1, "True"), Is.EqualTo("MAJSETTING_GENERAL_OPTION_True"));
            Assert.That(menu.KeyTemplates, Is.EqualTo(new[] { SettingTranslation.DefaultMenuNameTemplate }));
            Assert.That(menu.GetKey(0, string.Empty), Is.EqualTo("MAJSETTING_CATEGORY_Menu"));
            Assert.That(SettingTranslation.DefaultOptionValue.KeyTemplates, Is.EqualTo(value.KeyTemplates));
            Assert.That(SettingTranslation.DefaultOptionValue.IsExplicit, Is.False);
        }

        /// <summary>Checks that explicit templates come before the general table and substitute the option name override.</summary>
        [Test]
        public void Resolve_ExplicitAttributes_OrderTemplatesAndUseOptionName()
        {
            var attributes = Attributes(nameof(RuleProbe.Custom));
            var value = SettingTranslation.Resolve(attributes, TranslationTarget.OptionValue, nameof(RuleProbe.Custom));
            var name = SettingTranslation.Resolve(attributes, TranslationTarget.OptionName, nameof(RuleProbe.Custom));
            var menu = SettingTranslation.Resolve(attributes, TranslationTarget.MenuName, nameof(RuleProbe.Custom));

            Assert.That(value.IsExplicit, Is.True);
            Assert.That(value.OptionName, Is.EqualTo("Alias"));
            Assert.That(value.KeyTemplates, Is.EqualTo(new[] { "MAJTEXT_FX_{0}_{1}", SettingTranslation.GeneralOptionValueTemplate }));
            Assert.That(value.GetKey(0, "True"), Is.EqualTo("MAJTEXT_FX_Alias_True"));
            Assert.That(name.KeyTemplates, Is.EqualTo(new[] { "MAJTEXT_FX_{0}" }));
            Assert.That(name.GetKey(0, nameof(RuleProbe.Custom)), Is.EqualTo("MAJTEXT_FX_Alias"));
            Assert.That(menu.IsExplicit, Is.False);
            Assert.That(menu.OptionName, Is.EqualTo(nameof(RuleProbe.Custom)));
        }

        /// <summary>Checks that NoTranslation wins over templates for the same target.</summary>
        [Test]
        public void Resolve_NoTranslation_WinsOverTemplates()
        {
            UseLanguage(("IGNORED_True", "translated"));
            var value = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Silent)), TranslationTarget.OptionValue, nameof(RuleProbe.Silent));
            var name = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Silent)), TranslationTarget.OptionName, nameof(RuleProbe.Silent));

            Assert.That(value.IsExplicit, Is.True);
            Assert.That(value.IsTranslated, Is.False);
            Assert.That(value.KeyTemplates, Is.Empty);
            Assert.That(value.Localize("True"), Is.EqualTo("True"));
            Assert.That(name.Localize(nameof(RuleProbe.Silent)), Is.EqualTo(nameof(RuleProbe.Silent)));
            Assert.Throws<ArgumentOutOfRangeException>(() => value.GetKey(0, "True"));
        }

        /// <summary>Checks that a malformed template is dropped with an error instead of failing every lookup.</summary>
        [Test]
        public void Resolve_InvalidTemplate_IsDropped()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var name = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Broken)), TranslationTarget.OptionName, nameof(RuleProbe.Broken));

                Assert.That(name.IsExplicit, Is.True);
                Assert.That(name.IsTranslated, Is.False);
                Assert.That(name.Localize(nameof(RuleProbe.Broken)), Is.EqualTo(nameof(RuleProbe.Broken)));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>Checks the lookup order and the fallbacks of values and names.</summary>
        [Test]
        public void Localize_TriesTemplatesInOrderAndFallsBack()
        {
            UseLanguage(("MAJSETTING_GENERAL_OPTION_True", "On"), ("MAJSETTING_PROPERTY_Plain_OPTION_False", "Nope"));
            var value = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Plain)), TranslationTarget.OptionValue, nameof(RuleProbe.Plain));
            var name = SettingTranslation.Resolve(Attributes(nameof(RuleProbe.Plain)), TranslationTarget.OptionName, nameof(RuleProbe.Plain));

            Assert.That(value.Localize("False"), Is.EqualTo("Nope"));
            Assert.That(value.Localize("True"), Is.EqualTo("On"));
            Assert.That(value.Localize("Other"), Is.EqualTo("Other"));
            Assert.That(name.Localize(nameof(RuleProbe.Plain)), Is.EqualTo("MAJSETTING_PROPERTY_Plain"));

            UseLanguage(("MAJSETTING_PROPERTY_Plain", "Plain option"));

            Assert.That(name.Localize(nameof(RuleProbe.Plain)), Is.EqualTo("Plain option"));
        }

        /// <summary>Checks that a literal key is used verbatim, including braces.</summary>
        [Test]
        public void Literal_UsesKeyVerbatim()
        {
            var literal = SettingTranslation.Literal(TranslationTarget.OptionName, nameof(RuleProbe.Legacy), "LEGACY_{KEY}");

            Assert.That(literal.IsExplicit, Is.False);
            Assert.That(literal.IsTranslated, Is.True);
            Assert.That(literal.OptionName, Is.EqualTo(nameof(RuleProbe.Legacy)));
            Assert.That(literal.GetKey(0, "ignored"), Is.EqualTo("LEGACY_{KEY}"));

            UseLanguage(("LEGACY_{KEY}", "Legacy name"));

            Assert.That(literal.Localize(nameof(RuleProbe.Legacy)), Is.EqualTo("Legacy name"));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.Literal(TranslationTarget.OptionName, null!, "KEY"));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.Literal(TranslationTarget.OptionName, "Name", null!));
        }

        /// <summary>Checks the option name used for templates and the argument validation.</summary>
        [Test]
        public void GetOptionName_PrefersNonEmptyOverride()
        {
            Assert.That(SettingTranslation.GetOptionName(Attributes(nameof(RuleProbe.Custom)), nameof(RuleProbe.Custom)), Is.EqualTo("Alias"));
            Assert.That(SettingTranslation.GetOptionName(Attributes(nameof(RuleProbe.Plain)), nameof(RuleProbe.Plain)), Is.EqualTo(nameof(RuleProbe.Plain)));
            Assert.That(SettingTranslation.GetOptionName(Attributes(nameof(RuleProbe.EmptyName)), nameof(RuleProbe.EmptyName)), Is.EqualTo(nameof(RuleProbe.EmptyName)));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.GetOptionName(null!, "Name"));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.GetOptionName(Array.Empty<Attribute>(), null!));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.Resolve(null!, TranslationTarget.OptionName, "Name"));
            Assert.Throws<ArgumentNullException>(() => SettingTranslation.Resolve(Array.Empty<Attribute>(), TranslationTarget.OptionName, null!));
        }

        /// <summary>Checks that the option metadata applies the name rules, keeping a plain OptionName override as the complete key.</summary>
        [Test]
        public void Metadata_AppliesNameRules()
        {
            var custom = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(RuleProbe), nameof(RuleProbe.Custom)));
            var silent = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(RuleProbe), nameof(RuleProbe.Silent)));
            var legacy = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(RuleProbe), nameof(RuleProbe.Legacy)));
            var emptyName = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(RuleProbe), nameof(RuleProbe.EmptyName)));

            Assert.That(custom.OptionName, Is.EqualTo("Alias"));
            Assert.That(custom.NameKey, Is.EqualTo("MAJTEXT_FX_Alias"));
            Assert.That(silent.NameKey, Is.EqualTo(nameof(RuleProbe.Silent)));
            Assert.That(silent.LocalizeName(), Is.EqualTo(nameof(RuleProbe.Silent)));
            Assert.That(legacy.OptionName, Is.EqualTo(nameof(RuleProbe.Legacy)));
            Assert.That(legacy.NameKey, Is.EqualTo("LEGACY_{KEY}"));
            Assert.That(legacy.NameTranslation.IsExplicit, Is.False);
            Assert.That(emptyName.NameKey, Is.Empty);

            UseLanguage(("MAJTEXT_FX_Alias", "Custom name"), ("LEGACY_{KEY}", "Legacy name"));

            Assert.That(custom.LocalizeName(), Is.EqualTo("Custom name"));
            Assert.That(legacy.LocalizeName(), Is.EqualTo("Legacy name"));
        }

        /// <summary>Checks that menu titles read the translation attributes of their category property.</summary>
        [Test]
        public void GetMenuTranslation_ReadsCategoryAttributes()
        {
            var titled = SettingReflectionCache.GetMenuTranslation(typeof(RootProbe).GetProperty(nameof(RootProbe.Titled)), nameof(RootProbe.Titled));
            var general = SettingReflectionCache.GetMenuTranslation(typeof(RootProbe).GetProperty(nameof(RootProbe.General)), nameof(RootProbe.General));
            var named = SettingReflectionCache.GetMenuTranslation(typeof(RootProbe).GetProperty(nameof(RootProbe.Named)), nameof(RootProbe.Named));
            var chart = SettingReflectionCache.GetMenuTranslation(null, nameof(ChartSetting));

            Assert.That(titled.KeyTemplates, Is.EqualTo(new[] { "MAJTEXT_MENU_{0}" }));
            Assert.That(titled.GetKey(0, string.Empty), Is.EqualTo("MAJTEXT_MENU_Titled"));
            Assert.That(general.KeyTemplates, Is.EqualTo(new[] { SettingTranslation.GeneralMenuNameTemplate }));
            Assert.That(general.GetKey(0, string.Empty), Is.EqualTo("MAJSETTING_GENERAL_CATEGORY_General"));
            Assert.That(named.GetKey(0, string.Empty), Is.EqualTo("MAJSETTING_GENERAL_CATEGORY_Shared"));
            Assert.That(chart.IsExplicit, Is.False);
            Assert.That(chart.Localize(nameof(ChartSetting)), Is.EqualTo("MAJSETTING_CATEGORY_ChartSetting"));

            UseLanguage(("MAJTEXT_MENU_Titled", "Titled menu"));

            Assert.That(titled.Localize(nameof(RootProbe.Titled)), Is.EqualTo("Titled menu"));
            Assert.Throws<ArgumentNullException>(() => SettingReflectionCache.GetMenuTranslation(null, null!));
        }
    }
}
