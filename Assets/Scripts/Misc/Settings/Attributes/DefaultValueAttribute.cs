using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Declares the default value of a setting option as text, together with the type the text should be converted to.
    /// </summary>
    /// <remarks>
    /// The value is stored as a string so that any numeric or enum default can be expressed in an attribute argument.
    /// Consumers convert <see cref="Value"/> to <see cref="Type"/> when they need the typed default.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class DefaultValueAttribute : Attribute
    {
        /// <summary>
        /// Gets the type that <see cref="Value"/> should be converted to.
        /// </summary>
        public Type Type
        {
            get => _type;
        }
        /// <summary>
        /// Gets the textual representation of the default value.
        /// </summary>
        public string Value
        {
            get => _value;
        }
        private readonly string _value;
        private readonly Type _type;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultValueAttribute"/> class.
        /// </summary>
        /// <param name="value">The textual representation of the default value.</param>
        /// <param name="type">The type that <paramref name="value"/> should be converted to.</param>
        public DefaultValueAttribute(string value, Type type)
        {
            _value = value;
            _type = type;
        }
    }
}
