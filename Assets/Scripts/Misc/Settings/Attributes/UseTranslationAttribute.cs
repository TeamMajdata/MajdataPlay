using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Translates one part of a setting option through an option-specific localization key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply the attribute once per <see cref="TranslationTarget"/>. When <see cref="Key"/> is null the
    /// default key for the target is used: <c>MAJSETTING_CATEGORY_{PropertyName}</c> for menu names,
    /// <c>MAJSETTING_PROPERTY_{PropertyName}</c> for option names and
    /// <c>MAJSETTING_PROPERTY_{PropertyName}_OPTION_{ValueText}</c> for option values.
    /// For <see cref="TranslationTarget.OptionValue"/> a custom key replaces the <c>MAJSETTING_PROPERTY_{PropertyName}</c> prefix.
    /// </para>
    /// <para>
    /// Numeric option values (options of integer or floating-point types) are not translated by default.
    /// Apply this attribute with <see cref="TranslationTarget.OptionValue"/> to opt a numeric option into translation.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class UseTranslationAttribute : Attribute
    {
        /// <summary>
        /// Gets the part of the option that is translated.
        /// </summary>
        public TranslationTarget Target { get; }
        /// <summary>
        /// Gets the custom localization key, or null to use the default key of <see cref="Target"/>.
        /// </summary>
        public string? Key { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UseTranslationAttribute"/> class that uses the default key of the target.
        /// </summary>
        /// <param name="target">The part of the option that is translated.</param>
        public UseTranslationAttribute(TranslationTarget target) : this(target, string.Empty) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="UseTranslationAttribute"/> class with a custom key.
        /// </summary>
        /// <param name="target">The part of the option that is translated.</param>
        /// <param name="key">The custom localization key; null or empty selects the default key of <paramref name="target"/>.</param>
        public UseTranslationAttribute(TranslationTarget target, string key)
        {
            Target = target;
            if (string.IsNullOrEmpty(key))
            {
                Key = null;
            }
            else
            {
                Key = key;
            }
        }
    }
}
