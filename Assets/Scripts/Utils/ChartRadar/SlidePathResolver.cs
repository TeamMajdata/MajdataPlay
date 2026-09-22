using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Scenes.Game.Parsing;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

/// <summary>
/// Interprets only the path retained in a parsed SimaiNote. It never reads the
/// original chart and never creates notes that MajSimai did not return.
/// </summary>
internal sealed class SlidePathResolver
{
    private const string SingleCharacterShapes = "-^v<>Vpqszw";

    internal IReadOnlyList<SlidePathSegment> Resolve(
        string rawContent,
        double slideStartTimeSeconds,
        double slideEndTimeSeconds)
    {
        if (string.IsNullOrEmpty(rawContent) || rawContent[0] is < '1' or > '8')
            throw new MajSimaiAdaptationException($"Invalid MajSimai Slide RawContent: {rawContent}");
        if (rawContent.Contains('K'))
            return ResolveExtended(rawContent, slideStartTimeSeconds, slideEndTimeSeconds);

        var parsed = new List<ParsedSegment>();
        var start = rawContent[0] - '0';
        var cursor = 1;
        while (cursor < rawContent.Length)
        {
            var segmentStart = cursor;
            string shape;
            if (cursor + 1 < rawContent.Length &&
                (rawContent.AsSpan(cursor, 2).SequenceEqual("pp".AsSpan()) ||
                 rawContent.AsSpan(cursor, 2).SequenceEqual("qq".AsSpan())))
            {
                shape = rawContent.Substring(cursor, 2);
                cursor += 2;
            }
            else if (SingleCharacterShapes.Contains(rawContent[cursor]))
            {
                shape = rawContent[cursor++].ToString();
            }
            else
            {
                throw new MajSimaiAdaptationException(
                    $"Unknown Slide path in MajSimai RawContent at offset {cursor}: {rawContent}");
            }

            var positionCount = shape == "V" ? 2 : 1;
            if (cursor + positionCount > rawContent.Length)
                throw new MajSimaiAdaptationException($"Incomplete Slide endpoint: {rawContent}");
            var via = shape == "V" ? ParsePosition(rawContent[cursor++], rawContent) : (int?)null;
            var end = ParsePosition(rawContent[cursor++], rawContent);

            while (cursor < rawContent.Length && rawContent[cursor] is 'b' or 'm' or 'c') cursor++;
            if (cursor < rawContent.Length && rawContent[cursor] == '[')
            {
                var close = rawContent.IndexOf(']', cursor + 1);
                if (close < 0)
                    throw new MajSimaiAdaptationException($"Unclosed Slide duration: {rawContent}");
                cursor = close + 1;
            }

            var rawSegment = rawContent.Substring(segmentStart, cursor - segmentStart);
            var barCount = ResolveStandardBarCount(shape, start, via, end);
            if (barCount <= 0)
                throw new MajSimaiAdaptationException($"Non-positive bar count for {start}{shape}{via}{end}.");
            parsed.Add(new ParsedSegment(shape, start, via, end, barCount, rawSegment));
            start = end;
        }

        if (parsed.Count == 0)
            throw new MajSimaiAdaptationException($"Slide has no path: {rawContent}");
        var totalBars = parsed.Sum(segment => segment.BarCount);
        var duration = slideEndTimeSeconds - slideStartTimeSeconds;
        var elapsedBars = 0;
        var output = new List<SlidePathSegment>(parsed.Count);
        for (var index = 0; index < parsed.Count; index++)
        {
            var segment = parsed[index];
            var segmentStartTime = slideStartTimeSeconds + duration * elapsedBars / totalBars;
            elapsedBars += segment.BarCount;
            var segmentEndTime = index == parsed.Count - 1
                ? slideEndTimeSeconds
                : slideStartTimeSeconds + duration * elapsedBars / totalBars;
            output.Add(new SlidePathSegment
            {
                Shape = segment.Shape,
                StartPosition = segment.Start,
                ViaPosition = segment.Via,
                EndPosition = segment.End,
                BarCount = segment.BarCount,
                StartTimeSeconds = segmentStartTime,
                EndTimeSeconds = segmentEndTime,
                RawSegment = segment.Raw
            });
        }
        return output;
    }

