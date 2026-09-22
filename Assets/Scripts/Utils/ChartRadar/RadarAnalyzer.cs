using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

internal static class RadarFeatureNames
{
    internal const string Note = "note";
    internal const string Peak = "peak";
    internal const string Sweep = "sweep";
    internal const string SlideTricky = "slide_tricky";
    internal const string SlideSequence = "slide_sequence";
    internal const string Jack = "jack";
    internal const string SlideCumulate = "slide_cumulate";
    internal const string FittedConstant = "fitted_constant";

    // Fixed regression input columns. Their indexes align the regression model's
    // center, scale and coefficient arrays; changing the order requires refitting.
    internal static readonly IReadOnlyList<string> ModelInputOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, SlideCumulate
    });
}

internal sealed class RadarFeatureResult
{
    internal double? Value { get; set; }
    internal bool IsSuccess { get; set; }
    internal string? Error { get; set; }

    internal static RadarFeatureResult Success(double value) => new()
    {
        Value = value,
        IsSuccess = true
    };

    internal static RadarFeatureResult Failure(string error) => new()
    {
        Error = error
    };
}

internal sealed class RadarAnalysisResult
{
    internal IReadOnlyDictionary<string, RadarFeatureResult> Features { get; set; } =
        new Dictionary<string, RadarFeatureResult>();

    internal bool IsCancelled { get; set; }

    internal bool IsSuccess => Features.Count == RadarFeatureNames.ModelInputOrder.Count &&
        Features.Values.All(result => result.IsSuccess) && !IsCancelled;

    internal string Status
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

internal sealed class RadarAnalyzer
{
    private const int MaximumChartEvents = 40_000;

    // Keep this fixed execution/output order identical to ModelInputOrder.
    // Display-axis selection belongs after scoring and must not edit this list.
    private readonly IReadOnlyList<(string Name, IRadarFeatureAnalyzer Analyzer)> _features =
        new (string, IRadarFeatureAnalyzer)[]
        {
            (RadarFeatureNames.Note, new NoteDensityAnalyzer()),
            (RadarFeatureNames.Peak, new PeakDensityAnalyzer()),
            (RadarFeatureNames.Sweep, new SweepBurstAnalyzer()),
            (RadarFeatureNames.SlideTricky, new SlideTrickyAnalyzer()),
            (RadarFeatureNames.SlideSequence, new SlideSequenceAnalyzer()),
            (RadarFeatureNames.Jack, new JackSequenceAnalyzer()),
            (RadarFeatureNames.SlideCumulate, new SlideCumulateAnalyzer())
        };

    internal RadarAnalysisResult Analyze(
        RadarChartInput? chart,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, RadarFeatureResult>();
        if (cancellationToken.IsCancellationRequested)
            return new RadarAnalysisResult { Features = results, IsCancelled = true };
        if (chart is null)
        {
            foreach (var feature in _features)
                results[feature.Name] = RadarFeatureResult.Failure("Chart input is null.");
            return new RadarAnalysisResult { Features = results };
        }
        if (chart.Events.Count > MaximumChartEvents)
        {
            foreach (var feature in _features)
                results[feature.Name] = RadarFeatureResult.Failure(
                    $"Chart event budget exceeded ({chart.Events.Count} > {MaximumChartEvents}).");
            return new RadarAnalysisResult { Features = results };
        }

        var context = new AnalysisContext(chart, cancellationToken);
        foreach (var feature in _features)
        {
            if (cancellationToken.IsCancellationRequested)
                return new RadarAnalysisResult { Features = results, IsCancelled = true };
            try
            {
                var result = feature.Analyzer.Analyze(context);
                if (result.IsSuccess &&
                    (result.Value is null || double.IsNaN(result.Value.Value) ||
                     double.IsInfinity(result.Value.Value)))
                    throw new InvalidOperationException("Successful feature returned a non-finite value.");
                results[feature.Name] = result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new RadarAnalysisResult { Features = results, IsCancelled = true };
            }
            catch (Exception exception)
            {
                results[feature.Name] = RadarFeatureResult.Failure(exception.Message);
            }
        }
        return new RadarAnalysisResult { Features = results };
    }
}
