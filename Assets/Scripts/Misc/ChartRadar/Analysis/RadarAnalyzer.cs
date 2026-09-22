using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SimaiRadar.Analysis.Features;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis;

public sealed class RadarAnalyzer
{
    private const int MaximumChartEvents = 20_000;
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

    public RadarAnalysisResult Analyze(
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
            catch (OperationCanceledException)
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
