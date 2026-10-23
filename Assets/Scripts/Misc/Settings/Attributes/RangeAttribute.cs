using System;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Limits the values a numeric setting option may be stepped to in the setting UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bounds are written as decimal strings so that any numeric member type can share the attribute.
    /// A bound is only applied when its matching <see cref="HasMin"/> or <see cref="HasMax"/> flag is set,
    /// which allows an open-ended range on either side.
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
        /// Gets or initializes whether <see cref="Min"/> is enforced.
        /// </summary>
        public bool HasMin
        {
            get => _hasMin;
            init => _hasMin = value;
        }
        /// <summary>
        /// Gets or initializes whether <see cref="Max"/> is enforced.
        /// </summary>
        public bool HasMax
        {
            get => _hasMax;
            init => _hasMax = value;
        }

        private readonly bool _hasMin;
        private readonly bool _hasMax;

        private readonly decimal _min;
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
        /// <param name="min">The inclusive lower bound as a decimal string, or null or empty to leave <see cref="Min"/> at zero.</param>
        /// <param name="max">The inclusive upper bound as a decimal string, or null or empty to leave <see cref="Max"/> at zero.</param>
        /// <exception cref="FormatException"><paramref name="min"/> or <paramref name="max"/> is not a valid decimal number.</exception>
        /// <exception cref="OverflowException"><paramref name="min"/> or <paramref name="max"/> is outside the range of <see cref="decimal"/>.</exception>
        public RangeAttribute(string? min, string? max)
        {
            if (!string.IsNullOrEmpty(min))
            {
                _min = decimal.Parse(min);
            }
            if (!string.IsNullOrEmpty(max))
            {
                _max = decimal.Parse(max);
            }
        }
    }
}
