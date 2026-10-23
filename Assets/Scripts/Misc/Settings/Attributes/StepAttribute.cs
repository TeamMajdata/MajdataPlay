using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Sets the amount by which a numeric setting option changes per navigation step in the setting UI.
    /// </summary>
    /// <remarks>
    /// The step is written as a decimal string so that any numeric member type can share the attribute.
    /// When neither this attribute nor <see cref="RangeAttribute"/> is present, the number enumerator
    /// uses a default step of 1 for integers and 0.001 for floating-point types.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class StepAttribute : Attribute
    {
        /// <summary>
        /// Gets the increment applied per step.
        /// </summary>
        public decimal Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StepAttribute"/> class.
        /// </summary>
        /// <param name="value">The increment applied per step, as a decimal string.</param>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not a valid decimal number.</exception>
        /// <exception cref="OverflowException"><paramref name="value"/> is outside the range of <see cref="decimal"/>.</exception>
        public StepAttribute(string value)
        {
            Value = decimal.Parse(value);
        }
    }
}
