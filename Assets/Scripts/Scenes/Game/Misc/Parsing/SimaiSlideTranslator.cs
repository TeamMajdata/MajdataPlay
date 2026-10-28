using System;
using System.Collections.Generic;
using System.Text;

#nullable enable
namespace MajdataPlay.Scenes.Game.Parsing
{
    /// <summary>
    /// Translates simai slide shapes into slide code, so that every simai slide except wifi is built by the slide code
    /// parser. The codes follow the slide code documentation, for example <c>1pp6</c> is <c>1P3K6</c>. A connection
    /// slide may also chain slide code segments with simai segments, for example <c>3Q9K4</c> in <c>1^2^3Q9K4</c>.
    /// </summary>
    public static class SimaiSlideTranslator
    {
        /// <summary>The number of keys on the judgment ring.</summary>
        private const int KeyCount = 8;

        /// <summary>The slide code index of the centre circle walked by p and q slides.</summary>
        private const int CenterCircle = 0;

        /// <summary>The slide code index of the judgment circle walked by &lt;, &gt; and ^ slides.</summary>
        private const int JudgmentCircle = 9;

        /// <summary>
        /// Translates one simai slide segment, such as <c>1-4</c>, <c>8&gt;3</c>, <c>1pp6</c> or <c>2V46</c>, into slide code.
        /// A segment written as slide code, such as <c>3Q9K4</c>, is checked and returned without shorthand.
        /// </summary>
        /// <param name="segment">
        /// The segment: the start key, the shape and the end key (the turn key and the end key for <c>V</c>), or the
        /// start key and slide code commands up to <c>K</c> and the end key, without durations or note flags.
        /// </param>
        /// <returns>The slide code that draws the same path, for example <c>1K4</c> for <c>1-4</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the segment is malformed, the shape is unknown or the shape cannot connect its keys.
        /// </exception>
        /// <exception cref="NotSupportedException">Thrown for wifi slides, which slide code cannot express.</exception>
        public static string Translate(string segment)
        {
            if (segment is null)
            {
                throw new ArgumentNullException(nameof(segment));
            }
            if (segment.Length > 1 && IsSlideCodeCommand(segment[1]))
            {
                return TranslateSlideCode(segment);
            }
            var start = ParseKey(segment, 0);
            var shapeLength = GetShapeLength(segment);
            var shape = segment.Substring(1, shapeLength);
            var keyIndex = 1 + shapeLength;
            if (shape == "V")
            {
                RequireLength(segment, keyIndex + 2);
                return TranslateTurn(start, ParseKey(segment, keyIndex), ParseKey(segment, keyIndex + 1));
            }

            RequireLength(segment, keyIndex + 1);
            var end = ParseKey(segment, keyIndex);
            return shape switch
            {
                "-" => TranslateLine(start, end),
                ">" => TranslateCircle(start, end, IsUpperHalf(start)),
                "<" => TranslateCircle(start, end, !IsUpperHalf(start)),
                "^" => TranslateShortCircle(start, end),
                "v" => TranslateV(start, end),
                "p" => $"{start}P{CenterCircle}K{end}",
                "q" => $"{start}Q{CenterCircle}K{end}",
                "pp" => TranslateSideLoop(start, end, true),
                "qq" => TranslateSideLoop(start, end, false),
                "s" => TranslateZigzag(start, end, false),
                "z" => TranslateZigzag(start, end, true),
                "w" => throw new NotSupportedException($"\"{segment}\" is a wifi slide, which slide code cannot express"),
                _ => throw new ArgumentException($"\"{segment}\" has an unknown slide shape \"{shape}\"", nameof(segment))
            };
        }

