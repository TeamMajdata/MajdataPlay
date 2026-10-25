using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Disables i18n translation for one part of a setting option, so that the raw text is displayed as-is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply the attribute once per <see cref="TranslationTarget"/> that should stay untranslated.
    /// It takes precedence over <see cref="UseTranslationAttribute"/> and
    /// <see cref="UseGeneralTranslationAttribute"/> for the same target.
    /// </para>
    /// <para>
    /// Numeric option values (options of integer or floating-point types, not enums) are not translated at all by
    /// default and are displayed using their formatted value text, so this attribute is only needed for
    /// <see cref="TranslationTarget.OptionValue"/> on non-numeric options such as enums, booleans and strings.
    /// </para>
    /// <para>
    /// Applied to <see cref="TranslationTarget.OptionValue"/>, it also suppresses the automatic fallback of boolean
    /// values to <c>MAJSETTING_GENERAL_OPTION_{ValueText}</c>, so neither the option-specific nor the general key is
    /// queried.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class NoTranslationAttribute : Attribute
    {
        /// <summary>
        /// Gets the part of the option that is excluded from translation.
        /// </summary>
        public TranslationTarget Target { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NoTranslationAttribute"/> class.
        /// </summary>
        /// <param name="target">The part of the option that is excluded from translation.</param>
        public NoTranslationAttribute(TranslationTarget target)
        {
            Target = target;
        }
    }
}
