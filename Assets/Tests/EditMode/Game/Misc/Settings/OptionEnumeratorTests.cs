using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers the default option enumerators used by the setting menu and the option attributes they read.</summary>
    public class OptionEnumeratorTests
    {
        /// <summary>A settings section with numeric options.</summary>
        public sealed class NumberProbe
        {
            /// <summary>A bounded option with a half step.</summary>
            [Step("0.5")]
            [global::MajdataPlay.Settings.Range("0", "2")]
            public float Bounded { get; set; } = 1f;

            /// <summary>An integer option without attributes.</summary>
            public int Plain { get; set; } = 3;

            /// <summary>A floating point option without attributes.</summary>
            public double Fine { get; set; } = 0.5;

            /// <summary>An option with a lower bound only.</summary>
            [Step("2")]
            [global::MajdataPlay.Settings.Range("0", null)]
            public long OpenEnded { get; set; } = 4;

            /// <summary>An option with an upper bound only.</summary>
            [Step("2")]
            [global::MajdataPlay.Settings.Range(null, "0")]
            public long UpperBounded { get; set; } = -4;

            /// <summary>An explicitly unbounded option with a custom step.</summary>
            [Step("2")]
            [global::MajdataPlay.Settings.Range]
            public long Unbounded { get; set; }

            /// <summary>An option marked read-only.</summary>
            [ReadOnlyOption]
            public int Locked { get; set; } = 7;

            /// <summary>An option without a setter.</summary>
            public int GetOnly => 5;

            /// <summary>A non-numeric option.</summary>
            public string Text { get; set; } = "x";

            /// <summary>A writable numeric field.</summary>
            public int Field = 2;

            /// <summary>A read-only numeric field.</summary>
            public readonly int ReadOnlyField = 3;

            /// <summary>An integer option whose values opt into translation.</summary>
            [UseTranslation(TranslationTarget.OptionValue)]
            public int Translated { get; set; } = 3;

            /// <summary>An integer option whose values use a custom key template with an option name override.</summary>
            [OptionName("Alias")]
            [UseTranslation(TranslationTarget.OptionValue, "MAJTEXT_FX_{0}_{1}")]
            public int Templated { get; set; } = 1;

            /// <summary>An untranslated integer option that translates only the value 3.</summary>
            [TranslateValues("3")]
            public int Selected { get; set; } = 3;

            /// <summary>A translated integer option whose selected value is ignored.</summary>
            [UseTranslation(TranslationTarget.OptionValue)]
            [TranslateValues("3", Key = "SELECTED_{1}")]
            public int SelectionIgnored { get; set; } = 3;
        }

        /// <summary>A settings section with boolean, enum and text options.</summary>
        public sealed class ValueProbe
        {
            /// <summary>A boolean option.</summary>
            public bool Enabled { get; set; } = true;

            /// <summary>An optional nullable boolean option.</summary>
            [Optional]
            public bool? Maybe { get; set; }

            /// <summary>A nullable boolean option that is not optional.</summary>
            public bool? Plain { get; set; } = false;

            /// <summary>An enum option.</summary>
            public MirrorOption Mirror { get; set; } = MirrorOption.UDMirror;

            /// <summary>A text option.</summary>
            public string Text { get; set; } = "abc";

            /// <summary>An optional text option.</summary>
            [Optional]
            public string? OptionalText { get; set; }

            /// <summary>A required text option that may be null.</summary>
            public string? NullableText { get; set; }

            /// <summary>An integer option.</summary>
            public int Number { get; set; }

            /// <summary>A boolean option whose values are never translated.</summary>
            [NoTranslation(TranslationTarget.OptionValue)]
            public bool Raw { get; set; } = true;

            /// <summary>A boolean option whose values only use the general table.</summary>
            [UseGeneralTranslation(TranslationTarget.OptionValue)]
            public bool GeneralOnly { get; set; } = true;

            /// <summary>An enum option whose values are translated in the general table.</summary>
            [UseGeneralTranslation(TranslationTarget.OptionValue)]
            public MirrorOption SharedMirror { get; set; } = MirrorOption.Off;

            /// <summary>An untranslated boolean option that translates only <see langword="true"/>.</summary>
            [NoTranslation(TranslationTarget.OptionValue)]
            [TranslateValues("True")]
            public bool SelectedFlag { get; set; } = true;

            /// <summary>A read-only nullable enum without a value.</summary>
            public MirrorOption? NullableMirror { get; }

            /// <summary>A boolean option that explicitly requests its option-specific value keys.</summary>
            [UseTranslation(TranslationTarget.OptionValue)]
            public bool Specific { get; set; } = true;

            /// <summary>An optional boolean option with explicit option-specific value keys, like <see cref="ChartSetting.SlideSkipping"/>.</summary>
            [Optional]
            [UseTranslation(TranslationTarget.OptionValue)]
            public bool? SpecificMaybe { get; set; }

            /// <summary>A read-only nullable number.</summary>
            public int? NullableNumber { get; }
        }

        /// <summary>A settings section whose enum options hold a value that is not a defined constant.</summary>
        public sealed class UndefinedEnumProbe
        {
            /// <summary>A mirror value outside the declared constants.</summary>
            public const MirrorOption UndefinedMirror = (MirrorOption)99;

            /// <summary>An editable enum option holding an undefined value.</summary>
            public MirrorOption Mirror { get; set; } = UndefinedMirror;

            /// <summary>A read-only enum option holding an undefined value.</summary>
            [ReadOnlyOption]
            public MirrorOption LockedMirror { get; set; } = UndefinedMirror;
        }

        /// <summary>The language active before the test.</summary>
        private Language _savedLanguage = null!;

        /// <summary>The languages available before the test.</summary>
        private Language[] _savedAvailable = Array.Empty<Language>();

        /// <summary>The offset unit configured before the test.</summary>
        private OffsetUnitOption _savedOffsetUnit;

        /// <summary>Captures the localization and offset unit state.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLanguage = PrivateMember.GetStaticField<Language>(typeof(Localization), "_current");
            _savedAvailable = Localization.Available;
            _savedOffsetUnit = MajEnv.Settings.Debug.OffsetUnit;
            PrivateMember.SetStaticField(typeof(Localization), "_current", Language.Default);
        }

        /// <summary>Restores the localization and offset unit state.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", _savedLanguage);
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), _savedAvailable);
            MajEnv.Settings.Debug.OffsetUnit = _savedOffsetUnit;
        }

        /// <summary>Creates and initializes an enumerator for a property.</summary>
        /// <typeparam name="T">The enumerator type.</typeparam>
        /// <param name="target">The object owning the property.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The initialized enumerator.</returns>
        private static T Create<T>(object target, string name) where T : IOptionEnumerator, new()
        {
            var enumerator = new T();
            enumerator.Init(target.GetType().GetProperty(name)!, target);
            return enumerator;
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

        /// <summary>Checks that initialization rejects null members and owners.</summary>
        [Test]
        public void Init_RejectsNullArguments()
        {
            var enumerator = new DefaultNumberEnumerator();
            var probe = new NumberProbe();

            Assert.Throws<ArgumentNullException>(() => enumerator.Init((PropertyInfo)null!, probe));
            Assert.Throws<ArgumentNullException>(() => enumerator.Init(typeof(NumberProbe).GetProperty(nameof(NumberProbe.Plain))!, null!));
            Assert.Throws<ArgumentNullException>(() => enumerator.Init((FieldInfo)null!, probe));
            Assert.Throws<ArgumentNullException>(() => enumerator.Init(typeof(NumberProbe).GetField(nameof(NumberProbe.Field))!, null!));
        }

        /// <summary>Checks stepping within the bounds of a range and step attribute.</summary>
        [Test]
        public void NumberEnumerator_StepsWithinRange()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Bounded));

            Assert.That(enumerator.Name, Is.EqualTo(nameof(NumberProbe.Bounded)));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Bounded, Is.EqualTo(1.5f));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(probe.Bounded, Is.EqualTo(2f));
            for (var i = 0; i < 4; i++)
            {
                Assert.That(enumerator.MovePrevious(), Is.True);
            }
            Assert.That(enumerator.MovePrevious(), Is.False);
            Assert.That(probe.Bounded, Is.EqualTo(0f));
            Assert.That(Convert.ToDecimal(enumerator.Current), Is.EqualTo(0m));
        }

        /// <summary>Checks the default steps used when no step or range attribute is present.</summary>
        [Test]
        public void NumberEnumerator_UsesTypeDefaultSteps()
        {
            var probe = new NumberProbe();
            var integer = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Plain));
            var floating = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Fine));

            Assert.That(integer.ValueText, Is.EqualTo("3"));
            Assert.That(integer.MoveNext(), Is.True);
            Assert.That(integer.MovePrevious(), Is.True);
            Assert.That(integer.MovePrevious(), Is.True);
            Assert.That(probe.Plain, Is.EqualTo(2));
            Assert.That(integer.ValueText, Is.EqualTo("2"));
            Assert.That(floating.MoveNext(), Is.True);
            Assert.That(probe.Fine, Is.EqualTo(0.501).Within(1e-9));
        }

        /// <summary>Checks an option that only has a lower bound.</summary>
        [Test]
        public void NumberEnumerator_LowerBoundOnly()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.OpenEnded));

            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(enumerator.MovePrevious(), Is.False);
            Assert.That(probe.OpenEnded, Is.EqualTo(0L));
            for (var i = 0; i < 10; i++)
            {
                Assert.That(enumerator.MoveNext(), Is.True);
            }
            Assert.That(probe.OpenEnded, Is.EqualTo(20L));
        }

        /// <summary>Checks that an upper-only range stops at zero and allows negative values.</summary>
        [Test]
        public void NumberEnumerator_UpperBoundOnly()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.UpperBounded));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(probe.UpperBounded, Is.EqualTo(0L));
            for (var i = 0; i < 10; i++)
            {
                Assert.That(enumerator.MovePrevious(), Is.True);
            }
            Assert.That(probe.UpperBounded, Is.EqualTo(-20L));
        }

        /// <summary>Checks that an explicitly unbounded range permits stepping across zero in both directions.</summary>
        [Test]
        public void NumberEnumerator_ExplicitlyUnbounded()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Unbounded));

            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(probe.Unbounded, Is.EqualTo(-2L));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Unbounded, Is.EqualTo(2L));
        }

        /// <summary>Checks that read-only options cannot be changed.</summary>
        [Test]
        public void NumberEnumerator_ReadOnlyOptions()
        {
            var probe = new NumberProbe();
            var marked = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Locked));
            var getOnly = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.GetOnly));
            var field = new DefaultNumberEnumerator();
            field.Init(typeof(NumberProbe).GetField(nameof(NumberProbe.ReadOnlyField))!, probe);

            Assert.That(marked.MoveNext(), Is.False);
            Assert.That(marked.MovePrevious(), Is.False);
            Assert.That(getOnly.MoveNext(), Is.False);
            Assert.That(field.MoveNext(), Is.False);
            Assert.That(probe.Locked, Is.EqualTo(7));
        }

        /// <summary>Checks that numeric fields are edited through field mode.</summary>
        [Test]
        public void NumberEnumerator_EditsFields()
        {
            var probe = new NumberProbe();
            var enumerator = new DefaultNumberEnumerator();
            enumerator.Init(typeof(NumberProbe).GetField(nameof(NumberProbe.Field))!, probe);

            Assert.That(enumerator.Name, Is.EqualTo(nameof(NumberProbe.Field)));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Field, Is.EqualTo(3));
            probe.Field = 10;
            enumerator.Refresh();
            Assert.That(enumerator.ValueText, Is.EqualTo("10"));
        }

        /// <summary>Checks that a non-numeric option is rejected.</summary>
        [Test]
        public void NumberEnumerator_RejectsNonNumbers()
        {
            Assert.Throws<InvalidOperationException>(() => Create<DefaultNumberEnumerator>(new NumberProbe(), nameof(NumberProbe.Text)));
        }

        /// <summary>Checks that refreshing picks up a value changed elsewhere and that numeric labels stay untranslated by default.</summary>
        [Test]
        public void NumberEnumerator_RefreshAndLocalization()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Plain));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("3"));

            UseLanguage(("MAJSETTING_PROPERTY_Plain_OPTION_8", "eight"), ("MAJSETTING_GENERAL_OPTION_8", "general eight"));
            probe.Plain = 8;
            enumerator.Refresh();

            Assert.That(Convert.ToDecimal(enumerator.Current), Is.EqualTo(8m));
            Assert.That(enumerator.ValueText, Is.EqualTo("8"));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("8"));
            enumerator.RefreshLocalization();
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("8"));
            enumerator.Dispose();
        }

        /// <summary>Checks that numeric values are translated only when the option opts in, substituting the option name into custom templates.</summary>
        [Test]
        public void NumberEnumerator_TranslatesValuesOnlyOnRequest()
        {
            UseLanguage(
                ("MAJSETTING_PROPERTY_Translated_OPTION_3", "drei"),
                ("MAJTEXT_FX_Alias_1", "eins"),
                ("MAJSETTING_PROPERTY_Templated_OPTION_1", "wrong template"));
            var probe = new NumberProbe();
            var translated = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Translated));
            var templated = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Templated));

            Assert.That(translated.LocalizedValueText, Is.EqualTo("drei"));
            Assert.That(templated.LocalizedValueText, Is.EqualTo("eins"));

            probe.Templated = 2;
            templated.Refresh();

            Assert.That(templated.ValueText, Is.EqualTo("2"));
            Assert.That(templated.LocalizedValueText, Is.EqualTo("2"));
        }

        /// <summary>Checks that numeric values with option-specific keys never fall back to the general table.</summary>
        [Test]
        public void NumberEnumerator_DoesNotFallBackToGeneral()
        {
            UseLanguage(
                ("MAJSETTING_GENERAL_OPTION_3", "general three"),
                ("MAJSETTING_GENERAL_OPTION_1", "general one"));
            var probe = new NumberProbe();
            var translated = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Translated));
            var templated = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Templated));
            var plain = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Plain));

            Assert.That(translated.LocalizedValueText, Is.EqualTo("3"));
            Assert.That(templated.LocalizedValueText, Is.EqualTo("1"));
            Assert.That(plain.LocalizedValueText, Is.EqualTo("3"));

            UseLanguage(
                ("MAJSETTING_PROPERTY_Translated_OPTION_3", "drei"),
                ("MAJSETTING_GENERAL_OPTION_3", "general three"));
            translated.RefreshLocalization();

            Assert.That(translated.LocalizedValueText, Is.EqualTo("drei"));
        }

        /// <summary>
        /// Checks that enum values only use the general table on request, while the placeholder of a missing enum value
        /// still falls back to it.
        /// </summary>
        [Test]
        public void EnumEnumerator_UsesGeneralTableOnlyOnRequest()
        {
            UseLanguage(
                ("MAJSETTING_GENERAL_OPTION_UDMirror", "General UD"),
                ("MAJSETTING_GENERAL_OPTION_Off", "General off"),
                ("MAJSETTING_GENERAL_OPTION_NULL", "none"));
            var probe = new ValueProbe();
            var plain = Create<DefaultEnumEnumerator>(probe, nameof(ValueProbe.Mirror));
            var shared = Create<DefaultEnumEnumerator>(probe, nameof(ValueProbe.SharedMirror));
            var nullable = Create<DefaultReadOnlyEnumerator>(probe, nameof(ValueProbe.NullableMirror));

            Assert.That(plain.LocalizedValueText, Is.EqualTo("UDMirror"));
            Assert.That(shared.LocalizedValueText, Is.EqualTo("General off"));
            Assert.That(nullable.LocalizedValueText, Is.EqualTo("none"));

            UseLanguage(("MAJSETTING_PROPERTY_Mirror_OPTION_UDMirror", "Upside down"), ("MAJSETTING_PROPERTY_SharedMirror_OPTION_Off", "ignored"));
            plain.RefreshLocalization();
            shared.RefreshLocalization();

            Assert.That(plain.LocalizedValueText, Is.EqualTo("Upside down"));
            Assert.That(shared.LocalizedValueText, Is.EqualTo("Off"));
        }

        /// <summary>
        /// Checks that TranslateValues translates only the listed values of untranslated options and is ignored when
        /// the option values are translated anyway.
        /// </summary>
        [Test]
        public void TranslateValues_TranslatesListedValuesOfUntranslatedOptions()
        {
            UseLanguage(
                ("MAJSETTING_PROPERTY_Selected_OPTION_3", "three"),
                ("MAJSETTING_PROPERTY_Selected_OPTION_4", "four"),
                ("MAJSETTING_PROPERTY_SelectedFlag_OPTION_True", "Yes"),
                ("MAJSETTING_PROPERTY_SelectedFlag_OPTION_False", "No"),
                ("SELECTED_3", "selected three"));
            var numbers = new NumberProbe();
            var selected = Create<DefaultNumberEnumerator>(numbers, nameof(NumberProbe.Selected));
            var ignored = Create<DefaultNumberEnumerator>(numbers, nameof(NumberProbe.SelectionIgnored));
            var flag = Create<DefaultBooleanEnumerator>(new ValueProbe(), nameof(ValueProbe.SelectedFlag));

            Assert.That(selected.LocalizedValueText, Is.EqualTo("three"));
            Assert.That(ignored.LocalizedValueText, Is.EqualTo("3"));
            Assert.That(flag.LocalizedValuesForWarmup, Is.EqualTo(new[] { "False", "Yes" }));

            numbers.Selected = 4;
            selected.Refresh();

            Assert.That(selected.LocalizedValueText, Is.EqualTo("4"));
        }

        /// <summary>Checks that a boolean option with explicit option-specific keys does not fall back to the general table.</summary>
        [Test]
        public void BooleanEnumerator_ExplicitKeysDoNotFallBack()
        {
            UseLanguage(("MAJSETTING_GENERAL_OPTION_True", "On"));
            var enumerator = Create<DefaultBooleanEnumerator>(new ValueProbe(), nameof(ValueProbe.Specific));

            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("True"));

            UseLanguage(("MAJSETTING_PROPERTY_Specific_OPTION_True", "Yes"), ("MAJSETTING_GENERAL_OPTION_True", "On"));
            enumerator.RefreshLocalization();

            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("Yes"));
        }

        /// <summary>
        /// Checks that an optional boolean with explicit keys translates its values only through those keys while its
        /// unset placeholder still falls back to the general table.
        /// </summary>
        [Test]
        public void BooleanEnumerator_ExplicitOptionalKeepsGeneralPlaceholder()
        {
            UseLanguage(
                ("MAJSETTING_GENERAL_OPTION_UNSET", "Not set"),
                ("MAJSETTING_GENERAL_OPTION_False", "general off"),
                ("MAJSETTING_PROPERTY_SpecificMaybe_OPTION_True", "Enabled"));
            var enumerator = Create<DefaultBooleanEnumerator>(new ValueProbe(), nameof(ValueProbe.SpecificMaybe));

            Assert.That(enumerator.LocalizedValuesForWarmup, Is.EquivalentTo(new[] { "Not set", "False", "Enabled" }));
        }

        /// <summary>Checks that a boolean enumerator cycles between false and true.</summary>
        [Test]
        public void BooleanEnumerator_CyclesValues()
        {
            var probe = new ValueProbe();
            var enumerator = Create<DefaultBooleanEnumerator>(probe, nameof(ValueProbe.Enabled));

            Assert.That(enumerator.Current, Is.EqualTo(true));
            Assert.That(enumerator.ValueText, Is.EqualTo("True"));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Enabled, Is.False);
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(probe.Enabled, Is.True);
            probe.Enabled = false;
            enumerator.Refresh();
            Assert.That(enumerator.Current, Is.EqualTo(false));
        }

        /// <summary>Checks that optional booleans include an unset state with a localizable label.</summary>
        [Test]
        public void BooleanEnumerator_OptionalIncludesUnset()
        {
            UseLanguage(("MAJSETTING_GENERAL_OPTION_UNSET", "Not set"), ("MAJSETTING_GENERAL_OPTION_True", "Yes"), ("MAJSETTING_PROPERTY_Maybe_OPTION_True", "ignored"));
            var probe = new ValueProbe();
            var enumerator = Create<DefaultBooleanEnumerator>(probe, nameof(ValueProbe.Maybe));

            Assert.That(enumerator.Current, Is.Null);
            Assert.That(enumerator.ValueText, Is.EqualTo("UNSET"));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("Not set"));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Maybe, Is.False);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Maybe, Is.True);
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("Yes"));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Maybe, Is.Null);
        }

        /// <summary>Checks that refreshing the localization updates every value label.</summary>
        [Test]
        public void BooleanEnumerator_RefreshLocalization()
        {
            var probe = new ValueProbe();
            var enumerator = Create<DefaultBooleanEnumerator>(probe, nameof(ValueProbe.Plain));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("False"));

            UseLanguage(("MAJSETTING_GENERAL_OPTION_False", "Off"));
            enumerator.RefreshLocalization();

            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("Off"));
        }

        /// <summary>Checks that a boolean enumerator rejects other types.</summary>
        [Test]
        public void BooleanEnumerator_RejectsOtherTypes()
        {
            Assert.Throws<InvalidOperationException>(() => Create<DefaultBooleanEnumerator>(new ValueProbe(), nameof(ValueProbe.Number)));
        }

        /// <summary>Checks that NoTranslation keeps the raw value text and UseGeneralTranslation skips the option-specific key.</summary>
        [Test]
        public void BooleanEnumerator_HonoursTranslationAttributes()
        {
            UseLanguage(
                ("MAJSETTING_GENERAL_OPTION_True", "On"),
                ("MAJSETTING_PROPERTY_Raw_OPTION_True", "Translated raw"),
                ("MAJSETTING_PROPERTY_GeneralOnly_OPTION_True", "Specific"));
            var probe = new ValueProbe();
            var raw = Create<DefaultBooleanEnumerator>(probe, nameof(ValueProbe.Raw));
            var general = Create<DefaultBooleanEnumerator>(probe, nameof(ValueProbe.GeneralOnly));

            Assert.That(raw.LocalizedValueText, Is.EqualTo("True"));
            Assert.That(general.LocalizedValueText, Is.EqualTo("On"));

            raw.RefreshLocalization();
            general.RefreshLocalization();

            Assert.That(raw.LocalizedValueText, Is.EqualTo("True"));
            Assert.That(general.LocalizedValueText, Is.EqualTo("On"));
        }

        /// <summary>Checks that the null placeholder of a numeric option is still translated through the general table.</summary>
        [Test]
        public void ReadOnlyEnumerator_TranslatesNullPlaceholderOfNumbers()
        {
            UseLanguage(("MAJSETTING_GENERAL_OPTION_NULL", "none"));
            var enumerator = Create<DefaultReadOnlyEnumerator>(new ValueProbe(), nameof(ValueProbe.NullableNumber));

            Assert.That(enumerator.ValueText, Is.EqualTo("NULL"));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("none"));
        }

        /// <summary>Checks that an enum enumerator cycles through every constant without changing the shared cache.</summary>
        [Test]
        public void EnumEnumerator_CyclesConstants()
        {
            var probe = new ValueProbe();
            var enumerator = Create<DefaultEnumEnumerator>(probe, nameof(ValueProbe.Mirror));

            Assert.That(enumerator.Current, Is.EqualTo(MirrorOption.UDMirror));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Mirror, Is.EqualTo(MirrorOption.Off));
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(probe.Mirror, Is.EqualTo(MirrorOption.UDMirror));
            probe.Mirror = MirrorOption.LRMirror;
            enumerator.Refresh();
            Assert.That(enumerator.ValueText, Is.EqualTo("LRMirror"));
            Assert.That(SettingReflectionCache.GetEnumValues(typeof(MirrorOption))[0], Is.EqualTo(MirrorOption.Off));
            Assert.Throws<InvalidOperationException>(() => Create<DefaultEnumEnumerator>(new ValueProbe(), nameof(ValueProbe.Number)));
        }

        /// <summary>
        /// Checks that an enum value that is not a defined constant, as an unknown number in settings.json deserializes,
        /// selects and stores the first constant instead of leaving the enumerator without a valid label.
        /// </summary>
        [Test]
        public void EnumEnumerator_UndefinedValue_SelectsFirstConstant()
        {
            var probe = new UndefinedEnumProbe();

            var enumerator = Create<DefaultEnumEnumerator>(probe, nameof(UndefinedEnumProbe.Mirror));

            Assert.That(enumerator.Current, Is.EqualTo(MirrorOption.Off));
            Assert.That(enumerator.ValueText, Is.EqualTo("Off"));
            Assert.That(() => enumerator.LocalizedValueText, Throws.Nothing);
            Assert.That(probe.Mirror, Is.EqualTo(MirrorOption.Off));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Mirror, Is.EqualTo(MirrorOption.LRMirror));
        }

        /// <summary>Checks that a read-only enum option with an undefined value shows the first constant without writing it.</summary>
        [Test]
        public void EnumEnumerator_UndefinedReadOnlyValue_KeepsStoredValue()
        {
            var probe = new UndefinedEnumProbe();

            var enumerator = Create<DefaultEnumEnumerator>(probe, nameof(UndefinedEnumProbe.LockedMirror));

            Assert.That(enumerator.Current, Is.EqualTo(MirrorOption.Off));
            Assert.That(() => enumerator.LocalizedValueText, Throws.Nothing);
            Assert.That(probe.LockedMirror, Is.EqualTo(UndefinedEnumProbe.UndefinedMirror));
        }

        /// <summary>Checks that the read-only enumerator shows the current value and never edits it.</summary>
        [Test]
        public void ReadOnlyEnumerator_ShowsValue()
        {
            var probe = new ValueProbe();
            var text = Create<DefaultReadOnlyEnumerator>(probe, nameof(ValueProbe.Text));
            var optional = Create<DefaultReadOnlyEnumerator>(probe, nameof(ValueProbe.OptionalText));
            var nullable = Create<DefaultReadOnlyEnumerator>(probe, nameof(ValueProbe.NullableText));

            Assert.That(text.Current, Is.EqualTo("abc"));
            Assert.That(text.MoveNext(), Is.False);
            Assert.That(text.MovePrevious(), Is.False);
            Assert.That(optional.ValueText, Is.EqualTo("UNSET"));
            Assert.That(nullable.ValueText, Is.EqualTo("NULL"));

            probe.Text = "changed";
            probe.NullableText = "value";
            text.Refresh();
            nullable.Refresh();

            Assert.That(text.ValueText, Is.EqualTo("changed"));
            Assert.That(nullable.LocalizedValueText, Is.EqualTo("value"));
        }

        /// <summary>Checks that the note mask enumerator selects a stored mask and repairs unknown values.</summary>
        [Test]
        public void NoteMaskEnumerator_SelectsAndRepairs()
        {
            var valid = new ModOptions { NoteMask = "Outer" };
            var invalid = new ModOptions { NoteMask = "Bogus" };

            var validEnumerator = Create<NoteMaskEnumerator>(valid, nameof(ModOptions.NoteMask));
            var invalidEnumerator = Create<NoteMaskEnumerator>(invalid, nameof(ModOptions.NoteMask));

            Assert.That(validEnumerator.Current, Is.EqualTo("Outer"));
            Assert.That(validEnumerator.MoveNext(), Is.True);
            Assert.That(valid.NoteMask, Is.EqualTo("Disable"));
            Assert.That(invalidEnumerator.Current, Is.EqualTo("Disable"));
            Assert.That(invalid.NoteMask, Is.EqualTo("Disable"));
        }

        /// <summary>Checks that offsets step by frames or seconds and follow a unit change on refresh.</summary>
        [Test]
        public void GameOffsetEnumerator_FollowsOffsetUnit()
        {
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
            var judge = new JudgeOptions();
            var enumerator = Create<GameOffsetEnumerator>(judge, nameof(JudgeOptions.AudioOffset));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(judge.AudioOffset, Is.EqualTo(0.1f).Within(1e-6f));

            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Second;
            enumerator.Refresh();
            Assert.That(enumerator.MoveNext(), Is.True);

            Assert.That(judge.AudioOffset, Is.EqualTo(0.101f).Within(1e-6f));
            enumerator.Refresh();
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(judge.AudioOffset, Is.EqualTo(0.102f).Within(1e-6f));
        }

        /// <summary>Checks that the display offset cannot become negative.</summary>
        [Test]
        public void GameOffsetEnumerator_DisplayOffsetHasLowerBound()
        {
            MajEnv.Settings.Debug.OffsetUnit = OffsetUnitOption.Frame;
            var debug = new DebugOptions();
            var enumerator = Create<GameOffsetEnumerator>(debug, nameof(DebugOptions.DisplayOffset));

            Assert.That(enumerator.MovePrevious(), Is.False);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(debug.DisplayOffset, Is.EqualTo(0.1f).Within(1e-6f));
        }

        /// <summary>Checks that the language enumerator lists the available languages and switches the active language.</summary>
        [Test]
        public void LanguageEnumerator_SwitchesLanguage()
        {
            var first = new Language { Code = "aa-AA", Author = "A" };
            var second = new Language { Code = "bb-BB", Author = "B" };
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), new[] { first, second });
            PrivateMember.SetStaticField(typeof(Localization), "_current", second);
            var display = new DisplayOptions();

            var enumerator = Create<LanguageEnumerator>(display, nameof(DisplayOptions.Language));

            Assert.That(enumerator.Current, Is.EqualTo("bb-BB - B"));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(Localization.Current, Is.SameAs(first));
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(Localization.Current, Is.SameAs(second));
        }

        /// <summary>Checks that an unknown current language selects the first available language.</summary>
        [Test]
        public void LanguageEnumerator_UnknownCurrentSelectsFirst()
        {
            var first = new Language { Code = "aa-AA", Author = "A" };
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), new[] { first });

            var enumerator = Create<LanguageEnumerator>(new DisplayOptions(), nameof(DisplayOptions.Language));

            Assert.That(enumerator.Current, Is.EqualTo("aa-AA - A"));
        }

        /// <summary>Checks that the language enumerator becomes read-only when no language is available.</summary>
        [Test]
        public void LanguageEnumerator_NoLanguages_IsReadOnly()
        {
            PrivateMember.SetStaticProperty(typeof(Localization), nameof(Localization.Available), Array.Empty<Language>());
            var display = new DisplayOptions();

            var enumerator = Create<LanguageEnumerator>(display, nameof(DisplayOptions.Language));

            Assert.That(enumerator.Current, Is.EqualTo("Unavailable"));
            Assert.That(display.Language, Is.EqualTo("Unavailable"));
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MovePrevious(), Is.False);
        }

        /// <summary>Checks that omitted bounds stay disabled and supplied zero bounds are enforced.</summary>
        /// <param name="min">The lower bound text, or null or empty to omit it.</param>
        /// <param name="max">The upper bound text, or null or empty to omit it.</param>
        /// <param name="hasMin">Whether the lower bound should be enforced.</param>
        /// <param name="hasMax">Whether the upper bound should be enforced.</param>
        [TestCase(null, null, false, false)]
        [TestCase(null, "", false, false)]
        [TestCase("", null, false, false)]
        [TestCase("", "", false, false)]
        [TestCase("0", null, true, false)]
        [TestCase("0", "", true, false)]
        [TestCase(null, "0", false, true)]
        [TestCase("", "0", false, true)]
        [TestCase("0", "0", true, true)]
        public void RangeAttribute_InfersEnabledBounds(string? min, string? max, bool hasMin, bool hasMax)
        {
            var range = new global::MajdataPlay.Settings.RangeAttribute(min, max);

            Assert.That(range.HasMin, Is.EqualTo(hasMin));
            Assert.That(range.HasMax, Is.EqualTo(hasMax));
            Assert.That(range.Min, Is.EqualTo(0m));
            Assert.That(range.Max, Is.EqualTo(0m));
        }

        /// <summary>Checks that decimal bounds retain their values regardless of the current number culture.</summary>
        /// <param name="cultureName">The culture active while constructing the attribute.</param>
        [TestCase("en-US")]
        [TestCase("zh-CN")]
        [TestCase("de-DE")]
        [TestCase("fr-FR")]
        public void RangeAttribute_ParsesInvariantBounds(string cultureName)
        {
            var savedCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var range = new global::MajdataPlay.Settings.RangeAttribute("-0.05", "1.5");

                Assert.That(range.Min, Is.EqualTo(-0.05m));
                Assert.That(range.Max, Is.EqualTo(1.5m));
                Assert.That(range.HasMin, Is.True);
                Assert.That(range.HasMax, Is.True);
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
            }
        }

        /// <summary>Checks that the full decimal range can be supplied without losing precision.</summary>
        [Test]
        public void RangeAttribute_PreservesDecimalLimits()
        {
            var range = new global::MajdataPlay.Settings.RangeAttribute("-79228162514264337593543950335", "79228162514264337593543950335");

            Assert.That(range.Min, Is.EqualTo(decimal.MinValue));
            Assert.That(range.Max, Is.EqualTo(decimal.MaxValue));
            Assert.That(range.HasMin, Is.True);
            Assert.That(range.HasMax, Is.True);
        }

        /// <summary>Checks that malformed supplied bounds are rejected rather than treated as omitted.</summary>
        /// <param name="min">The lower bound text.</param>
        /// <param name="max">The upper bound text.</param>
        [TestCase("invalid", null)]
        [TestCase(null, "invalid")]
        [TestCase(" ", null)]
        [TestCase(null, " ")]
        public void RangeAttribute_RejectsMalformedBounds(string? min, string? max)
        {
            Assert.Throws<FormatException>(() => new global::MajdataPlay.Settings.RangeAttribute(min, max));
        }

        /// <summary>Checks that out-of-range bounds retain the documented overflow behavior.</summary>
        /// <param name="min">The lower bound text.</param>
        /// <param name="max">The upper bound text.</param>
        [TestCase("-79228162514264337593543950336", null)]
        [TestCase(null, "79228162514264337593543950336")]
        public void RangeAttribute_RejectsOverflowingBounds(string? min, string? max)
        {
            Assert.Throws<OverflowException>(() => new global::MajdataPlay.Settings.RangeAttribute(min, max));
        }

        /// <summary>Checks the values parsed by the option attributes.</summary>
        [Test]
        public void OptionAttributes_StoreValues()
        {
            var range = new global::MajdataPlay.Settings.RangeAttribute("1.5", "5");
            var empty = new global::MajdataPlay.Settings.RangeAttribute(null, string.Empty);
            var defaultRange = new global::MajdataPlay.Settings.RangeAttribute();
            var defaultValue = new DefaultValueAttribute("3", typeof(int));
            var enumeratorAttribute = new OptionEnumeratorAttribute(typeof(DefaultBooleanEnumerator));

            Assert.That(range.Min, Is.EqualTo(1.5m));
            Assert.That(range.Max, Is.EqualTo(5m));
            Assert.That(range.HasMin, Is.True);
            Assert.That(range.HasMax, Is.True);
            Assert.That(empty.Min, Is.EqualTo(0m));
            Assert.That(empty.Max, Is.EqualTo(0m));
            Assert.That(defaultRange.HasMin, Is.False);
            Assert.That(defaultRange.HasMax, Is.False);
            Assert.That(new StepAttribute("0.25").Value, Is.EqualTo(0.25m));
            Assert.That(defaultValue.Value, Is.EqualTo("3"));
            Assert.That(defaultValue.Type, Is.EqualTo(typeof(int)));
            Assert.That(enumeratorAttribute.EnumeratorType, Is.EqualTo(typeof(DefaultBooleanEnumerator)));
            Assert.That(enumeratorAttribute.Instance(), Is.InstanceOf<DefaultBooleanEnumerator>());
            Assert.That(new global::MajdataPlay.Settings.DescriptionAttribute(null!).Text, Is.Empty);
            Assert.That(new MenuNameAttribute(null!).Name, Is.Empty);
            Assert.That(new MenuNameAttribute("Menu").Name, Is.EqualTo("Menu"));
            Assert.That(new OptionNameAttribute(null!).Name, Is.Empty);
            Assert.That(new OptionalAttribute(), Is.Not.Null);
            Assert.That(new NoDescriptionAttribute(), Is.Not.Null);
            Assert.That(new HideInSettingUIAttribute(), Is.Not.Null);
        }
    }
}
