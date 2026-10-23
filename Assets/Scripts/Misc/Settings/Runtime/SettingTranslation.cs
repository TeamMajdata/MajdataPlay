using MajdataPlay.Diagnostics;
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
    /// in order and the first key that has a translation wins. Instances are immutable and may be shared.
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
        /// Resolves the localization rule of one target from the translation attributes of a setting member.
        /// </summary>
        /// <remarks>
        /// <see cref="NoTranslationAttribute"/> wins over the other attributes. Otherwise the keys of every
        /// <see cref="UseTranslationAttribute"/> are tried first, then those of every
        /// <see cref="UseGeneralTranslationAttribute"/>; their <c>{0}</c> is the <see cref="OptionNameAttribute"/>
        /// value when present, otherwise <paramref name="memberName"/>. Without any attribute for the target the
        /// built-in default applies with <c>{0}</c> set to <paramref name="memberName"/>: the default template,
        /// followed by the general template for option values.
        /// </remarks>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="target">The part of the option to localize.</param>
        /// <param name="memberName">The declared member name; for a menu, the displayed menu name.</param>
        /// <returns>The resolved rule.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or <paramref name="memberName"/> is null.</exception>
        internal static SettingTranslation Resolve(Attribute[] attributes, TranslationTarget target, string memberName)
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
            specificTemplates.AddRange(generalTemplates);
            return new SettingTranslation(target, optionName, true, specificTemplates.ToArray());
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
            if (_templates.Length == 0)
            {
                return rawText;
            }
            string? primaryKey = null;
            for (var i = 0; i < _templates.Length; i++)
            {
                var key = string.Format(_templates[i], OptionName, rawText);
                if (key.Tryi18n(out var localizedText))
                {
                    return localizedText;
                }
                primaryKey ??= key;
            }
            return Target == TranslationTarget.OptionValue ? rawText : primaryKey!;
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
