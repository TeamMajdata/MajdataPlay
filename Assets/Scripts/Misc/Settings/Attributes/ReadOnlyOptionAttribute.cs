using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Displays a setting option in the setting UI without allowing the user to change its value.
    /// </summary>
    /// <remarks>
    /// The option enumerator reports the current value but ignores navigation requests.
    /// Properties without a setter and <c>readonly</c> fields are treated as read-only even without this attribute.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ReadOnlyOptionAttribute : Attribute
    {
    }
}
