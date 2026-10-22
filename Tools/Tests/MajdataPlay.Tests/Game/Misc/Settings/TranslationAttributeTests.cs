using System;
using System.Linq;
using System.Reflection;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers the setting translation attributes <see cref="UseTranslationAttribute"/>,
    /// <see cref="UseGeneralTranslationAttribute"/> and <see cref="NoTranslationAttribute"/>, and how
    /// <see cref="SettingReflectionCache"/> exposes them.
    /// </summary>
    public class TranslationAttributeTests
    {
        /// <summary>A settings section whose options carry translation attributes.</summary>
        public sealed class TranslatedProbe
        {
            /// <summary>Gets or sets an option with a custom key for its name and the general table for its values.</summary>
            [UseTranslation(TranslationTarget.OptionName, "CUSTOM_KEY")]
            [UseTranslation(TranslationTarget.MenuName)]
            [UseGeneralTranslation(TranslationTarget.OptionValue)]
            public MirrorOption Translated { get; set; }

            /// <summary>Gets or sets an option whose name and values are never translated.</summary>
            [NoTranslation(TranslationTarget.OptionName)]
            [NoTranslation(TranslationTarget.OptionValue)]
            public bool Untranslated { get; set; }

            /// <summary>A field carrying a translation attribute.</summary>
            [UseGeneralTranslation(TranslationTarget.OptionName)]
            public int Field;
        }

        /// <summary>Gets the attribute usage declared on an attribute type.</summary>
        /// <param name="type">The attribute type.</param>
        /// <returns>The attribute usage.</returns>
        private static AttributeUsageAttribute Usage(Type type)
        {
            return type.GetCustomAttribute<AttributeUsageAttribute>()!;
        }

        /// <summary>Checks the translation targets in declaration order.</summary>
        [Test]
        public void TranslationTarget_ListsTargets()
        {
            Assert.That(Enum.GetNames(typeof(TranslationTarget)), Is.EqualTo(new[] { "MenuName", "OptionName", "OptionValue" }));
        }

        /// <summary>Checks that a custom translation keeps its target and treats an empty key as no key.</summary>
        [Test]
        public void UseTranslation_StoresTargetAndKey()
        {
            var withKey = new UseTranslationAttribute(TranslationTarget.OptionValue, "KEY");
            var withoutKey = new UseTranslationAttribute(TranslationTarget.MenuName);
            var emptyKey = new UseTranslationAttribute(TranslationTarget.OptionName, string.Empty);
            var nullKey = new UseTranslationAttribute(TranslationTarget.OptionName, null!);

            Assert.That(withKey.Target, Is.EqualTo(TranslationTarget.OptionValue));
            Assert.That(withKey.Key, Is.EqualTo("KEY"));
            Assert.That(withoutKey.Target, Is.EqualTo(TranslationTarget.MenuName));
            Assert.That(withoutKey.Key, Is.Null);
            Assert.That(emptyKey.Key, Is.Null);
            Assert.That(nullKey.Key, Is.Null);
        }

        /// <summary>Checks that the general translation and opt-out attributes keep their target.</summary>
        /// <param name="target">The translation target.</param>
        [TestCase(TranslationTarget.MenuName)]
        [TestCase(TranslationTarget.OptionName)]
        [TestCase(TranslationTarget.OptionValue)]
        public void GeneralAndNoTranslation_StoreTarget(TranslationTarget target)
        {
            Assert.That(new UseGeneralTranslationAttribute(target).Target, Is.EqualTo(target));
            Assert.That(new NoTranslationAttribute(target).Target, Is.EqualTo(target));
        }

        /// <summary>Checks that every translation attribute may be repeated on properties and fields and is not inherited.</summary>
        /// <param name="type">The attribute type.</param>
        [TestCase(typeof(UseTranslationAttribute))]
        [TestCase(typeof(UseGeneralTranslationAttribute))]
        [TestCase(typeof(NoTranslationAttribute))]
        public void TranslationAttributes_AreRepeatable(Type type)
        {
            var usage = Usage(type);

            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Property | AttributeTargets.Field));
            Assert.That(usage.AllowMultiple, Is.True);
            Assert.That(usage.Inherited, Is.False);
        }

        /// <summary>Checks that the other option attributes still allow a single use on properties and fields.</summary>
        [Test]
        public void OptionAttributes_AreSingleUse()
        {
            var types = new[]
            {
                typeof(DefaultValueAttribute), typeof(global::MajdataPlay.Settings.DescriptionAttribute), typeof(MenuNameAttribute),
                typeof(NoDescriptionAttribute), typeof(OptionEnumeratorAttribute), typeof(OptionNameAttribute), typeof(OptionalAttribute),
                typeof(global::MajdataPlay.Settings.RangeAttribute), typeof(ReadOnlyOptionAttribute), typeof(StepAttribute)
            };

            foreach (var type in types)
            {
                var usage = Usage(type);
                Assert.That(usage.AllowMultiple, Is.False, type.Name);
                Assert.That(usage.Inherited, Is.False, type.Name);
                Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Property | AttributeTargets.Field), type.Name);
            }
            Assert.That(Usage(typeof(HideInSettingUIAttribute)).ValidOn, Is.EqualTo(AttributeTargets.Property));
            Assert.That(typeof(DefaultValueAttribute).IsSealed, Is.True);
            Assert.That(typeof(StepAttribute).IsSealed, Is.True);
            Assert.That(typeof(global::MajdataPlay.Settings.RangeAttribute).IsSealed, Is.True);
        }

        /// <summary>Checks that the reflection cache returns every repeated translation attribute of an option.</summary>
        [Test]
        public void ReflectionCache_ReturnsRepeatedTranslationAttributes()
        {
            var translated = SettingReflectionCache.GetProperty(typeof(TranslatedProbe), nameof(TranslatedProbe.Translated));
            var untranslated = SettingReflectionCache.GetProperty(typeof(TranslatedProbe), nameof(TranslatedProbe.Untranslated));

            var translatedAttributes = SettingReflectionCache.GetAttributes(translated);
            var untranslatedAttributes = SettingReflectionCache.GetAttributes(untranslated);
            var fieldAttributes = SettingReflectionCache.GetAttributes(typeof(TranslatedProbe).GetField(nameof(TranslatedProbe.Field))!);

            var custom = translatedAttributes.OfType<UseTranslationAttribute>().ToArray();
            Assert.That(custom.Select(attribute => attribute.Target), Is.EquivalentTo(new[] { TranslationTarget.OptionName, TranslationTarget.MenuName }));
            Assert.That(custom.Single(attribute => attribute.Target == TranslationTarget.OptionName).Key, Is.EqualTo("CUSTOM_KEY"));
            Assert.That(translatedAttributes.OfType<UseGeneralTranslationAttribute>().Single().Target, Is.EqualTo(TranslationTarget.OptionValue));
            Assert.That(untranslatedAttributes.OfType<NoTranslationAttribute>().Select(attribute => attribute.Target),
                        Is.EquivalentTo(new[] { TranslationTarget.OptionName, TranslationTarget.OptionValue }));
            Assert.That(fieldAttributes.OfType<UseGeneralTranslationAttribute>().Single().Target, Is.EqualTo(TranslationTarget.OptionName));
            Assert.That(SettingReflectionCache.GetAttributes(translated), Is.SameAs(translatedAttributes));
        }

        /// <summary>Checks that translation attributes leave the option metadata, visibility and enumerator choice unchanged.</summary>
        [Test]
        public void ReflectionCache_MetadataIgnoresTranslationAttributes()
        {
            var visible = SettingReflectionCache.GetVisibleProperties(typeof(TranslatedProbe)).Select(property => property.Name).ToArray();
            var translated = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(TranslatedProbe), nameof(TranslatedProbe.Translated)));
            var untranslated = SettingReflectionCache.GetOption(SettingReflectionCache.GetProperty(typeof(TranslatedProbe), nameof(TranslatedProbe.Untranslated)));

            Assert.That(visible, Is.EquivalentTo(new[] { nameof(TranslatedProbe.Translated), nameof(TranslatedProbe.Untranslated) }));
            Assert.That(translated.NameKey, Is.EqualTo("MAJSETTING_PROPERTY_Translated"));
            Assert.That(translated.DescriptionKey, Is.EqualTo("MAJSETTING_PROPERTY_Translated_DESC"));
            Assert.That(translated.HasDescription, Is.True);
            Assert.That(translated.CreateEnumerator(), Is.InstanceOf<DefaultEnumEnumerator>());
            Assert.That(untranslated.CreateEnumerator(), Is.InstanceOf<DefaultBooleanEnumerator>());
        }
    }
}
