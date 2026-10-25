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
    /// For <see cref="TranslationTarget.OptionValue"/> this attribute selects exactly its keys, with no automatic
    /// fallback to the general table for any value type; add <see cref="UseGeneralTranslationAttribute"/> to try
    /// <c>MAJSETTING_GENERAL_OPTION_{1}</c> afterwards. If no key yields a translation, the raw value text is displayed.
    /// </para>
    /// <para>
    /// Without translation attributes, boolean option values (including <see cref="Nullable{T}"/> of
    /// <see cref="bool"/>) only look up <c>MAJSETTING_GENERAL_OPTION_{1}</c>; apply this attribute to give a boolean
    /// option its own <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c> texts instead. Enum option values only look up
    /// <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c>, and values of other types such as strings look up
    /// <c>MAJSETTING_PROPERTY_{0}_OPTION_{1}</c> and then <c>MAJSETTING_GENERAL_OPTION_{1}</c>.
    /// </para>
    /// <para>
    /// The <c>UNSET</c> and <c>NULL</c> placeholders of a missing value are words rather than values: they are looked
    /// up with the keys of this attribute and then always fall back to the general table.
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
