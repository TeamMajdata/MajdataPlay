using System;
using System.Linq;

namespace MajdataPlay.Utils.ChartRadar;

internal sealed class NoteDensityAnalyzer : IRadarFeatureAnalyzer
{
    private const double BurstWeight = 0.3;

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        context.ThrowIfCancellationRequested();
        var duration = context.DurationSeconds;
        if (duration <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        var ratio = duration / Workload.WindowSeconds;
        var nearest = Math.Round(ratio);
        if (Math.Abs(ratio - nearest) <= 1e-12) ratio = nearest;
        var count = Math.Max(1, (int)Math.Ceiling(ratio));
        var workload = new double[count];
        foreach (var point in Workload.CorrectedPoints(context.Events))
        {
            context.ThrowIfCancellationRequested();
            var index = Math.Min(
                (int)Math.Floor((point.TimeSeconds + 1e-9) / Workload.WindowSeconds),
                count - 1);
            workload[index] += point.Weight;
        }
        var densities = workload.Select(value => value / Workload.WindowSeconds).ToArray();
        var mean = densities.Sum() / count;
        if (mean == 0) return RadarFeatureResult.Success(0);
        var variance = densities.Sum(value => (value - mean) * (value - mean)) / count;
        return RadarFeatureResult.Success(mean * (1 + BurstWeight * Math.Sqrt(variance) / mean));
    }
}
