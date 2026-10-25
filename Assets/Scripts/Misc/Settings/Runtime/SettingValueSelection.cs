using MajdataPlay.Diagnostics;
using MajdataPlay.i18n;
using System;
using System.Collections.Generic;
using System.Globalization;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Translates the option values selected by <see cref="TranslateValuesAttribute"/> while the other values stay untranslated.
    /// </summary>
    /// <remarks>
    /// The option enumerators use this only while the option values are otherwise untranslated; see
    /// <see cref="TranslateValuesAttribute"/>. Instances are immutable and may be shared.
    /// </remarks>
    internal sealed class SettingValueSelection
    {
        /// <summary>
        /// Gets the selection of an option without <see cref="TranslateValuesAttribute"/>, which translates no value.
        /// </summary>
        internal static SettingValueSelection Empty { get; } = new(string.Empty, Array.Empty<Entry>());

        /// <summary>
        /// Gets the option name substituted for <c>{0}</c> in the key templates.
        /// </summary>
        internal string OptionName { get; }

        /// <summary>
        /// Gets whether no value is selected.
        /// </summary>
        internal bool IsEmpty => _entries.Length == 0;

        /// <summary>
        /// The selected values in declaration order.
        /// </summary>
        private readonly Entry[] _entries;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingValueSelection"/> class.
        /// </summary>
        /// <param name="optionName">The option name substituted for <c>{0}</c>.</param>
        /// <param name="entries">The selected values.</param>
        private SettingValueSelection(string optionName, Entry[] entries)
        {
            OptionName = optionName;
            _entries = entries;
        }

        /// <summary>
        /// Resolves the selected values from the <see cref="TranslateValuesAttribute"/> attributes of a setting member.
        /// </summary>
        /// <remarks>
        /// A value of a numeric option that is not an invariant-culture number, and a malformed key template, are logged as
        /// errors once and dropped instead of failing every lookup.
        /// </remarks>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="memberName">The declared member name.</param>
        /// <param name="valueType">The type of the option value, possibly nullable, or null when it is unknown.</param>
        /// <returns>The selection, or <see cref="Empty"/> when the member selects no value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or <paramref name="memberName"/> is null.</exception>
        internal static SettingValueSelection Resolve(Attribute[] attributes, string memberName, Type? valueType)
        {
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }
            if (memberName is null)
            {
                throw new ArgumentNullException(nameof(memberName));
            }

            var optionName = SettingTranslation.GetOptionName(attributes, memberName);
            var underlyingType = valueType is null ? null : Nullable.GetUnderlyingType(valueType) ?? valueType;
            var isNumeric = underlyingType is not null && (underlyingType.IsIntType() || underlyingType.IsFloatType());
            var entries = new List<Entry>();
            foreach (var attribute in attributes)
            {
                if (attribute is not TranslateValuesAttribute translateValues)
                {
                    continue;
                }
                var template = string.IsNullOrEmpty(translateValues.Key) ? SettingTranslation.DefaultOptionValueTemplate : translateValues.Key!;
                if (!IsValidTemplate(template, optionName))
                {
                    continue;
                }
                foreach (var text in translateValues.Values)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }
                    decimal? number = null;
                    if (isNumeric)
                    {
                        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                        {
                            MajDebug.LogError(nameof(SettingValueSelection), $"[Translation] Value \"{text}\" of {nameof(TranslateValuesAttribute)} on option {optionName} is not an invariant-culture number.");
                            continue;
                        }
                        number = parsed;
                    }
                    entries.Add(new Entry(text, template, number));
                }
            }
            return entries.Count == 0 ? Empty : new SettingValueSelection(optionName, entries.ToArray());
        }

        /// <summary>
        /// Gets the localization key of a value when it is selected.
        /// </summary>
        /// <param name="value">The option value.</param>
        /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
        /// <param name="key">Receives the key of the first matching selected value.</param>
        /// <returns>True when the value is selected; otherwise false.</returns>
        internal bool TryGetKey(object value, string valueText, out string key)
        {
            foreach (var entry in _entries)
            {
                if (entry.Matches(value, valueText))
                {
                    key = string.Format(entry.Template, OptionName, entry.Text);
                    return true;
                }
            }
            key = string.Empty;
            return false;
        }

        /// <summary>
        /// Localizes a value when it is selected and keeps the raw text otherwise.
        /// </summary>
        /// <remarks>
        /// A selected value without a translation is reported as a missing translation, since the attribute requests it.
        /// </remarks>
        /// <param name="value">The option value.</param>
        /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
        /// <returns>The translation of a selected value, or <paramref name="valueText"/>.</returns>
        internal string Localize(object value, string valueText)
        {
            if (!TryGetKey(value, valueText, out var key))
            {
                return valueText;
            }
            if (Localization.TryFindLocalizedText(key, out var localizedText))
            {
                return localizedText;
            }
            Localization.ReportMissingTranslation(key);
            return valueText;
        }

        /// <summary>
        /// Checks that a key template can be formatted, logging a malformed one.
        /// </summary>
        /// <param name="template">The key template.</param>
        /// <param name="optionName">The option name the template is reported for.</param>
        /// <returns>True when the template can be formatted; otherwise false.</returns>
        private static bool IsValidTemplate(string template, string optionName)
        {
            try
            {
                string.Format(template, optionName, string.Empty);
                return true;
            }
            catch (FormatException e)
            {
                MajDebug.LogError(nameof(SettingValueSelection), $"[Translation] Invalid localization key template \"{template}\" on option {optionName}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// One selected value and the key template it is looked up with.
        /// </summary>
        private sealed class Entry
        {
            /// <summary>
            /// Gets the value as written in the attribute, substituted for <c>{1}</c>.
            /// </summary>
            internal string Text { get; }

            /// <summary>
            /// Gets the key template.
            /// </summary>
            internal string Template { get; }

            /// <summary>
            /// Gets the numeric value matched against numeric options, or null to match the value text.
            /// </summary>
            internal decimal? Number { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="Entry"/> class.
            /// </summary>
            /// <param name="text">The value as written in the attribute.</param>
            /// <param name="template">The key template.</param>
            /// <param name="number">The numeric value, or null to match the value text.</param>
            internal Entry(string text, string template, decimal? number)
            {
                Text = text;
                Template = template;
                Number = number;
            }

            /// <summary>
            /// Checks whether an option value is this selected value.
            /// </summary>
            /// <param name="value">The option value.</param>
            /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
            /// <returns>True for a numeric value equal to <see cref="Number"/>, or a value text equal to <see cref="Text"/>.</returns>
            internal bool Matches(object value, string valueText)
            {
                if (Number is not decimal number)
                {
                    return string.Equals(valueText, Text, StringComparison.Ordinal);
                }
                try
                {
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture) == number;
                }
                catch (Exception exception) when (exception is InvalidCastException || exception is OverflowException || exception is FormatException)
                {
                    return false;
                }
            }
        }
    }
}
