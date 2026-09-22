using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajSimai;
using SimaiRadar.Analysis;
using SimaiRadar.Core;
using SimaiRadar.MajSimaiAdapter;
using SimaiRadar.Regression;
using SimaiRadar.Scoring;

#nullable enable

namespace SimaiRadar.Runtime;

public sealed class RadarComputationResult
{
    public RadarChartInput? ChartInput { get; set; }
    public RadarAnalysisResult? Analysis { get; set; }
    public double? FittedConstant { get; set; }
    public RadarScoreResult? Scores { get; set; }
    public bool IsCancelled { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public bool IsSuccess => ChartInput is not null && Analysis?.IsSuccess == true &&
                             FittedConstant is not null && Scores is not null && Errors.Count == 0 &&
                             !IsCancelled;
}

/// <summary>Unity-free public entry point for Play and standalone callers.</summary>
public sealed class RadarRuntime
{
    private readonly MajSimaiChartAdapter _adapter = new();
    private readonly RadarAnalyzer _analyzer = new();
    private readonly RegressionBetaModel _model = new();
    private readonly RadarScoreMapper _scorer = new();

    public RadarComputationResult Analyze(
        SimaiChart chart,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new RadarComputationResult { IsCancelled = true };
        var adapted = _adapter.Adapt(chart, cancellationToken);
        if (adapted.IsCancelled)
            return new RadarComputationResult { IsCancelled = true };
        if (!adapted.IsSuccess)
            return new RadarComputationResult { Errors = adapted.Errors };
        return Analyze(adapted.Chart!, cancellationToken);
    }

    public async Task<RadarComputationResult> ParseAndAnalyzeAsync(
        string inote,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new RadarComputationResult { IsCancelled = true };
        var adapted = await _adapter.ParseAndAdaptAsync(inote, cancellationToken).ConfigureAwait(false);
        if (adapted.IsCancelled)
            return new RadarComputationResult { IsCancelled = true };
        if (!adapted.IsSuccess)
            return new RadarComputationResult { Errors = adapted.Errors };
        return Analyze(adapted.Chart!, cancellationToken);
    }

    public RadarComputationResult Analyze(
        RadarChartInput chart,
        CancellationToken cancellationToken = default)
    {
        var result = new RadarComputationResult { ChartInput = chart };
        try
        {
            var analysis = _analyzer.Analyze(chart, cancellationToken);
            result.Analysis = analysis;
            if (analysis.IsCancelled)
            {
                result.IsCancelled = true;
                return result;
            }
            if (!analysis.IsSuccess)
            {
                result.Errors = analysis.Features
                    .Where(pair => !pair.Value.IsSuccess)
                    .Select(pair => $"{pair.Key}: {pair.Value.Error}")
                    .ToArray();
                return result;
            }
            var raw = RadarFeatureNames.ModelInputOrder
                .Select(name => analysis.Features[name].Value!.Value).ToArray();
            result.FittedConstant = _model.Predict(raw);
            result.Scores = _scorer.Map(analysis, result.FittedConstant.Value);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.IsCancelled = true;
            return result;
        }
        catch (Exception exception)
        {
            result.Errors = new[] { $"Radar regression or scoring failed: {exception.Message}" };
            return result;
        }
    }
}
