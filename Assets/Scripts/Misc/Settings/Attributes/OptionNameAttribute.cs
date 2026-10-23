using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Overrides the localization key used for the displayed name of a setting option.
    /// </summary>
    /// <remarks>
    /// Without this attribute the name key is <c>MAJSETTING_PROPERTY_{PropertyName}</c>.
    /// <see cref="Name"/> is a localization key, not the display text itself.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class OptionNameAttribute : Attribute
    {
        /// <summary>
        /// Gets the localization key of the option name. A null argument is stored as an empty string.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="OptionNameAttribute"/> class.
        /// </summary>
        /// <param name="name">The localization key of the option name; null is treated as an empty string.</param>
        public OptionNameAttribute(string name)
        {
            Name = name ?? string.Empty;
        }
    }
}
