using System;
using System.Globalization;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Formats large numeric option values in a short form for options marked with <see cref="FoldNumberAttribute"/>.
    /// </summary>
    /// <remarks>
    /// The value is divided by 1000 until its magnitude is below 1000 and the SI suffix of that magnitude is appended
    /// (<c>k</c>, <c>M</c>, <c>G</c>, <c>T</c>, <c>P</c>, <c>E</c>, <c>Z</c>, <c>Y</c>). The mantissa keeps at most two
    /// decimal places, truncated toward zero so that a value never reads as the next magnitude (999999 becomes
    /// <c>999.99k</c> rather than <c>1000k</c>), and is formatted with the invariant culture.
    /// </remarks>
    internal static class SettingNumberFolding
    {
        /// <summary>
        /// The factor between two neighbouring magnitude suffixes, which is also the smallest magnitude that is folded.
        /// </summary>
        private const decimal FoldBase = 1000m;

        /// <summary>
        /// The largest float or double magnitude that converts to <see cref="decimal"/> without overflowing.
        /// </summary>
        private const double MaxDecimalMagnitude = 7.9e28;

        /// <summary>
        /// The magnitude suffixes, where index <c>i</c> stands for a factor of <c>1000^(i + 1)</c>.
        /// </summary>
        private static readonly string[] s_suffixes = { "k", "M", "G", "T", "P", "E", "Z", "Y" };

        /// <summary>
        /// Tries to fold a numeric value into its short form.
        /// </summary>
        /// <param name="value">The boxed option value. Only integer and floating-point values are folded.</param>
        /// <param name="foldedText">Receives the folded text, or an empty string when the method returns false.</param>
        /// <returns>
        /// True when <paramref name="value"/> is a finite number whose magnitude is at least 1000; otherwise false, in
        /// which case the value should keep its normal text.
        /// </returns>
        internal static bool TryFold(object value, out string foldedText)
        {
            foldedText = string.Empty;
            if (!TryGetDecimal(value, out var number))
            {
                return false;
            }
            var magnitude = Math.Abs(number);
            if (magnitude < FoldBase)
            {
                return false;
            }
            var suffixIndex = -1;
            while (magnitude >= FoldBase && suffixIndex < s_suffixes.Length - 1)
            {
                magnitude /= FoldBase;
                suffixIndex++;
            }
            var mantissa = Math.Truncate(magnitude * 100m) / 100m;
            var sign = number < 0m ? "-" : string.Empty;
            foldedText = sign + mantissa.ToString("0.##", CultureInfo.InvariantCulture) + s_suffixes[suffixIndex];
            return true;
        }

        /// <summary>
        /// Converts a boxed integer or floating-point value to <see cref="decimal"/>.
        /// </summary>
        /// <param name="value">The boxed value. Enums and other types are not converted.</param>
        /// <param name="number">Receives the converted value, or zero when the method returns false.</param>
        /// <returns>
        /// True when <paramref name="value"/> is an integer, a <see cref="decimal"/>, or a finite float or double within the
        /// range of <see cref="decimal"/>; otherwise false.
        /// </returns>
        private static bool TryGetDecimal(object value, out decimal number)
        {
            switch (value)
            {
                case sbyte sbyteValue:
                    number = sbyteValue;
                    return true;
                case byte byteValue:
                    number = byteValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case ushort ushortValue:
                    number = ushortValue;
                    return true;
                case int intValue:
                    number = intValue;
                    return true;
                case uint uintValue:
                    number = uintValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case ulong ulongValue:
                    number = ulongValue;
                    return true;
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case float floatValue:
                    return TryGetDecimal((double)floatValue, out number);
                case double doubleValue:
                    return TryGetDecimal(doubleValue, out number);
                default:
                    number = 0m;
                    return false;
            }
        }

        /// <summary>
        /// Converts a double to <see cref="decimal"/> when it is finite and within the range of <see cref="decimal"/>.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="number">Receives the converted value, or zero when the method returns false.</param>
        /// <returns>True when the value could be converted; otherwise false.</returns>
        private static bool TryGetDecimal(double value, out decimal number)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) >= MaxDecimalMagnitude)
            {
                number = 0m;
                return false;
            }
            number = (decimal)value;
            return true;
        }
    }
}
