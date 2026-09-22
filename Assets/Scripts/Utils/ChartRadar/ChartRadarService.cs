using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajSimai;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

internal static class ChartRadarOutputDimensions
{
    internal const string Note = RadarFeatureNames.Note;
    internal const string Peak = RadarFeatureNames.Peak;
    internal const string Sweep = RadarFeatureNames.Sweep;
    internal const string SlideTricky = RadarFeatureNames.SlideTricky;
    internal const string SlideSequence = RadarFeatureNames.SlideSequence;
    internal const string Jack = RadarFeatureNames.Jack;
    internal const string FittedConstant = RadarFeatureNames.FittedConstant;

    internal static readonly IReadOnlyList<string> DefaultOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, FittedConstant
    });

    internal static IReadOnlyDictionary<string, double?> EmptyValues() =>
        DefaultOrder.ToDictionary(name => name, _ => (double?)null);
}

/// <summary>UI-facing scalar snapshot with no parser or analysis implementation details.</summary>
public sealed class ChartRadarSnapshot
{
    public bool IsSuccess { get; set; }
    public bool IsCancelled { get; set; }
    public string Status { get; set; } = "error";
    /// <summary>Fixed public keys; unavailable or failed dimensions are null.</summary>
    public IReadOnlyDictionary<string, double?> RawValues { get; set; } =
        ChartRadarOutputDimensions.EmptyValues();
    /// <summary>
    /// Mapped radar values. FittedConstant is intentionally identity-mapped and
    /// is not on the same 0-250 scale as the radar dimensions.
    /// </summary>
    public IReadOnlyDictionary<string, double?> Scores { get; set; } =
        ChartRadarOutputDimensions.EmptyValues();
    public IReadOnlyList<string> DimensionOrder { get; set; } =
        ChartRadarOutputDimensions.DefaultOrder;
    public double? FittedConstant { get; set; }
    public string? MappingVersion { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Chart radar entry point for Play. The caller owns per-song/difficulty caching;
/// cancellation is cooperatively propagated into the analysis loops.
/// </summary>
public sealed class ChartRadarService
{
    private readonly MajSimaiChartAdapter _adapter = new();
    private readonly RadarAnalyzer _analyzer = new();
    private readonly RegressionBetaModel _model = new();
    private readonly RadarScoreMapper _scorer = new();

    public ChartRadarSnapshot Analyze(
        SimaiChart? chart,
        CancellationToken cancellationToken = default)
    {
        if (chart is null)
            return Failure("SimaiChart is null.");
        if (cancellationToken.IsCancellationRequested)
            return Cancelled();

        var adapted = _adapter.Adapt(chart, cancellationToken);
        if (adapted.IsCancelled)
            return Cancelled();
        if (!adapted.IsSuccess)
            return Failure(adapted.Errors);

        var analysis = _analyzer.Analyze(adapted.Chart!, cancellationToken);
        if (analysis.IsCancelled)
            return Cancelled();

        var errors = analysis.Features
            .Where(pair => !pair.Value.IsSuccess)
            .Select(pair => $"{pair.Key}: {pair.Value.Error}")
            .ToList();
        double? fittedConstant = null;
        IReadOnlyDictionary<string, double>? scores = null;
        string? mappingVersion = null;

        if (analysis.IsSuccess)
        {
            try
            {
                var raw = RadarFeatureNames.ModelInputOrder
                    .Select(name => analysis.Features[name].Value!.Value)
                    .ToArray();
                fittedConstant = _model.Predict(raw);
                scores = _scorer.Map(analysis, fittedConstant.Value);
                mappingVersion = RadarScoreMapper.MappingVersion;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled();
            }
            catch (Exception exception)
            {
                errors.Add($"Radar regression or scoring failed: {exception.Message}");
            }
        }

        var rawValues = analysis.Features
            .Where(pair => pair.Value.IsSuccess && pair.Value.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value!.Value);
        if (fittedConstant is double fitted)
            rawValues[RadarFeatureNames.FittedConstant] = fitted;

        var isSuccess = analysis.IsSuccess && fittedConstant is not null &&
                        scores is not null && errors.Count == 0;
        return new ChartRadarSnapshot
        {
            IsSuccess = isSuccess,
            Status = isSuccess ? "ok" : analysis.IsSuccess ? "error" : analysis.Status,
            RawValues = Project(rawValues),
            Scores = Project(scores),
            FittedConstant = fittedConstant,
            MappingVersion = mappingVersion,
            Errors = errors
        };
    }

    /// <summary>
    /// Runs the Unity-free calculation off the caller thread. Cancellation returns
    /// a snapshot with IsCancelled=true rather than faulting the task.
    /// </summary>
    public Task<ChartRadarSnapshot> AnalyzeAsync(
        SimaiChart? chart,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(chart, cancellationToken));

    private static ChartRadarSnapshot Cancelled() => new()
    {
        IsCancelled = true,
        Status = "cancelled"
    };

    private static ChartRadarSnapshot Failure(string error) => Failure(new[] { error });

    private static ChartRadarSnapshot Failure(IReadOnlyList<string> errors) => new()
    {
        Errors = errors
    };

    private static IReadOnlyDictionary<string, double?> Project(
        IReadOnlyDictionary<string, double>? source) =>
        ChartRadarOutputDimensions.DefaultOrder.ToDictionary(
            name => name,
            name => source is not null && source.TryGetValue(name, out var value)
                ? (double?)value
                : null);
}
