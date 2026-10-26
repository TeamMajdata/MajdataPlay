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
    /// the same choices (for example booleans or common enums) reuse one translation. Option names use
    /// <c>MAJSETTING_GENERAL_PROPERTY_{OptionName}</c> and menu names <c>MAJSETTING_GENERAL_CATEGORY_{MenuName}</c>,
    /// where the option name is the <see cref="OptionNameAttribute"/> value when present. The general key is tried
    /// after any <see cref="UseTranslationAttribute"/> key of the same target.
    /// </para>
    /// <para>
    /// Numeric option values (options of integer or floating-point types, not enums) are not translated at all by
    /// default. Apply this attribute with <see cref="TranslationTarget.OptionValue"/> to opt a numeric option into
    /// the general table, alone or after the keys of <see cref="UseTranslationAttribute"/>; numeric values never
    /// fall back to the general table without it.
    /// </para>
    /// <para>
    /// Boolean option values (including <see cref="Nullable{T}"/> of <see cref="bool"/>) already use only
    /// <c>MAJSETTING_GENERAL_OPTION_{ValueText}</c> without translation attributes, so this attribute is not needed for
    /// them. With <see cref="UseTranslationAttribute"/>, no value type falls back to the general table automatically;
    /// apply this attribute as well to try <c>MAJSETTING_GENERAL_OPTION_{ValueText}</c> after the option-specific keys.
    /// </para>
    /// <para>
    /// Enum option values never use the general table on their own, not even without translation attributes. Apply
    /// this attribute to an enum option whose values are translated in the general table, such as options sharing
    /// <c>JudgeDisplayOption</c>; combine it with <see cref="UseTranslationAttribute"/> to try the option-specific key
    /// first.
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
