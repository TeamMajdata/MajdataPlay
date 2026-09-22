using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    // Fixed regression input columns. These indexes also align Center, Scale and
    // the first seven linear coefficients in RegressionBetaParameters:
    //   0 Note, 1 Peak, 2 Sweep, 3 SlideTricky,
    //   4 SlideSequence, 5 Jack, 6 SlideCumulate.
    // This is not a UI-axis setting. Changing a name or its position requires
    // retraining the model and replacing all fitted constants together.
    public static readonly IReadOnlyList<string> ModelInputOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, SlideCumulate
    });

    // Stable enumeration order for callers that expose every scored scalar.
    // A UI may display a subset, but model input always remains ModelInputOrder.
    public static readonly IReadOnlyList<string> ScoredOutputOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, SlideCumulate,
        FittedConstant
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

    public bool IsCancelled { get; set; }

    public bool IsSuccess => Features.Count == RadarFeatureNames.ModelInputOrder.Count &&
        Features.Values.All(result => result.IsSuccess) && !IsCancelled;

    public string Status
    {
        get
        {
            if (IsCancelled) return "cancelled";
            var successful = Features.Values.Count(result => result.IsSuccess);
            if (successful == Features.Count && Features.Count > 0) return "ok";
            return successful > 0 ? "partial" : "error";
        }
    }
}

internal readonly struct AnalysisContext
{
    internal AnalysisContext(RadarChartInput chart, CancellationToken cancellationToken)
    {
        Events = chart.Events;
        ChartEndTimeSeconds = chart.ChartEndTimeSeconds;
        LastEventEndTimeSeconds = chart.LastEventEndTimeSeconds;
        CancellationToken = cancellationToken;
    }

    internal IReadOnlyList<RadarEvent> Events { get; }
    internal double ChartEndTimeSeconds { get; }
    internal double? LastEventEndTimeSeconds { get; }
    internal CancellationToken CancellationToken { get; }
    internal double DurationSeconds => Math.Max(ChartEndTimeSeconds, LastEventEndTimeSeconds ?? 0);
    internal void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();
}

internal interface IRadarFeatureAnalyzer
{
    RadarFeatureResult Analyze(AnalysisContext context);
}