        /// <summary>
        /// Translates the segments of a simai connection slide, such as <c>1-4</c> and <c>4q7</c> from <c>1-4q7</c>, into
        /// one slide code, for example <c>1A4Q0K7</c>. Every segment but the last ends at an <c>A</c> node instead of
        /// <c>K</c>, so the whole chain is parsed and judged as a single slide. Segments may be slide code, so
        /// <c>1^2</c>, <c>2^3</c> and <c>3Q9K4</c> from <c>1^2^3Q9K4</c> become <c>1Q9A2Q9A3Q9K4</c>.
        /// </summary>
        /// <param name="segments">
        /// The segments in order, each accepted by <see cref="Translate"/>; each starts at the end key of the segment before it.
        /// </param>
        /// <returns>The slide code of the whole chain.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments"/> or a segment is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when there is no segment, a segment cannot be translated or a segment does not start where the one
        /// before it ends.
        /// </exception>
        /// <exception cref="NotSupportedException">Thrown when a segment is a wifi slide.</exception>
        public static string TranslateChain(IReadOnlyList<string> segments)
        {
            if (segments is null)
            {
                throw new ArgumentNullException(nameof(segments));
            }
            if (segments.Count == 0)
            {
                throw new ArgumentException("A slide needs at least one segment", nameof(segments));
            }
            var builder = new StringBuilder();
            var lastEnd = '\0';
            for (var i = 0; i < segments.Count; i++)
            {
                // 每段的形式都是“起点 + 指令 + K + 终点”
                var code = Translate(segments[i]);
                if (i == 0)
                {
                    builder.Append(code[0]);
                }
                else if (code[0] != lastEnd)
                {
                    throw new ArgumentException($"\"{segments[i]}\" does not start where \"{segments[i - 1]}\" ends", nameof(segments));
                }
                var isLast = i == segments.Count - 1;
                lastEnd = code[code.Length - 1];
                builder.Append(code, 1, code.Length - 3);
                // 中间段的终点改成 A 节点，下一段从这里继续
                builder.Append(isLast ? 'K' : 'A');
                builder.Append(lastEnd);
            }
            return builder.ToString();
        }

        /// <summary>Checks that a wifi segment such as <c>1w5</c> ends at the opposite key.</summary>
        /// <param name="segment">The segment: the start key, <c>w</c> and the end key.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when the segment is not a wifi slide to the opposite key.</exception>
        public static void ValidateWifi(string segment)
        {
            if (segment is null)
            {
                throw new ArgumentNullException(nameof(segment));
            }
            RequireLength(segment, 3);
            var start = ParseKey(segment, 0);
            var end = ParseKey(segment, 2);
            if (segment[1] != 'w')
            {
                throw new ArgumentException($"\"{segment}\" is not a wifi slide", nameof(segment));
            }
            if (GetRelativeKey(start, end) != 4)
            {
                throw new ArgumentException("w星星尾部错误\nwスライドエラー", nameof(segment));
            }
        }

        /// <summary>
        /// Checks whether a character is a slide code command, which starts a slide code segment when it follows a key,
        /// such as <c>Q</c> in <c>1^2^3Q9K4</c>.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns><see langword="true"/> for <c>A</c>, <c>B</c>, <c>C</c>, <c>P</c>, <c>Q</c> and <c>K</c>.</returns>
        public static bool IsSlideCodeCommand(char c)
        {
            return Array.IndexOf(SlideCodeParser.CommandChars, c) >= 0;
        }

        /// <summary>
        /// Finds the end of the slide code segment whose commands start at an index of a slide note: the segment runs to
        /// the end key after <c>K</c>, so in <c>1^2^3Q9K4[1:100]</c> the commands starting at 5 end before 9.
        /// </summary>
        /// <param name="text">The slide note text, without note flags.</param>
        /// <param name="commandIndex">The index of the first command, right after the start key of the segment.</param>
        /// <returns>The index after the end key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when no command starts at <paramref name="commandIndex"/>, or the commands contain another character or
        /// do not end with <c>K</c> and the end key.
        /// </exception>
        public static int FindSlideCodeEnd(string text, int commandIndex)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (commandIndex < 0 || commandIndex >= text.Length || !IsSlideCodeCommand(text[commandIndex]))
            {
                throw new ArgumentException($"\"{text}\" has no slide code command at position {commandIndex}", nameof(commandIndex));
            }
            for (var i = commandIndex; i < text.Length; i++)
            {
                var c = text[i];
                if (c == 'K')
                {
                    if (i + 1 < text.Length && IsDigit(text[i + 1]))
                    {
                        return i + 2;
                    }
                    break;
                }
                if (!IsSlideCodeCommand(c) && !IsDigit(c))
                {
                    throw new ArgumentException($"\"{text}\" has \"{c}\" in the slide code that starts at position {commandIndex}", nameof(text));
                }
            }
            throw new ArgumentException($"The slide code that starts at position {commandIndex} of \"{text}\" does not end with K and the end key", nameof(text));
        }

