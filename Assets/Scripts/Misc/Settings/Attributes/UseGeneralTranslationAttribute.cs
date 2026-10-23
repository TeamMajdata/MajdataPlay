using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Translates one part of a setting option through the shared general localization table instead of an option-specific key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply the attribute once per <see cref="TranslationTarget"/>. For <see cref="TranslationTarget.OptionValue"/>
    /// the value text is looked up as <c>MAJSETTING_GENERAL_OPTION_{ValueText}</c>, which lets options that share
    /// the same choices (for example booleans or common enums) reuse one translation.
    /// </para>
    /// <para>
    /// Numeric option values (options of integer or floating-point types) are not translated by default.
    /// Apply this attribute with <see cref="TranslationTarget.OptionValue"/> to opt a numeric option into the general table.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class UseGeneralTranslationAttribute : Attribute
    {
        /// <summary>
        /// Gets the part of the option that is translated through the general table.
        /// </summary>
        public TranslationTarget Target { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UseGeneralTranslationAttribute"/> class.
        /// </summary>
        /// <param name="target">The part of the option that is translated through the general table.</param>
        public UseGeneralTranslationAttribute(TranslationTarget target)
        {
            Target = target;
        }
    }
}
