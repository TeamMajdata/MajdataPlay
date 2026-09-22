using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Analysis.Features;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis;

public sealed class RadarAnalyzer
{
    private readonly IReadOnlyList<(string Name, IRadarFeatureAnalyzer Analyzer)> _features =
        new (string, IRadarFeatureAnalyzer)[]
        {
            (RadarFeatureNames.Note, new NoteDensityAnalyzer()),
            (RadarFeatureNames.Peak, new PeakDensityAnalyzer()),
            (RadarFeatureNames.Sweep, new SweepBurstAnalyzer()),
            (RadarFeatureNames.SlideTricky, new SlideTrickyAnalyzer()),
            (RadarFeatureNames.SlideSequence, new SlideSequenceAnalyzer())
        };

    public RadarAnalysisResult Analyze(RadarChartInput? chart)
    {
        var results = new Dictionary<string, RadarFeatureResult>();
        if (chart is null)
        {
            foreach (var feature in _features)
                results[feature.Name] = RadarFeatureResult.Failure("Chart input is null.");
            return new RadarAnalysisResult { Features = results };
        }

        var context = new AnalysisContext(chart);
        foreach (var feature in _features)
        {
            try
            {
                var result = feature.Analyzer.Analyze(context);
                if (result.IsSuccess &&
                    (result.Value is null || double.IsNaN(result.Value.Value) ||
                     double.IsInfinity(result.Value.Value)))
                    throw new InvalidOperationException("Successful feature returned a non-finite value.");
                results[feature.Name] = result;
            }
            catch (Exception exception)
            {
                results[feature.Name] = RadarFeatureResult.Failure(exception.Message);
            }
        }
        return new RadarAnalysisResult { Features = results };
    }
}
