using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.MajSimaiAdapter;

/// <summary>
/// Interprets only the path retained in a parsed SimaiNote. It never reads the
/// original chart and never creates notes that MajSimai did not return.
/// </summary>
internal sealed class SlidePathResolver
{
    private const string SingleCharacterShapes = "-^v<>Vpqszw";
    private readonly IExtendedSlideBarCountProvider? _extendedSlides;

    internal SlidePathResolver(IExtendedSlideBarCountProvider? extendedSlides = null)
    {
        _extendedSlides = extendedSlides;
    }

    public IReadOnlyList<SlidePathSegment> Resolve(
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
                if (close < 0) throw new MajSimaiAdaptationException($"Unclosed Slide duration: {rawContent}");
                cursor = close + 1;
            }

            var rawSegment = rawContent.Substring(segmentStart, cursor - segmentStart);
            var barCount = MajdataPlaySlideBarCounts.Resolve(shape, start, via, end);
            if (barCount <= 0)
                throw new MajSimaiAdaptationException($"Non-positive bar count for {start}{shape}{via}{end}.");
            parsed.Add(new ParsedSegment(
                shape, start, via, end, barCount,
                rawSegment));
            start = end;
        }

        if (parsed.Count == 0) throw new MajSimaiAdaptationException($"Slide has no path: {rawContent}");
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

    private IReadOnlyList<SlidePathSegment> ResolveExtended(
        string rawContent,
        double slideStartTimeSeconds,
        double slideEndTimeSeconds)
    {
        if (_extendedSlides is null)
            throw new MajSimaiAdaptationException(
                "Extended K Slides require the MajdataPlay geometry provider.");
        var marker = rawContent.IndexOf('K');
        if (marker < 1 || marker + 1 >= rawContent.Length)
            throw new MajSimaiAdaptationException($"Incomplete extended Slide endpoint: {rawContent}");
        var start = ParsePosition(rawContent[0], rawContent);
        var end = ParsePosition(rawContent[marker + 1], rawContent);
        var barCount = _extendedSlides.ResolveBarCount(rawContent);
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

    private static int ParsePosition(char value, string rawContent)
    {
        if (value is < '1' or > '8')
            throw new MajSimaiAdaptationException($"Invalid Slide endpoint in: {rawContent}");
        return value - '0';
    }

    private sealed class ParsedSegment
    {
        public ParsedSegment(string shape, int start, int? via, int end, int barCount, string raw)
        {
            Shape = shape;
            Start = start;
            Via = via;
            End = end;
            BarCount = barCount;
            Raw = raw;
        }

        public string Shape { get; }
        public int Start { get; }
        public int? Via { get; }
        public int End { get; }
        public int BarCount { get; }
        public string Raw { get; }
    }
}

internal sealed class MajSimaiAdaptationException : Exception
{
    public MajSimaiAdaptationException(string message) : base(message) { }
}
