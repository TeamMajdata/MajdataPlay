using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Translates only the listed values of a setting option whose values are otherwise not translated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute only takes effect while the option values are untranslated: for numeric options (integer or
    /// floating-point types, not enums) without <see cref="UseTranslationAttribute"/> or
    /// <see cref="UseGeneralTranslationAttribute"/> for <see cref="TranslationTarget.OptionValue"/>, and for
    /// options with <see cref="NoTranslationAttribute"/> for <see cref="TranslationTarget.OptionValue"/>. When
    /// the option values are translated anyway, the attribute has no effect and the translation analyzer reports it.
    /// </para>
    /// <para>
    /// A listed value is looked up as <see cref="Key"/>, a key template whose <c>{0}</c> is the option name (the
    /// <see cref="OptionNameAttribute"/> value when present, otherwise the member name) and whose <c>{1}</c> is the value
    /// exactly as written in the attribute; the default template is <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c>. Every
    /// other value, and a listed value without a translation, displays its raw text. The <c>UNSET</c> and <c>NULL</c>
    /// placeholders of a missing value are not affected.
    /// </para>
    /// <para>
    /// Values of numeric options are written as invariant-culture numbers and match by numeric value, so <c>"0"</c>
    /// matches <c>0</c>, <c>0f</c> and <c>0.0m</c> and the key does not depend on the current culture's number format.
    /// Values of other options match the displayed value text exactly, such as an enum constant name or <c>True</c>.
    /// Apply the attribute several times to use different key templates.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Displays "OFF" for 0 through MAJSETTING_PROPERTY_OuterJudgeDistance_OPTION_0 and the number otherwise.
    /// [TranslateValues("0")]
    /// public float OuterJudgeDistance { get; set; } = 1f;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public sealed class TranslateValuesAttribute : Attribute
    {
        /// <summary>
        /// Gets the values to translate, written as invariant-culture numbers for numeric options and as the displayed
        /// value text otherwise.
        /// </summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>
        /// Gets or sets the key template of the listed values, or null to use <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c>.
        /// </summary>
        /// <remarks>
        /// <c>{0}</c> is replaced by the option name and <c>{1}</c> by the value as written in the attribute; an empty
        /// string also selects the default template.
        /// </remarks>
        public string? Key { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TranslateValuesAttribute"/> class.
        /// </summary>
        /// <param name="values">The values to translate; null is treated as no values.</param>
        public TranslateValuesAttribute(params string[] values)
        {
            Values = values is null ? Array.Empty<string>() : (string[])values.Clone();
        }
    }
}
