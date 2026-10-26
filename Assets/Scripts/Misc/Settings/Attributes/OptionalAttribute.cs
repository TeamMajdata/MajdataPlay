using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Marks a nullable setting option as optional, so that a null value is displayed as <c>UNSET</c> instead of <c>NULL</c>.
    /// </summary>
    /// <remarks>
    /// Enumerators that support it also add the null choice to the selectable values; for example a
    /// nullable boolean option cycles through false, true and unset. The attribute does not add a null
    /// choice to non-nullable options.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class OptionalAttribute : Attribute
    {

    }
}
