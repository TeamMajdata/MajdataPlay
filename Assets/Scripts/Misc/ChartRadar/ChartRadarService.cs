using System;
using System.Collections.Generic;
using System.Linq;
using MajSimai;
using SimaiRadar.Analysis;
using SimaiRadar.Runtime;

#nullable enable

namespace MajdataPlay.ChartRadar;

/// <summary>UI-facing scalar snapshot with no parser or analysis implementation details.</summary>
public sealed class ChartRadarSnapshot
{
    public bool IsSuccess { get; set; }
    public string Status { get; set; } = "error";
    public IReadOnlyDictionary<string, double> RawValues { get; set; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Scores { get; set; } =
        new Dictionary<string, double>();
    public double? FittedConstant { get; set; }
    public string? MappingVersion { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Thin entry point for selection/game UI. The caller owns background scheduling,
/// cancellation and per-song/difficulty caching; this service never reparses a chart.
/// </summary>
public sealed class ChartRadarService
{
    private readonly RadarRuntime _runtime = new();

    public ChartRadarSnapshot Analyze(SimaiChart? chart)
    {
        if (chart is null)
            return new ChartRadarSnapshot { Errors = new[] { "SimaiChart is null." } };

        var result = _runtime.Analyze(chart);
        var raw = result.Analysis?.Features
            .Where(pair => pair.Value.IsSuccess && pair.Value.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value!.Value)
            ?? new Dictionary<string, double>();
        if (result.FittedConstant is double fitted)
            raw[RadarFeatureNames.FittedConstant] = fitted;

        return new ChartRadarSnapshot
        {
            IsSuccess = result.IsSuccess,
            Status = result.Analysis?.Status ?? "error",
            RawValues = raw,
            Scores = result.Scores?.Values ?? new Dictionary<string, double>(),
            FittedConstant = result.FittedConstant,
            MappingVersion = result.Scores?.MappingVersion,
            Errors = result.Errors
        };
    }
}
