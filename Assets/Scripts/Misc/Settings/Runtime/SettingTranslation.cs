using MajdataPlay.Diagnostics;
using MajdataPlay.i18n;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Describes how one part of a setting option (its menu name, option name or option values) is localized,
    /// as selected by <see cref="UseTranslationAttribute"/>, <see cref="UseGeneralTranslationAttribute"/> and
    /// <see cref="NoTranslationAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Localization keys are built from key templates: <c>{0}</c> is replaced by <see cref="OptionName"/> and,
    /// for <see cref="TranslationTarget.OptionValue"/>, <c>{1}</c> by the value text. The templates are tried
    /// in order and the first key that has a translation wins. Boolean option values always end with the general
    /// template <see cref="GeneralOptionValueTemplate"/> after their option-specific templates. Instances are immutable
    /// and may be shared.
    /// </remarks>
    internal sealed class SettingTranslation
    {
        /// <summary>
        /// The default key template of a menu title.
        /// </summary>
        internal const string DefaultMenuNameTemplate = "MAJSETTING_CATEGORY_{0}";
        /// <summary>
        /// The default key template of an option name.
        /// </summary>
        internal const string DefaultOptionNameTemplate = "MAJSETTING_PROPERTY_{0}";
        /// <summary>
        /// The default key template of an option value.
        /// </summary>
        internal const string DefaultOptionValueTemplate = "MAJSETTING_PROPERTY_{0}_OPTION_{1}";
        /// <summary>
        /// The general table key template of a menu title.
        /// </summary>
        internal const string GeneralMenuNameTemplate = "MAJSETTING_GENERAL_CATEGORY_{0}";
        /// <summary>
        /// The general table key template of an option name.
        /// </summary>
        internal const string GeneralOptionNameTemplate = "MAJSETTING_GENERAL_PROPERTY_{0}";
        /// <summary>
        /// The general table key template of an option value.
        /// </summary>
        internal const string GeneralOptionValueTemplate = "MAJSETTING_GENERAL_OPTION_{1}";

        /// <summary>
        /// Gets the part of the option this rule localizes.
        /// </summary>
        internal TranslationTarget Target { get; }
        /// <summary>
        /// Gets the option name substituted for <c>{0}</c> in the key templates.
        /// </summary>
        internal string OptionName { get; }
        /// <summary>
        /// Gets whether a translation attribute selected this rule, as opposed to the built-in default.
        /// </summary>
        internal bool IsExplicit { get; }
        /// <summary>
        /// Gets whether the text is looked up at all; false when <see cref="NoTranslationAttribute"/> applies.
        /// </summary>
        internal bool IsTranslated => _templates.Length != 0;
        /// <summary>
        /// Gets the key templates in lookup order.
        /// </summary>
        internal IReadOnlyList<string> KeyTemplates => _templates;

        private readonly string[] _templates;

        private SettingTranslation(TranslationTarget target, string optionName, bool isExplicit, string[] templates)
        {
            Target = target;
            OptionName = optionName;
            IsExplicit = isExplicit;
            _templates = templates;
        }

        /// <summary>
        /// Gets the option name used for <c>{0}</c> in explicitly selected templates: the <see cref="OptionNameAttribute"/>
        /// value when one is present, otherwise the member name.
        /// </summary>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="memberName">The declared member name.</param>
        /// <returns>The option name.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or <paramref name="memberName"/> is null.</exception>
        internal static string GetOptionName(Attribute[] attributes, string memberName)
        {
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }
            if (memberName is null)
            {
                throw new ArgumentNullException(nameof(memberName));
            }
            foreach (var attribute in attributes)
            {
                if (attribute is OptionNameAttribute optionName && !string.IsNullOrEmpty(optionName.Name))
                {
                    return optionName.Name;
                }
            }
            return memberName;
        }

        /// <summary>
        /// Gets the rule applied to option values that carry no translation attribute.
        /// </summary>
        internal static SettingTranslation DefaultOptionValue { get; } = Resolve(Array.Empty<Attribute>(), TranslationTarget.OptionValue, string.Empty);

        /// <summary>
        /// Resolves the localization rule of one target from the translation attributes of a setting member
        /// whose value type is unknown, so no type-specific general fallback is added.
        /// </summary>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="target">The part of the option to localize.</param>
        /// <param name="memberName">The declared member name; for a menu, the displayed menu name.</param>
        /// <returns>The resolved rule.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or <paramref name="memberName"/> is null.</exception>
        internal static SettingTranslation Resolve(Attribute[] attributes, TranslationTarget target, string memberName)
        {
            return Resolve(attributes, target, memberName, null);
        }

        /// <summary>
        /// Resolves the localization rule of one target from the translation attributes of a setting member.
        /// </summary>
        /// <remarks>
        /// <see cref="NoTranslationAttribute"/> wins over the other attributes. Otherwise the keys of every
        /// <see cref="UseTranslationAttribute"/> are tried first, then those of every
        /// <see cref="UseGeneralTranslationAttribute"/>; their <c>{0}</c> is the <see cref="OptionNameAttribute"/>
        /// value when present, otherwise <paramref name="memberName"/>. For option values of a boolean
        /// <paramref name="valueType"/>, the general template <c>MAJSETTING_GENERAL_OPTION_{1}</c> always follows the
        /// option-specific templates, even without <see cref="UseGeneralTranslationAttribute"/>; numeric and enum values
        /// use exactly the templates of their attributes. Without any attribute for the target the built-in default
        /// applies with <c>{0}</c> set to <paramref name="memberName"/>: the default template, followed by the general
        /// template for option values. Numeric option values are not looked up at all without an attribute, which the
        /// option enumerators enforce.
        /// </remarks>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="target">The part of the option to localize.</param>
        /// <param name="memberName">The declared member name; for a menu, the displayed menu name.</param>
        /// <param name="valueType">The type of the option value, possibly nullable, or null when it is unknown or irrelevant.</param>
        /// <returns>The resolved rule.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or <paramref name="memberName"/> is null.</exception>
        internal static SettingTranslation Resolve(Attribute[] attributes, TranslationTarget target, string memberName, Type? valueType)
        {
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }
            if (memberName is null)
            {
                throw new ArgumentNullException(nameof(memberName));
            }

            var optionName = GetOptionName(attributes, memberName);
            var isExplicit = false;
            var specificTemplates = new List<string>();
            var generalTemplates = new List<string>();
            foreach (var attribute in attributes)
            {
                switch (attribute)
                {
                    case NoTranslationAttribute noTranslation when noTranslation.Target == target:
                        return new SettingTranslation(target, optionName, true, Array.Empty<string>());
                    case UseTranslationAttribute useTranslation when useTranslation.Target == target:
                        isExplicit = true;
                        AddTemplate(specificTemplates, useTranslation.Key ?? GetDefaultTemplate(target), optionName);
                        break;
                    case UseGeneralTranslationAttribute useGeneral when useGeneral.Target == target:
                        isExplicit = true;
                        AddTemplate(generalTemplates, GetGeneralTemplate(target), optionName);
                        break;
                }
            }

            if (!isExplicit)
            {
                specificTemplates.Add(GetDefaultTemplate(target));
                if (target == TranslationTarget.OptionValue)
                {
                    specificTemplates.Add(GeneralOptionValueTemplate);
                }
                return new SettingTranslation(target, memberName, false, specificTemplates.ToArray());
            }
            if (target == TranslationTarget.OptionValue && specificTemplates.Count != 0 && HasGeneralValueFallback(valueType))
            {
                AddTemplate(generalTemplates, GeneralOptionValueTemplate, optionName);
            }
            specificTemplates.AddRange(generalTemplates);
            return new SettingTranslation(target, optionName, true, specificTemplates.ToArray());
        }

        /// <summary>
        /// Gets whether option values of a type always fall back to the general table after their option-specific keys.
        /// </summary>
        /// <remarks>
        /// Boolean values share their spelling across options (<c>True</c>, <c>False</c>), so a missing option-specific
        /// translation is completed by <c>MAJSETTING_GENERAL_OPTION_{1}</c>. Numeric values are only translated as their
        /// attributes request, and enums are not numeric, so neither falls back automatically.
        /// </remarks>
        /// <param name="valueType">The option value type, possibly nullable, or null when it is unknown.</param>
        /// <returns>True for <see cref="bool"/> and <see cref="Nullable{T}"/> of <see cref="bool"/>; otherwise false.</returns>
        internal static bool HasGeneralValueFallback(Type? valueType)
        {
            if (valueType is null)
            {
                return false;
            }
            return (Nullable.GetUnderlyingType(valueType) ?? valueType) == typeof(bool);
        }

        /// <summary>
        /// Creates a rule that looks up one complete localization key, which is used verbatim.
        /// </summary>
        /// <param name="target">The part of the option to localize.</param>
        /// <param name="optionName">The option name reported by <see cref="OptionName"/>.</param>
        /// <param name="key">The complete localization key.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static SettingTranslation Literal(TranslationTarget target, string optionName, string key)
        {
            if (optionName is null)
            {
                throw new ArgumentNullException(nameof(optionName));
            }
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            var template = key.Replace("{", "{{").Replace("}", "}}");
            return new SettingTranslation(target, optionName, false, new[] { template });
        }

        /// <summary>
        /// Builds the localization key of one template.
        /// </summary>
        /// <param name="templateIndex">The index into <see cref="KeyTemplates"/>.</param>
        /// <param name="valueText">The value text substituted for <c>{1}</c>.</param>
        /// <returns>The localization key.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="templateIndex"/> is outside <see cref="KeyTemplates"/>.</exception>
        internal string GetKey(int templateIndex, string valueText)
        {
            if (templateIndex < 0 || templateIndex >= _templates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(templateIndex));
            }
            return string.Format(_templates[templateIndex], OptionName, valueText);
        }

        /// <summary>
        /// Localizes a text by trying the key templates in order.
        /// </summary>
        /// <param name="rawText">The untranslated text: the value text of an option value, or the displayed name otherwise.</param>
        /// <returns>
        /// The first translation found. Without one, an option value falls back to <paramref name="rawText"/> while a
        /// name falls back to its primary key so that a missing translation stays visible. An untranslated rule
        /// returns <paramref name="rawText"/> unchanged.
        /// </returns>
        internal string Localize(string rawText)
        {
            return Localize(rawText, false);
        }

        /// <summary>
        /// Localizes a text by trying the key templates in order.
        /// </summary>
        /// <remarks>
        /// The individual lookups are silent, because a miss on an earlier template is expected whenever a later one
        /// provides the text. Only when every template misses is the primary key reported, together with the fallback
        /// keys, once per language through <see cref="Localization.ReportMissingTranslation"/>.
        /// </remarks>
        /// <param name="rawText">The untranslated text: the value text of an option value, or the displayed name otherwise.</param>
        /// <param name="isNumericValue">
        /// True when <paramref name="rawText"/> is the text of a numeric value. Numeric options usually translate only a
        /// few special values, so their misses are reported at debug level instead of as warnings.
        /// </param>
        /// <returns>
        /// The first translation found. Without one, an option value falls back to <paramref name="rawText"/> while a
        /// name falls back to its primary key so that a missing translation stays visible. An untranslated rule
        /// returns <paramref name="rawText"/> unchanged.
        /// </returns>
        internal string Localize(string rawText, bool isNumericValue)
        {
            if (_templates.Length == 0)
            {
                return rawText;
            }
            var primaryKey = string.Empty;
            for (var i = 0; i < _templates.Length; i++)
            {
                var key = string.Format(_templates[i], OptionName, rawText);
                if (Localization.TryFindLocalizedText(key, out var localizedText))
                {
                    return localizedText;
                }
                if (i == 0)
                {
                    primaryKey = key;
                }
            }
            Localization.ReportMissingTranslation(primaryKey, GetFallbackKeys(rawText), isNumericValue);
            return Target == TranslationTarget.OptionValue ? rawText : primaryKey;
        }

        /// <summary>
        /// Builds the fallback keys that follow the primary key, for a missing-translation report.
        /// </summary>
        /// <param name="rawText">The value text substituted for <c>{1}</c>.</param>
        /// <returns>The comma-separated fallback keys, or null when the rule has a single template.</returns>
        private string? GetFallbackKeys(string rawText)
        {
            if (_templates.Length < 2)
            {
                return null;
            }
            var fallbackKeys = new string[_templates.Length - 1];
            for (var i = 1; i < _templates.Length; i++)
            {
                fallbackKeys[i - 1] = string.Format(_templates[i], OptionName, rawText);
            }
            return string.Join(", ", fallbackKeys);
        }

        /// <summary>
        /// Gets the built-in key template of a target.
        /// </summary>
        /// <param name="target">The part of the option.</param>
        /// <returns>The template.</returns>
        internal static string GetDefaultTemplate(TranslationTarget target)
        {
            return target switch
            {
                TranslationTarget.MenuName => DefaultMenuNameTemplate,
                TranslationTarget.OptionValue => DefaultOptionValueTemplate,
                _ => DefaultOptionNameTemplate,
            };
        }

        /// <summary>
        /// Gets the general table key template of a target.
        /// </summary>
        /// <param name="target">The part of the option.</param>
        /// <returns>The template.</returns>
        internal static string GetGeneralTemplate(TranslationTarget target)
        {
            return target switch
            {
                TranslationTarget.MenuName => GeneralMenuNameTemplate,
                TranslationTarget.OptionValue => GeneralOptionValueTemplate,
                _ => GeneralOptionNameTemplate,
            };
        }

        /// <summary>
        /// Adds a template after checking that it can be formatted, so that a malformed attribute key is reported once instead of on every lookup.
        /// </summary>
        /// <param name="templates">The template list to extend.</param>
        /// <param name="template">The key template.</param>
        /// <param name="optionName">The option name the template is reported for.</param>
        private static void AddTemplate(List<string> templates, string template, string optionName)
        {
            if (templates.Contains(template))
            {
                return;
            }
            try
            {
                string.Format(template, optionName, string.Empty);
            }
            catch (FormatException e)
            {
                MajDebug.LogError(nameof(SettingTranslation), $"[Translation] Invalid localization key template \"{template}\" on option {optionName}: {e.Message}");
                return;
            }
            templates.Add(template);
        }
    }
}
