using System;
using System.Globalization;

#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Limits the values a numeric setting option may be stepped to in the setting UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bounds are written as invariant-culture decimal strings so that any numeric member type can share the attribute.
    /// A non-null, non-empty bound automatically enables its matching <see cref="HasMin"/> or <see cref="HasMax"/> flag.
    /// A null or empty bound leaves that side of the range unbounded.
    /// </para>
    /// <para>
    /// When neither this attribute nor <see cref="StepAttribute"/> is present, the number enumerator
    /// uses an unbounded range with a default step of 1 for integers and 0.001 for floating-point types.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class RangeAttribute : Attribute
    {
        /// <summary>
        /// Gets the inclusive lower bound. Only meaningful when <see cref="HasMin"/> is true.
        /// </summary>
        public decimal Min
        {
            get => _min;
        }
        /// <summary>
        /// Gets the inclusive upper bound. Only meaningful when <see cref="HasMax"/> is true.
        /// </summary>
        public decimal Max
        {
            get => _max;
        }
        /// <summary>
        /// Gets whether a non-null, non-empty lower bound was supplied.
        /// </summary>
        public bool HasMin { get; }
        /// <summary>
        /// Gets whether a non-null, non-empty upper bound was supplied.
        /// </summary>
        public bool HasMax { get; }

        /// <summary>
        /// Stores the parsed lower bound, or zero when no lower bound is supplied.
        /// </summary>
        private readonly decimal _min;
        /// <summary>
        /// Stores the parsed upper bound, or zero when no upper bound is supplied.
        /// </summary>
        private readonly decimal _max;

        /// <summary>
        /// Initializes a new instance of the <see cref="RangeAttribute"/> class without bounds.
        /// </summary>
        public RangeAttribute()
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="RangeAttribute"/> class with the given bounds.
        /// </summary>
        /// <param name="min">The inclusive lower bound as an invariant-culture decimal string, or null or empty for no lower bound.</param>
        /// <param name="max">The inclusive upper bound as an invariant-culture decimal string, or null or empty for no upper bound.</param>
        /// <exception cref="FormatException"><paramref name="min"/> or <paramref name="max"/> is not a valid decimal number.</exception>
        /// <exception cref="OverflowException"><paramref name="min"/> or <paramref name="max"/> is outside the range of <see cref="decimal"/>.</exception>
        public RangeAttribute(string? min, string? max)
        {
            if (!string.IsNullOrEmpty(min))
            {
                _min = decimal.Parse(min, CultureInfo.InvariantCulture);
                HasMin = true;
            }
            if (!string.IsNullOrEmpty(max))
            {
                _max = decimal.Parse(max, CultureInfo.InvariantCulture);
                HasMax = true;
            }
        }
    }
}
