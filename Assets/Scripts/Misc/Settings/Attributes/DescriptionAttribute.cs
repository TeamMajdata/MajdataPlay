using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Overrides the localization key used for the description of a setting option.
    /// </summary>
    /// <remarks>
    /// Without this attribute the description key is <c>MAJSETTING_PROPERTY_{PropertyName}_DESC</c>.
    /// <see cref="Text"/> is a localization key, not the display text itself.
    /// Use <see cref="NoDescriptionAttribute"/> to hide the description entirely.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class DescriptionAttribute : Attribute
    {
        /// <summary>
        /// Gets the localization key of the description. A null argument is stored as an empty string.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DescriptionAttribute"/> class.
        /// </summary>
        /// <param name="text">The localization key of the description; null is treated as an empty string.</param>
        public DescriptionAttribute(string text)
        {
            Text = text ?? string.Empty;
        }
    }
}
