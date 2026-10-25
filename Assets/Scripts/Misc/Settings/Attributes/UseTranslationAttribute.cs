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
    /// Apply the attribute once per <see cref="TranslationTarget"/>. <see cref="Key"/> is a key template such as
    /// <c>MAJTEXT_XXX_{0}</c>: <c>{0}</c> is replaced by the option name, which is the <see cref="OptionNameAttribute"/>
    /// value when the member has one and the member name otherwise, and for <see cref="TranslationTarget.OptionValue"/>
    /// <c>{1}</c> is replaced by the value text. The formatted key is then looked up through i18n.
    /// </para>
    /// <para>
    /// When <see cref="Key"/> is null the default template of the target is used: <c>MAJSETTING_CATEGORY_{0}</c> for
    /// menu names, <c>MAJSETTING_PROPERTY_{0}</c> for option names and <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c> for
    /// option values. Several templates for the same target are tried in declaration order, followed by any
    /// <see cref="UseGeneralTranslationAttribute"/> template; <see cref="NoTranslationAttribute"/> disables them all.
    /// </para>
    /// <para>
    /// Numeric option values (options of integer or floating-point types, not enums) are not translated at all by
    /// default: no key is looked up and the formatted number is displayed. Apply this attribute with
    /// <see cref="TranslationTarget.OptionValue"/> to opt a numeric option into translation; only the keys of this
    /// attribute are then looked up, with no automatic general fallback.
    /// </para>
    /// <para>
    /// Boolean option values (including <see cref="Nullable{T}"/> of <see cref="bool"/>) automatically fall back to
    /// the general table: after the option-specific keys of this attribute, such as
    /// <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c>, have been queried without a translation,
    /// <c>MAJSETTING_GENERAL_OPTION_{1}</c> is queried as well, so <c>True</c> and <c>False</c> only need one
    /// general translation. <see cref="UseGeneralTranslationAttribute"/> is not required for that fallback. Option
    /// values of every other type, including numbers, enums and strings, only use the general table after an
    /// explicit <see cref="UseTranslationAttribute"/> when <see cref="UseGeneralTranslationAttribute"/> is also
    /// applied. If no key yields a translation, the raw value text is displayed.
    /// </para>
    /// <para>
    /// Enum option values do not use the general table even without translation attributes: by default they only
    /// look up <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c>. The <c>UNSET</c> and <c>NULL</c> placeholders of a nullable
    /// enum are words and still fall back to the general table.
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
