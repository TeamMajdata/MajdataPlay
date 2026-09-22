using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajSimai;
using SimaiRadar.Analysis;
using SimaiRadar.Runtime;

#nullable enable

namespace MajdataPlay.ChartRadar;

/// <summary>
/// Fixed public projection. The regression still consumes all seven entries in
/// RadarFeatureNames.ModelInputOrder; SlideCumulate is intentionally internal.
/// To change the UI axes, edit these constants and DefaultOrder together.
/// </summary>
public static class ChartRadarOutputDimensions
{
    public const string Note = RadarFeatureNames.Note;
    public const string Peak = RadarFeatureNames.Peak;
    public const string Sweep = RadarFeatureNames.Sweep;
    public const string SlideTricky = RadarFeatureNames.SlideTricky;
    public const string SlideSequence = RadarFeatureNames.SlideSequence;
    public const string Jack = RadarFeatureNames.Jack;
    public const string FittedConstant = RadarFeatureNames.FittedConstant;

    // Default public order: six displayed raw dimensions plus fitted constant.
    public static readonly IReadOnlyList<string> DefaultOrder = Array.AsReadOnly(new[]
    {
        Note, Peak, Sweep, SlideTricky, SlideSequence, Jack, FittedConstant
    });

    internal static IReadOnlyDictionary<string, double?> EmptyValues()
    {
        var output = new Dictionary<string, double?>();
        foreach (var name in DefaultOrder) output[name] = null;
        return output;
    }
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
    /// is not on the same 0-250 scale as the seven radar dimensions.
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
/// Thin entry point for selection/game UI. The caller owns per-song/difficulty
/// caching; cancellation is cooperatively propagated into analysis loops.
/// </summary>
public sealed class ChartRadarService
{
    private readonly RadarRuntime _runtime = new();

    public ChartRadarSnapshot Analyze(
        SimaiChart? chart,
        CancellationToken cancellationToken = default)
    {
        if (chart is null)
            return new ChartRadarSnapshot { Errors = new[] { "SimaiChart is null." } };

        var result = _runtime.Analyze(chart, cancellationToken);
        var raw = result.Analysis?.Features
            .Where(pair => pair.Value.IsSuccess && pair.Value.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value!.Value)
            ?? new Dictionary<string, double>();
        if (result.FittedConstant is double fitted)
            raw[RadarFeatureNames.FittedConstant] = fitted;

        return new ChartRadarSnapshot
        {
            IsSuccess = result.IsSuccess,
            IsCancelled = result.IsCancelled,
            Status = result.IsCancelled ? "cancelled" :
                result.IsSuccess ? "ok" :
                result.Analysis?.IsSuccess == true ? "error" :
                result.Analysis?.Status ?? "error",
            RawValues = Project(raw),
            Scores = Project(result.Scores?.Values),
            DimensionOrder = ChartRadarOutputDimensions.DefaultOrder,
            FittedConstant = result.FittedConstant,
            MappingVersion = result.Scores?.MappingVersion,
            Errors = result.Errors
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

    private static IReadOnlyDictionary<string, double?> Project(
        IReadOnlyDictionary<string, double>? source)
    {
        var output = new Dictionary<string, double?>();
        foreach (var name in ChartRadarOutputDimensions.DefaultOrder)
            output[name] = source is not null && source.TryGetValue(name, out var value)
                ? value : null;
        return output;
    }
}
