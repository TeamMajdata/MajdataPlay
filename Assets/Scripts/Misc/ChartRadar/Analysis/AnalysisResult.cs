using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis;

public static class RadarFeatureNames
{
    public const string Note = "note";
    public const string Peak = "peak";
    public const string Sweep = "sweep";
    public const string SlideTricky = "slide_tricky";
    public const string SlideSequence = "slide_sequence";
    public const string Jack = "jack";
    public const string SlideCumulate = "slide_cumulate";
    public const string FittedConstant = "fitted_constant";

    public static readonly IReadOnlyList<string> ModelInputOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, SlideCumulate
    });
}

public sealed class RadarFeatureResult
{
    public double? Value { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }

    public static RadarFeatureResult Success(double value) => new()
    {
        Value = value,
        IsSuccess = true
    };

    public static RadarFeatureResult Failure(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };
}

public sealed class RadarAnalysisResult
{
    public IReadOnlyDictionary<string, RadarFeatureResult> Features { get; set; } =
        new Dictionary<string, RadarFeatureResult>();

    public bool IsSuccess => Features.Count == RadarFeatureNames.ModelInputOrder.Count &&
        Features.Values.All(result => result.IsSuccess);

    public string Status
    {
        get
        {
            var successful = Features.Values.Count(result => result.IsSuccess);
            if (successful == Features.Count && Features.Count > 0) return "ok";
            return successful > 0 ? "partial" : "error";
        }
    }
}

internal readonly struct AnalysisContext
{
    internal AnalysisContext(RadarChartInput chart)
    {
        Events = chart.Events;
        ChartEndTimeSeconds = chart.ChartEndTimeSeconds;
        LastEventEndTimeSeconds = chart.LastEventEndTimeSeconds;
    }

    internal IReadOnlyList<RadarEvent> Events { get; }
    internal double ChartEndTimeSeconds { get; }
    internal double? LastEventEndTimeSeconds { get; }
    internal double DurationSeconds => Math.Max(ChartEndTimeSeconds, LastEventEndTimeSeconds ?? 0);
}

internal interface IRadarFeatureAnalyzer
{
    RadarFeatureResult Analyze(AnalysisContext context);
}
