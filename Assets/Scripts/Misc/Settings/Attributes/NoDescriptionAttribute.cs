using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Marks a setting option as having no description.
    /// </summary>
    /// <remarks>
    /// The setting UI skips the description lookup for the option, so no
    /// <c>MAJSETTING_PROPERTY_{PropertyName}_DESC</c> localization entry is required.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NoDescriptionAttribute : Attribute
    {
    }
}
