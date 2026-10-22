using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
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
            [global::MajdataPlay.Settings.Range("0", "2", HasMax = true, HasMin = true)]
            public float Bounded { get; set; } = 1f;

            /// <summary>An integer option without attributes.</summary>
            public int Plain { get; set; } = 3;

            /// <summary>A floating point option without attributes.</summary>
            public double Fine { get; set; } = 0.5;

            /// <summary>An option with a lower bound only.</summary>
            [Step("2")]
            [global::MajdataPlay.Settings.Range("0", null, HasMin = true, HasMax = false)]
            public long OpenEnded { get; set; } = 4;

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

        /// <summary>Checks that refreshing picks up a value changed elsewhere and that value labels are localized.</summary>
        [Test]
        public void NumberEnumerator_RefreshAndLocalization()
        {
            var probe = new NumberProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(NumberProbe.Plain));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("3"));

            UseLanguage(("MAJSETTING_PROPERTY_Plain_OPTION_8", "eight"));
            probe.Plain = 8;
            enumerator.Refresh();

            Assert.That(Convert.ToDecimal(enumerator.Current), Is.EqualTo(8m));
            Assert.That(enumerator.ValueText, Is.EqualTo("8"));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("eight"));
            enumerator.Dispose();
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
            UseLanguage(("MAJSETTING_GENERAL_OPTION_UNSET", "Not set"), ("MAJSETTING_PROPERTY_Maybe_OPTION_True", "Yes"));
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

        /// <summary>Checks the values parsed by the option attributes.</summary>
        [Test]
        public void OptionAttributes_StoreValues()
        {
            var range = new global::MajdataPlay.Settings.RangeAttribute("1.5", "5") { HasMin = true, HasMax = true };
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
