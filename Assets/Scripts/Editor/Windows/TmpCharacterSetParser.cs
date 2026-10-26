using System;
using System.Collections.Generic;
using System.Globalization;
#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Turns literal text and Unicode range lists into the code points a TMP font asset should contain.</summary>
    internal static class TmpCharacterSetParser
    {
        /// <summary>The largest valid Unicode code point.</summary>
        public const uint MaxCodePoint = 0x10FFFF;

        /// <summary>Common Unicode blocks offered as range shortcuts, in display order.</summary>
        public static IReadOnlyList<UnicodeRangePreset> Presets { get; } = new UnicodeRangePreset[]
        {
            new UnicodeRangePreset("Basic Latin (ASCII)", "0020-007E"),
            new UnicodeRangePreset("Latin-1 Supplement", "00A0-00FF"),
            new UnicodeRangePreset("General Punctuation", "2000-206F"),
            new UnicodeRangePreset("CJK Symbols and Punctuation", "3000-303F"),
            new UnicodeRangePreset("Hiragana", "3040-309F"),
            new UnicodeRangePreset("Katakana", "30A0-30FF"),
            new UnicodeRangePreset("CJK Unified Ideographs", "4E00-9FFF"),
            new UnicodeRangePreset("Hangul Syllables", "AC00-D7A3"),
            new UnicodeRangePreset("Halfwidth and Fullwidth Forms", "FF00-FFEF"),
        };

        /// <summary>Characters that separate entries of a Unicode range list.</summary>
        private static readonly char[] s_rangeSeparators = { ',', ';', ' ', '\t', '\r', '\n' };

        /// <summary>Adds every code point of literal text, combining surrogate pairs and skipping unrenderable characters.</summary>
        /// <param name="text">The characters to add; null or empty adds nothing.</param>
        /// <param name="codePoints">The set that receives the code points.</param>
        /// <returns>The number of code points that were not already in <paramref name="codePoints"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="codePoints"/> is null.</exception>
        public static int AddCharacters(string? text, ISet<uint> codePoints)
        {
            if (codePoints is null)
            {
                throw new ArgumentNullException(nameof(codePoints));
            }
            if (text is null || text.Length == 0)
            {
                return 0;
            }
            var addedCount = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];
                uint codePoint;
                if (char.IsHighSurrogate(current) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = (uint)char.ConvertToUtf32(current, text[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(current))
                {
                    continue;
                }
                else
                {
                    codePoint = current;
                }
                if (IsSkipped(codePoint))
                {
                    continue;
                }
                if (codePoints.Add(codePoint))
                {
                    addedCount++;
                }
            }
            return addedCount;
        }

        /// <summary>
        /// Adds the code points of a range list such as <c>0020-007E, U+3042, 0x4E00-0x9FFF</c>.
        /// Entries are hexadecimal, optionally prefixed with <c>U+</c> or <c>0x</c>, and are separated by commas,
        /// semicolons or whitespace. Surrogates and control characters inside a range are skipped.
        /// </summary>
        /// <param name="text">The range list; null or whitespace adds nothing.</param>
        /// <param name="codePoints">The set that receives the code points.</param>
        /// <returns>The number of code points that were not already in <paramref name="codePoints"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="codePoints"/> is null.</exception>
        /// <exception cref="FormatException">An entry is not a hexadecimal code point or range, exceeds U+10FFFF, or ends before it starts.</exception>
        public static int AddUnicodeRanges(string? text, ISet<uint> codePoints)
        {
            if (codePoints is null)
            {
                throw new ArgumentNullException(nameof(codePoints));
            }
            if (text is null || string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }
            var addedCount = 0;
            var entries = text.Split(s_rangeSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var (start, end) = ParseRange(entry);
                for (var codePoint = start; codePoint <= end; codePoint++)
                {
                    if (IsSkipped(codePoint))
                    {
                        continue;
                    }
                    if (codePoints.Add(codePoint))
                    {
                        addedCount++;
                    }
                }
            }
            return addedCount;
        }

        /// <summary>Checks whether a code point can never be a glyph: out of range, a surrogate, a C0/C1 control or the byte order mark.</summary>
        /// <param name="codePoint">The code point to check.</param>
        /// <returns>True when the code point is ignored by this parser.</returns>
        public static bool IsSkipped(uint codePoint)
        {
            return codePoint > MaxCodePoint ||
                   codePoint < 0x20 ||
                   (codePoint >= 0x7F && codePoint <= 0x9F) ||
                   (codePoint >= 0xD800 && codePoint <= 0xDFFF) ||
                   codePoint == 0xFEFF;
        }

        /// <summary>Parses one range list entry: a single code point or two code points joined by a hyphen.</summary>
        /// <param name="entry">The entry without separators.</param>
        /// <returns>The inclusive start and end of the range.</returns>
        /// <exception cref="FormatException">The entry is malformed or its end is before its start.</exception>
        private static (uint Start, uint End) ParseRange(string entry)
        {
            var dashIndex = entry.IndexOf('-');
            if (dashIndex < 0)
            {
                var codePoint = ParseCodePoint(entry, entry);
                return (codePoint, codePoint);
            }
            if (dashIndex == 0 || dashIndex == entry.Length - 1 || entry.IndexOf('-', dashIndex + 1) >= 0)
            {
                throw new FormatException($"\"{entry}\" is not a hexadecimal Unicode code point or range.");
            }
            var start = ParseCodePoint(entry.Substring(0, dashIndex), entry);
            var end = ParseCodePoint(entry.Substring(dashIndex + 1), entry);
            if (start > end)
            {
                throw new FormatException($"The range \"{entry}\" ends before it starts.");
            }
            return (start, end);
        }

        /// <summary>Parses a hexadecimal code point with an optional <c>U+</c> or <c>0x</c> prefix.</summary>
        /// <param name="value">The code point text.</param>
        /// <param name="entry">The whole entry, quoted in the error message.</param>
        /// <returns>The code point.</returns>
        /// <exception cref="FormatException">The value is not hexadecimal or exceeds U+10FFFF.</exception>
        private static uint ParseCodePoint(string value, string entry)
        {
            var digits = value;
            if (digits.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
                digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                digits = digits.Substring(2);
            }
            if (digits.Length == 0 ||
                digits.Length > 6 ||
                !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
            {
                throw new FormatException($"\"{entry}\" is not a hexadecimal Unicode code point or range.");
            }
            if (codePoint > MaxCodePoint)
            {
                throw new FormatException($"\"{entry}\" is beyond U+10FFFF.");
            }
            return codePoint;
        }
    }

    /// <summary>A named Unicode range shortcut for the range list input.</summary>
    internal readonly struct UnicodeRangePreset
    {
        /// <summary>The name shown in the preset menu.</summary>
        public string Name { get; }

        /// <summary>The range list appended when the preset is chosen.</summary>
        public string Ranges { get; }

        /// <summary>Creates a preset.</summary>
        /// <param name="name">The name shown in the preset menu.</param>
        /// <param name="ranges">The range list appended when the preset is chosen.</param>
        public UnicodeRangePreset(string name, string ranges)
        {
            Name = name;
            Ranges = ranges;
        }
    }
}
