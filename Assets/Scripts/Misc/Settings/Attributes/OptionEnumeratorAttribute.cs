using System;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Replaces the option enumerator chosen from the member type with a custom <see cref="IOptionEnumerator"/>.
    /// </summary>
    /// <remarks>
    /// Without this attribute the setting UI picks an enumerator from the member type: enum, boolean,
    /// number or read-only. The custom type must implement <see cref="IOptionEnumerator"/> and expose a
    /// public parameterless constructor, because it is created through reflection on every platform,
    /// including IL2CPP.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class OptionEnumeratorAttribute : Attribute
    {
        /// <summary>
        /// Gets the <see cref="IOptionEnumerator"/> implementation that enumerates the option values.
        /// </summary>
        public Type EnumeratorType
        {
            get;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="OptionEnumeratorAttribute"/> class.
        /// </summary>
        /// <param name="enumeratorType">The <see cref="IOptionEnumerator"/> implementation that enumerates the option values.</param>
        public OptionEnumeratorAttribute(Type enumeratorType)
        {
            EnumeratorType = enumeratorType;
        }
        /// <summary>
        /// Creates a new, uninitialized instance of <see cref="EnumeratorType"/>.
        /// </summary>
        /// <returns>A new enumerator that still has to be initialized with the target member.</returns>
        /// <exception cref="MissingMethodException"><see cref="EnumeratorType"/> has no public parameterless constructor.</exception>
        /// <exception cref="InvalidCastException"><see cref="EnumeratorType"/> does not implement <see cref="IOptionEnumerator"/>.</exception>
        public IOptionEnumerator Instance()
        {
            return (IOptionEnumerator)Activator.CreateInstance(EnumeratorType);
        }
    }
}
