using System;
using System.Linq;
using System.Reflection;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers the metadata cache <see cref="SettingReflectionCache"/> and <see cref="SettingOptionMetadata"/>.</summary>
    public class SettingReflectionCacheTests
    {
        /// <summary>A settings section whose properties exercise every metadata rule.</summary>
        public sealed class OptionProbe
        {
            /// <summary>Gets or sets an option with custom localization keys.</summary>
            [OptionName("CUSTOM_NAME")]
            [global::MajdataPlay.Settings.Description("CUSTOM_DESC")]
            public int Named { get; set; }

            /// <summary>Gets or sets an option without a description.</summary>
            [NoDescription]
            public bool Quiet { get; set; }

            /// <summary>Gets or sets an option hidden from the setting UI.</summary>
            [HideInSettingUI]
            public int Hidden { get; set; }

            /// <summary>Gets or sets an enum option.</summary>
            public MirrorOption Mirror { get; set; }

            /// <summary>Gets or sets a nullable boolean option.</summary>
            public bool? MaybeFlag { get; set; }

            /// <summary>Gets or sets a floating point option.</summary>
            public float Ratio { get; set; }

            /// <summary>Gets or sets a text option, which has no editable enumerator.</summary>
            public string Text { get; set; } = string.Empty;

            /// <summary>Gets or sets an option recognized as an offset by name.</summary>
            public float AudioOffset { get; set; }

            /// <summary>Gets or sets an option whose custom enumerator cannot be created.</summary>
            [OptionEnumerator(typeof(ThrowingEnumerator))]
            public int Broken { get; set; }

            /// <summary>Gets or sets an option with a custom enumerator.</summary>
            [OptionEnumerator(typeof(NoteMaskEnumerator))]
            public string Mask { get; set; } = "Disable";
        }

        /// <summary>A base class whose property is hidden by a derived property of another type.</summary>
        public class AmbiguousBase
        {
            /// <summary>Gets or sets the hidden value.</summary>
            public int Value { get; set; }
        }

        /// <summary>A derived class that hides <see cref="AmbiguousBase.Value"/> with a different type.</summary>
        public sealed class AmbiguousDerived : AmbiguousBase
        {
            /// <summary>Gets or sets the hiding value.</summary>
            public new string Value { get; set; } = string.Empty;
        }

        /// <summary>A minimal option enumerator whose members are never used by these tests.</summary>
        public abstract class StubEnumerator : IOptionEnumerator
        {
            /// <summary>Gets an empty name.</summary>
            public string Name => string.Empty;

            /// <summary>Gets no current value.</summary>
            public object? Current => null;

            /// <summary>Gets an empty value text.</summary>
            public string ValueText => string.Empty;

            /// <summary>Gets an empty localized value text.</summary>
            public string LocalizedValueText => string.Empty;

            /// <summary>Ignores field initialization.</summary>
            /// <param name="fieldInfo">The ignored field.</param>
            /// <param name="field">The ignored owner.</param>
            public void Init(FieldInfo fieldInfo, object field)
            {
            }

            /// <summary>Ignores property initialization.</summary>
            /// <param name="propertyInfo">The ignored property.</param>
            /// <param name="property">The ignored owner.</param>
            public void Init(PropertyInfo propertyInfo, object property)
            {
            }

            /// <summary>Never moves.</summary>
            /// <returns>Always false.</returns>
            public bool MoveNext()
            {
                return false;
            }

            /// <summary>Never moves.</summary>
            /// <returns>Always false.</returns>
            public bool MovePrevious()
            {
                return false;
            }

            /// <summary>Does nothing.</summary>
            public void Refresh()
            {
            }

            /// <summary>Does nothing.</summary>
            public void RefreshLocalization()
            {
            }

            /// <summary>Does nothing.</summary>
            public void Dispose()
            {
            }
        }

        /// <summary>An enumerator whose constructor always fails.</summary>
        public sealed class ThrowingEnumerator : StubEnumerator
        {
            /// <summary>Always throws.</summary>
            /// <exception cref="InvalidOperationException">Always thrown.</exception>
            public ThrowingEnumerator()
            {
                throw new InvalidOperationException("broken enumerator");
            }
        }

        /// <summary>An enumerator without a parameterless constructor.</summary>
        public sealed class ParameterizedEnumerator : StubEnumerator
        {
            /// <summary>Creates the enumerator.</summary>
            /// <param name="value">An ignored value.</param>
            public ParameterizedEnumerator(int value)
            {
            }
        }

        /// <summary>A value type enumerator, which has no declared parameterless constructor.</summary>
        public struct StructEnumerator : IOptionEnumerator
        {
            /// <summary>Gets an empty name.</summary>
            public string Name => string.Empty;

            /// <summary>Gets no current value.</summary>
            public object? Current => null;

            /// <summary>Gets an empty value text.</summary>
            public string ValueText => string.Empty;

            /// <summary>Gets an empty localized value text.</summary>
            public string LocalizedValueText => string.Empty;

            /// <summary>Ignores field initialization.</summary>
            /// <param name="fieldInfo">The ignored field.</param>
            /// <param name="field">The ignored owner.</param>
            public void Init(FieldInfo fieldInfo, object field)
            {
            }

            /// <summary>Ignores property initialization.</summary>
            /// <param name="propertyInfo">The ignored property.</param>
            /// <param name="property">The ignored owner.</param>
            public void Init(PropertyInfo propertyInfo, object property)
            {
            }

            /// <summary>Never moves.</summary>
            /// <returns>Always false.</returns>
            public bool MoveNext()
            {
                return false;
            }

            /// <summary>Never moves.</summary>
            /// <returns>Always false.</returns>
            public bool MovePrevious()
            {
                return false;
            }

            /// <summary>Does nothing.</summary>
            public void Refresh()
            {
            }

            /// <summary>Does nothing.</summary>
            public void RefreshLocalization()
            {
            }

            /// <summary>Does nothing.</summary>
            public void Dispose()
            {
            }
        }

        /// <summary>Gets the metadata of one probe property.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The cached metadata.</returns>
        private static SettingOptionMetadata Option(string name)
        {
            return SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(OptionProbe), name));
        }

        /// <summary>Checks that hidden properties are excluded and the visible list is cached.</summary>
        [Test]
        public void GetVisibleProperties_ExcludesHiddenOptions()
        {
            var visible = SettingReflectionCache.GetVisibleProperties(typeof(OptionProbe));
            var chartVisible = SettingReflectionCache.GetVisibleProperties(typeof(ChartSetting)).Select(property => property.Name).ToArray();

            Assert.That(visible.Select(property => property.Name), Does.Not.Contain(nameof(OptionProbe.Hidden)));
            Assert.That(visible.Select(property => property.Name), Does.Contain(nameof(OptionProbe.Named)));
            Assert.That(SettingReflectionCache.GetVisibleProperties(typeof(OptionProbe)), Is.SameAs(visible));
            Assert.That(chartVisible, Does.Not.Contain(nameof(ChartSetting.Hash)));
            Assert.That(chartVisible, Does.Not.Contain(nameof(ChartSetting.Unit)));
            Assert.That(chartVisible, Does.Contain(nameof(ChartSetting.AudioOffset)));
        }

        /// <summary>Checks property lookup, including missing and ambiguous names.</summary>
        [Test]
        public void GetProperty_FindsUniqueProperty()
        {
            Assert.That(SettingReflectionCache.GetProperty(typeof(GameOptions), nameof(GameOptions.TapSpeed)).Name, Is.EqualTo(nameof(GameOptions.TapSpeed)));
            Assert.Throws<MissingMemberException>(() => SettingReflectionCache.GetProperty(typeof(GameOptions), "Missing"));
            Assert.Throws<AmbiguousMatchException>(() => SettingReflectionCache.GetProperty(typeof(AmbiguousDerived), "Value"));
        }

        /// <summary>Checks the default and customized localization keys of an option.</summary>
        [Test]
        public void GetOption_ReadsLocalizationAttributes()
        {
            var named = Option(nameof(OptionProbe.Named));
            var quiet = Option(nameof(OptionProbe.Quiet));
            var ratio = Option(nameof(OptionProbe.Ratio));

            Assert.That(named.NameKey, Is.EqualTo("CUSTOM_NAME"));
            Assert.That(named.DescriptionKey, Is.EqualTo("CUSTOM_DESC"));
            Assert.That(named.HasDescription, Is.True);
            Assert.That(quiet.HasDescription, Is.False);
            Assert.That(ratio.NameKey, Is.EqualTo("MAJSETTING_PROPERTY_Ratio"));
            Assert.That(ratio.DescriptionKey, Is.EqualTo("MAJSETTING_PROPERTY_Ratio_DESC"));
            Assert.That(ratio.PropertyInfo.Name, Is.EqualTo(nameof(OptionProbe.Ratio)));
            Assert.That(Option(nameof(OptionProbe.Ratio)), Is.SameAs(ratio));
        }

        /// <summary>Checks that offset options are recognized by name.</summary>
        [Test]
        public void GetOption_DetectsOffsetOptions()
        {
            Assert.That(Option(nameof(OptionProbe.AudioOffset)).IsOffsetOption, Is.True);
            Assert.That(Option(nameof(OptionProbe.Ratio)).IsOffsetOption, Is.False);
            var displayOffset = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(DebugOptions), nameof(DebugOptions.DisplayOffset)));
            Assert.That(displayOffset.IsOffsetOption, Is.True);
        }

        /// <summary>Checks the enumerator kind created for each option type.</summary>
        [Test]
        public void CreateEnumerator_MatchesOptionType()
        {
            Assert.That(Option(nameof(OptionProbe.Mirror)).CreateEnumerator(), Is.InstanceOf<DefaultEnumEnumerator>());
            Assert.That(Option(nameof(OptionProbe.Quiet)).CreateEnumerator(), Is.InstanceOf<DefaultBooleanEnumerator>());
            Assert.That(Option(nameof(OptionProbe.MaybeFlag)).CreateEnumerator(), Is.InstanceOf<DefaultBooleanEnumerator>());
            Assert.That(Option(nameof(OptionProbe.Ratio)).CreateEnumerator(), Is.InstanceOf<DefaultNumberEnumerator>());
            Assert.That(Option(nameof(OptionProbe.Named)).CreateEnumerator(), Is.InstanceOf<DefaultNumberEnumerator>());
            Assert.That(Option(nameof(OptionProbe.Text)).CreateEnumerator(), Is.InstanceOf<DefaultReadOnlyEnumerator>());
            Assert.That(Option(nameof(OptionProbe.Mask)).CreateEnumerator(), Is.InstanceOf<NoteMaskEnumerator>());
        }

        /// <summary>Checks that a failing custom enumerator falls back to a read-only enumerator.</summary>
        [Test]
        public void CreateEnumerator_FailingCustomEnumerator_FallsBack()
        {
            var enumerator = Option(nameof(OptionProbe.Broken)).CreateEnumerator();

            Assert.That(enumerator, Is.InstanceOf<DefaultReadOnlyEnumerator>());
        }

        /// <summary>Checks the factories created for value types and types without a parameterless constructor.</summary>
        [Test]
        public void GetEnumeratorFactory_HandlesSpecialTypes()
        {
            var structFactory = SettingReflectionCache.GetEnumeratorFactory(typeof(StructEnumerator));
            var parameterizedFactory = SettingReflectionCache.GetEnumeratorFactory(typeof(ParameterizedEnumerator));

            Assert.That(structFactory(), Is.InstanceOf<StructEnumerator>());
            Assert.Throws<MissingMethodException>(() => parameterizedFactory());
            Assert.That(SettingReflectionCache.GetEnumeratorFactory(typeof(StructEnumerator)), Is.SameAs(structFactory));
        }

        /// <summary>Checks that enum constants and attributes are cached.</summary>
        [Test]
        public void EnumValuesAndAttributes_AreCached()
        {
            var values = SettingReflectionCache.GetEnumValues(typeof(MirrorOption));
            var property = typeof(OptionProbe).GetProperty(nameof(OptionProbe.Named))!;
            var attributes = SettingReflectionCache.GetAttributes(property);

            Assert.That(values, Is.EqualTo(new object[] { MirrorOption.Off, MirrorOption.LRMirror, MirrorOption.UDMirror }));
            Assert.That(SettingReflectionCache.GetEnumValues(typeof(MirrorOption)), Is.SameAs(values));
            Assert.That(attributes.OfType<OptionNameAttribute>().Single().Name, Is.EqualTo("CUSTOM_NAME"));
            Assert.That(SettingReflectionCache.GetAttributes(property), Is.SameAs(attributes));
        }
    }
}
