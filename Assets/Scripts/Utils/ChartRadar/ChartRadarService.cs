using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MajRadar.Runtime;
using MajSimai;

namespace MajdataPlay.Utils.ChartRadar;

/// <summary>
/// Lightweight Play-facing result. It deliberately does not retain MajRadar's
/// adapted chart or full seven-dimension analysis graph.
/// </summary>
public sealed class ChartRadarSnapshot
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public string Status { get; init; } = "error";
    public IReadOnlyDictionary<string, double?> RawValues { get; init; } =
        new Dictionary<string, double?>();
    public IReadOnlyDictionary<string, double?> Scores { get; init; } =
        new Dictionary<string, double?>();
    public IReadOnlyList<string> DimensionOrder { get; init; } = Array.Empty<string>();
    public double? FittedConstant { get; init; }
    public string? ModelVersion { get; init; }
    public string? MappingVersion { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public double? GetRawValue(RadarOutputDimension dimension) =>
        Value(RawValues, dimension);

    public double? GetScore(RadarOutputDimension dimension) =>
        Value(Scores, dimension);

    internal static ChartRadarSnapshot From(RadarResult result) => new()
    {
        IsSuccess = result.IsSuccess,
        IsCancelled = result.IsCancelled,
        Status = result.Status,
        RawValues = result.RawValues,
        Scores = result.Scores,
        DimensionOrder = result.DimensionOrder,
        FittedConstant = result.FittedConstant,
        ModelVersion = result.ModelVersion,
        MappingVersion = result.MappingVersion,
        Errors = result.Errors
    };

    private static double? Value(
        IReadOnlyDictionary<string, double?> values,
        RadarOutputDimension dimension) =>
        values.TryGetValue(RadarOutputDimensions.Key(dimension), out var value)
            ? value
            : null;
}

/// <summary>
/// Play's long-lived radar service. The only supported gameplay geometry is
/// wired directly here; callers do not select or replace dependencies per call.
/// </summary>
public static class ChartRadarService
{
    private static readonly RadarRuntime Runtime =
        new(new PlayExtendedSlideBarCountProvider());

    public static ChartRadarSnapshot Analyze(
        SimaiChart chart,
        CancellationToken cancellationToken = default) =>
        ChartRadarSnapshot.From(Runtime.Analyze(chart, cancellationToken));

    public static async Task<ChartRadarSnapshot> AnalyzeAsync(
        SimaiChart chart,
        CancellationToken cancellationToken = default)
    {
        var result = await Runtime.AnalyzeAsync(chart, cancellationToken);
        return ChartRadarSnapshot.From(result);
    }
}
