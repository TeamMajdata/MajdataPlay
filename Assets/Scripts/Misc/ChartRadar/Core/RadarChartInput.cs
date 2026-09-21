using System;
using System.Collections.Generic;

#nullable enable

namespace SimaiRadar.Core;

public enum RadarEventKind
{
    Tap,
    Hold,
    Touch,
    TouchHold,
    Slide,
    Timing
}

public sealed class SlidePathSegment
{
    public string Shape { get; set; } = string.Empty;
    public int StartPosition { get; set; }
    public int? ViaPosition { get; set; }
    public int EndPosition { get; set; }
    public int BarCount { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public string RawSegment { get; set; } = string.Empty;
}

public sealed class RadarEvent
{
    public int EventId { get; set; }
    public RadarEventKind Kind { get; set; }
    public bool? IsSlideHead { get; set; }
    public double? SlideDeclareTimeSeconds { get; set; }
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public BeatPosition StartBeat { get; set; }
    public BeatPosition EndBeat { get; set; }
    public double? Bpm { get; set; }
    public string? Position { get; set; }
    public int? HeadEventId { get; set; }
    public IReadOnlyList<SlidePathSegment>? SlidePath { get; set; }
    public bool? IsBreak { get; set; }
    public bool? IsEx { get; set; }
    public bool? IsMine { get; set; }
    public IReadOnlyDictionary<string, bool>? Flags { get; set; }
    public string RawToken { get; set; } = string.Empty;
    public int DeclarationOrder { get; set; }
}

public sealed class RadarChartInput
{
    public IReadOnlyList<RadarEvent> Events { get; set; } = Array.Empty<RadarEvent>();
    public double ChartEndTimeSeconds { get; set; }
    public double? LastEventEndTimeSeconds { get; set; }
}