        /// <summary>
        /// Checks whether a slide note is one slide code, such as <c>1P3K6[8:1]</c>, rather than simai shapes or a
        /// connection slide that contains slide code segments, such as <c>1^2^3Q9K4[1:100]</c> or <c>1Q9K3-7[1:1]</c>.
        /// </summary>
        /// <param name="text">The slide note text, without note flags.</param>
        /// <returns>
        /// <see langword="true"/> when the text starts with slide code commands and only durations follow the end key
        /// after the first <c>K</c>. A slide code without an end key also counts, so that its error names the slide code.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
        public static bool IsSingleSlideCode(string text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (text.Length < 2 || !IsSlideCodeCommand(text[1]))
            {
                return false;
            }
            var endFlagIndex = text.IndexOf('K');
            var afterEndKey = endFlagIndex + 2;
            return endFlagIndex < 0 || afterEndKey >= text.Length || text[afterEndKey] == '[';
        }

        /// <summary>Checks and normalizes a segment written as slide code, such as <c>3Q9K4</c> or <c>1P33K4</c>.</summary>
        /// <param name="segment">The start key and the slide code commands up to <c>K</c> and the end key.</param>
        /// <returns>The slide code without shorthand, for example <c>1P3P3K4</c> for <c>1P33K4</c>.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when the segment does not end right after its end key, a key is outside 1 to 8 or the commands are invalid.
        /// </exception>
        private static string TranslateSlideCode(string segment)
        {
            ParseKey(segment, 0);
            RequireLength(segment, FindSlideCodeEnd(segment, 1));
            ParseKey(segment, segment.Length - 1);
            return SlideCodeShape.Format(SlideCodeParser.ParseCommands(segment));
        }

        /// <summary>Translates a straight slide, <c>-</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when the end key is the start key or one of its neighbours.</exception>
        private static string TranslateLine(int start, int end)
        {
            var relative = GetRelativeKey(start, end);
            if (relative < 2 || relative > 6)
            {
                throw new ArgumentException("-星星至少隔开一键\n-スライドエラー", nameof(end));
            }
            return $"{start}K{end}";
        }

        /// <summary>Translates a slide along the judgment circle, <c>&lt;</c> or <c>&gt;</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key; the start key itself turns a whole circle.</param>
        /// <param name="isClockwise">Whether the slide turns clockwise.</param>
        /// <returns>The slide code.</returns>
        private static string TranslateCircle(int start, int end, bool isClockwise)
        {
            return $"{start}{(isClockwise ? 'Q' : 'P')}{JudgmentCircle}K{end}";
        }

        /// <summary>Translates a slide along the shorter arc of the judgment circle, <c>^</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when the end key is the start key or the opposite key.</exception>
        private static string TranslateShortCircle(int start, int end)
        {
            var relative = GetRelativeKey(start, end);
            if (relative == 0 || relative == 4)
            {
                throw new ArgumentException("^星星不合法\n^スライドエラー", nameof(end));
            }
            return TranslateCircle(start, end, relative < 4);
        }

        /// <summary>Translates a slide through the centre, <c>v</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when the end key is the opposite key.</exception>
        private static string TranslateV(int start, int end)
        {
            if (GetRelativeKey(start, end) == 4)
            {
                throw new ArgumentException("v星星不合法\nvスライドエラー", nameof(end));
            }
            return $"{start}CK{end}";
        }

        /// <summary>Translates a slide around a side circle, <c>pp</c> or <c>qq</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key.</param>
        /// <param name="isCounterClockwise">Whether the slide is a pp slide, which turns counter-clockwise.</param>
        /// <returns>The slide code.</returns>
        private static string TranslateSideLoop(int start, int end, bool isCounterClockwise)
        {
            // pp 绕起点逆时针方向第 2 个侧边圆（1pp 绕 3 号圆），qq 是它的镜像（1qq 绕 8 号圆）
            var circle = WrapKey(isCounterClockwise ? start + 2 : start - 1);
            var command = isCounterClockwise ? 'P' : 'Q';
            var travel = isCounterClockwise ? GetRelativeKey(start, end) : GetRelativeKey(end, start);
            if (travel == 3 || travel == 4)
            {
                // 1pp4 和 1pp5 绕侧边圆超过一圈，需要重复轨道指令多绕一圈
                return $"{start}{command}{circle}{command}{circle}K{end}";
            }
            return $"{start}{command}{circle}K{end}";
        }