    private static IReadOnlyList<SlidePathSegment> ResolveExtended(
        string rawContent,
        double slideStartTimeSeconds,
        double slideEndTimeSeconds)
    {
        var marker = rawContent.IndexOf('K');
        if (marker < 1 || marker + 1 >= rawContent.Length)
            throw new MajSimaiAdaptationException($"Incomplete extended Slide endpoint: {rawContent}");
        var start = ParsePosition(rawContent[0], rawContent);
        var end = ParsePosition(rawContent[marker + 1], rawContent);
        var slideCode = rawContent.Substring(0, marker + 2);
        var path = SlideCodeParser.Parse(slideCode);
        var barCount = SlideDataBuilder.BuildArrowData(path).Length - 2;
        if (barCount <= 0)
            throw new MajSimaiAdaptationException(
                $"Non-positive Play arrow count for extended Slide: {rawContent}");
        return new[]
        {
            new SlidePathSegment
            {
                Shape = "slidecode",
                StartPosition = start,
                EndPosition = end,
                BarCount = barCount,
                StartTimeSeconds = slideStartTimeSeconds,
                EndTimeSeconds = slideEndTimeSeconds,
                RawSegment = rawContent
            }
        };
    }

    /// <summary>
    /// MajdataPlay's standard Slide prefab lengths. These are gameplay length units
    /// used to divide connected-Slide time, not physical distance or note counts.
    /// </summary>
    private static int ResolveStandardBarCount(
        string shape,
        int startPosition,
        int? viaPosition,
        int endPosition)
    {
        var relativeEnd = PositiveModulo(endPosition - startPosition, 8) + 1;
        var prefab = shape switch
        {
            "-" => $"line{relativeEnd}",
            ">" => $"circle{(startPosition is 7 or 8 or 1 or 2 ? relativeEnd : Mirror(relativeEnd))}",
            "<" => $"circle{(startPosition is 3 or 4 or 5 or 6 ? relativeEnd : Mirror(relativeEnd))}",
            "^" => $"circle{(relativeEnd < 5 ? relativeEnd : Mirror(relativeEnd))}",
            "v" => $"v{relativeEnd}",
            "p" => $"pq{relativeEnd}",
            "q" => $"pq{Mirror(relativeEnd)}",
            "pp" => $"ppqq{relativeEnd}",
            "qq" => $"ppqq{Mirror(relativeEnd)}",
            "s" or "z" => "s",
            "w" => "wifi",
            "V" => ResolveLargeV(startPosition, viaPosition, relativeEnd),
            _ => throw new MajSimaiAdaptationException($"Unsupported Slide shape: {shape}")
        };
        return prefab switch
        {
            "line3" => 14,
            "line4" => 19,
            "line5" => 20,
            "line6" => 19,
            "line7" => 14,
            "circle1" => 64,
            "circle2" => 8,
            "circle3" => 16,
            "circle4" => 24,
            "circle5" => 32,
            "circle6" => 40,
            "circle7" => 48,
            "circle8" => 56,
            "v1" => 21,
            "v2" or "v3" or "v4" or "v6" or "v7" or "v8" => 20,
            "ppqq1" => 36,
            "ppqq2" => 29,
            "ppqq3" => 23,
            "ppqq4" or "ppqq5" => 50,
            "ppqq6" => 49,
            "ppqq7" => 47,
            "ppqq8" => 42,
            "pq1" => 34,
            "pq2" => 31,
            "pq3" => 28,
            "pq4" => 25,
            "pq5" => 22,
            "pq6" => 43,
            "pq7" => 41,
            "pq8" => 37,
            "s" => 31,
            "wifi" => 12,
            "L2" => 33,
            "L3" => 35,
            "L4" => 33,
            "L5" => 29,
            _ => throw new MajSimaiAdaptationException(
                $"No standard MajdataPlay bar count for {startPosition}{shape}{viaPosition}{endPosition} ({prefab}).")
        };
    }

    private static string ResolveLargeV(int start, int? via, int relativeEnd)
    {
        if (via is null)
            throw new MajSimaiAdaptationException("A V Slide requires a via position.");
        var turn = PositiveModulo(via.Value - start, 8);
        return turn switch
        {
            6 => $"L{relativeEnd}",
            2 => $"L{Mirror(relativeEnd)}",
            _ => throw new MajSimaiAdaptationException("A V Slide has an invalid via position.")
        };
    }

    private static int ParsePosition(char value, string rawContent)
    {
        if (value is < '1' or > '8')
            throw new MajSimaiAdaptationException($"Invalid Slide endpoint in: {rawContent}");
        return value - '0';
    }

    private static int Mirror(int position) => position == 1 ? 1 : 10 - position;
    private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

    private sealed class ParsedSegment
    {
        internal ParsedSegment(string shape, int start, int? via, int end, int barCount, string raw)
        {
            Shape = shape;
            Start = start;
            Via = via;
            End = end;
            BarCount = barCount;
            Raw = raw;
        }

        internal string Shape { get; }
        internal int Start { get; }
        internal int? Via { get; }
        internal int End { get; }
        internal int BarCount { get; }
        internal string Raw { get; }
    }
}

internal sealed class MajSimaiAdaptationException : Exception
{
    internal MajSimaiAdaptationException(string message) : base(message) { }
}
