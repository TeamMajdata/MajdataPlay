using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Declares a display name for the settings menu represented by a setting category member.
    /// </summary>
    /// <remarks>
    /// The setting UI currently derives the menu localization key from the member name
    /// (<c>MAJSETTING_CATEGORY_{PropertyName}</c>); this attribute is informational metadata
    /// for tooling and custom menu presentations.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class MenuNameAttribute : Attribute
    {
        /// <summary>
        /// Gets the menu name. A null argument is stored as an empty string.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="MenuNameAttribute"/> class.
        /// </summary>
        /// <param name="name">The menu name; null is treated as an empty string.</param>
        public MenuNameAttribute(string name)
        {
            Name = name ?? string.Empty;
        }
    }
}