        /// <summary>Translates a zigzag slide through the centre, <c>s</c> or its mirror <c>z</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="end">The end key.</param>
        /// <param name="isMirrored">Whether the slide is a z slide.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when the end key is not the opposite key.</exception>
        private static string TranslateZigzag(int start, int end, bool isMirrored)
        {
            if (GetRelativeKey(start, end) != 4)
            {
                throw new ArgumentException(isMirrored ? "z星星尾部错误\nzスライドエラー" : "s星星尾部错误\nsスライドエラー", nameof(end));
            }
            var first = WrapKey(start - 2);
            var second = WrapKey(start + 2);
            if (isMirrored)
            {
                (first, second) = (second, first);
            }
            return $"{start}B{first}B{second}K{end}";
        }

        /// <summary>Translates a slide that turns at a key on the judgment circle, <c>V</c>.</summary>
        /// <param name="start">The start key.</param>
        /// <param name="turn">The turn key, two keys away from the start key.</param>
        /// <param name="end">The end key.</param>
        /// <returns>The slide code.</returns>
        /// <exception cref="ArgumentException">Thrown when the turn key or the end key is out of reach.</exception>
        private static string TranslateTurn(int start, int turn, int end)
        {
            var relativeTurn = GetRelativeKey(start, turn);
            var relativeEnd = GetRelativeKey(start, end);
            if (relativeTurn != 2 && relativeTurn != 6)
            {
                throw new ArgumentException("V星星拐点只能隔开一键\nVスライドエラー", nameof(turn));
            }
            var isEndReachable = relativeTurn == 6 ? relativeEnd >= 1 && relativeEnd <= 4 : relativeEnd >= 4;
            if (!isEndReachable)
            {
                throw new ArgumentException("V星星终点不合法\nVスライドエラー", nameof(end));
            }
            return $"{start}A{turn}K{end}";
        }

        /// <summary>Counts the shape characters that follow the start key.</summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The number of non-digit characters after the start key.</returns>
        /// <exception cref="ArgumentException">Thrown when the segment has no shape.</exception>
        private static int GetShapeLength(string segment)
        {
            var length = 0;
            while (1 + length < segment.Length && !char.IsDigit(segment[1 + length]))
            {
                length++;
            }
            if (length == 0)
            {
                throw new ArgumentException($"\"{segment}\" has no slide shape", nameof(segment));
            }
            return length;
        }

        /// <summary>Reads a key digit.</summary>
        /// <param name="segment">The segment.</param>
        /// <param name="index">The index of the digit.</param>
        /// <returns>The key, between 1 and 8.</returns>
        /// <exception cref="ArgumentException">Thrown when the character is missing or not a key.</exception>
        private static int ParseKey(string segment, int index)
        {
            if (index >= segment.Length || segment[index] < '1' || segment[index] > '8')
            {
                throw new ArgumentException($"\"{segment}\" needs a key between 1 and 8 at position {index}", nameof(segment));
            }
            return segment[index] - '0';
        }

        /// <summary>Checks that the segment ends right after its last key.</summary>
        /// <param name="segment">The segment.</param>
        /// <param name="length">The expected length.</param>
        /// <exception cref="ArgumentException">Thrown when the segment is shorter or longer.</exception>
        private static void RequireLength(string segment, int length)
        {
            if (segment.Length != length)
            {
                throw new ArgumentException($"\"{segment}\" is not a single slide segment", nameof(segment));
            }
        }

        /// <summary>Checks whether a character is an ASCII digit.</summary>
        /// <param name="c">The character.</param>
        /// <returns><see langword="true"/> for <c>0</c> to <c>9</c>.</returns>
        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        /// <summary>Gets how many keys clockwise a key lies from another.</summary>
        /// <param name="from">The key to count from.</param>
        /// <param name="to">The key to count to.</param>
        /// <returns>The clockwise distance, between 0 and 7.</returns>
        private static int GetRelativeKey(int from, int to)
        {
            return ((to - from) % KeyCount + KeyCount) % KeyCount;
        }

        /// <summary>Wraps a key index that may have left the range 1 to 8.</summary>
        /// <param name="key">The key index.</param>
        /// <returns>The key index, between 1 and 8.</returns>
        private static int WrapKey(int key)
        {
            return ((key - 1) % KeyCount + KeyCount) % KeyCount + 1;
        }

        /// <summary>Checks whether a key is on the upper half, where &gt; turns clockwise.</summary>
        /// <param name="key">The key.</param>
        /// <returns><see langword="true"/> for keys 7, 8, 1 and 2.</returns>
        private static bool IsUpperHalf(int key)
        {
            return key == 7 || key == 8 || key == 1 || key == 2;
        }
    }
}
