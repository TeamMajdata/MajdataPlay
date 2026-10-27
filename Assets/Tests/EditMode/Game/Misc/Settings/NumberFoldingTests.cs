using System;
using System.Collections.Generic;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Tests;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers <see cref="FoldNumberAttribute"/> and the number folding it applies to the option value texts.</summary>
    public class NumberFoldingTests
    {
        /// <summary>A settings section with options marked for folding.</summary>
        public sealed class FoldProbe
        {
            /// <summary>A folded integer option with a large step.</summary>
            [FoldNumber]
            [Step("500")]
            [global::MajdataPlay.Settings.Range("0", null)]
            public int Folded { get; set; } = 1000;

            /// <summary>The same integer option without folding.</summary>
            [Step("500")]
            [global::MajdataPlay.Settings.Range("0", null)]
            public int Unfolded { get; set; } = 1000;

            /// <summary>A folded floating-point option.</summary>
            [FoldNumber]
            public double FoldedDouble { get; set; } = 2500000.5;

            /// <summary>A folded integer option that translates the value 1000.</summary>
            [FoldNumber]
            [TranslateValues("1000")]
            public int Selected { get; set; } = 1000;

            /// <summary>A folded read-only nullable integer option.</summary>
            [FoldNumber]
            public int? MissingNumber { get; }

            /// <summary>A folded read-only nullable integer option holding a value.</summary>
            [FoldNumber]
            public long? NullableValue { get; } = 2000;

            /// <summary>A text option marked for folding, which must be ignored.</summary>
            [FoldNumber]
            public string Text { get; set; } = "1000";

            /// <summary>An enum option marked for folding whose value prints as a number, which must be ignored.</summary>
            [FoldNumber]
            public MirrorOption Mirror { get; } = (MirrorOption)1000;
        }

        /// <summary>The language active before the test.</summary>
        private Language _savedLanguage = null!;

        /// <summary>Captures the localization state.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLanguage = PrivateMember.GetStaticField<Language>(typeof(Localization), "_current");
            PrivateMember.SetStaticField(typeof(Localization), "_current", Language.Default);
        }

        /// <summary>Restores the localization state.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(Localization), "_current", _savedLanguage);
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

        /// <summary>Checks the folded text of integer values across the magnitudes.</summary>
        /// <param name="value">The value to fold.</param>
        /// <param name="expected">The expected folded text.</param>
        [TestCase(1000L, "1k")]
        [TestCase(1500L, "1.5k")]
        [TestCase(1234L, "1.23k")]
        [TestCase(999999L, "999.99k")]
        [TestCase(1000000L, "1M")]
        [TestCase(1234567L, "1.23M")]
        [TestCase(1000000000L, "1G")]
        [TestCase(-2500L, "-2.5k")]
        [TestCase(long.MinValue, "-9.22E")]
        public void TryFold_FoldsLargeIntegers(long value, string expected)
        {
            Assert.That(SettingNumberFolding.TryFold(value, out var foldedText), Is.True);
            Assert.That(foldedText, Is.EqualTo(expected));
        }

        /// <summary>Checks that every integer and floating-point type is folded.</summary>
        [Test]
        public void TryFold_FoldsEveryNumericType()
        {
            var values = new object[]
            {
                (short)1000, (ushort)1000, 1000, 1000u, 1000L, 1000UL, 1000f, 1000d, 1000m
            };

            foreach (var value in values)
            {
                Assert.That(SettingNumberFolding.TryFold(value, out var foldedText), Is.True, value.GetType().Name);
                Assert.That(foldedText, Is.EqualTo("1k"), value.GetType().Name);
            }
            Assert.That(SettingNumberFolding.TryFold(ulong.MaxValue, out var maxText), Is.True);
            Assert.That(maxText, Is.EqualTo("18.44E"));
            Assert.That(SettingNumberFolding.TryFold(decimal.MaxValue, out var decimalText), Is.True);
            Assert.That(decimalText, Is.EqualTo("79228.16Y"));
            Assert.That(SettingNumberFolding.TryFold(1500.75f, out var floatText), Is.True);
            Assert.That(floatText, Is.EqualTo("1.5k"));
        }

        /// <summary>Checks that small numbers, non-finite or oversized floats and non-numeric values are not folded.</summary>
        [Test]
        public void TryFold_LeavesOtherValuesAlone()
        {
            var values = new object[]
            {
                999, -999, 999.999m, 0.001d, (sbyte)127, (byte)255, double.NaN, double.PositiveInfinity, float.NegativeInfinity,
                1e30d, "1000", (MirrorOption)1000, true
            };

            foreach (var value in values)
            {
                Assert.That(SettingNumberFolding.TryFold(value, out var foldedText), Is.False, value.ToString());
                Assert.That(foldedText, Is.Empty, value.ToString());
            }
        }

        /// <summary>Checks that a marked number option shows its folded value while the raw value text stays unfolded.</summary>
        [Test]
        public void NumberEnumerator_FoldsDisplayedValue()
        {
            var probe = new FoldProbe();
            var folded = Create<DefaultNumberEnumerator>(probe, nameof(FoldProbe.Folded));
            var unfolded = Create<DefaultNumberEnumerator>(probe, nameof(FoldProbe.Unfolded));
            var foldedDouble = Create<DefaultNumberEnumerator>(probe, nameof(FoldProbe.FoldedDouble));

            Assert.That(folded.ValueText, Is.EqualTo("1000"));
            Assert.That(folded.LocalizedValueText, Is.EqualTo("1k"));
            Assert.That(unfolded.LocalizedValueText, Is.EqualTo("1000"));
            Assert.That(foldedDouble.LocalizedValueText, Is.EqualTo("2.5M"));

            Assert.That(folded.MoveNext(), Is.True);
            Assert.That(probe.Folded, Is.EqualTo(1500));
            Assert.That(folded.LocalizedValueText, Is.EqualTo("1.5k"));

            probe.Folded = 999;
            folded.Refresh();
            Assert.That(folded.LocalizedValueText, Is.EqualTo("999"));

            probe.Folded = 3000000;
            folded.Refresh();
            folded.RefreshLocalization();
            Assert.That(folded.LocalizedValueText, Is.EqualTo("3M"));
        }

        /// <summary>Checks that translations are looked up with the unfolded text and win over the folded text.</summary>
        [Test]
        public void NumberEnumerator_TranslationWinsOverFolding()
        {
            UseLanguage(("MAJSETTING_PROPERTY_Selected_OPTION_1000", "one thousand"));
            var probe = new FoldProbe();
            var selected = Create<DefaultNumberEnumerator>(probe, nameof(FoldProbe.Selected));

            Assert.That(selected.LocalizedValueText, Is.EqualTo("one thousand"));

            probe.Selected = 2000;
            selected.Refresh();

            Assert.That(selected.LocalizedValueText, Is.EqualTo("2k"));
        }

        /// <summary>Checks that read-only nullable numbers are folded while their missing value keeps its placeholder.</summary>
        [Test]
        public void ReadOnlyEnumerator_FoldsNullableNumbers()
        {
            var probe = new FoldProbe();
            var missing = Create<DefaultReadOnlyEnumerator>(probe, nameof(FoldProbe.MissingNumber));
            var present = Create<DefaultReadOnlyEnumerator>(probe, nameof(FoldProbe.NullableValue));

            Assert.That(missing.LocalizedValueText, Is.EqualTo("NULL"));
            Assert.That(present.ValueText, Is.EqualTo("2000"));
            Assert.That(present.LocalizedValueText, Is.EqualTo("2k"));
        }

        /// <summary>Checks that the attribute is ignored on text and enum options.</summary>
        [Test]
        public void ReadOnlyEnumerator_IgnoresNonNumericOptions()
        {
            var probe = new FoldProbe();
            var text = Create<DefaultReadOnlyEnumerator>(probe, nameof(FoldProbe.Text));
            var mirror = Create<DefaultReadOnlyEnumerator>(probe, nameof(FoldProbe.Mirror));

            Assert.That(text.LocalizedValueText, Is.EqualTo("1000"));
            Assert.That(mirror.LocalizedValueText, Is.EqualTo("1000"));
        }
    }
}
